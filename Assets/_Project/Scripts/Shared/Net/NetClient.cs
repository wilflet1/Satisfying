using System.Collections.Generic;

namespace Satisfying.Shared
{
    public interface IInputSource
    {
        InputCommand Sample(uint tick, float dt);
    }

    /// <summary>Local knobs that trade latency against smoothness. Editable live from the tuning panel.</summary>
    public sealed class ClientNetTuning
    {
        [Tune("Network", 0f, 250f, Tip = "How far in the past other players are rendered. Lower = twitchier, more jitter.")]
        public float interpolationDelayMs = 55f;

        [Tune("Network", 0f, 8f, Tip = "Extra input frames the server keeps buffered. Higher survives jitter, costs latency.")]
        public float inputBufferTarget = 2f;

        [Tune("Network", 0f, 0.2f, Tip = "How aggressively the client speeds up or slows down to hold that buffer.")]
        public float clockCorrectionRate = 0.035f;

        [Tune("Network", 0f, 0.5f, Tip = "Seconds over which a prediction correction is visually smoothed out.")]
        public float errorSmoothTime = 0.09f;

        [Tune("Network", 0f, 1f, Tip = "0 = snap corrections instantly (debug), 1 = always smooth.")]
        public float smoothCorrections = 1f;
    }

    /// <summary>
    /// Predicting client. It runs the same MovementCore as the server one tick per input, keeps the
    /// inputs around, and when the server disagrees it rewinds, applies the truth and replays -
    /// so your own movement never waits for the network, and the server still owns the outcome.
    /// </summary>
    public sealed class NetClient
    {
        public enum Status { Disconnected, Connecting, Connected, Rejected }

        public sealed class RemotePlayer
        {
            public int PeerId;
            public string Name = "duellist";
            public int Kills;
            public int Deaths;
            public bool Alive = true;
            public float Health = 100f;

            public const int Capacity = 64;
            public readonly PlayerNetState[] States = new PlayerNetState[Capacity];
            public readonly uint[] Ticks = new uint[Capacity];
            public uint Newest;

            public PlayerNetState Render;
            public bool HasRender;

            public void Push(uint tick, in PlayerNetState state)
            {
                int slot = (int)(tick % Capacity);
                States[slot] = state;
                Ticks[slot] = tick;
                if (tick > Newest) Newest = tick;
            }

            public bool Sample(uint tick, out PlayerNetState state)
            {
                int slot = (int)(tick % Capacity);
                state = States[slot];
                return Ticks[slot] == tick && (Ticks[slot] != 0 || tick == 0);
            }

            /// <summary>Interpolates between the two snapshots bracketing a fractional server tick.</summary>
            public bool SampleAt(float renderTick, out PlayerNetState state)
            {
                uint lo = (uint)MathK.Max(0, MathK.FloorToInt(renderTick));
                float frac = renderTick - lo;

                PlayerNetState a, b;
                bool hasA = Sample(lo, out a);
                bool hasB = Sample(lo + 1, out b);

                if (hasA && hasB) { state = PlayerNetState.Interpolate(in a, in b, frac); return true; }
                if (hasA) { state = a; return true; }
                if (hasB) { state = b; return true; }

                // Nothing bracketing: fall back to the newest thing we have rather than popping to origin.
                for (uint back = 0; back < Capacity; back++)
                {
                    uint t = Newest - back;
                    if (Sample(t, out a)) { state = a; return true; }
                    if (t == 0) break;
                }
                state = new PlayerNetState();
                return false;
            }
        }

        readonly ITransport _transport;
        readonly ICollisionWorld _world;
        readonly NetBuffer _write = new NetBuffer(Protocol.MaxPacketSize);
        readonly NetBuffer _read = new NetBuffer(Protocol.MaxPacketSize);
        readonly List<byte[]> _events = new List<byte[]>();
        readonly ReliableChannel _reliable = new ReliableChannel();
        readonly Dictionary<int, RemotePlayer> _remotes = new Dictionary<int, RemotePlayer>();

        const int HistorySize = 256;
        readonly InputCommand[] _inputs = new InputCommand[HistorySize];
        readonly PlayerSimState[] _states = new PlayerSimState[HistorySize];
        readonly uint[] _stateTicks = new uint[HistorySize];

        public GameTuning Tuning = new GameTuning();
        public WorldModel Model = new WorldModel();
        public readonly WorldState World = new WorldState();
        public ClientNetTuning NetTuning = new ClientNetTuning();
        public IInputSource InputSource;
        public IEventSink Sink;
        /// <summary>Fired after every predicted tick so the presentation layer can react immediately.</summary>
        public System.Action<InputCommand, SimEvents> OnPredictedTick;

