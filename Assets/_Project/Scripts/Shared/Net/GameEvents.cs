using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>Anything a client needs to be told about exactly once.</summary>
    public interface IEventSink
    {
        void OnPlayerJoined(int peerId, string name);
        void OnPlayerLeft(int peerId, DisconnectReason reason);
        void OnSpawn(int peerId, Vec3 position, float yaw);
        void OnDeath(int victim, int killer, HitZone zone, float distance);
        void OnHitConfirm(int target, HitZone zone, float damage, bool killed);
        void OnTargetHit(HitZone zone, float distance);
        void OnScore(int peerId, int kills, int deaths);
        void OnMatchPhase(MatchPhase phase, float timer, int winner);
        void OnTuning(string tuningText);
        void OnRemoteShot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit, Vec3 hitPoint);
        /// <summary>A pane just went from intact to broken - the state itself arrives in the snapshot.</summary>
        void OnWindowBroken(int windowIndex, Vec3 centre);
    }

    public static class GameEvents
    {
        static NetBuffer Writer(NetEventType type)
        {
            NetBuffer b = new NetBuffer(Protocol.MaxPacketSize);
            b.ResetWrite();
            b.WriteByte((byte)type);
            return b;
        }

        public static byte[] PlayerJoined(int peerId, string name)
        {
            NetBuffer b = Writer(NetEventType.PlayerJoined);
            b.WriteBits((uint)peerId, 3);
            b.WriteString(name);
            return b.ToArray();
        }

        public static byte[] PlayerLeft(int peerId, DisconnectReason reason)
        {
            NetBuffer b = Writer(NetEventType.PlayerLeft);
            b.WriteBits((uint)peerId, 3);
            b.WriteByte((byte)reason);
            return b.ToArray();
        }

        public static byte[] Spawn(int peerId, Vec3 position, float yaw)
        {
            NetBuffer b = Writer(NetEventType.Spawn);
            b.WriteBits((uint)peerId, 3);
            b.WriteVec3(position);
            b.WriteFloat(yaw);
            return b.ToArray();
        }

        public static byte[] Death(int victim, int killer, HitZone zone, float distance)
        {
            NetBuffer b = Writer(NetEventType.Death);
            b.WriteBits((uint)victim, 3);
            b.WriteBits((uint)(killer < 0 ? 7 : killer), 3);
            b.WriteBits((uint)zone, 3);
            b.WriteQ(distance, 0f, 400f, 12);
            return b.ToArray();
        }

        public static byte[] HitConfirm(int target, HitZone zone, float damage, bool killed)
        {
            NetBuffer b = Writer(NetEventType.HitConfirm);
            b.WriteBits((uint)target, 3);
            b.WriteBits((uint)zone, 3);
            b.WriteQ(damage, 0f, 400f, 12);
            b.WriteBool(killed);
            return b.ToArray();
        }

        /// <summary>A practice target took the round. No damage, no score - just the confirmation.</summary>
        public static byte[] TargetHit(HitZone zone, float distance)
        {
            NetBuffer b = Writer(NetEventType.TargetHit);
            b.WriteBits((uint)zone, 3);
            b.WriteQ(distance, 0f, 400f, 12);
            return b.ToArray();
        }

        public static byte[] Score(int peerId, int kills, int deaths)
        {
            NetBuffer b = Writer(NetEventType.Score);
            b.WriteBits((uint)peerId, 3);
            b.WriteBits((uint)MathK.Clamp(kills, 0, 4095), 12);
            b.WriteBits((uint)MathK.Clamp(deaths, 0, 4095), 12);
            return b.ToArray();
        }

        public static byte[] Phase(MatchPhase phase, float timer, int winner)
        {
            NetBuffer b = Writer(NetEventType.MatchPhase);
            b.WriteBits((uint)phase, 3);
            b.WriteQ(MathK.Clamp(timer, 0f, 600f), 0f, 600f, 14);
            b.WriteBits((uint)(winner < 0 ? 7 : winner), 3);
            return b.ToArray();
        }

        public static byte[] TuningSync(string text)
        {
            NetBuffer b = new NetBuffer(64 * 1024);
            b.ResetWrite();
            b.WriteByte((byte)NetEventType.TuningSync);
            b.WriteString2(text);
            return b.ToArray();
        }

        public static byte[] Shot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit, Vec3 hitPoint)
        {
            NetBuffer b = Writer(NetEventType.Shot);
            b.WriteBits((uint)shooter, 3);
            b.WriteVec3(origin);
            b.WriteQVec3(direction, -1f, 1f, 12);
            b.WriteBits(weaponIndex, 3);
            b.WriteBool(hit);
            if (hit) b.WriteVec3(hitPoint);
            return b.ToArray();
        }

        public static void Dispatch(byte[] payload, IEventSink sink)
        {
            NetBuffer b = new NetBuffer(payload.Length + 4);
            b.ResetRead(payload, payload.Length);
            NetEventType type = (NetEventType)b.ReadByte();
            switch (type)
            {
                case NetEventType.PlayerJoined:
                    sink.OnPlayerJoined((int)b.ReadBits(3), b.ReadString());
                    break;
                case NetEventType.PlayerLeft:
                    sink.OnPlayerLeft((int)b.ReadBits(3), (DisconnectReason)b.ReadByte());
                    break;
                case NetEventType.Spawn:
                    sink.OnSpawn((int)b.ReadBits(3), b.ReadVec3(), b.ReadFloat());
                    break;
                case NetEventType.Death:
                {
                    int victim = (int)b.ReadBits(3);
                    int killer = (int)b.ReadBits(3);
                    HitZone zone = (HitZone)b.ReadBits(3);
                    float dist = b.ReadQ(0f, 400f, 12);
                    sink.OnDeath(victim, killer == 7 ? -1 : killer, zone, dist);
                    break;
                }
                case NetEventType.HitConfirm:
                {
                    int target = (int)b.ReadBits(3);
                    HitZone zone = (HitZone)b.ReadBits(3);
                    float dmg = b.ReadQ(0f, 400f, 12);
                    bool killed = b.ReadBool();
                    sink.OnHitConfirm(target, zone, dmg, killed);
                    break;
                }
                case NetEventType.TargetHit:
                {
                    HitZone zone = (HitZone)b.ReadBits(3);
                    float distance = b.ReadQ(0f, 400f, 12);
                    sink.OnTargetHit(zone, distance);
                    break;
                }
                case NetEventType.Score:
                    sink.OnScore((int)b.ReadBits(3), (int)b.ReadBits(12), (int)b.ReadBits(12));
                    break;
                case NetEventType.MatchPhase:
                {
                    MatchPhase phase = (MatchPhase)b.ReadBits(3);
                    float timer = b.ReadQ(0f, 600f, 14);
                    int winner = (int)b.ReadBits(3);
                    sink.OnMatchPhase(phase, timer, winner == 7 ? -1 : winner);
                    break;
                }
                case NetEventType.TuningSync:
                    sink.OnTuning(b.ReadString2());
                    break;
                case NetEventType.Shot:
                {
                    int shooter = (int)b.ReadBits(3);
                    Vec3 origin = b.ReadVec3();
                    Vec3 dir = b.ReadQVec3(-1f, 1f, 12);
                    byte weapon = (byte)b.ReadBits(3);
                    bool hit = b.ReadBool();
                    Vec3 point = hit ? b.ReadVec3() : Vec3.Zero;
                    sink.OnRemoteShot(shooter, origin, dir.Normalized, weapon, hit, point);
                    break;
                }
            }
        }

        /// <summary>Convenience for callers that want to drain a list of payloads.</summary>
        public static void DispatchAll(List<byte[]> payloads, IEventSink sink)
        {
            for (int i = 0; i < payloads.Count; i++) Dispatch(payloads[i], sink);
            payloads.Clear();
        }
    }
}
