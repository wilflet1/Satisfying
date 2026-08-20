namespace Satisfying.Shared
{
    /// <summary>
    /// One player's replicated state for a single tick. Packs to ~19 bytes, so a 1v1 snapshot at 64Hz
    /// costs well under 5 KB/s each way.
    /// </summary>
    public struct PlayerNetState
    {
        public byte PeerId;
        public bool Alive;
        public byte Health;

        public Vec3 Position;
        public Vec3 Velocity;
        public float Yaw;
        public float Pitch;
        public float Lean;
        public float SideStep;
        public float Ads;
        public float BlindFire;
        public float BlindAngle;
        public float Height;
        public float Stamina;
        public Stance Stance;
        public bool Grounded;
        public bool Mantling;

        public byte WeaponIndex;
        public short Ammo;
        public bool Reloading;
        public float Spread;

        public static PlayerNetState FromSim(byte peerId, in PlayerSimState s, bool alive, float health)
        {
            PlayerNetState n = new PlayerNetState();
            n.PeerId = peerId;
            n.Alive = alive;
            n.Health = (byte)MathK.Clamp(MathK.RoundToInt(health), 0, 255);
            n.Position = s.Position;
            n.Velocity = s.Velocity;
            n.Yaw = s.Yaw;
            n.Pitch = s.Pitch;
            n.Lean = s.Lean;
            n.SideStep = s.SideStep;
            n.Ads = s.Ads;
            n.BlindFire = s.BlindFire;
            n.BlindAngle = s.BlindAngle;
            n.Height = s.Height;
            n.Stamina = s.Stamina;
            n.Stance = s.Stance;
            n.Grounded = s.Grounded;
            n.Mantling = s.Mantling;
            n.WeaponIndex = s.Weapon.Index;
            n.Ammo = s.Weapon.Ammo;
            n.Reloading = s.Weapon.Reloading;
            n.Spread = s.Weapon.Spread;
            return n;
        }

        /// <summary>Overwrites the predicted state with the authoritative one during reconciliation.</summary>
        public void ApplyTo(ref PlayerSimState s, MovementTuning t)
        {
            s.Position = Position;
            s.Velocity = Velocity;
            s.Yaw = Yaw;
            s.Pitch = Pitch;
            s.Lean = Lean;
            s.SideStep = SideStep;
            s.Ads = Ads;
            s.BlindFire = BlindFire;
            s.BlindAngle = BlindAngle;
            s.Height = Height;
            s.Stamina = Stamina;
            s.Stance = Stance;
            s.Grounded = Grounded;
            s.Mantling = Mantling;
            s.Weapon.Index = WeaponIndex;
            s.Weapon.Ammo = Ammo;
            s.Weapon.Spread = Spread;
            if (!Reloading) s.Weapon.ReloadTimer = 0f;
        }

        /// <summary>Builds a display-only sim state (remote players, interpolated).</summary>
        public PlayerSimState ToDisplayState(float staminaMax)
        {
            PlayerSimState s = new PlayerSimState();
            s.Position = Position;
            s.Velocity = Velocity;
            s.Yaw = Yaw;
            s.Pitch = Pitch;
            s.Lean = Lean;
            s.SideStep = SideStep;
            s.Ads = Ads;
            s.BlindFire = BlindFire;
            s.BlindAngle = BlindAngle;
            s.Height = Height;
            s.Stamina = Stamina;
            s.Stance = Stance;
            s.Grounded = Grounded;
            s.Mantling = Mantling;
            s.Weapon.Index = WeaponIndex;
            s.Weapon.Ammo = Ammo;
            s.Weapon.Spread = Spread;
            return s;
        }

        public void Write(NetBuffer b)
        {
            b.WriteBits(PeerId, 3);
            b.WriteBool(Alive);
            b.WriteByte(Health);
            b.WriteQ(Position.x, Protocol.WorldMin, Protocol.WorldMax, Protocol.WorldBits);
            b.WriteQ(Position.y, Protocol.VerticalMin, Protocol.VerticalMax, Protocol.VerticalBits);
            b.WriteQ(Position.z, Protocol.WorldMin, Protocol.WorldMax, Protocol.WorldBits);
            b.WriteQ(Velocity.x, -Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            b.WriteQ(Velocity.y, -Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            b.WriteQ(Velocity.z, -Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            b.WriteQ(MathK.Repeat(Yaw, 360f), 0f, 360f, 13);
            b.WriteQ(MathK.Clamp(Pitch, -90f, 90f), -90f, 90f, 12);
            b.WriteQ(Lean, -1f, 1f, 9);
            b.WriteQ(SideStep, -1f, 1f, 8);
            b.WriteQ(Ads, 0f, 1f, 6);
            b.WriteQ(BlindFire, 0f, 1f, 5);
            b.WriteQ(BlindAngle, -1f, 1f, 6);
            b.WriteQ(Height, 0.2f, 2.4f, 9);
            b.WriteQ(Stamina, 0f, 300f, 9);
            b.WriteBits((uint)Stance, 2);
            b.WriteBool(Grounded);
            b.WriteBool(Mantling);
            b.WriteBits(WeaponIndex, 3);
            b.WriteBits((uint)MathK.Clamp(Ammo, 0, 511), 9);
            b.WriteBool(Reloading);
            b.WriteQ(Spread, 0f, 24f, 8);
        }

        public static PlayerNetState Read(NetBuffer b)
        {
            PlayerNetState n = new PlayerNetState();
            n.PeerId = (byte)b.ReadBits(3);
            n.Alive = b.ReadBool();
            n.Health = b.ReadByte();
            n.Position.x = b.ReadQ(Protocol.WorldMin, Protocol.WorldMax, Protocol.WorldBits);
            n.Position.y = b.ReadQ(Protocol.VerticalMin, Protocol.VerticalMax, Protocol.VerticalBits);
            n.Position.z = b.ReadQ(Protocol.WorldMin, Protocol.WorldMax, Protocol.WorldBits);
            n.Velocity.x = b.ReadQ(-Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            n.Velocity.y = b.ReadQ(-Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            n.Velocity.z = b.ReadQ(-Protocol.VelocityMax, Protocol.VelocityMax, Protocol.VelocityBits);
            n.Yaw = MathK.NormalizeAngle180(b.ReadQ(0f, 360f, 13));
            n.Pitch = b.ReadQ(-90f, 90f, 12);
            n.Lean = b.ReadQ(-1f, 1f, 9);
            n.SideStep = b.ReadQ(-1f, 1f, 8);
            n.Ads = b.ReadQ(0f, 1f, 6);
            n.BlindFire = b.ReadQ(0f, 1f, 5);
            n.BlindAngle = b.ReadQ(-1f, 1f, 6);
            n.Height = b.ReadQ(0.2f, 2.4f, 9);
            n.Stamina = b.ReadQ(0f, 300f, 9);
            n.Stance = (Stance)b.ReadBits(2);
            n.Grounded = b.ReadBool();
            n.Mantling = b.ReadBool();
            n.WeaponIndex = (byte)b.ReadBits(3);
            n.Ammo = (short)b.ReadBits(9);
            n.Reloading = b.ReadBool();
            n.Spread = b.ReadQ(0f, 24f, 8);
            return n;
        }

        /// <summary>Smooth blend used to render remote players between two received ticks.</summary>
        public static PlayerNetState Interpolate(in PlayerNetState a, in PlayerNetState b, float t)
        {
            PlayerNetState r = b;
            r.Position = Vec3.Lerp(a.Position, b.Position, t);
            r.Velocity = Vec3.Lerp(a.Velocity, b.Velocity, t);
            r.Yaw = a.Yaw + MathK.DeltaAngle(a.Yaw, b.Yaw) * MathK.Clamp01(t);
            r.Pitch = MathK.Lerp(a.Pitch, b.Pitch, t);
            r.Lean = MathK.Lerp(a.Lean, b.Lean, t);
            r.SideStep = MathK.Lerp(a.SideStep, b.SideStep, t);
            r.Ads = MathK.Lerp(a.Ads, b.Ads, t);
            r.BlindFire = MathK.Lerp(a.BlindFire, b.BlindFire, t);
            r.BlindAngle = MathK.Lerp(a.BlindAngle, b.BlindAngle, t);
            r.Height = MathK.Lerp(a.Height, b.Height, t);
            r.Stamina = MathK.Lerp(a.Stamina, b.Stamina, t);
            r.Spread = MathK.Lerp(a.Spread, b.Spread, t);
            return r;
        }
    }
}
