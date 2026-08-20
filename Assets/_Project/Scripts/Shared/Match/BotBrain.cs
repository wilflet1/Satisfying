namespace Satisfying.Shared
{
    /// <summary>
    /// A training opponent that produces InputCommands exactly like a human client does, so it goes
    /// through the same simulation, the same hit registration and the same scoreboard. Useful for
    /// practising a peek alone, and useful for exercising the whole server path in a test.
    ///
    /// Deliberately not clever: it moves, takes cover angles, leans, crouches and shoots when it can
    /// see you. It is a target that shoots back, not an AI opponent.
    /// </summary>
    public sealed class BotBrain
    {
        public float Skill = 0.55f;          // 0 = harmless, 1 = uncomfortably good
        public float ReactionTime = 0.28f;

        DeterministicRandom _rng;
        Vec3 _wanderTarget;
        float _repathTimer;
        float _burstTimer;
        float _visibleTimer;
        float _leanTimer;
        float _leanTarget;
        float _stanceTimer;
        Stance _stance = Stance.Stand;
        float _strafeSign = 1f;

        /// <summary>Which of the three weapons this bot carries - picked once so each one plays differently.</summary>
        public byte Weapon;

        public BotBrain(int seed)
        {
            _rng = new DeterministicRandom((uint)(seed * 2654435761u + 17u));
            Weapon = (byte)(_rng.NextUInt() % 3u);
        }

        public InputCommand Think(uint tick, in PlayerSimState self, MovementTuning t, ICollisionWorld world,
                                  bool hasTarget, Vec3 targetEye, SpawnSet arena, float dt)
        {
            InputCommand cmd = InputCommand.Default(tick);
            cmd.SpeedDial = 0.85f;
            cmd.WeaponIndex = Weapon;

            UpdateTimers(dt);
            Vec3 selfEye = self.EyePosition(t);

            bool canSee = false;
            if (hasTarget)
            {
                Vec3 delta = targetEye - selfEye;
                float distance = delta.Magnitude;
                float wallDistance;
                Vec3 normal;
                bool blocked = world.Raycast(selfEye, delta.Normalized, distance, out wallDistance, out normal)
                               && wallDistance < distance - 0.3f;
                canSee = !blocked;
            }

            _visibleTimer = canSee ? _visibleTimer + dt : 0f;

            // ---------------------------------------------------------- aim
            if (hasTarget)
            {
                Vec3 aim = (targetEye - selfEye).Normalized;
                float wobble = (1f - Skill) * 5f;
                cmd.Yaw = ViewMath.YawOf(aim) + _rng.NextSigned() * wobble;
                cmd.Pitch = ViewMath.PitchOf(aim) + _rng.NextSigned() * wobble * 0.4f;
            }
            else
            {
                Vec3 toWander = (_wanderTarget - self.Position).Flat;
                if (toWander.SqrMagnitude > 0.5f) cmd.Yaw = ViewMath.YawOf(toWander.Normalized);
                else cmd.Yaw = self.Yaw;
                cmd.Pitch = 0f;
            }

            // ---------------------------------------------------------- movement
            if (_repathTimer <= 0f)
            {
                _repathTimer = 2.5f + _rng.NextFloat() * 3f;
                _strafeSign = _rng.NextFloat() > 0.5f ? 1f : -1f;
                SpawnPoint point = arena.Pick((int)(tick + _rng.NextUInt() % 97u), null);
                _wanderTarget = point.Position;
            }

            if (canSee)
            {
                // Hold the angle and strafe across it rather than walking into the open.
                cmd.MoveX = _strafeSign * 0.8f;
                cmd.MoveY = 0.15f;
            }
            else
            {
                Vec3 toTarget = (_wanderTarget - self.Position).Flat;
                if (toTarget.Magnitude < 2f) _repathTimer = 0f;
                cmd.MoveY = 1f;
                cmd.MoveX = _strafeSign * 0.25f;
                if (self.Velocity.Flat.Magnitude < 0.4f)
                {
                    // Wedged on geometry: back off and pick somewhere else.
                    cmd.MoveY = -0.6f;
                    _repathTimer = 0f;
                }
            }

            // ---------------------------------------------------------- lean and stance
            if (_leanTimer <= 0f)
            {
                _leanTimer = 1.2f + _rng.NextFloat() * 2.5f;
                float roll = _rng.NextFloat();
                _leanTarget = roll < 0.3f ? -1f : (roll < 0.6f ? 1f : 0f);
            }
            cmd.LeanAxis = canSee ? _leanTarget : 0f;
            if (_rng.NextFloat() < 0.02f) cmd.Buttons |= Buttons.SlowLean;

            if (_stanceTimer <= 0f)
            {
                _stanceTimer = 3f + _rng.NextFloat() * 5f;
                float roll = _rng.NextFloat();
                _stance = roll < 0.6f ? Stance.Stand : (roll < 0.9f ? Stance.Crouch : Stance.Prone);
            }
            cmd.StanceRequest = _stance;

            // ---------------------------------------------------------- shooting
            if (canSee && _visibleTimer > ReactionTime * (1.4f - Skill))
            {
                if (_burstTimer <= 0f)
                {
                    _burstTimer = 0.35f + _rng.NextFloat() * 0.5f;
                    cmd.Buttons |= Buttons.Ads;
                }
                cmd.Buttons |= Buttons.Fire | Buttons.Ads;
            }

            if (self.Weapon.Ammo <= 0) cmd.Buttons |= Buttons.Reload;

            return cmd;
        }

        void UpdateTimers(float dt)
        {
            _repathTimer = MathK.Max(0f, _repathTimer - dt);
            _leanTimer = MathK.Max(0f, _leanTimer - dt);
            _stanceTimer = MathK.Max(0f, _stanceTimer - dt);
            _burstTimer = MathK.Max(0f, _burstTimer - dt);
        }
    }
}
