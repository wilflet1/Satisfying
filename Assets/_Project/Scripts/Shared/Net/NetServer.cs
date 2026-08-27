using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>
    /// Authoritative server. Owns the simulation, the match, and the truth about who shot whom.
    /// Clients only ever send intent; everything here validates before it believes.
    /// Runs identically inside the Unity host and inside the headless tests.
    /// </summary>
    public sealed class NetServer
    {
        public sealed class ServerPlayer
        {
            public int PeerId;
            public string Name = "duellist";
            public bool Active;
            public double LastPacketTime;

            public PlayerSimState Sim;
            public InputCommand LastInput;
            public readonly Dictionary<uint, InputCommand> Pending = new Dictionary<uint, InputCommand>();
            public uint NextClientTick;
            public bool InputStarted;       // false until we have adopted this client's tick numbering
            public uint LastExecutedTick;
            public int StarvedTicks;
            public int BufferHealth;
            public float RenderTick;

            public float Health = 100f;
            public bool Alive;
            public float RespawnTimer;
            public float SpawnProtection;
            public int Kills;
            public int Deaths;

            public float Rtt = 0.05f;
            public uint LastClientTimeMs;

            public readonly ReliableChannel Reliable = new ReliableChannel();
            /// <summary>Non null for a training bot: it produces its own input instead of receiving it.</summary>
            public BotBrain Bot;

            public readonly PlayerSimState[] History = new PlayerSimState[Protocol.HistoryTicks];
            public readonly uint[] HistoryTick = new uint[Protocol.HistoryTicks];
            public readonly bool[] HistoryAlive = new bool[Protocol.HistoryTicks];
        }

        readonly ITransport _transport;
        readonly ICollisionWorld _world;
        readonly SpawnSet _spawns;
        readonly NetBuffer _write = new NetBuffer(Protocol.MaxPacketSize);
        readonly NetBuffer _read = new NetBuffer(Protocol.MaxPacketSize);
        readonly List<ServerPlayer> _players = new List<ServerPlayer>();
        readonly List<Vec3> _avoid = new List<Vec3>();
        readonly List<byte[]> _scratch = new List<byte[]>();

        public GameTuning Tuning;
        public uint Tick;
        public MatchPhase Phase = MatchPhase.Warmup;
        public float PhaseTimer;
        public int Winner = -1;
        public int SnapshotEveryTicks = 1;
        /// <summary>Rewind targets to what the shooter actually saw. Turn off to feel why it exists.</summary>
        public bool LagCompensation = true;
        public string ServerName = "duel";
        public MapId Map = MapId.DuelArena;

        /// <summary>What is being played. Set before Host and sent in the accept, like the map.</summary>
        public GameMode Mode = GameMode.Duel;

        /// <summary>
        /// The rooms worth standing in, filled by whoever built the map. Empty means there is no hill
        /// to fight over, and the server falls back to a duel however it was configured - a mode that
        /// cannot be played is worse than one that was not asked for.
        /// </summary>
        public readonly System.Collections.Generic.List<ZoneDef> Zones = new System.Collections.Generic.List<ZoneDef>();

        public KothState Hill;

        readonly GrenadeState[] _grenades = new GrenadeState[GrenadeSim.MaxLive];
        byte _nextGrenadeId;
        float _grenadeAnnounce;
        float _zoneAnnounce;
        readonly float[] _zonePoints = new float[Protocol.MaxPeerId + 1];

        int[] _propDirty;
        double _accumulator;
        double _now;
        int _spawnCounter;

        public readonly WorldModel Model;
        public readonly WorldState World = new WorldState();

        public NetServer(ITransport transport, ICollisionWorld world, SpawnSet spawns, GameTuning tuning,
                         WorldModel model = null)
        {
            _transport = transport;
            _world = world;
            _spawns = spawns;
            Tuning = tuning;
            Model = model ?? new WorldModel();
            World.Reset(Model);
            _propDirty = new int[World.Props.Length];
        }

        public IReadOnlyList<ServerPlayer> Players { get { return _players; } }
        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _players.Count; i++) if (_players[i].Active) n++;
                return n;
            }
        }

        public ServerPlayer Find(int peerId)
        {
            for (int i = 0; i < _players.Count; i++) if (_players[i].PeerId == peerId) return _players[i];
            return null;
        }

        // ================================================================== loop
        public void Update(double now, float dt)
        {
            _now = now;
            _transport.Update(now);
            Receive();

            _accumulator += dt;
            int guard = 0;
            while (_accumulator >= Protocol.TickDt && guard++ < 8)
            {
                _accumulator -= Protocol.TickDt;
                StepTick();
            }

            CheckTimeouts();
        }

        void StepTick()
        {
            Tick++;

            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active) continue;

                InputCommand cmd = p.Bot != null ? ThinkForBot(p) : DequeueInput(p);
                p.SpawnProtection = MathK.Max(0f, p.SpawnProtection - Protocol.TickDt);

                if (!p.Alive)
                {
                    p.RespawnTimer -= Protocol.TickDt;
                    if (p.RespawnTimer <= 0f && Phase != MatchPhase.Ended) Respawn(p);
                    RecordHistory(p);
                    continue;
                }

                SimEvents ev = new SimEvents();
                WeaponTuning weapon = Tuning.Weapon(cmd.WeaponIndex);
                SightTuning sight = Tuning.Sight(cmd.SightIndex);
                MovementCore.Step(ref p.Sim, cmd, Tuning.move, weapon, sight, Tuning.grenade,
                                  Protocol.TickDt, _world, ref ev);

                if (ev.GrenadeReleased)
                {
                    Vec3 from, velocity;
                    GrenadeSim.Throw(in p.Sim, Tuning.move, Tuning.grenade, ev.GrenadeHard, out from, out velocity);
                    SpawnGrenade(p, from, velocity);
                }

                // Always resolve the shot, live or not. Only damage waits for the match: a round still
                // travels, still takes out a pane and still cracks off a wall while you are warming up,
                // and being alone in the arena is exactly when you want to be able to test that.
                if (ev.ShotsFired > 0) ResolveShots(p, cmd, weapon, ev);

                if (ev.MeleeStrike) ResolveMelee(p, cmd);

                Vec3 beforeDrag = Vec3.Zero;
                int heldBefore = World.FindPropHeldBy(p.PeerId);
                if (heldBefore >= 0) beforeDrag = World.Props[heldBefore].Position;

                PropSim.Step(p.PeerId, ref p.Sim, cmd, Tuning.move, Model, World, _world, Protocol.TickDt, ref ev);

                int heldAfter = World.FindPropHeldBy(p.PeerId);
                if (heldAfter >= 0) MarkPropDirty(heldAfter, Vec3.Distance(World.Props[heldAfter].Position, beforeDrag) > 0.001f);
                if (ev.GrabbedProp || ev.ReleasedProp)
                {
                    if (heldBefore >= 0) MarkPropDirty(heldBefore, true);
                    if (heldAfter >= 0) MarkPropDirty(heldAfter, true);
                }

                RecordHistory(p);
            }

            for (int i = 0; i < _propDirty.Length; i++)
                if (_propDirty[i] > 0) _propDirty[i]--;

            StepMatch();

            if (Tick % (uint)MathK.Max(1, SnapshotEveryTicks) == 0) SendSnapshots();
        }

        // ================================================================== input buffering
        InputCommand DequeueInput(ServerPlayer p)
        {
            InputCommand cmd;
            if (p.Pending.TryGetValue(p.NextClientTick, out cmd))
            {
                p.Pending.Remove(p.NextClientTick);
                p.LastExecutedTick = p.NextClientTick;
                p.NextClientTick++;
                p.LastInput = cmd;
                p.StarvedTicks = 0;
            }
            else
            {
                // Nothing arrived for this tick: hold the last intent rather than stalling the player.
                p.StarvedTicks++;
                cmd = p.LastInput.Repeat(p.NextClientTick);
                p.LastInput = cmd;

                if (p.StarvedTicks > 3 && p.Pending.Count > 0)
                {
                    uint earliest = uint.MaxValue;
                    foreach (uint k in p.Pending.Keys) if (k < earliest) earliest = k;
                    p.NextClientTick = earliest;
                    p.StarvedTicks = 0;
                }
            }

            p.RenderTick = cmd.RenderTick;
            p.BufferHealth = p.Pending.Count;
            return cmd;
        }

        InputCommand ThinkForBot(ServerPlayer bot)
        {
            ServerPlayer target = null;
            float best = float.MaxValue;
            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer other = _players[i];
                if (other == bot || !other.Active || !other.Alive) continue;
                float distance = Vec3.Distance(other.Sim.Position, bot.Sim.Position);
                if (distance >= best) continue;
                best = distance;
                target = other;
            }

            Vec3 targetEye = target != null ? target.Sim.EyePosition(Tuning.move) : Vec3.Zero;
            InputCommand cmd = bot.Bot.Think(Tick, in bot.Sim, Tuning.move, _world,
                target != null, targetEye, _spawns, Protocol.TickDt);
            cmd.RenderTick = Tick;          // a bot has no latency to compensate for
            bot.LastInput = cmd;
            bot.RenderTick = Tick;
            return cmd;
        }

        /// <summary>Adds a practice opponent. It is a real player to the rest of the server.</summary>
        /// <summary>
        /// A bot's whole personality - weapon, wander, when it shoots - comes out of this seed, and by
        /// default the seed comes from its peer id and the tick it joined on. That makes any test that
        /// asserts what a bot did hostage to unrelated changes, so a test can pin the seed instead.
        /// </summary>
        public ServerPlayer AddBot(string name, float skill = 0.55f, int seed = 0)
        {
            int peerId = -1;
            for (int candidate = Protocol.MaxPlayers - 1; candidate >= 1; candidate--)
            {
                if (Find(candidate) != null) continue;
                peerId = candidate;
                break;
            }
            if (peerId < 0) return null;

            ServerPlayer bot = new ServerPlayer();
            bot.PeerId = peerId;
            bot.Name = name;
            bot.Active = true;
            bot.LastPacketTime = _now;
            bot.Bot = new BotBrain(seed != 0 ? seed : peerId * 7919 + (int)Tick);
            bot.Bot.Skill = MathK.Clamp01(skill);
            bot.LastInput = InputCommand.Default(0);
            _players.Add(bot);

            Broadcast(GameEvents.PlayerJoined(peerId, name));
            Broadcast(GameEvents.Score(peerId, 0, 0));
            Respawn(bot);
            return bot;
        }

        public int BotCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _players.Count; i++) if (_players[i].Active && _players[i].Bot != null) n++;
                return n;
            }
        }

        public void RemoveBots()
        {
            for (int i = _players.Count - 1; i >= 0; i--)
            {
                ServerPlayer p = _players[i];
                if (p.Bot == null) continue;
                p.Active = false;
                p.Alive = false;
                BroadcastExcept(p.PeerId, GameEvents.PlayerLeft(p.PeerId, DisconnectReason.ClosedByUser));
                _players.RemoveAt(i);
            }
        }

        // ================================================================== shooting
        void ResolveShots(ServerPlayer shooter, InputCommand cmd, WeaponTuning weapon, SimEvents ev)
        {
            float spread = MovementCore.CurrentSpread(in shooter.Sim, Tuning.move, weapon, Tuning.Sight(cmd.SightIndex));
            // Blind fire lifts the muzzle over cover and swings it round the corner; the head stays put.
            Vec3 origin = shooter.Sim.WeaponOrigin(Tuning.move);
            Vec3 aim = shooter.Sim.WeaponDirection(Tuning.move);

            for (int shot = 0; shot < ev.ShotsFired; shot++)
            {
                uint shotIndex = ev.FirstShotIndex + (uint)shot;
                int pellets = weapon.PelletsInt;
                bool anyImpact = false;
                bool hitPlayer = false;
                Vec3 firstImpact = origin + aim * weapon.range;

                for (int pellet = 0; pellet < pellets; pellet++)
                {
                    Vec3 dir = ShotSolver.PelletDirection(aim, spread, shooter.PeerId, shotIndex, pellet);

                    // A round is traced in segments rather than in one go, because it does not
                    // necessarily stop at the first thing it touches. Each time it comes to a wall
                    // that the map has marked penetrable and its budget can pay for, it comes out the
                    // far side with less damage and carries on from there.
                    Vec3 segment = origin;
                    float remaining = weapon.range;
                    float travelled = 0f;
                    float damageScale = 1f;
                    float budget = MathK.Max(0f, weapon.penetration);
                    int layers = 0;
                    int maxLayers = Tuning.penetration.MaxLayersInt;
                    bool reported = false;

                    while (remaining > 0.05f)
                    {
                        float wallDist;
                        Vec3 wallNormal;
                        bool hitWorld = _world.Raycast(segment, dir, remaining, out wallDist, out wallNormal);
                        float limit = hitWorld ? wallDist : remaining;

                        // A round through a pane takes it out, and keeps going.
                        int glassIndex;
                        float glassDistance;
                        if (World.RaycastWindows(Model, segment, dir, limit, out glassIndex, out glassDistance))
                            BreakWindow(glassIndex);

                        // Practice targets are ordinary geometry, so a hit on one comes back at exactly
                        // the distance the wall cast already found. Only the first pellet reports, or a
                        // shotgun would ring the marker once per pellet.
                        int targetIndex;
                        bool targetHead;
                        float targetDistance;
                        if (pellet == 0 && !reported &&
                            Model.RaycastTargets(segment, dir, limit + 0.05f, out targetIndex, out targetHead, out targetDistance))
                        {
                            shooter.Reliable.Queue(GameEvents.TargetHit(targetHead ? HitZone.Head : HitZone.Chest,
                                                                       travelled + targetDistance));
                            reported = true;
                        }

                        ServerPlayer victim = null;
                        HitTestResult best = new HitTestResult();
                        best.Distance = limit;

                        for (int i = 0; i < _players.Count && Phase == MatchPhase.Live; i++)
                        {
                            ServerPlayer target = _players[i];
                            if (target == shooter || !target.Active || !target.Alive) continue;
                            if (target.SpawnProtection > 0f) continue;

                            PlayerSimState rewound = target.Sim;
                            if (LagCompensation && !RewindPlayer(target, cmd.RenderTick, out rewound)) continue;

                            PlayerHitbox box = PlayerHitbox.FromState(in rewound, Tuning.move, Tuning.Weapon(rewound.Weapon.Index));
                            HitTestResult hit;
                            if (!RayGeometry.TestPlayer(segment, dir, in box, best.Distance, out hit)) continue;

                            best = hit;
                            victim = target;
                        }

                        Vec3 impact = victim != null
                            ? best.Point
                            : (hitWorld ? segment + dir * wallDist : segment + dir * remaining);

                        if (pellet == 0 && layers == 0)
                        {
                            firstImpact = impact;
                            anyImpact = victim != null || hitWorld;
                        }
                        if (victim != null) hitPlayer = true;

                        if (victim != null)
                        {
                            // Range for the falloff is how far the round has actually flown, not how
                            // far it is from the last wall it came through.
                            float flown = travelled + best.Distance;
                            float damage = ShotSolver.Damage(weapon, best.Zone, flown) * damageScale;
                            ApplyDamage(victim, shooter, damage, best.Zone, flown, best.Point);

                            // A LIMB IS NOT ARMOUR. A round that goes through an arm carries on into
                            // whatever was behind it, which is usually the chest - so an opponent who
                            // happens to have a forearm across their sternum does not get to take a
                            // rifle round for 22 damage. The limb still takes its own hit, above; this
                            // is what is left of the round afterwards.
                            if (best.Zone != HitZone.Arm && best.Zone != HitZone.Leg) break;
                            if (layers >= maxLayers) break;

                            PlayerHitbox limbBox = PlayerHitbox.FromState(in victim.Sim, Tuning.move,
                                Tuning.Weapon(victim.Sim.Weapon.Index));
                            if (LagCompensation)
                            {
                                PlayerSimState rewoundVictim;
                                if (RewindPlayer(victim, cmd.RenderTick, out rewoundVictim))
                                    limbBox = PlayerHitbox.FromState(in rewoundVictim, Tuning.move,
                                        Tuning.Weapon(rewoundVictim.Weapon.Index));
                            }

                            Vec3 la, lb;
                            float lr;
                            HitZone lzone;
                            limbBox.Segment(best.Segment, out la, out lb, out lr, out lzone);
                            float limbExit = RayGeometry.CapsuleExit(segment, dir, la, lb, lr, best.Distance);
                            float crossed = MathK.Max(0.01f, limbExit - best.Distance);

                            float limbCost = crossed * Tuning.penetration.flesh;
                            if (limbCost > budget) break;
                            budget -= limbCost;
                            damageScale *= MathK.Clamp01(Tuning.penetration.fleshExitScale);
                            layers++;

                            float pastLimb = limbExit + 0.01f;
                            segment += dir * pastLimb;
                            travelled += pastLimb;
                            remaining -= pastLimb;
                            continue;
                        }

                        if (!hitWorld || layers >= maxLayers) break;

                        PanelDef panel;
                        float thickness;
                        float exit;
                        if (!Penetration.PanelAt(Model, segment, dir, wallDist, 0.08f, out panel, out thickness, out exit))
                            break;

                        float scale;
                        if (!Penetration.Through(Tuning.penetration, budget, panel.Kind, thickness, out scale))
                            break;

                        damageScale *= scale;
                        budget -= thickness * Tuning.penetration.CostPerMetre(panel.Kind);

                        // Step out of the far face and carry on. The nudge is what stops the next cast
                        // starting inside the surface it just left and immediately hitting it again.
                        float step = exit + 0.02f;
                        segment += dir * step;
                        travelled += step;
                        remaining -= step;
                        layers++;
                    }
                }

                // One shot event per trigger pull: the other clients need a muzzle flash, a crack and a tracer.
                BroadcastExcept(shooter.PeerId,
                    GameEvents.Shot(shooter.PeerId, origin, aim, shooter.Sim.Weapon.Index, anyImpact,
                                    firstImpact, hitPlayer));
            }
        }

        /// <summary>
        /// A stock swing. Glass in the way takes the hit instead of the player behind it, which is
        /// the whole point of being able to break it first.
        /// </summary>
        void ResolveMelee(ServerPlayer attacker, InputCommand cmd)
        {
            Vec3 origin = attacker.Sim.EyePosition(Tuning.move);
            Vec3 aim = attacker.Sim.LookDirection();
            float range = Tuning.move.meleeRange;

            int windowIndex;
            float windowDistance;
            bool hitWindow = World.RaycastWindows(Model, origin, aim, range, out windowIndex, out windowDistance);

            ServerPlayer victim = null;
            HitTestResult best = new HitTestResult();
            best.Distance = range;

            if (Phase == MatchPhase.Live)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    ServerPlayer target = _players[i];
                    if (target == attacker || !target.Active || !target.Alive) continue;
                    if (target.SpawnProtection > 0f) continue;

                    PlayerSimState rewound = target.Sim;
                    if (LagCompensation && !RewindPlayer(target, cmd.RenderTick, out rewound)) continue;

                    PlayerHitbox box = PlayerHitbox.FromState(in rewound, Tuning.move, Tuning.Weapon(rewound.Weapon.Index));
                    HitTestResult hit;
                    if (!RayGeometry.TestPlayer(origin, aim, in box, best.Distance, out hit)) continue;
                    best = hit;
                    victim = target;
                }
            }

            if (victim != null && (!hitWindow || best.Distance <= windowDistance))
            {
                float damage = Tuning.move.meleeDamage;
                // A stock across the throat counts the same as one across the face.
                if (best.Zone == HitZone.Head || best.Zone == HitZone.Neck) damage *= Tuning.move.meleeHeadMultiplier;
                ApplyDamage(victim, attacker, damage, best.Zone, best.Distance, best.Point);
                return;
            }

            if (hitWindow) BreakWindow(windowIndex);
        }

        void MarkPropDirty(int index, bool moved)
        {
            if (index < 0 || index >= _propDirty.Length) return;
            if (moved || _propDirty[index] <= 0) _propDirty[index] = Protocol.PropDirtyTicks;
        }

        public void BreakWindow(int index)
        {
            if (index < 0 || index >= World.WindowBroken.Length) return;
            if (World.WindowBroken[index]) return;
            World.WindowBroken[index] = true;
        }

        bool RewindPlayer(ServerPlayer target, float renderTick, out PlayerSimState state)
        {
            state = target.Sim;
            float oldest = MathK.Max(0f, (float)Tick - (Protocol.HistoryTicks - 2));
            float clamped = MathK.Clamp(renderTick, oldest, Tick);
            uint lo = (uint)MathK.Max(0, MathK.FloorToInt(clamped));
            float frac = clamped - lo;

            PlayerSimState a, b;
            bool hasA = SampleHistory(target, lo, out a);
            bool hasB = SampleHistory(target, lo + 1, out b);

            if (!hasA && !hasB) return false;
            if (!hasA) { state = b; return true; }
            if (!hasB) { state = a; return true; }

            state = a;
            state.Position = Vec3.Lerp(a.Position, b.Position, frac);
            state.Yaw = a.Yaw + MathK.DeltaAngle(a.Yaw, b.Yaw) * frac;
            state.Pitch = MathK.Lerp(a.Pitch, b.Pitch, frac);
            state.Lean = MathK.Lerp(a.Lean, b.Lean, frac);
            state.SideStep = MathK.Lerp(a.SideStep, b.SideStep, frac);
            state.Height = MathK.Lerp(a.Height, b.Height, frac);
            state.Ads = MathK.Lerp(a.Ads, b.Ads, frac);
            state.Stance = frac < 0.5f ? a.Stance : b.Stance;
            return true;
        }

        bool SampleHistory(ServerPlayer p, uint tick, out PlayerSimState state)
        {
            int slot = (int)(tick % Protocol.HistoryTicks);
            if (p.HistoryTick[slot] != tick || !p.HistoryAlive[slot])
            {
                state = p.Sim;
                return false;
            }
            state = p.History[slot];
            return true;
        }

        void RecordHistory(ServerPlayer p)
        {
            int slot = (int)(Tick % Protocol.HistoryTicks);
            p.History[slot] = p.Sim;
            p.HistoryTick[slot] = Tick;
            p.HistoryAlive[slot] = p.Alive;
        }

        // ================================================================== grenades
        /// <summary>
        /// Every live grenade, one tick. Bounces make a noise at whoever is near them and the fuse is
        /// absolute - it started when the thing left a hand and nothing stops it, including the
        /// thrower dying, which is the entire point of the pin being out.
        /// </summary>
        void StepGrenades()
        {
            bool any = false;
            for (int i = 0; i < _grenades.Length; i++)
            {
                if (!_grenades[i].Active) continue;
                any = true;

                bool bounced;
                GrenadeSim.Step(ref _grenades[i], _world, Model, Tuning.grenade, Tuning.move.gravity,
                                Protocol.TickDt, out bounced);

                if (_grenades[i].Fuse > 0f) continue;
                Detonate(ref _grenades[i]);
            }

            if (!any) { _grenadeAnnounce = 0f; return; }

            // Twelve a second. A grenade you cannot see arrive is a grenade you cannot leave.
            _grenadeAnnounce -= Protocol.TickDt;
            if (_grenadeAnnounce > 0f) return;
            _grenadeAnnounce = 1f / 12f;

            for (int i = 0; i < _grenades.Length; i++)
            {
                if (!_grenades[i].Active) continue;
                Broadcast(GameEvents.Grenade(_grenades[i].Id, _grenades[i].Owner, _grenades[i].Position,
                                             _grenades[i].Fuse, _grenades[i].Bounces, _grenades[i].LastSurface));
            }
        }

        void Detonate(ref GrenadeState g)
        {
            Vec3 at = g.Position;
            g.Active = false;
            Broadcast(GameEvents.Blast(at));

            GrenadeTuning tuning = Tuning.grenade;
            ServerPlayer owner = Find(g.Owner);

            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active || !p.Alive) continue;
                if (p.SpawnProtection > 0f) continue;

                // Measured to the middle of them rather than to their feet, so lying down under a
                // blast is worth something and standing on it is not survivable.
                Vec3 centre = p.Sim.Position + Vec3.Up * (p.Sim.Height * 0.45f);
                float distance = (centre - at).Magnitude;
                if (distance > tuning.radius) continue;

                // Cover works. Without this a grenade in the next room kills you through the wall,
                // which makes the whole map one room.
                if (tuning.NeedsLineOfSight && distance > 0.35f)
                {
                    Vec3 toward = (centre - at) / distance;
                    float blocked;
                    Vec3 normal;
                    if (_world.Raycast(at, toward, distance - 0.25f, out blocked, out normal)) continue;
                }

                float damage = tuning.DamageAt(distance);
                if (damage <= 0.5f) continue;
                ApplyDamage(p, owner != null ? owner : p, damage, HitZone.Chest, distance, centre);
            }
        }

        /// <summary>Puts one in the air. Silently does nothing when there is no room for it, which is
        /// better than the alternative of one player filling the array and stopping everyone else.</summary>
        void SpawnGrenade(ServerPlayer thrower, Vec3 position, Vec3 velocity)
        {
            for (int i = 0; i < _grenades.Length; i++)
            {
                if (_grenades[i].Active) continue;
                _grenades[i] = new GrenadeState();
                _grenades[i].Active = true;
                _grenades[i].Id = _nextGrenadeId;
                _nextGrenadeId = (byte)((_nextGrenadeId + 1) & 15);
                _grenades[i].Owner = (byte)thrower.PeerId;
                _grenades[i].Position = position;
                _grenades[i].Velocity = velocity;
                _grenades[i].Fuse = MathK.Max(0.2f, Tuning.grenade.fuse);
                _grenades[i].LastSurface = SurfaceKind.Concrete;
                return;
            }
        }

        /// <summary>
        /// Kills a peer outright, for tests. Dying has consequences that nothing else can arrange -
        /// a grenade with the pin out is dropped where you fall - and a test that reproduced the
        /// damage path itself would be testing its own copy of it.
        /// </summary>
        public void KillForTest(int peerId)
        {
            ServerPlayer p = Find(peerId);
            if (p == null || !p.Alive) return;
            ApplyDamage(p, p, p.Health + 1000f, HitZone.Head, 0f, p.Sim.Position);
        }

        void ApplyDamage(ServerPlayer victim, ServerPlayer shooter, float damage, HitZone zone, float distance,
                         Vec3 point)
        {
            victim.Health -= damage;
            bool killed = victim.Health <= 0f;

            shooter.Reliable.Queue(GameEvents.HitConfirm(victim.PeerId, zone, damage, killed, point));
            victim.Reliable.Queue(GameEvents.Damaged(shooter.PeerId, zone, shooter.Sim.Weapon.Index,
                                                     damage, killed));

            if (!killed) return;

            victim.Health = 0f;
            victim.Alive = false;

            // Killed with the pin out. It goes where they fell and the fuse runs from now - which is
            // what makes pulling one in the open a commitment rather than a free option.
            if (victim.Sim.PinPulled && victim.Sim.GrenadesLeft > 0)
            {
                SpawnGrenade(victim, victim.Sim.Position + Vec3.Up * 0.4f,
                             new Vec3(0f, 1.2f, 0f) + victim.Sim.Velocity.Flat * 0.3f);
                victim.Sim.GrenadesLeft--;
            }
            victim.Sim.Carry = GrenadeCarry.Stowed;
            victim.Sim.CarryTimer = 0f;
            PropSim.ReleaseAll(victim.PeerId, World);
            victim.RespawnTimer = Tuning.match.respawnDelay;
            victim.Deaths++;
            if (!PlayingHill) shooter.Kills++;

            Broadcast(GameEvents.Death(victim.PeerId, shooter.PeerId, zone, distance));
            Broadcast(GameEvents.Score(shooter.PeerId, shooter.Kills, shooter.Deaths));
            Broadcast(GameEvents.Score(victim.PeerId, victim.Kills, victim.Deaths));

            // On the hill, Kills is the points column and kills do not add to it - the shooting is
            // how you take the room, not how you win.
            if (Phase == MatchPhase.Live && !PlayingHill &&
                shooter.Kills >= MathK.RoundToInt(Tuning.match.killsToWin))
            {
                Winner = shooter.PeerId;
                SetPhase(MatchPhase.Ended, 8f);
            }
        }

        // ================================================================== match
        bool PlayingHill { get { return Mode == GameMode.KingOfTheHill && Zones.Count > 0; } }

        /// <summary>
        /// The hill: who is standing in it, who is scoring for it, and when it moves.
        ///
        /// Contested stops the clock rather than letting the pair of you both bank points, because a
        /// mode where the answer to "he is in my room" is to stand in it too and wait is not a mode
        /// about the room. You have to make him leave.
        /// </summary>
        void StepHill()
        {
            if (!PlayingHill) return;

            if (Hill.ActiveZone < 0 || Hill.ActiveZone >= Zones.Count) Hill.ActiveZone = 0;

            int holder = KothState.Nobody;
            int inside = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active || !p.Alive) continue;
                if (!Koth.Inside(Zones[Hill.ActiveZone], p.Sim.Position)) continue;
                inside++;
                holder = inside == 1 ? p.PeerId : KothState.Contested;
            }
            Hill.Holder = inside == 0 ? KothState.Nobody : holder;

            if (Phase == MatchPhase.Live)
            {
                bool scoring = Hill.Holder >= 0 || (!Tuning.koth.ContestedStops && inside > 0);
                if (scoring && Hill.Holder >= 0)
                {
                    _zonePoints[Hill.Holder] += Tuning.koth.pointsPerSecond * Protocol.TickDt;
                    ServerPlayer p = Find(Hill.Holder);
                    if (p != null)
                    {
                        int whole = MathK.RoundToInt(_zonePoints[Hill.Holder]);
                        if (whole != p.Kills)
                        {
                            p.Kills = whole;
                            Broadcast(GameEvents.Score(p.PeerId, p.Kills, p.Deaths));
                        }
                        if (p.Kills >= Tuning.koth.PointsToWinInt)
                        {
                            Winner = p.PeerId;
                            SetPhase(MatchPhase.Ended, 8f);
                        }
                    }
                }

                Hill.RotateTimer -= Protocol.TickDt;
                if (Hill.RotateTimer <= 0f) MoveHill();
            }

            // Told often enough that the countdown on the HUD is the server's and not a guess.
            _zoneAnnounce -= Protocol.TickDt;
            if (_zoneAnnounce > 0f) return;
            _zoneAnnounce = 0.4f;
            Broadcast(GameEvents.Zone(Hill.ActiveZone, MathK.Max(0f, Hill.RotateTimer), Hill.Holder));
        }

        void MoveHill()
        {
            Hill.ActiveZone = Koth.NextZone(Hill.ActiveZone, Zones.Count, Tick);
            Hill.RotateTimer = MathK.Max(5f, Tuning.koth.rotateSeconds);
            Broadcast(GameEvents.Zone(Hill.ActiveZone, Hill.RotateTimer, KothState.Nobody));
        }

        void StepMatch()
        {
            PhaseTimer = MathK.Max(0f, PhaseTimer - Protocol.TickDt);
            StepHill();
            StepGrenades();

            switch (Phase)
            {
                case MatchPhase.Warmup:
                    if (ActiveCount >= 2) SetPhase(MatchPhase.Countdown, Tuning.match.warmupTime);
                    break;
                case MatchPhase.Countdown:
                    if (ActiveCount < 2) SetPhase(MatchPhase.Warmup, 0f);
                    else if (PhaseTimer <= 0f)
                    {
                        ResetScores();
                        for (int i = 0; i < _zonePoints.Length; i++) _zonePoints[i] = 0f;
                        if (PlayingHill) { Hill.ActiveZone = 0; Hill.RotateTimer = MathK.Max(5f, Tuning.koth.rotateSeconds); }
                        SetPhase(MatchPhase.Live, 0f);
                    }
                    break;
                case MatchPhase.Live:
                    if (ActiveCount < 2) SetPhase(MatchPhase.Warmup, 0f);
                    break;
                case MatchPhase.Ended:
                    if (PhaseTimer <= 0f)
                    {
                        ResetScores();
                        SetPhase(ActiveCount >= 2 ? MatchPhase.Countdown : MatchPhase.Warmup, Tuning.match.warmupTime);
                    }
                    break;
            }
        }

        void ResetScores()
        {
            Winner = -1;
            World.Reset(Model);
            for (int i = 0; i < _propDirty.Length; i++) _propDirty[i] = Protocol.PropDirtyTicks;
            for (int i = 0; i < _players.Count; i++)
            {
                _players[i].Kills = 0;
                _players[i].Deaths = 0;
                Broadcast(GameEvents.Score(_players[i].PeerId, 0, 0));
                if (_players[i].Active) Respawn(_players[i]);
            }
        }

        void SetPhase(MatchPhase phase, float timer)
        {
            Phase = phase;
            PhaseTimer = timer;
            Broadcast(GameEvents.Phase(phase, timer, Winner));
        }

        void Respawn(ServerPlayer p)
        {
            _avoid.Clear();
            for (int i = 0; i < _players.Count; i++)
                if (_players[i] != p && _players[i].Active && _players[i].Alive) _avoid.Add(_players[i].Sim.Position);

            SpawnPoint sp = _spawns.Pick(_spawnCounter++, _avoid);
            PlayerSimState before = p.Sim;
            p.Sim = PlayerSimState.Spawn(sp.Position, sp.Yaw, Tuning.move, Tuning.Weapon(p.Sim.Weapon.Index));
            p.Sim.GrenadesLeft = (byte)Tuning.grenade.CountInt;
            p.Sim.CarryInputEdges(in before);
            p.Health = Tuning.match.maxHealth;
            p.Alive = true;
            p.SpawnProtection = Tuning.match.spawnProtection;
            p.RespawnTimer = 0f;
            Broadcast(GameEvents.Spawn(p.PeerId, sp.Position, sp.Yaw));
        }

        // ================================================================== packets in
        void Receive()
        {
            int peerId;
            byte[] data;
            int length;
            while (_transport.Poll(out peerId, out data, out length))
            {
                if (length < 1) continue;
                _read.ResetRead(data, length);
                MessageType type = (MessageType)_read.ReadByte();

                switch (type)
                {
                    case MessageType.ConnectRequest: HandleConnect(peerId); break;
                    case MessageType.Input: HandleInput(peerId); break;
                    case MessageType.Disconnect: Kick(peerId, DisconnectReason.ClosedByUser); break;
                }
            }
        }

        /// <summary>
        /// How many connection attempts have reached this server, and when the last one did. The
        /// single most useful number when someone cannot join: if this is climbing, the packets are
        /// arriving and the problem is on the way back; if it stays at zero, they never got here.
        /// </summary>
        public int ConnectAttemptsSeen { get; private set; }
        public double LastConnectAttemptAt { get; private set; }
        public string LastConnectResult { get; private set; }

        /// <summary>
        /// Attempts that came from another machine, and where the last one came from.
        ///
        /// The count above includes the host's own client, because hosting connects to its own server
        /// over loopback exactly like anybody else - so the panel that exists to answer "did they
        /// reach me" read "1 connection attempt arrived - last one accepted" on a machine nobody had
        /// ever tried to join, and went on reading it while a player sat there failing to connect.
        /// The one number worth having is the one that does not count yourself.
        /// </summary>
        public int ConnectAttemptsFromElsewhere { get; private set; }
        public string LastConnectFrom { get; private set; }
        public double LastElsewhereAttemptAt { get; private set; }

        void HandleConnect(int peerId)
        {
            ushort version = _read.ReadUShort();
            string name = _read.ReadString();

            ConnectAttemptsSeen++;
            LastConnectAttemptAt = _now;
            LastConnectResult = "accepted";

            string from = _transport.Describe(peerId);
            if (!IsLoopback(from))
            {
                ConnectAttemptsFromElsewhere++;
                LastConnectFrom = from;
                LastElsewhereAttemptAt = _now;
            }

            ServerPlayer existing = Find(peerId);
            if (existing != null && existing.Bot != null)
            {
                // A human takes precedence over a practice bot holding that slot.
                existing.Active = false;
                _players.Remove(existing);
                BroadcastExcept(peerId, GameEvents.PlayerLeft(peerId, DisconnectReason.ClosedByUser));
                existing = null;
            }
            if (existing != null && existing.Active)
            {
                existing.LastPacketTime = _now;
                SendConnectAccept(existing);   // the accept was lost; say it again
                return;
            }

            if (version != Protocol.Version)
            {
                LastConnectResult = "rejected: their build is version " + version + ", this one is " + Protocol.Version;
                SendSimple(peerId, MessageType.ConnectReject, (byte)DisconnectReason.VersionMismatch);
                return;
            }
            if (ActiveCount >= Protocol.MaxPlayers)
            {
                LastConnectResult = "rejected: server full";
                SendSimple(peerId, MessageType.ConnectReject, (byte)DisconnectReason.ServerFull);
                return;
            }

            ServerPlayer p = existing ?? new ServerPlayer();
            p.PeerId = peerId;
            p.Name = string.IsNullOrEmpty(name) ? ("duellist " + peerId) : name;
            p.Active = true;
            p.LastPacketTime = _now;
            p.Reliable.Reset();
            p.Pending.Clear();
            p.NextClientTick = 0;
            p.InputStarted = false;
            p.LastInput = InputCommand.Default(0);
            p.Kills = 0;
            p.Deaths = 0;
            if (existing == null) _players.Add(p);

            SendConnectAccept(p);

            // Bring the newcomer up to date, then tell everyone else about them.
            string diff = TuningSerializer.ToTextDiff(Tuning, new GameTuning());
            if (diff.Length > 0) p.Reliable.Queue(GameEvents.TuningSync(diff));
            for (int i = 0; i < _players.Count; i++)
            {
                if (!_players[i].Active) continue;
                p.Reliable.Queue(GameEvents.PlayerJoined(_players[i].PeerId, _players[i].Name));
                p.Reliable.Queue(GameEvents.Score(_players[i].PeerId, _players[i].Kills, _players[i].Deaths));
            }
            p.Reliable.Queue(GameEvents.Phase(Phase, PhaseTimer, Winner));
            BroadcastExcept(p.PeerId, GameEvents.PlayerJoined(p.PeerId, p.Name));

            Respawn(p);
        }

        /// <summary>
        /// Is this endpoint this machine talking to itself? Text rather than an IPAddress because the
        /// simulation layer has no System.Net and is not having any.
        /// </summary>
        public static bool IsLoopback(string endPoint)
        {
            if (string.IsNullOrEmpty(endPoint)) return true;   // unknown: do not claim a visitor
            if (endPoint.StartsWith("loopback")) return true;  // the in-process link used by the tests
            return endPoint.StartsWith("127.") || endPoint.StartsWith("::1") || endPoint.StartsWith("[::1]");
        }

        void SendConnectAccept(ServerPlayer p)
        {
            _write.ResetWrite();
            _write.WriteByte((byte)MessageType.ConnectAccept);
            _write.WriteBits((uint)p.PeerId, 3);
            _write.WriteUInt(Tick);
            _write.WriteByte(Protocol.TickRate);
            _write.WriteByte((byte)Map);
            _write.WriteByte((byte)Mode);
            _write.WriteString(ServerName);
            Send(p.PeerId);
        }

        void HandleInput(int peerId)
        {
            ServerPlayer p = Find(peerId);
            if (p == null || !p.Active) return;

            p.LastPacketTime = _now;
            p.LastClientTimeMs = _read.ReadUInt();
            uint reliableAck = _read.ReadUInt();
            p.Reliable.OnAck(reliableAck);

            uint headTick = _read.ReadUInt();
            int count = (int)_read.ReadBits(5);

            for (int i = 0; i < count; i++)
            {
                InputCommand cmd = InputCommand.Read(_read, headTick);
                if (_read.Overflowed) break;
                // The sanity window is relative to where this client's stream actually is, and we do
                // not have that until the first command arrives. Applying it beforehand rejected every
                // input from anyone who joined more than two seconds after the server started - their
                // clock begins at the server's tick, which by then is far past the window.
                if (p.InputStarted)
                {
                    if (cmd.Tick < p.NextClientTick) continue;              // already executed
                    if (cmd.Tick > p.NextClientTick + 128) continue;        // absurd: ignore
                }
                if (!p.Pending.ContainsKey(cmd.Tick)) p.Pending[cmd.Tick] = cmd;
            }

            if (!p.InputStarted && p.Pending.Count > 0)
            {
                uint earliest = uint.MaxValue;
                foreach (uint k in p.Pending.Keys) if (k < earliest) earliest = k;
                p.NextClientTick = earliest;
                p.InputStarted = true;
            }
        }

        void CheckTimeouts()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active || p.Bot != null) continue;
                if (_now - p.LastPacketTime > Protocol.TimeoutSeconds) Kick(p.PeerId, DisconnectReason.Timeout);
            }
        }

        public void Kick(int peerId, DisconnectReason reason)
        {
            ServerPlayer p = Find(peerId);
            if (p == null || !p.Active) return;
            p.Active = false;
            p.Alive = false;
            PropSim.ReleaseAll(peerId, World);
            SendSimple(peerId, MessageType.Disconnect, (byte)reason);
            _transport.Forget(peerId);
            BroadcastExcept(peerId, GameEvents.PlayerLeft(peerId, reason));
        }

        public void Shutdown()
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].Active) SendSimple(_players[i].PeerId, MessageType.Disconnect, (byte)DisconnectReason.HostShutdown);
            _players.Clear();
        }

        // ================================================================== packets out
        void SendSnapshots()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active || p.Bot != null) continue;

                _write.ResetWrite();
                _write.WriteByte((byte)MessageType.Snapshot);
                _write.WriteUInt(Tick);
                _write.WriteUInt(p.LastExecutedTick);
                _write.WriteUInt(p.LastClientTimeMs);
                _write.WriteBits((uint)MathK.Clamp(p.BufferHealth, 0, 15), 4);
                _write.WriteBits((uint)Phase, 3);

                int countPos = _write.BitPosition;
                _write.WriteBits(0u, 4);
                int written = 0;
                for (int k = 0; k < _players.Count; k++)
                {
                    ServerPlayer other = _players[k];
                    if (!other.Active) continue;
                    PlayerNetState.FromSim((byte)other.PeerId, in other.Sim, other.Alive, other.Health).Write(_write);
                    written++;
                }
                int afterPlayers = _write.BitPosition;
                _write.SeekBits(countPos);
                _write.WriteBits((uint)written, 4);
                _write.SeekBits(afterPlayers);

                // The changeable world is small enough to send whole every snapshot, which means it
                // repairs itself after any lost packet and a late joiner is correct immediately.
                int windows = MathK.Min(World.WindowBroken.Length, 255);
                _write.WriteByte((byte)windows);
                for (int k = 0; k < windows; k++) _write.WriteBool(World.WindowBroken[k]);

                WriteProps(_write);

                int budget = Protocol.MaxPacketSize - _write.BytePosition - 8;
                p.Reliable.WritePending(_write, _now, budget);

                Send(p.PeerId);
            }
        }

        /// <summary>
        /// Only props that are actually doing something go out, plus a full sweep twice a second so a
        /// released object's resting place always lands even through loss.
        /// </summary>
        void WriteProps(NetBuffer b)
        {
            bool full = (Tick % Protocol.PropFullRefreshTicks) == 0;
            int count = 0;
            for (int i = 0; i < World.Props.Length && i < Protocol.MaxProps; i++)
                if (full || _propDirty[i] > 0) count++;

            b.WriteBits((uint)count, 6);
            for (int i = 0; i < World.Props.Length && i < Protocol.MaxProps; i++)
            {
                if (!full && _propDirty[i] <= 0) continue;
                b.WriteBits((uint)i, 5);
                b.WriteQ(World.Props[i].Position.x, Protocol.WorldMin, Protocol.WorldMax, Protocol.PropBits);
                b.WriteQ(World.Props[i].Position.y, Protocol.PropVerticalMin, Protocol.PropVerticalMax, Protocol.PropVerticalBits);
                b.WriteQ(World.Props[i].Position.z, Protocol.WorldMin, Protocol.WorldMax, Protocol.PropBits);
                b.WriteQ(MathK.Repeat(World.Props[i].Yaw, 360f), 0f, 360f, 9);
                b.WriteBits(World.Props[i].Grabber == PropSim.Nobody ? 7u : World.Props[i].Grabber, 3);
            }
        }

        void SendSimple(int peerId, MessageType type, byte value)
        {
            _write.ResetWrite();
            _write.WriteByte((byte)type);
            _write.WriteByte(value);
            Send(peerId);
        }

        void Send(int peerId)
        {
            _transport.Send(peerId, _write.Data, _write.BytePosition);
        }

        public void Broadcast(byte[] payload)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].Active) _players[i].Reliable.Queue(payload);
        }

        public void BroadcastExcept(int peerId, byte[] payload)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].Active && _players[i].PeerId != peerId) _players[i].Reliable.Queue(payload);
        }

        /// <summary>Host tweaked a value in the tuning panel - push the delta to everyone.</summary>
        public void PushTuning()
        {
            string diff = TuningSerializer.ToTextDiff(Tuning, new GameTuning());
            Broadcast(GameEvents.TuningSync(diff));
        }
    }
}