        public Status State = Status.Disconnected;
        public DisconnectReason LastDisconnectReason = DisconnectReason.Unknown;
        public int PeerId = -1;
        public string PlayerName = "duellist";
        public string ServerName = "";
        /// <summary>The arena the server told us to build. The view layer rebuilds when this changes.</summary>
        public MapId Map = MapId.DuelArena;

        public uint ClientTick;
        public uint ServerTick;
        public float ServerTimeline;        // fractional estimate of the server's current tick
        public float Rtt;                   // seconds
        public float Jitter;
        public int BufferHealth;
        public MatchPhase Phase = MatchPhase.Warmup;
        public float Health = 100f;
        public bool Alive = true;

        // diagnostics for the net graph
        public int Corrections;
        public float LastCorrectionError;
        public int HistoryMisses;       // corrections forced because the acked tick had aged out of the buffer
        uint _spawnTick;                // prediction restarted here; acks older than this mean nothing
        public int PacketsIn;
        public int PacketsOut;
        public int BytesInPerSecond;
        public int BytesOutPerSecond;

        PlayerSimState _predicted;
        Vec3 _renderError;
        float _renderErrorTimer;
        double _now;
        double _connectSentAt;
        double _lastPacketTime;
        double _accumulator;
        float _dilation;
        uint _lastAckTick;
        int _bytesIn, _bytesOut;
        double _rateWindowStart;
        bool _hasSpawned;

        public NetClient(ITransport transport, ICollisionWorld world)
        {
            _transport = transport;
            _world = world;
        }

        public PlayerSimState Predicted { get { return _predicted; } }
        public Dictionary<int, RemotePlayer> Remotes { get { return _remotes; } }
        public float RenderTick { get { return ServerTimeline - InterpDelayTicks; } }
        public float InterpDelayTicks { get { return MathK.Max(1f, NetTuning.interpolationDelayMs * 0.001f * Protocol.TickRate); } }
        public bool Connected { get { return State == Status.Connected; } }

        /// <summary>Predicted position with the last correction blended out, so a rewind never jolts the camera.</summary>
        public Vec3 RenderPosition
        {
            get
            {
                if (_renderErrorTimer <= 0f) return _predicted.Position;
                float k = MathK.Clamp01(_renderErrorTimer / MathK.Max(0.001f, NetTuning.errorSmoothTime));
                return _predicted.Position + _renderError * k;
            }
        }

        // ================================================================== lifecycle
        public void Connect(double now, string playerName)
        {
            PlayerName = playerName;
            State = Status.Connecting;
            _now = now;
            _connectSentAt = -100.0;
            _lastPacketTime = now;
            _reliable.Reset();
            _remotes.Clear();
            ClientTick = 0;
            _hasSpawned = false;
            Corrections = 0;
        }

        public void Disconnect()
        {
            if (State == Status.Connected || State == Status.Connecting)
            {
                _write.ResetWrite();
                _write.WriteByte((byte)MessageType.Disconnect);
                _write.WriteByte((byte)DisconnectReason.ClosedByUser);
                _transport.Send(0, _write.Data, _write.BytePosition);
            }
            State = Status.Disconnected;
            _remotes.Clear();
        }

        public void Update(double now, float dt)
        {
            _now = now;
            _transport.Update(now);
            Receive();

            if (State == Status.Connecting)
            {
                if (now - _connectSentAt >= Protocol.ConnectRetryInterval)
                {
                    _connectSentAt = now;
                    SendConnectRequest();
                }
                return;
            }

            if (State != Status.Connected) return;

            if (now - _lastPacketTime > Protocol.TimeoutSeconds)
            {
                LastDisconnectReason = DisconnectReason.Timeout;
                State = Status.Disconnected;
                return;
            }

            // Server clock estimate keeps running between packets.
            ServerTimeline += dt * Protocol.TickRate;

            if (_renderErrorTimer > 0f) _renderErrorTimer = MathK.Max(0f, _renderErrorTimer - dt);

            float interval = Protocol.TickDt * (1f + _dilation);
            _accumulator += dt;
            int guard = 0;
            while (_accumulator >= interval && guard++ < 8)
            {
                _accumulator -= interval;
                StepTick();
            }

            UpdateRemoteRenderStates();
            UpdateRates(now);
        }

