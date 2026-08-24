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

        /// <summary>
        /// One set of conditions per machine, applied to everything it sends - server and client alike.
        /// Two ends each running 75ms gives the 150ms round trip you would actually feel.
        /// </summary>
        public NetConditions Conditions = new NetConditions();

        /// <summary>Map the host will run. The server sends it to clients, who rebuild to match.</summary>
        public MapId HostMap = MapId.DuelArena;
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

            View.Render(in state, Client.RenderPosition.ToUnity(), Client.Tuning.move,
                        Client.Tuning.Weapon(state.Weapon.Index), Client.Tuning.Sight(state.Weapon.Sight),
                        state.Yaw, state.Pitch, dt, sprinting);

            RenderOwnBody(in state, dt);

            PlayWeaponCue(View.ConsumeWeaponCue(), View.Camera.transform.position, 0.55f);
            TrackFootsteps(in state, Client.Tuning.move, dt);

            // Hands go where the object is, not where the gun is.
            int held = Client.World.FindPropHeldBy(Client.PeerId);
            HeldProp = held;
            View.SetGrabTarget(held >= 0 && Scenery != null ? Scenery.GrabPoint(held) : (Vector3?)null);
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

            Sound.PlayAt(Audio.Footstep, View.Camera.transform.position, 0.25f + loudness * 0.5f,
                Random.Range(0.9f, 1.1f));
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
                    view = new RemotePlayerView(Root, kv.Key, Palette, Client.Tuning.move, PlayerLayer);
                    _remoteViews[kv.Key] = view;
                }

                if (!kv.Value.HasRender) { view.SetVisible(false); continue; }
                view.SetVisible(true);

                float footstep;
                view.Render(in kv.Value.Render, dt, Client.Tuning.Weapon(kv.Value.Render.WeaponIndex), out footstep);

                Vector3 where = kv.Value.Render.Position.ToUnity();
                if (footstep > 0.02f)
                {
                    float quiet = kv.Value.Render.Stance == Stance.Prone ? 0.25f
                                : (kv.Value.Render.Stance == Stance.Crouch ? 0.5f : 1f);
                    Sound.PlayOccluded(Audio.Footstep, where, 0.7f * footstep * quiet, Random.Range(0.92f, 1.08f), true);
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
                PlayerHitbox box = PlayerHitbox.FromState(in shown, Client.Tuning.move);
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
            _lastHealth = Client.Tuning.match.maxHealth;
            RespawnCountdown = 0f;
        }

        public void OnDeath(int victim, int killer, HitZone zone, float distance)
        {
            KillFeedEntry entry;
            entry.Killer = killer >= 0 ? Info(killer).Name : "the world";
            entry.Victim = Info(victim).Name;
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

        public void OnHitConfirm(int target, HitZone zone, float damage, bool killed)
        {
            HitMarkerTimer = 0.28f;
            HitMarkerHeadshot = zone == HitZone.Head;
            Sound.Play2D(zone == HitZone.Head ? Audio.HeadshotMarker : Audio.HitMarker, 0.45f);
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

        public void OnRemoteShot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit, Vec3 hitPoint)
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
            Sound.PlayOccluded(report, from, 0.85f, Random.Range(0.95f, 1.05f), true);
            if (hit) Fx.Impact(to, -direction.ToUnity());
        }
    }
}
