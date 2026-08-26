using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Glue between the engine-free netcode and everything you can see and hear. Owns the server (when
    /// hosting), the local client, the view rig, the opponent views and the effects.
    ///
    /// Hosting runs a real server and then connects to it over loopback like any other client, so there
    /// is exactly one gameplay code path - the host is never a special case.
    /// </summary>
    public sealed class NetGame : IEventSink
    {
        public enum Mode { Offline, Hosting, Joining }

        public sealed class PlayerInfo
        {
            public int PeerId;
            public string Name = "duellist";
            public int Kills;
            public int Deaths;
        }

        public struct KillFeedEntry
        {
            public string Killer;
            public string Victim;
            // The ids as well as the names, so the feed can colour the line you are in without
            // comparing strings - two players are allowed to pick the same name.
            public int KillerId;
            public int VictimId;
            public HitZone Zone;
            public float Time;
        }

        // ------------------------------------------------------------------ wiring
        public GameTuning Tuning = new GameTuning();
        public FeelTuning Feel;
        public IPlayerInput Input;
        public PlayerView View;
        public Palette Palette;
        public AudioBank Audio;
        public SoundPlayer Sound;
        public CombatFx Fx;
        public UnityCollisionWorld World;
        public SpawnSet Spawns;
        public WorldModel Model = new WorldModel();
        public WorldView Scenery;
        public Transform Root;
        public int PlayerLayer;
        public int FxLayer;

        /// <summary>
        /// One set of conditions per machine, applied to everything it sends - server and client alike.
        /// Two ends each running 75ms gives the 150ms round trip you would actually feel.
        /// </summary>
        public NetConditions Conditions = new NetConditions();

        /// <summary>Map the host will run. The server sends it to clients, who rebuild to match.</summary>
        public MapId HostMap = MapId.DuelArena;
        public GameMode HostMode = GameMode.Duel;
        public MapId CurrentMap = MapId.DuelArena;
        public System.Action<MapId> OnMapRequested;
        public List<ArenaBuilder.Station> Stations;

        public NetServer Server;
        public NetClient Client;
        public LanDiscovery Discovery;

        /// <summary>
        /// Asks the router to forward the game port while we are hosting, so that inviting someone
        /// from outside the house does not start with a router admin page. Null when not hosting.
        /// </summary>
        public PortMapper Mapper;
        /// <summary>Whether the outside world can actually reach us. Null unless hosting.</summary>
        public ReachabilityProbe Reachability { get { return _serverSocket != null ? _serverSocket.Reachability : null; } }
        public bool OpenPortAutomatically = true;

        public Mode CurrentMode = Mode.Offline;
        public string Status = "";
        public string PlayerName = "duellist";
        public int Port = Protocol.DefaultPort;

        public readonly Dictionary<int, PlayerInfo> Players = new Dictionary<int, PlayerInfo>();
        public readonly List<KillFeedEntry> KillFeed = new List<KillFeedEntry>();

        /// <summary>Prop this player is dragging, -1 when empty handed. Used by the HUD.</summary>
        public int HeldProp = -1;
        /// <summary>
        /// A boot carries about twenty-five metres of open ground, which is a tenth of what a rifle
        /// does. That difference is the whole reason you can hear a shot through a wall and not a
        /// footstep - the transmission model reads it, it is not two separate rules.
        /// </summary>
        const float FootstepCarry = 25f;

        /// <summary>
        /// The hill, as last heard from the server. The countdown is run down locally between
        /// messages so it ticks smoothly, and corrected every time one arrives.
        /// </summary>
        public int ActiveZone = -1;
        public float ZoneTimer;
        public int ZoneHolder = KothState.Nobody;
        public readonly System.Collections.Generic.List<ZoneDef> Zones = new System.Collections.Generic.List<ZoneDef>();

        public bool PlayingHill { get { return Client != null && Client.Mode == GameMode.KingOfTheHill && Zones.Count > 0; } }

        public string ZoneName(int index)
        {
            if (index < 0 || index >= Zones.Count) return "";
            return Zones[index].Name;
        }

        public float HitMarkerTimer;
        public bool HitMarkerHeadshot;
        public float DamageFlashTimer;
        public float RespawnCountdown;
        public MatchPhase Phase = MatchPhase.Warmup;
        public float PhaseTimer;
        public int Winner = -1;

        UdpTransport _serverSocket;
        UdpTransport _clientSocket;
        ConditionedTransport _serverTransport;
        ConditionedTransport _clientTransport;
        readonly Dictionary<int, RemotePlayerView> _remoteViews = new Dictionary<int, RemotePlayerView>();
        RemotePlayerView _ownBody;
        readonly List<int> _scratchIds = new List<int>();
        float _lastHealth = 100f;
        float _stepDistance;

        public bool InGame { get { return Client != null && Client.Connected; } }
        public bool IsHost { get { return Server != null; } }

        // ================================================================== lifecycle
        public bool Host(int port, string playerName, out string error, bool dedicated = false)
        {
            Leave();
            error = null;
            Port = port;
            PlayerName = playerName;

            // Build the map before the server exists: it takes the spawn points by reference.
            if (CurrentMap != HostMap && OnMapRequested != null) OnMapRequested(HostMap);

            _serverSocket = UdpTransport.CreateServer(port, out error);
            if (_serverSocket == null) { Status = error; return false; }

            _serverTransport = new ConditionedTransport(_serverSocket);
            _serverTransport.Conditions = Conditions;
            Server = new NetServer(_serverTransport, World, Spawns, Tuning, Model);
            Server.ServerName = playerName + "'s duel";
            Server.Map = HostMap;
            Server.Mode = HostMode;

            Discovery = new LanDiscovery(false);
            CurrentMode = Mode.Hosting;
            Status = "Hosting on port " + port + " (" + UdpTransport.LocalAddress() + ")";

            if (OpenPortAutomatically)
            {
                Mapper = new PortMapper(port, UdpTransport.LocalAddress());
                Mapper.Begin();
            }

            // Ask the outside world separately. UPnP only ever knows about mappings it made itself, so
            // without this a port forwarded by hand on the router reads as shut.
            _serverSocket.BeginReachabilityProbe();

            // A dedicated server has no player of its own: nobody to render, nobody to predict for.
            // It just runs the simulation and answers everyone who turns up.
            if (dedicated)
            {
                Status = "Dedicated server on UDP " + port;
                return true;
            }

            return Connect("127.0.0.1", port, playerName, out error);
        }

        public bool Join(string host, int port, string playerName, out string error)
        {
            Leave();
            PlayerName = playerName;
            CurrentMode = Mode.Joining;
            Status = "Connecting to " + host + ":" + port + "...";
            return Connect(host, port, playerName, out error);
        }

        bool Connect(string host, int port, string playerName, out string error)
        {
            _clientSocket = UdpTransport.CreateClient(host, port, out error);
            if (_clientSocket == null)
            {
                Status = error;
                Leave();
                return false;
            }

            _clientTransport = new ConditionedTransport(_clientSocket);
            _clientTransport.Conditions = Conditions;

            Client = new NetClient(_clientTransport, World);
            Client.Model = Model;
            Client.ResetWorld();
            // The host edits the authoritative tuning directly; a client gets it pushed over the wire.
            Client.Tuning = Server != null ? Server.Tuning : Tuning;
            Client.Sink = this;
            Client.InputSource = Input;
            Client.OnPredictedTick = OnPredictedTick;
            Client.Connect(Now(), playerName);

            Input.Tuning = Client.Tuning;
            _lastHealth = Tuning.match.maxHealth;
            return true;
        }

        public void Leave()
        {
            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }
            if (Server != null)
            {
                Server.Shutdown();
                Server = null;
            }
            if (_clientSocket != null) { _clientSocket.Dispose(); _clientSocket = null; }
            if (_serverSocket != null) { _serverSocket.Dispose(); _serverSocket = null; }
            if (Discovery != null) { Discovery.Dispose(); Discovery = null; }
            if (Mapper != null) { Mapper.Dispose(); Mapper = null; }

            _clientTransport = null;
            _serverTransport = null;
            CurrentMode = Mode.Offline;

            foreach (KeyValuePair<int, RemotePlayerView> kv in _remoteViews) kv.Value.Destroy();
            _remoteViews.Clear();
            if (_ownBody != null) { _ownBody.Destroy(); _ownBody = null; }
            Players.Clear();
            KillFeed.Clear();
        }

        public static double Now() { return Time.realtimeSinceStartupAsDouble; }

        // ================================================================== per frame
        public void Update(float dt)
        {
            double now = Now();

            if (Server != null)
            {
                Server.Update(now, dt);
                if (Discovery != null) Discovery.Broadcast(now, Server.ServerName, Port);
            }

            if (Client != null)
            {
                Input.PollFrame(dt, Client.Predicted);
                Client.Update(now, dt);

                if (Client.State == NetClient.Status.Rejected || Client.State == NetClient.Status.Disconnected)
                {
                    if (CurrentMode != Mode.Offline)
                    {
                        Status = "Disconnected: " + Client.LastDisconnectReason;
                        Leave();
                        return;
                    }
                }

                // The server decides the map; both ends must collide with the same geometry.
                if (Client.Connected && Client.Map != CurrentMap && OnMapRequested != null)
                    OnMapRequested(Client.Map);

                if (Client.Connected)
                {
                    Phase = Client.Phase;
                    TrackDamage();
                    if (Scenery != null) Scenery.Apply(Client.World, dt);
                    RenderRemotes(dt);
                    RenderLocal(dt);
                }
            }

            if (Fx != null) Fx.Update();

            HitMarkerTimer = Mathf.Max(0f, HitMarkerTimer - dt);
            ZoneTimer = Mathf.Max(0f, ZoneTimer - dt);
            SweepGrenades();

            if (HillMarker != null && PlayingHill)
                HillMarker.Render(ZoneHolder, Client.PeerId, ZoneTimer, Client.Tuning.koth.warnSeconds);
            LastTargetTimer = Mathf.Max(0f, LastTargetTimer - dt);
            DamageFlashTimer = Mathf.Max(0f, DamageFlashTimer - dt);
            RespawnCountdown = Mathf.Max(0f, RespawnCountdown - dt);

            for (int i = KillFeed.Count - 1; i >= 0; i--)
                if (Time.time - KillFeed[i].Time > 7f) KillFeed.RemoveAt(i);
        }

        void TrackDamage()
        {
            if (Client.Health < _lastHealth - 0.5f && Client.Alive)
            {
                DamageFlashTimer = 0.35f;
                Sound.Play2D(Audio.Hurt, 0.5f);
            }
            _lastHealth = Client.Health;
        }

        void RenderLocal(float dt)
        {
            PlayerSimState state = Client.Predicted;
            bool sprinting = Input.Enabled && Input.WantsSprint &&
                             state.Velocity.Flat.Magnitude > Tuning.move.walkSpeed * 1.02f;

            // Hand the wheel to the power ring while a variable optic is up, and hand it back the
            // moment it comes down. The limits come from the sight that is actually fitted, so a
            // tuning change to the optic is felt immediately and there is no second copy of them.
            SightTuning optic = Client.Tuning.Sight(state.Weapon.Sight);
            bool onScope = optic != null && optic.IsScope && state.Ads > 0.5f;
            Input.ScopeWheel = onScope && optic.IsVariable;
            if (optic != null && optic.IsScope)
            {
                Input.ScopeMin = Mathf.Max(1f, optic.magnification);
                Input.ScopeMax = Mathf.Max(Input.ScopeMin, optic.magnificationMax);
                Input.Magnification = optic.ClampMagnification(Input.Magnification);
            }
            View.Magnification = Input.Magnification;
            View.GrenadeTuning = Client.Tuning.grenade;

            // Your own capsules, if you asked for them. Rendered from the PREDICTED state, which is
            // what your own screen is showing - the server's copy of you is a few frames behind and
            // would draw the overlay lagging your body.
            PlayerSimState own = state;
            own.Position = Client.RenderPosition;
            RenderHitbox(Client.PeerId, in own, ShowHitboxes && Feel != null && Feel.showOwnHitbox >= 0.5f);

            View.Render(in state, Client.RenderPosition.ToUnity(), Client.Tuning.move,
                        Client.Tuning.Weapon(state.Weapon.Index), Client.Tuning.Sight(state.Weapon.Sight),
                        state.Yaw, state.Pitch, dt, sprinting);

            RenderOwnBody(in state, dt);

            PlayWeaponCue(View.ConsumeWeaponCue(), View.Camera.transform.position, 0.55f);
            PlayGrenadeCues(in state);
            TrackFootsteps(in state, Client.Tuning.move, dt);

            // Hands go where the object is, not where the gun is.
            int held = Client.World.FindPropHeldBy(Client.PeerId);
            HeldProp = held;
            View.SetGrabTarget(held >= 0 && Scenery != null ? Scenery.GrabPoint(held) : (Vector3?)null);
        }

        GrenadeCarry _lastCarry;

        /// <summary>
        /// The noises your own grenade makes in your own hands. Driven off the predicted carry state
        /// changing rather than off the sim events, because the events are consumed by the prediction
        /// loop and replayed on every correction - you would hear the pin come out four times.
        /// </summary>
        void PlayGrenadeCues(in PlayerSimState state)
        {
            if (state.Carry == _lastCarry) return;
            GrenadeCarry from = _lastCarry;
            _lastCarry = state.Carry;

            if (from == GrenadeCarry.Stowed && state.Carry == GrenadeCarry.Drawing)
                Sound.Play2D(Audio.GrenadeDraw, 0.75f);
            else if (from == GrenadeCarry.Drawing && state.Carry == GrenadeCarry.Held)
                Sound.Play2D(Audio.GrenadeSettle, 0.6f);
            else if (from == GrenadeCarry.Held && state.Carry == GrenadeCarry.Primed)
                Sound.Play2D(Audio.GrenadePin, 0.95f);
            else if (state.Carry == GrenadeCarry.Stowed && from == GrenadeCarry.Primed)
                Sound.Play2D(Audio.GrenadeThrow, 0.6f);
        }

        void PlayWeaponCue(WeaponAnimator.SoundCue cue, Vector3 position, float volume)
        {
            switch (cue)
            {
                case WeaponAnimator.SoundCue.MagOut: Sound.PlayAt(Audio.MagOut, position, volume); break;
                case WeaponAnimator.SoundCue.MagIn: Sound.PlayAt(Audio.MagIn, position, volume); break;
                case WeaponAnimator.SoundCue.Bolt: Sound.PlayAt(Audio.BoltRelease, position, volume); break;
            }
        }

        int _stepVariant;

        /// <summary>
        /// What is underfoot, from the map itself.
        ///
        /// This used to be "is there a roof over your head", which was a guess and sounded like one -
        /// boards under a porch, boards on a landing you were standing beside rather than on, boards
        /// anywhere the ray happened to find a ceiling. The map now says what its floors are made of
        /// (WorldModel.Panels), both machines build the same map from the same code, so the answer is
        /// exact and the same at both ends.
        /// </summary>
        StepSurface SurfaceUnder(Vector3 position)
        {
            if (Model == null) return StepSurface.Concrete;

            // A little way INTO the floor, not at the feet: standing on a board puts your feet at its
            // top face, and a point exactly on a boundary belongs to neither box.
            Vec3 probe = (position + Vector3.down * 0.04f).ToSim();
            switch (Model.SurfaceAt(probe))
            {
                case SurfaceKind.Wood: return StepSurface.Wood;
                case SurfaceKind.Metal: return StepSurface.Metal;
                default: return StepSurface.Concrete;
            }
        }

        /// <summary>
        /// Footsteps are driven by distance travelled rather than a timer, so the analog speed dial
        /// changes how loud and how often you are - creeping really is quieter.
        /// </summary>
        void TrackFootsteps(in PlayerSimState state, MovementTuning move, float dt)
        {
            if (!state.Grounded || state.Mantling) return;

            float speed = state.Velocity.Flat.Magnitude;
            if (speed < 0.35f) { _stepDistance = 0f; return; }

            _stepDistance += speed * dt;

            float stride = state.Stance == Stance.Prone ? 1.1f : (state.Stance == Stance.Crouch ? 1.5f : 2.0f);
            if (_stepDistance < stride) return;
            _stepDistance = 0f;

            float loudness = Mathf.Clamp01(speed / Mathf.Max(1f, move.sprintSpeed));
            if (state.Stance == Stance.Crouch) loudness *= 0.45f;
            else if (state.Stance == Stance.Prone) loudness *= 0.25f;

            Vector3 feet = Client.RenderPosition.ToUnity();
            Sound.PlayAt(Audio.StepFor(SurfaceUnder(feet), _stepVariant++), View.Camera.transform.position,
                0.25f + loudness * 0.5f, Random.Range(0.94f, 1.06f));
        }

        /// <summary>
        /// You have a body like everyone else: look down and your legs and chest are there, posed from
        /// the same replicated values an opponent would see, so what you feel and what they see agree.
        /// </summary>
        void RenderOwnBody(in PlayerSimState state, float dt)
        {
            if (_ownBody == null)
                _ownBody = new RemotePlayerView(Root, Client.PeerId, Palette, Client.Tuning.move, PlayerLayer, true);

            PlayerNetState shown = PlayerNetState.FromSim((byte)Client.PeerId, in state, Client.Alive, Client.Health);
            shown.Position = Client.RenderPosition;

            float footstep;
            _ownBody.SetVisible(true);
            _ownBody.Render(in shown, dt, Client.Tuning.Weapon(state.Weapon.Index), out footstep);
        }

        void RenderRemotes(float dt)
        {
            _scratchIds.Clear();
            foreach (KeyValuePair<int, NetClient.RemotePlayer> kv in Client.Remotes)
            {
                if (kv.Key == Client.PeerId) continue;
                _scratchIds.Add(kv.Key);

                RemotePlayerView view;
                if (!_remoteViews.TryGetValue(kv.Key, out view))
                {
                    view = new RemotePlayerView(Root, kv.Key, Palette, Client.Tuning.move, PlayerLayer,
                                                false, Pool != null ? Pool.VariantFor(kv.Key) : -1);
                    _remoteViews[kv.Key] = view;
                    ApplyAvatar(kv.Key, view);
                }

                if (!kv.Value.HasRender) { view.SetVisible(false); continue; }
                view.SetVisible(true);

                float footstep;
                view.Render(in kv.Value.Render, dt, Client.Tuning.Weapon(kv.Value.Render.WeaponIndex), out footstep);

                PlayerSimState remoteShown = kv.Value.Render.ToDisplayState(Client.Tuning.move.staminaMax);
                remoteShown.Position = kv.Value.Render.Position;
                remoteShown.Yaw = kv.Value.Render.Yaw;
                RenderHitbox(kv.Key, in remoteShown, ShowHitboxes && kv.Value.Alive);

                Vector3 where = kv.Value.Render.Position.ToUnity();
                if (footstep > 0.02f)
                {
                    float quiet = kv.Value.Render.Stance == Stance.Prone ? 0.25f
                                : (kv.Value.Render.Stance == Stance.Crouch ? 0.5f : 1f);
                    Sound.Play(Audio.StepFor(SurfaceUnder(where), _stepVariant++), where,
                        0.7f * footstep * quiet, Random.Range(0.92f, 1.08f), FootstepCarry);
                }
                PlayWeaponCue(view.ConsumeWeaponCue(), where, 0.7f);

                int remoteHeld = Client.World.FindPropHeldBy(kv.Key);
                view.SetGrabTarget(remoteHeld >= 0 && Scenery != null ? Scenery.GrabPoint(remoteHeld) : (Vector3?)null);
            }

            // Drop views for players who left.
            _scratchIds.Sort();
            List<int> stale = null;
            foreach (KeyValuePair<int, RemotePlayerView> kv in _remoteViews)
            {
                if (_scratchIds.BinarySearch(kv.Key) >= 0) continue;
                if (stale == null) stale = new List<int>();
                stale.Add(kv.Key);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++)
            {
                _remoteViews[stale[i]].Destroy();
                _remoteViews.Remove(stale[i]);

                HitboxView box;
                if (_hitboxes.TryGetValue(stale[i], out box)) { box.Destroy(); _hitboxes.Remove(stale[i]); }
            }
        }

        // ================================================================== local prediction effects
        void OnPredictedTick(InputCommand cmd, SimEvents ev)
        {
            if (ev.Landed && ev.LandImpact > 1.5f)
            {
                View.OnLanded(ev.LandImpact);
                Sound.PlayAt(Audio.Land, View.Camera.transform.position, Mathf.Clamp01(ev.LandImpact / 12f) * 0.7f);
            }
            if (ev.Jumped) Sound.PlayAt(Audio.Jump, View.Camera.transform.position, 0.35f);
            if (ev.StanceChanged) Sound.PlayAt(Audio.StanceChange, View.Camera.transform.position, 0.4f);
            if (ev.StartedSideStep) Sound.PlayAt(Audio.Lean, View.Camera.transform.position, 0.35f);
            if (ev.StartedSlide) Sound.PlayAt(Audio.Land, View.Camera.transform.position, 0.55f, 0.75f);
            if (ev.EndedSlide) Sound.PlayAt(Audio.StanceChange, View.Camera.transform.position, 0.3f);
            if (ev.StartedVault) Sound.PlayAt(Audio.Jump, View.Camera.transform.position, 0.5f, 1.15f);
            if (ev.MeleeSwing) Sound.PlayAt(Audio.MeleeSwing, View.Camera.transform.position, 0.6f);
            if (ev.GrabbedProp) Sound.Play2D(Audio.Grab, 0.5f);
            if (ev.ReleasedProp) Sound.Play2D(Audio.Grab, 0.35f, 0.8f);

            if (ev.MeleeStrike)
            {
                // Local feedback for hitting the world; players are confirmed by the server.
                Vector3 swingOrigin = Client.Predicted.EyePosition(Client.Tuning.move).ToUnity();
                Vector3 swingAim = Client.Predicted.LookDirection().ToUnity();
                RaycastHit hit;
                if (World.RaycastDetailed(swingOrigin, swingAim, Client.Tuning.move.meleeRange, out hit))
                {
                    Sound.PlayAt(Audio.MeleeHit, hit.point, 0.7f);
                    Fx.Impact(hit.point, hit.normal);
                }
            }
            if (ev.StartedMantle) Sound.PlayAt(Audio.Jump, View.Camera.transform.position, 0.5f, 0.8f);
            if (ev.Reloaded) Sound.Play2D(Audio.Reload, 0.6f);
            if (ev.DryFire) Sound.Play2D(Audio.DryFire, 0.5f);

            if (ev.ShotsFired <= 0) return;

            WeaponTuning weapon = Client.Tuning.Weapon(cmd.WeaponIndex);
            PlayerSimState state = Client.Predicted;
            float spread = MovementCore.CurrentSpread(in state, Client.Tuning.move, weapon, Client.Tuning.Sight(cmd.SightIndex));
            // Blind fire moves the muzzle, not the camera - the tracer has to come from the gun.
            Vec3 simOriginVec = state.WeaponOrigin(Client.Tuning.move);
            Vec3 simAimVec = state.WeaponDirection(Client.Tuning.move);
            Vector3 origin = simOriginVec.ToUnity();
            Vector3 aim = simAimVec.ToUnity();

            for (int shot = 0; shot < ev.ShotsFired; shot++)
            {
                uint shotIndex = ev.FirstShotIndex + (uint)shot;
                Input.ApplyRecoil(Client.PeerId, shotIndex, weapon);
                View.OnShot(weapon);

                Vector3 muzzle = View.MuzzleWorldPoint(origin, aim);
                Fx.MuzzleFlash(muzzle, aim);
                Sound.PlayAt(Audio.ShotFor(cmd.WeaponIndex), origin, 0.75f, Random.Range(0.96f, 1.05f));

                int pellets = weapon.PelletsInt;
                for (int pellet = 0; pellet < pellets; pellet++)
                {
                    // Same seed the server will use, so the tracer is the shot, not a decoration.
                    Vector3 dir = ShotSolver.PelletDirection(simAimVec, spread, Client.PeerId, shotIndex, pellet).ToUnity();
                    Vector3 impact;
                    bool hitPlayer;
                    ResolveVisualImpact(origin, dir, weapon.range, out impact, out hitPlayer);
                    Fx.Tracer(muzzle, impact);
                    if (!hitPlayer) Fx.Impact(impact, -dir);
                }
            }
        }

        /// <summary>Purely cosmetic trace so the tracer ends where you would expect. The server decides damage.</summary>
        void ResolveVisualImpact(Vector3 origin, Vector3 direction, float range, out Vector3 point, out bool hitPlayer)
        {
            hitPlayer = false;
            RaycastHit hit;
            float best = range;
            point = origin + direction * range;
            if (World.RaycastDetailed(origin, direction, range, out hit))
            {
                best = hit.distance;
                point = hit.point;
            }

            Vec3 simOrigin = origin.ToSim();
            Vec3 simDir = direction.ToSim();
            foreach (KeyValuePair<int, NetClient.RemotePlayer> kv in Client.Remotes)
            {
                if (kv.Key == Client.PeerId || !kv.Value.HasRender || !kv.Value.Alive) continue;
                PlayerSimState shown = kv.Value.Render.ToDisplayState(Client.Tuning.move.staminaMax);
                PlayerHitbox box = PlayerHitbox.FromState(in shown, Client.Tuning.move, Client.Tuning.Weapon(shown.Weapon.Index));
                HitTestResult result;
                if (!RayGeometry.TestPlayer(simOrigin, simDir, in box, best, out result)) continue;
                best = result.Distance;
                point = result.Point.ToUnity();
                hitPlayer = true;
            }
        }

        // ================================================================== IEventSink
        public PlayerInfo Info(int peerId)
        {
            PlayerInfo info;
            if (Players.TryGetValue(peerId, out info)) return info;
            info = new PlayerInfo();
            info.PeerId = peerId;
            info.Name = "duellist " + peerId;
            Players[peerId] = info;
            return info;
        }

        public void OnPlayerJoined(int peerId, string name)
        {
            Info(peerId).Name = name;
        }

        public void OnPlayerLeft(int peerId, DisconnectReason reason)
        {
            Players.Remove(peerId);
            RemotePlayerView view;
            if (!_remoteViews.TryGetValue(peerId, out view)) return;
            view.Destroy();
            _remoteViews.Remove(peerId);
        }

        public void OnSpawn(int peerId, Vec3 position, float yaw)
        {
            if (Client == null || peerId != Client.PeerId) return;
            Client.ForceSpawn(position, yaw);
            Input.ResetView(yaw, 0f);
            if (View != null && View.Blur != null) View.Blur.Clear();
            _lastHealth = Client.Tuning.match.maxHealth;
            RespawnCountdown = 0f;
        }

        public void OnDeath(int victim, int killer, HitZone zone, float distance)
        {
            KillFeedEntry entry;
            entry.Killer = killer >= 0 ? Info(killer).Name : "the world";
            entry.Victim = Info(victim).Name;
            entry.KillerId = killer;
            entry.VictimId = victim;
            entry.Zone = zone;
            entry.Time = Time.time;
            KillFeed.Add(entry);

            if (Client == null) return;
            if (victim == Client.PeerId)
            {
                RespawnCountdown = Client.Tuning.match.respawnDelay;
                Sound.Play2D(Audio.Death, 0.7f);
            }
            else if (killer == Client.PeerId)
            {
                Sound.Play2D(Audio.HeadshotMarker, 0.5f, 1.2f);
            }
        }

        public void OnHitConfirm(int target, HitZone zone, float damage, bool killed, Vec3 point)
        {
            HitMarkerTimer = 0.28f;
            HitMarkerHeadshot = zone == HitZone.Head;
            Sound.Play2D(zone == HitZone.Head ? Audio.HeadshotMarker : Audio.HitMarker, 0.45f);

            // The shooter is not sent their own Shot event, so their blood comes from here instead.
            // Either way it is drawn on the server's word and never on the trigger pull.
            if (Fx == null || View == null) return;
            Vector3 at = point.ToUnity();
            Vector3 along = (at - View.Camera.transform.position).normalized;
            Fx.Blood(at, along, Mathf.Clamp01(damage / 45f));
        }

        /// <summary>
        /// You took one. A head hit you walked away from leaves you unable to see properly for a
        /// moment, and how long for depends on what hit you: a rifle round rings a helmet far harder
        /// than a pistol round does, and both are survivable at full health.
        /// </summary>
        public void OnDamaged(int attacker, HitZone zone, byte weaponIndex, float damage, bool killed)
        {
            if (killed || View == null || View.Blur == null) return;
            if (zone != HitZone.Head) return;

            WeaponTuning weapon = Client.Tuning.Weapon(weaponIndex);
            View.Blur.Hit(weapon.concussionStrength, weapon.concussionTime);
        }

        /// <summary>
        /// A range target. Same marker and the same ding as a player hit, because the point of a range
        /// is to tell you what your rounds are doing.
        /// </summary>
        public void OnTargetHit(HitZone zone, float distance)
        {
            HitMarkerTimer = 0.28f;
            HitMarkerHeadshot = zone == HitZone.Head;
            LastTargetDistance = distance;
            LastTargetTimer = 1.6f;
            Sound.Play2D(zone == HitZone.Head ? Audio.HeadshotMarker : Audio.HitMarker, 0.4f);
        }

        public float LastTargetDistance;
        public float LastTargetTimer;

        public void OnScore(int peerId, int kills, int deaths)
        {
            PlayerInfo info = Info(peerId);
            info.Kills = kills;
            info.Deaths = deaths;
        }

        /// <summary>
        /// A live grenade, as the server last saw it. Drawn from a small pool keyed by id, and the
        /// bounce counter is what makes the noise: the server does not send an event per bounce, it
        /// counts them, and a count that has gone up since last time is a bounce we have not heard.
        /// </summary>
        sealed class LiveGrenade
        {
            public GameObject Body;
            public int Bounces;
            public float LastSeen;
        }

        readonly System.Collections.Generic.Dictionary<int, LiveGrenade> _grenades =
            new System.Collections.Generic.Dictionary<int, LiveGrenade>();

        public void OnGrenade(int id, int owner, Vec3 position, float fuse, int bounces, SurfaceKind surface)
        {
            Vector3 at = position.ToUnity();

            LiveGrenade live;
            if (!_grenades.TryGetValue(id, out live))
            {
                live = new LiveGrenade();
                live.Body = WeaponModels.BuildGrenade(Root, Palette, PlayerLayer);
                live.Bounces = bounces;
                _grenades[id] = live;
            }

            live.Body.transform.position = at;
            // Tumbling. It is not replicated - a grenade's spin changes nothing - so it is spun from
            // how far it has travelled, which at least means it stops turning when it stops moving.
            live.Body.transform.rotation = Quaternion.Euler(at.z * 220f, at.x * 190f, at.y * 260f);
            live.LastSeen = Time.time;

            if (bounces != live.Bounces)
            {
                live.Bounces = bounces;
                Sound.Play(Audio.BounceFor(surface), at, 0.75f, Random.Range(0.92f, 1.09f), 45f);
            }
        }

        public void OnBlast(Vec3 position)
        {
            Vector3 at = position.ToUnity();
            float distance = Vector3.Distance(at, View.Camera.transform.position);

            Fx.Explosion(at, Client.Tuning.grenade.lethalRadius);
            Sound.Play(distance > 40f ? Audio.ExplosionDistant : Audio.Explosion, at, 1.6f,
                       Random.Range(0.95f, 1.05f), 320f);

            // Anything close enough to hurt is close enough to ring your ears.
            if (View != null && View.Blur != null && distance < Client.Tuning.grenade.radius * 1.4f)
            {
                float k = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, Client.Tuning.grenade.radius * 1.4f));
                View.Blur.Hit(0.75f * k, 1.4f * k);
            }
        }

        /// <summary>Grenades the server has stopped mentioning have gone off or been cleaned up.</summary>
        void SweepGrenades()
        {
            if (_grenades.Count == 0) return;
            _staleGrenades.Clear();
            foreach (System.Collections.Generic.KeyValuePair<int, LiveGrenade> kv in _grenades)
                if (Time.time - kv.Value.LastSeen > 0.5f) _staleGrenades.Add(kv.Key);

            for (int i = 0; i < _staleGrenades.Count; i++)
            {
                LiveGrenade live = _grenades[_staleGrenades[i]];
                if (live.Body != null) Object.Destroy(live.Body);
                _grenades.Remove(_staleGrenades[i]);
            }
        }

        readonly System.Collections.Generic.List<int> _staleGrenades = new System.Collections.Generic.List<int>();

        /// <summary>The marker standing in the live room. Built on demand and moved when the hill does.</summary>
        public ZoneView HillMarker;

        /// <summary>
        /// The capsule overlay, one per player. Built the first time it is switched on and then kept -
        /// it is fifteen primitives a body and nobody toggles it every frame.
        /// </summary>
        readonly System.Collections.Generic.Dictionary<int, HitboxView> _hitboxes =
            new System.Collections.Generic.Dictionary<int, HitboxView>();

        public bool ShowHitboxes;

        /// <summary>
        /// Avatars. One library for the whole match: a character is downloaded and parsed once and
        /// every player who is wearing it gets a copy of the same loaded model.
        /// </summary>
        public AvatarLibrary Avatars;

        /// <summary>
        /// What the local player chose, or null for "deal them out". Set from the character panel.
        /// </summary>
        public string AvatarSource
        {
            get { return Pool != null ? Pool.Chosen : null; }
            set { if (Pool != null) Pool.Chosen = value; }
        }

        /// <summary>
        /// Who wears what. When nothing has been chosen deliberately every player and bot is dealt a
        /// character by their peer id - and because the deal is a hash of the id rather than a random
        /// number, both machines land on the same answer without replicating anything.
        /// </summary>
        public AvatarPool Pool;

        void ApplyAvatar(int peerId, RemotePlayerView view)
        {
            if (Avatars == null || Pool == null) { view.SetAvatar(null); return; }

            string source = Pool.SourceFor(peerId);
            if (string.IsNullOrEmpty(source)) { view.SetAvatar(null); return; }

            AvatarLibrary.Entry entry = Avatars.Get(source);
            if (entry != null && entry.Template != null)
            {
                view.SetAvatar(Avatars.Instantiate(entry, Root, PlayerLayer));
                return;
            }

            // Not loaded yet. The blockout stands in until it arrives, and only the players this
            // particular avatar belongs to get it when it does - everyone else keeps their own.
            //
            // `arrived` is not paranoia: a character already on the disk loads without going round
            // the frame loop, so this callback can run before Load returns. Clearing the avatar
            // afterwards unconditionally would then throw away the one that had just been put on,
            // and every duellist in the game would be a blockout mannequin again.
            bool arrived = false;
            Avatars.Load(source, delegate(AvatarLibrary.Entry loaded)
            {
                if (loaded.Error != null) return;
                arrived = true;
                foreach (System.Collections.Generic.KeyValuePair<int, RemotePlayerView> kv in _remoteViews)
                {
                    if (Pool.SourceFor(kv.Key) != loaded.Source) continue;
                    kv.Value.SetAvatar(Avatars.Instantiate(loaded, Root, PlayerLayer));
                }
            });

            if (!arrived) view.SetAvatar(null);
        }

        /// <summary>Puts the current choice on everyone. Called when the player picks a new one.</summary>
        public void RefreshAvatars()
        {
            foreach (System.Collections.Generic.KeyValuePair<int, RemotePlayerView> kv in _remoteViews)
                ApplyAvatar(kv.Key, kv.Value);
        }

        HitboxView HitboxFor(int peerId)
        {
            HitboxView view;
            if (_hitboxes.TryGetValue(peerId, out view)) return view;
            view = new HitboxView(Root, FxLayer);
            _hitboxes[peerId] = view;
            return view;
        }

        void RenderHitbox(int peerId, in PlayerSimState state, bool wanted)
        {
            if (!wanted)
            {
                HitboxView existing;
                if (_hitboxes.TryGetValue(peerId, out existing)) existing.SetVisible(false);
                return;
            }

            HitboxView view = HitboxFor(peerId);
            view.SetVisible(true);
            view.Render(in state, Client.Tuning.move, Client.Tuning.Weapon(state.Weapon.Index));
        }

        public void OnZone(int zone, float secondsLeft, int holder)
        {
            bool moved = zone != ActiveZone;
            if (moved && ActiveZone >= 0)
                Sound.Play2D(Audio.RoundStart, 0.5f, 1.35f);       // the hill moved; you need to know

            ActiveZone = zone;
            ZoneTimer = secondsLeft;
            ZoneHolder = holder;

            if (HillMarker == null && Zones.Count > 0)
                HillMarker = new ZoneView(Root, Palette, FxLayer);
            if (HillMarker == null) return;

            if (moved && zone >= 0 && zone < Zones.Count)
            {
                ZoneDef def = Zones[zone];
                HillMarker.SetZone(in def);
            }
            HillMarker.SetVisible(zone >= 0 && zone < Zones.Count);
        }

        public void OnMatchPhase(MatchPhase phase, float timer, int winner)
        {
            Phase = phase;
            PhaseTimer = timer;
            Winner = winner;
            if (phase == MatchPhase.Live) Sound.Play2D(Audio.RoundStart, 0.5f);
        }

        public void OnTuning(string tuningText)
        {
            // Only a client applies pushed tuning; the host is the one that produced it.
            if (Server != null) return;
            TuningSerializer.FromText(Tuning, tuningText);
        }

        /// <summary>
        /// The break itself arrives as replicated state, and WorldView plays the shatter when the bit
        /// flips - so this only needs to exist for anything that cares about the moment.
        /// </summary>
        public void OnWindowBroken(int windowIndex, Vec3 centre) { }

        public void OnRemoteShot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit,
                                 Vec3 hitPoint, bool hitPlayer)
        {
            Vector3 from = origin.ToUnity();

            // Fire the flash from the opponent's actual muzzle so it reads as their gun going off.
            RemotePlayerView view;
            if (_remoteViews.TryGetValue(shooter, out view))
            {
                view.OnShot();
                from = view.MuzzlePosition();
            }

            Vector3 to = hit ? hitPoint.ToUnity() : from + direction.ToUnity() * 60f;
            Fx.MuzzleFlash(from, direction.ToUnity());
            Fx.Tracer(from, to, 0.07f);

            // Far enough away and you hear the report rather than the crack.
            float distance = Vector3.Distance(from, View.Camera.transform.position);
            AudioClip report = distance > 35f ? Audio.DistantShotFor(weaponIndex) : Audio.ShotFor(weaponIndex);
            Sound.Play(report, from, 0.85f, Random.Range(0.95f, 1.05f),
                       Client.Tuning.Weapon(weaponIndex).soundCarry);
            if (!hit) return;

            // The server said this round landed on someone, so everyone watching draws the blood -
            // it is the same event that told them the round landed at all.
            if (hitPlayer) Fx.Blood(to, direction.ToUnity(), 0.6f);
            else Fx.Impact(to, -direction.ToUnity());
        }
    }
}