        // ================================================================== local tick
        void StepTick()
        {
            InputCommand cmd = InputSource != null
                ? InputSource.Sample(ClientTick, Protocol.TickDt)
                : InputCommand.Default(ClientTick);

            cmd.Tick = ClientTick;
            cmd.RenderTick = RenderTick;

            int slot = (int)(ClientTick % HistorySize);
            _inputs[slot] = cmd;

            SimEvents ev = new SimEvents();
            if (Alive)
            {
                WeaponTuning weapon = Tuning.Weapon(cmd.WeaponIndex);
                MovementCore.Step(ref _predicted, cmd, Tuning.move, weapon, Tuning.Sight(cmd.SightIndex),
                    Protocol.TickDt, _world, ref ev);
                PropSim.Step(PeerId, ref _predicted, cmd, Tuning.move, Model, World, _world, Protocol.TickDt, ref ev);
            }
            // While dead the server does not step you either, so predicting movement here would put the
            // two sides into a fight the server wins sixty times a second.
            LastEvents = ev;

            _states[slot] = _predicted;
            _stateTicks[slot] = ClientTick;

            SendInput();
            ClientTick++;

            if (OnPredictedTick != null) OnPredictedTick(cmd, ev);
        }

        public SimEvents LastEvents;

        // ================================================================== receive
        void Receive()
        {
            int peerId;
            byte[] data;
            int length;
            while (_transport.Poll(out peerId, out data, out length))
            {
                if (length < 1) continue;
                _bytesIn += length;
                PacketsIn++;
                _lastPacketTime = _now;
                _read.ResetRead(data, length);
                MessageType type = (MessageType)_read.ReadByte();

                switch (type)
                {
                    case MessageType.ConnectAccept: HandleAccept(); break;
                    case MessageType.ConnectReject:
                        LastDisconnectReason = (DisconnectReason)_read.ReadByte();
                        State = Status.Rejected;
                        break;
                    case MessageType.Snapshot: HandleSnapshot(); break;
                    case MessageType.Disconnect:
                        LastDisconnectReason = (DisconnectReason)_read.ReadByte();
                        State = Status.Disconnected;
                        break;
                }
            }
        }

        void HandleAccept()
        {
            int id = (int)_read.ReadBits(3);
            uint serverTick = _read.ReadUInt();
            byte tickRate = _read.ReadByte();
            Map = (MapId)_read.ReadByte();
            ServerName = _read.ReadString();

            if (State == Status.Connected && PeerId == id) return;   // duplicate accept

            PeerId = id;
            State = Status.Connected;
            ServerTick = serverTick;
            ServerTimeline = serverTick;

            // Start far enough ahead that our inputs land just before the server needs them.
            float leadTicks = Rtt * 0.5f * Protocol.TickRate + NetTuning.inputBufferTarget + 1f;
            ClientTick = serverTick + (uint)MathK.Max(1, MathK.CeilToInt(leadTicks));
            _lastAckTick = 0;
            _accumulator = 0.0;
        }

