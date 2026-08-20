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

        double _accumulator;
        double _now;
        int _spawnCounter;

        public NetServer(ITransport transport, ICollisionWorld world, SpawnSet spawns, GameTuning tuning)
        {
            _transport = transport;
            _world = world;
            _spawns = spawns;
            Tuning = tuning;
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

                InputCommand cmd = DequeueInput(p);
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
                MovementCore.Step(ref p.Sim, cmd, Tuning.move, weapon, Protocol.TickDt, _world, ref ev);

                if (ev.ShotsFired > 0 && Phase == MatchPhase.Live)
                    ResolveShots(p, cmd, weapon, ev);

                RecordHistory(p);
            }

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

        // ================================================================== shooting
        void ResolveShots(ServerPlayer shooter, InputCommand cmd, WeaponTuning weapon, SimEvents ev)
        {
            float spread = MovementCore.CurrentSpread(in shooter.Sim, Tuning.move, weapon);
            Vec3 origin = shooter.Sim.EyePosition(Tuning.move);
            Vec3 aim = shooter.Sim.LookDirection();

            for (int shot = 0; shot < ev.ShotsFired; shot++)
            {
                uint shotIndex = ev.FirstShotIndex + (uint)shot;
                int pellets = weapon.PelletsInt;
                bool anyImpact = false;
                Vec3 firstImpact = origin + aim * weapon.range;

                for (int pellet = 0; pellet < pellets; pellet++)
                {
                    Vec3 dir = ShotSolver.PelletDirection(aim, spread, shooter.PeerId, shotIndex, pellet);

                    float wallDist;
                    Vec3 wallNormal;
                    float maxDist = weapon.range;
                    bool hitWorld = _world.Raycast(origin, dir, maxDist, out wallDist, out wallNormal);
                    float limit = hitWorld ? wallDist : maxDist;

                    ServerPlayer victim = null;
                    HitTestResult best = new HitTestResult();
                    best.Distance = limit;

                    for (int i = 0; i < _players.Count; i++)
                    {
                        ServerPlayer target = _players[i];
                        if (target == shooter || !target.Active || !target.Alive) continue;
                        if (target.SpawnProtection > 0f) continue;

                        PlayerSimState rewound = target.Sim;
                        if (LagCompensation && !RewindPlayer(target, cmd.RenderTick, out rewound)) continue;

                        PlayerHitbox box = PlayerHitbox.FromState(in rewound, Tuning.move);
                        HitTestResult hit;
                        if (!RayGeometry.TestPlayer(origin, dir, in box, best.Distance, out hit)) continue;

                        best = hit;
                        victim = target;
                    }

                    Vec3 impact = victim != null
                        ? best.Point
                        : (hitWorld ? origin + dir * wallDist : origin + dir * maxDist);

                    if (pellet == 0) { firstImpact = impact; anyImpact = victim != null || hitWorld; }

                    if (victim == null) continue;

                    float damage = ShotSolver.Damage(weapon, best.Zone, best.Distance);
                    ApplyDamage(victim, shooter, damage, best.Zone, best.Distance);
                }

                // One shot event per trigger pull: the other clients need a muzzle flash, a crack and a tracer.
                BroadcastExcept(shooter.PeerId,
                    GameEvents.Shot(shooter.PeerId, origin, aim, shooter.Sim.Weapon.Index, anyImpact, firstImpact));
            }
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

        void ApplyDamage(ServerPlayer victim, ServerPlayer shooter, float damage, HitZone zone, float distance)
        {
            victim.Health -= damage;
            bool killed = victim.Health <= 0f;

            shooter.Reliable.Queue(GameEvents.HitConfirm(victim.PeerId, zone, damage, killed));

            if (!killed) return;

            victim.Health = 0f;
            victim.Alive = false;
            victim.RespawnTimer = Tuning.match.respawnDelay;
            victim.Deaths++;
            shooter.Kills++;

            Broadcast(GameEvents.Death(victim.PeerId, shooter.PeerId, zone, distance));
            Broadcast(GameEvents.Score(shooter.PeerId, shooter.Kills, shooter.Deaths));
            Broadcast(GameEvents.Score(victim.PeerId, victim.Kills, victim.Deaths));

            if (Phase == MatchPhase.Live && shooter.Kills >= MathK.RoundToInt(Tuning.match.killsToWin))
            {
                Winner = shooter.PeerId;
                SetPhase(MatchPhase.Ended, 8f);
            }
        }

        // ================================================================== match
        void StepMatch()
        {
            PhaseTimer = MathK.Max(0f, PhaseTimer - Protocol.TickDt);

            switch (Phase)
            {
                case MatchPhase.Warmup:
                    if (ActiveCount >= 2) SetPhase(MatchPhase.Countdown, Tuning.match.warmupTime);
                    break;
                case MatchPhase.Countdown:
                    if (ActiveCount < 2) SetPhase(MatchPhase.Warmup, 0f);
                    else if (PhaseTimer <= 0f) { ResetScores(); SetPhase(MatchPhase.Live, 0f); }
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
            p.Sim = PlayerSimState.Spawn(sp.Position, sp.Yaw, Tuning.move, Tuning.Weapon(p.Sim.Weapon.Index));
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

        void HandleConnect(int peerId)
        {
            ushort version = _read.ReadUShort();
            string name = _read.ReadString();

            ServerPlayer existing = Find(peerId);
            if (existing != null && existing.Active)
            {
                existing.LastPacketTime = _now;
                SendConnectAccept(existing);   // the accept was lost; say it again
                return;
            }

            if (version != Protocol.Version)
            {
                SendSimple(peerId, MessageType.ConnectReject, (byte)DisconnectReason.VersionMismatch);
                return;
            }
            if (ActiveCount >= Protocol.MaxPlayers)
            {
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

        void SendConnectAccept(ServerPlayer p)
        {
            _write.ResetWrite();
            _write.WriteByte((byte)MessageType.ConnectAccept);
            _write.WriteBits((uint)p.PeerId, 3);
            _write.WriteUInt(Tick);
            _write.WriteByte(Protocol.TickRate);
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
                if (cmd.Tick < p.NextClientTick) continue;                  // already executed
                if (cmd.Tick > p.NextClientTick + 128) continue;            // absurd: ignore
                if (!p.Pending.ContainsKey(cmd.Tick)) p.Pending[cmd.Tick] = cmd;
            }

            if (p.NextClientTick == 0 && p.Pending.Count > 0)
            {
                uint earliest = uint.MaxValue;
                foreach (uint k in p.Pending.Keys) if (k < earliest) earliest = k;
                p.NextClientTick = earliest;
            }
        }

        void CheckTimeouts()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                ServerPlayer p = _players[i];
                if (!p.Active) continue;
                if (_now - p.LastPacketTime > Protocol.TimeoutSeconds) Kick(p.PeerId, DisconnectReason.Timeout);
            }
        }

        public void Kick(int peerId, DisconnectReason reason)
        {
            ServerPlayer p = Find(peerId);
            if (p == null || !p.Active) return;
            p.Active = false;
            p.Alive = false;
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
                if (!p.Active) continue;

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

                int budget = Protocol.MaxPacketSize - _write.BytePosition - 8;
                p.Reliable.WritePending(_write, _now, budget);

                Send(p.PeerId);
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
