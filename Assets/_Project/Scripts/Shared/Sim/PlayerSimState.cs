namespace Satisfying.Shared
{
    /// <summary>Weapon state that has to be predicted (ammo, fire cadence, bloom).</summary>
    public struct WeaponSimState
    {
        public byte Index;
        public short Ammo;
        public float FireCooldown;
        public float ReloadTimer;
        public float Spread;
        public uint ShotIndex;
        public bool TriggerHeld;

        public bool Reloading { get { return ReloadTimer > 0f; } }
    }

    /// <summary>
    /// The complete predicted state of a player. Everything here is produced purely by
    /// MovementCore.Step, so client and server always agree given the same inputs.
    /// Position is the FOOT position (bottom centre of the capsule).
    /// </summary>
    public struct PlayerSimState
    {
        public Vec3 Position;
        public Vec3 Velocity;
        public float Yaw;
        public float Pitch;

        public Stance Stance;
        public float Height;            // current (animating) capsule height
        public float Lean;              // -1..1 signed lean amount
        public float SideStep;          // -1..1 signed lateral step offset
        public float SideStepCooldown;
        public float Ads;               // 0..1 aim blend
        public float Stamina;

        public bool Grounded;
        public float CoyoteTimer;
        public float JumpBufferTimer;
        public float JumpCooldownTimer;
        public float StaminaDelayTimer;
        public bool Exhausted;
        public float TimeSinceLanded;

        public bool Mantling;
        public float MantleTimer;
        public Vec3 MantleStart;
        public Vec3 MantleEnd;

        public WeaponSimState Weapon;

        public static PlayerSimState Spawn(Vec3 position, float yaw, MovementTuning t, WeaponTuning w)
        {
            PlayerSimState s = new PlayerSimState();
            s.Position = position;
            s.Yaw = yaw;
            s.Height = t.standHeight;
            s.Stance = Stance.Stand;
            s.Stamina = t.staminaMax;
            s.Grounded = true;
            s.Weapon.Ammo = (short)w.MagSizeInt;
            return s;
        }

        public float EyeHeight(MovementTuning t) { return Height + t.eyeDrop; }

        /// <summary>Effective lean after stance/aim multipliers - drives both the camera and the hitbox.</summary>
        public float EffectiveLean(MovementTuning t)
        {
            float mul = 1f;
            if (Stance == Stance.Prone) mul = t.proneLeanMul;
            else if (Stance == Stance.Crouch) mul = t.crouchLeanMul;
            mul *= MathK.Lerp(1f, t.adsLeanMul, Ads);
            return Lean * mul;
        }

        /// <summary>Head position including lean and side step. The server uses exactly this for hit registration.</summary>
        public Vec3 EyePosition(MovementTuning t)
        {
            float lean = EffectiveLean(t);
            Vec3 right = ViewMath.FlatRight(Yaw);
            Vec3 eye = Position + Vec3.Up * EyeHeight(t);
            eye += right * (lean * t.leanOffset);
            eye += Vec3.Down * (MathK.Abs(lean) * t.leanDrop);
            return eye;
        }

        /// <summary>Camera roll in degrees from lean plus side step.</summary>
        public float ViewRoll(MovementTuning t)
        {
            return -EffectiveLean(t) * t.leanAngle - SideStep * t.sideStepRoll;
        }

        public Vec3 LookDirection() { return ViewMath.Forward(Yaw, Pitch); }
    }

    /// <summary>One-frame outputs from a simulation step, used for effects and for server-side shot handling.</summary>
    public struct SimEvents
    {
        public bool Jumped;
        public bool Landed;
        public float LandImpact;
        public bool StanceChanged;
        public bool StartedSideStep;
        public bool StartedMantle;
        public bool Reloaded;
        public bool DryFire;
        public int ShotsFired;
        public uint FirstShotIndex;

        public void Clear() { this = new SimEvents(); }
    }
}