        void HandleSnapshot()
        {
            // A snapshot that overtook the connect accept would otherwise register us as our own opponent.
            if (State != Status.Connected || PeerId < 0) return;

            uint serverTick = _read.ReadUInt();
            uint ackTick = _read.ReadUInt();
            uint echoTimeMs = _read.ReadUInt();
            BufferHealth = (int)_read.ReadBits(4);
            Phase = (MatchPhase)_read.ReadBits(3);

            if (echoTimeMs != 0u)
            {
                float sample = (float)((NowMs() - echoTimeMs) / 1000.0);
                if (sample >= 0f && sample < 2f)
                {
                    float delta = MathK.Abs(sample - Rtt);
                    Jitter = MathK.Lerp(Jitter, delta, 0.1f);
                    Rtt = Rtt <= 0f ? sample : MathK.Lerp(Rtt, sample, 0.1f);
                }
            }

            if (serverTick > ServerTick) ServerTick = serverTick;
            float drift = serverTick - ServerTimeline;
            if (MathK.Abs(drift) > 8f) ServerTimeline = serverTick;
            else ServerTimeline += drift * 0.1f;

            // Steer our tick rate so the server always has a small, steady input buffer.
            float error = BufferHealth - NetTuning.inputBufferTarget;
            _dilation = MathK.Clamp(error * NetTuning.clockCorrectionRate, -0.15f, 0.15f);

            int count = (int)_read.ReadBits(4);
            for (int i = 0; i < count; i++)
            {
                PlayerNetState state = PlayerNetState.Read(_read);
                if (_read.Overflowed) return;

                if (state.PeerId == PeerId)
                {
                    Health = state.Health;
                    Alive = state.Alive;
                    Reconcile(ackTick, state);
                    continue;
                }

                RemotePlayer r = GetOrCreateRemote(state.PeerId);
                r.Alive = state.Alive;
                r.Health = state.Health;
                r.Push(serverTick, in state);
            }

            int windows = _read.ReadByte();
            if (World.WindowBroken.Length != windows) World.WindowBroken = new bool[windows];
            for (int i = 0; i < windows; i++)
            {
                bool broken = _read.ReadBool();
                if (broken && !World.WindowBroken[i] && Sink != null)
                {
                    Vec3 centre = i < Model.Windows.Count ? Model.Windows[i].Bounds.Center : Vec3.Zero;
                    Sink.OnWindowBroken(i, centre);
                }
                World.WindowBroken[i] = broken;
            }

            // Self-size against the map we were told to build: nobody has to remember to call Reset.
            if (World.Props.Length != Model.Props.Count) World.Reset(Model);

            int propCount = (int)_read.ReadBits(6);
            for (int i = 0; i < propCount; i++)
            {
                int index = (int)_read.ReadBits(5);
                Vec3 position;
                position.x = _read.ReadQ(Protocol.WorldMin, Protocol.WorldMax, Protocol.PropBits);
                position.y = _read.ReadQ(Protocol.PropVerticalMin, Protocol.PropVerticalMax, Protocol.PropVerticalBits);
                position.z = _read.ReadQ(Protocol.WorldMin, Protocol.WorldMax, Protocol.PropBits);
                float yaw = _read.ReadQ(0f, 360f, 9);
                uint grabber = _read.ReadBits(3);
                if (_read.Overflowed) return;
                if (index >= World.Props.Length) continue;

                byte holder = grabber == 7u ? PropSim.Nobody : (byte)grabber;
                World.Props[index].Grabber = holder;

                // Anything we are dragging ourselves is predicted, exactly like our own movement:
                // taking the server's older position back would drag it out of our hands every packet.
                if (holder == (byte)PeerId) continue;
                World.Props[index].Position = position;
                World.Props[index].Yaw = yaw;
            }

            _reliable.ReadInto(_read, _events);
            if (_events.Count > 0 && Sink != null) GameEvents.DispatchAll(_events, Sink);
            else _events.Clear();
        }

        /// <summary>Called once the world model is known, so the state arrays are the right size.</summary>
        public void ResetWorld()
        {
            World.Reset(Model);
        }

        public RemotePlayer GetOrCreateRemote(int peerId)
        {
            RemotePlayer r;
            if (_remotes.TryGetValue(peerId, out r)) return r;
            r = new RemotePlayer();
            r.PeerId = peerId;
            _remotes[peerId] = r;
            return r;
        }

        // ================================================================== reconciliation
        void Reconcile(uint ackTick, PlayerNetState authoritative)
        {
            if (!_hasSpawned)
            {
                authoritative.ApplyTo(ref _predicted, Tuning.move);
                _hasSpawned = true;
                _lastAckTick = ackTick;
                return;
            }

            if (ackTick == 0 || ackTick <= _lastAckTick) return;
            _lastAckTick = ackTick;

            // Inputs from before the respawn: the server's state for those ticks is the old life, and
            // comparing it against a prediction that restarted at the spawn point would report a
            // correction the size of the map on every packet until the ack caught up.
            if (ackTick < _spawnTick) return;
            if (ackTick > ClientTick) return;                     // server is ahead of us: next tick sorts it out

            int slot = (int)(ackTick % HistorySize);
            bool haveHistory = _stateTicks[slot] == ackTick;

            float posError = haveHistory ? Vec3.Distance(_states[slot].Position, authoritative.Position) : 999f;
            float velError = haveHistory ? Vec3.Distance(_states[slot].Velocity, authoritative.Velocity) : 999f;
            bool stanceMismatch = haveHistory && _states[slot].Stance != authoritative.Stance;

            if (posError < Protocol.ReconcilePositionError && velError < Protocol.ReconcileVelocityError && !stanceMismatch)
                return;

            Vec3 before = _predicted.Position;

            PlayerSimState corrected = _predicted;
            authoritative.ApplyTo(ref corrected, Tuning.move);

            // Replay everything the server has not seen yet.
            for (uint t = ackTick + 1; t < ClientTick; t++)
            {
                int s = (int)(t % HistorySize);
                if (_stateTicks[s] != t) continue;
                InputCommand cmd = _inputs[s];
                if (Alive)
                {
                    WeaponTuning weapon = Tuning.Weapon(cmd.WeaponIndex);
                    SimEvents ev = new SimEvents();
                    MovementCore.Step(ref corrected, cmd, Tuning.move, weapon, Tuning.Sight(cmd.SightIndex),
                        Protocol.TickDt, _world, ref ev);
                }
                _states[s] = corrected;
            }

            _predicted = corrected;
            Corrections++;
            // Without history there is no error to measure - only a tick too old to compare against.
            // Reporting the sentinel here would put a 999 m correction on the net graph.
            if (haveHistory) LastCorrectionError = posError;
            else HistoryMisses++;

            if (NetTuning.smoothCorrections > 0.5f)
            {
                _renderError = before - _predicted.Position;
                if (_renderError.Magnitude > 2f) _renderError = Vec3.Zero;   // teleport/respawn: do not smear across the map
                _renderErrorTimer = NetTuning.errorSmoothTime;
            }
        }

