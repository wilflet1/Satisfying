namespace Satisfying.Shared
{
    public enum MessageType : byte
    {
        ConnectRequest = 1,
        ConnectAccept = 2,
        ConnectReject = 3,
        Input = 4,
        Snapshot = 5,
        Disconnect = 6,
        Ping = 7,
        Pong = 8
    }

    /// <summary>
    /// Reliable, ordered-ish payloads that ride along inside snapshot packets.
    /// Named NetEventType rather than EventType so it never collides with UnityEngine.EventType.
    /// </summary>
    public enum NetEventType : byte
    {
        PlayerJoined = 1,
        PlayerLeft = 2,
        Spawn = 3,
        Death = 4,
        HitConfirm = 5,
        Score = 6,
        MatchPhase = 7,
        TuningSync = 8,
        Shot = 9,
        TargetHit = 10
    }

    /// <summary>
    /// Which arena to build. It rides in the connect accept because both machines have to build the
    /// same geometry - prediction is only quiet when the client collides with what the server collides with.
    /// </summary>
    public enum MapId : byte
    {
        DuelArena = 0,
        TestRange = 1
    }

    public enum MatchPhase : byte
    {
        Warmup = 0,
        Countdown = 1,
        Live = 2,
        Ended = 3
    }

    public enum DisconnectReason : byte
    {
        Unknown = 0,
        ServerFull = 1,
        VersionMismatch = 2,
        Timeout = 3,
        ClosedByUser = 4,
        HostShutdown = 5
    }

    public static class Protocol
    {
        /// <summary>Bumped whenever the packet layout or the simulation changes shape.</summary>
        public const ushort Version = 11;

        public const int DefaultPort = 7777;
        public const int MaxPlayers = 8;
        public const int MaxPacketSize = 1200;      // stays under the usual 1500 MTU with room for headers

        public const int TickRate = 64;
        public const float TickDt = 1f / TickRate;

        /// <summary>How many recent commands ride in every input packet, so one lost packet costs nothing.</summary>
        // Ten copies covers 156 ms of solid loss. It used to be twelve, from when a button edge that
        // fell in a hole was gone for good; presses now ride as counters that every later command
        // repeats, so the deep window is no longer what protects them.
        public const int InputRedundancy = 10;

        /// <summary>Ticks of history the server keeps for lag compensation (1 second).</summary>
        public const int HistoryTicks = 64;

        public const float ConnectRetryInterval = 0.25f;
        public const float TimeoutSeconds = 8f;
        public const float ReliableResendInterval = 0.2f;

        /// <summary>Position error (metres) above which the client snaps to the server and replays.</summary>
        public const float ReconcilePositionError = 0.035f;
        public const float ReconcileVelocityError = 1.2f;

        // Quantisation ranges - keep in sync between writer and reader.
        public const float WorldMin = -256f;
        public const float WorldMax = 256f;
        public const int WorldBits = 17;            // ~4mm precision
        public const float VerticalMin = -64f;
        public const float VerticalMax = 192f;
        public const int VerticalBits = 15;
        public const float VelocityMax = 48f;
        public const int VelocityBits = 13;

        /// <summary>Props are sent at 1.5cm precision, and only while they are moving.</summary>
        public const int PropBits = 15;
        public const float PropVerticalMin = -8f;
        public const float PropVerticalMax = 56f;
        public const int PropVerticalBits = 12;
        public const int PropFullRefreshTicks = 32;
        public const int PropDirtyTicks = 40;
        public const int MaxProps = 32;
    }
}