        /// <summary>What we predicted for a given tick - used by the net graph and by the tests.</summary>
        public bool TryGetPredictedAt(uint tick, out PlayerSimState state)
        {
            int slot = (int)(tick % HistorySize);
            state = _states[slot];
            return _stateTicks[slot] == tick;
        }

        /// <summary>Called by the event sink when the server respawns us - prediction restarts from the truth.</summary>
        public void ForceSpawn(Vec3 position, float yaw)
        {
            PlayerSimState before = _predicted;
            _predicted = PlayerSimState.Spawn(position, yaw, Tuning.move, Tuning.Weapon(_predicted.Weapon.Index));
            _predicted.CarryInputEdges(in before);
            _renderError = Vec3.Zero;
            _renderErrorTimer = 0f;
            _hasSpawned = true;

            // Everything before this tick belongs to the life that just ended. The history stays -
            // wiping it would also empty the redundant input window, which is the last thing you want
            // in the second after a respawn - and Reconcile simply refuses to compare across the line.
            _spawnTick = ClientTick;
        }

        /// <summary>Lets the view push the mouse-driven aim straight into the predicted state (no round trip).</summary>
        public void SetPredictedView(float yaw, float pitch)
        {
            _predicted.Yaw = yaw;
            _predicted.Pitch = pitch;
        }

        // ================================================================== remotes
        void UpdateRemoteRenderStates()
        {
            float renderTick = RenderTick;
            foreach (KeyValuePair<int, RemotePlayer> kv in _remotes)
            {
                PlayerNetState state;
                kv.Value.HasRender = kv.Value.SampleAt(renderTick, out state);
                if (kv.Value.HasRender) kv.Value.Render = state;
            }
        }

        // ================================================================== send
        void SendConnectRequest()
        {
            _write.ResetWrite();
            _write.WriteByte((byte)MessageType.ConnectRequest);
            _write.WriteUShort(Protocol.Version);
            _write.WriteString(PlayerName);
            SendToServer();
        }

        void SendInput()
        {
            _write.ResetWrite();
            _write.WriteByte((byte)MessageType.Input);
            _write.WriteUInt(NowMs());
            _write.WriteUInt(_reliable.AckValue);
            _write.WriteUInt(ClientTick);

            int count = 0;
            int countPos = _write.BitPosition;
            _write.WriteBits(0u, 5);

            for (int i = 0; i < Protocol.InputRedundancy; i++)
            {
                long tick = (long)ClientTick - i;
                if (tick < 0) break;
                int slot = (int)((uint)tick % HistorySize);
                if (_stateTicks[slot] != (uint)tick) break;
                _inputs[slot].Write(_write, ClientTick);
                count++;
                if (_write.BytePosition > Protocol.MaxPacketSize - 32) break;
            }

            int end = _write.BitPosition;
            _write.SeekBits(countPos);
            _write.WriteBits((uint)count, 5);
            _write.SeekBits(end);

            SendToServer();
        }

        void SendToServer()
        {
            _bytesOut += _write.BytePosition;
            PacketsOut++;
            _transport.Send(0, _write.Data, _write.BytePosition);
        }

        uint NowMs()
        {
            return (uint)((long)(_now * 1000.0) & 0x7FFFFFFF);
        }

        void UpdateRates(double now)
        {
            if (now - _rateWindowStart < 1.0) return;
            BytesInPerSecond = _bytesIn;
            BytesOutPerSecond = _bytesOut;
            _bytesIn = 0;
            _bytesOut = 0;
            _rateWindowStart = now;
        }
    }
}
