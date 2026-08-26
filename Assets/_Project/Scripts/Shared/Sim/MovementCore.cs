namespace Satisfying.Shared
{
    /// <summary>
    /// The single source of truth for how a player moves. Client prediction, the authoritative
    /// server and the headless tests all call this exact function with the same fixed dt, which is
    /// what keeps reconciliation quiet.
    ///
    /// Deliberately free of any engine type: geometry access goes through ICollisionWorld.
    /// </summary>
    public static class MovementCore
    {
        public const float SkinWidth = 0.02f;

        public static void Step(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w,
                                float dt, ICollisionWorld world, ref SimEvents ev)
        {
            Step(ref s, cmd, t, w, null, dt, world, ref ev);
        }

        public static void Step(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w,
                                SightTuning sight, float dt, ICollisionWorld world, ref SimEvents ev)
        {
            Step(ref s, cmd, t, w, sight, null, dt, world, ref ev);
        }

        /// <summary>
        /// The full step. A null GrenadeTuning means this caller has no grenades in it - the movement
        /// tests do not, and threading a tuning object through forty of them to say so would be worse
        /// than the branch.
        /// </summary>
        public static void Step(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w,
                                SightTuning sight, GrenadeTuning grenade, float dt, ICollisionWorld world,
                                ref SimEvents ev)
        {
            ev.Clear();
            if (dt <= 0f) return;

            StepView(ref s, cmd, t, dt);

            if (s.Mantling)
            {
                s.Sliding = false;
                s.BlindFire = MathK.MoveTowards(s.BlindFire, 0f, dt / MathK.Max(0.02f, t.blindFireBlendTime));
                StepMantle(ref s, t, dt);
                StepWeapon(ref s, cmd, t, w, dt, ref ev);
                StepStamina(ref s, cmd, t, dt, false, 0f);
                s.LastStanceRequest = cmd.StanceRequest;
                return;
            }

            bool wantsSprint = ResolveSprint(ref s, cmd, t, world);
            StepSlide(ref s, cmd, t, world, dt, ref wantsSprint, ref ev);
            StepBlindFire(ref s, cmd, t, dt, wantsSprint);
            StepMelee(ref s, cmd, t, dt, ref ev);
            StepGrenade(ref s, cmd, t, grenade, dt, ref ev);
            StepStance(ref s, cmd, t, world, dt, wantsSprint, ref ev);
            StepLean(ref s, cmd, t, world, dt, wantsSprint);
            StepAds(ref s, cmd, t, w, sight, dt, wantsSprint);

            Vec3 wish = WishDirection(s.Yaw, cmd);
            float targetSpeed = TargetSpeed(ref s, cmd, t, wantsSprint);

            GroundCheck(ref s, t, world, dt, ref ev);
            StepJump(ref s, cmd, t, world, dt, ref ev);
            Accelerate(ref s, wish, targetSpeed, t, dt, wantsSprint);
            ApplyGravity(ref s, cmd, t, dt);

            Vec3 sideStepDelta = StepSideStep(ref s, cmd, t, dt, ref ev);
            TryMantle(ref s, cmd, t, world, ref ev);

            Vec3 displacement = s.Velocity * dt + sideStepDelta;
            MoveResult res = world.MoveCapsule(s.Position, s.Height, t.radius, displacement, t.stepOffset, t.slopeLimit);
            s.Position = res.Position;
            ResolveVelocityAgainstHits(ref s, res);
            GroundSnap(ref s, t, world, res, dt);

            StepStamina(ref s, cmd, t, dt, wantsSprint, MathK.Abs(s.EffectiveLean(t)));
            StepWeapon(ref s, cmd, t, w, dt, ref ev);

            s.TimeSinceLanded += dt;
            s.LastStanceRequest = cmd.StanceRequest;
        }

        // ------------------------------------------------------------------ view
        static void StepView(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt)
        {
            float targetYaw = cmd.Yaw;
            if (s.Stance == Stance.Prone)
            {
                // Prone turning is deliberately heavy: the whole body has to come round.
                float maxDelta = t.proneYawRateLimit * dt;
                s.Yaw = MathK.NormalizeAngle180(s.Yaw + MathK.Clamp(MathK.DeltaAngle(s.Yaw, targetYaw), -maxDelta, maxDelta));
                s.Pitch = MathK.Clamp(cmd.Pitch, -t.pronePitchLimit, t.pronePitchLimit);
            }
            else
            {
                s.Yaw = MathK.NormalizeAngle180(targetYaw);
                s.Pitch = MathK.Clamp(cmd.Pitch, -t.pitchLimit, t.pitchLimit);
            }
        }

        // ------------------------------------------------------------------ stance
        static bool ResolveSprint(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world)
        {
            if (!cmd.Has(Buttons.Sprint)) return false;
            if (cmd.MoveY < 0.4f) return false;
            if (s.Exhausted) return false;
            if (s.Weapon.Reloading) { /* sprint-reload is allowed */ }
            // Sprinting forces you upright; if there is no headroom you simply do not sprint.
            if (s.Stance != Stance.Stand && world.CheckCapsule(s.Position, t.standHeight, t.radius)) return false;
            return true;
        }

        static void StepStance(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world,
                               float dt, bool sprinting, ref SimEvents ev)
        {
            if (s.Sliding)
            {
                // The slide owns the capsule while it lasts.
                s.Stance = Stance.Crouch;
                float slideRate = MathK.Max(0.01f, t.standHeight - t.slideHeight) / MathK.Max(0.02f, t.crouchTransitionTime * 0.6f);
                s.Height = MathK.MoveTowards(s.Height, t.slideHeight, slideRate * dt);
                return;
            }

            Stance want = sprinting ? Stance.Stand : cmd.StanceRequest;

            // Rising through stances needs headroom; sinking never does.
            if (want < s.Stance)
            {
                Stance probe = want;
                while (probe < s.Stance && world.CheckCapsule(s.Position, t.HeightFor(probe), t.radius))
                    probe = (Stance)((byte)probe + 1);
                want = probe;
            }

            if (want != s.Stance)
            {
                s.Stance = want;
                ev.StanceChanged = true;
            }

            float targetHeight = t.HeightFor(s.Stance);
            if (MathK.Abs(s.Height - targetHeight) > 1e-4f)
            {
                bool proneInvolved = s.Stance == Stance.Prone || s.Height < t.crouchHeight - 0.01f;
                float span = proneInvolved
                    ? MathK.Max(0.01f, t.crouchHeight - t.proneHeight)
                    : MathK.Max(0.01f, t.standHeight - t.crouchHeight);
                float time = proneInvolved ? t.proneTransitionTime : t.crouchTransitionTime;
                float rate = span / MathK.Max(0.01f, time);
                s.Height = MathK.MoveTowards(s.Height, targetHeight, rate * dt);
            }
        }

        public static bool IsChangingStance(in PlayerSimState s, MovementTuning t)
        {
            return MathK.Abs(s.Height - t.HeightFor(s.Stance)) > 0.02f;
        }

        // ------------------------------------------------------------------ lean
        static void StepLean(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world,
                             float dt, bool sprinting)
        {
            float axis = sprinting ? 0f : MathK.Clamp(cmd.LeanAxis, -1f, 1f);
            bool slow = cmd.Has(Buttons.SlowLean) && !sprinting;
            bool leanKey = MathK.Abs(axis) > 0.01f;

            // Slow lean is a "dial and latch" peek: it travels to the full extent, just slowly, and it holds
            // wherever it is - not only when you release the lean keys, but after you release the slow-lean
            // modifier too. A normal (non-slow) lean press takes manual control again and clears the latch,
            // so an ordinary tap-and-release still recentres.
            float target;
            if (sprinting)
            {
                s.LeanLatched = false;
                target = 0f;
            }
            else if (slow)
            {
                target = leanKey ? axis : s.Lean;      // dial toward the key, hold when none is pressed
                if (leanKey || MathK.Abs(s.Lean) > 0.01f) s.LeanLatched = true;
            }
            else if (leanKey)
            {
                s.LeanLatched = false;                 // manual lean overrides and drops the latch
                target = axis;
            }
            else
            {
                target = s.LeanLatched ? s.Lean : 0f;  // latched: hold; otherwise spring back to centre
            }

            bool returning = MathK.Abs(target) < MathK.Abs(s.Lean) || (target * s.Lean) < 0f;
            float rate = returning ? t.leanReturnSpeed : t.leanSpeed;
            if (slow) rate *= t.slowLeanSpeedMul;

            float next = MathK.MoveTowards(s.Lean, target, rate * dt);
            if (MathK.Abs(next) < 0.001f && !slow && !leanKey) s.LeanLatched = false;

            // Leaning into a wall gets crushed back instead of putting your head through concrete.
            if (t.leanWallPushback > 0f && MathK.Abs(next) > 0.01f)
                next = CrushLeanAgainstWorld(s, next, t, world);

            s.Lean = next;
        }

        static float CrushLeanAgainstWorld(PlayerSimState s, float candidate, MovementTuning t, ICollisionWorld world)
        {
            float mul = 1f;
            if (s.Stance == Stance.Prone) mul = t.proneLeanMul;
            else if (s.Stance == Stance.Crouch) mul = t.crouchLeanMul;
            mul *= MathK.Lerp(1f, t.adsLeanMul, s.Ads);

            Vec3 right = ViewMath.FlatRight(s.Yaw);
            Vec3 head = s.Position + Vec3.Up * s.EyeHeight(t);
            float probeRadius = t.radius * 0.62f;

            // Three fixed probes keep this deterministic across machines.
            float[] scales = { 1f, 0.66f, 0.33f };
            for (int i = 0; i < scales.Length; i++)
            {
                float test = candidate * scales[i];
                Vec3 probe = head + right * (test * mul * t.leanOffset) + Vec3.Down * (MathK.Abs(test * mul) * t.leanDrop);
                if (!world.CheckSphere(probe, probeRadius))
                    return MathK.Lerp(candidate, test, t.leanWallPushback);
            }
            return MathK.Lerp(candidate, 0f, t.leanWallPushback);
        }

        // ------------------------------------------------------------------ blind fire
        /// <summary>
        /// Holding the weapon over cover. Your head never moves, so nothing new is exposed - you simply
        /// cannot see where the rounds are going, which is what the spread penalty is for. Movement is
        /// deliberately still allowed: walking out of cover while spraying is the whole point.
        /// </summary>
        static void StepBlindFire(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt, bool sprinting)
        {
            bool wants = cmd.Has(Buttons.BlindFire) && !sprinting && !s.Mantling && s.ArmStamina > 0f;
            float rate = dt / MathK.Max(0.02f, t.blindFireBlendTime);
            s.BlindFire = MathK.MoveTowards(s.BlindFire, wants ? 1f : 0f, rate);
            s.BlindAngle = MathK.MoveTowards(s.BlindAngle, MathK.Clamp(cmd.BlindAngle, -1f, 1f), rate * 2f);
        }

        // ------------------------------------------------------------------ grenades
        /// <summary>
        /// Getting one out, and letting it go.
        ///
        /// The draw is slow and the pin comes out at the end of it, which is the whole shape of the
        /// decision: you are putting your weapon down for a second and change, in the open, and once
        /// the pin is out the only ways back are throwing it or dying with it.
        ///
        /// It is deliberately NOT cookable. The fuse starts when it leaves your hand, so holding it
        /// buys you nothing and there is no timing minigame - what you are choosing is the throw, not
        /// the moment. Dying with the pin out drops a live one where you fell; that is the price, and
        /// it is handled by the server, which is the only thing that knows you died.
        /// </summary>
        static void StepGrenade(ref PlayerSimState s, InputCommand cmd, MovementTuning t, GrenadeTuning g,
                                float dt, ref SimEvents ev)
        {
            if (g == null) return;

            bool drawPressed = InputCommand.Advanced(cmd.GrenadeSeq, s.GrenadeSeqSeen);
            if (drawPressed) s.GrenadeSeqSeen = cmd.GrenadeSeq;

            bool throwPressed = InputCommand.Advanced(cmd.ThrowSeq, s.ThrowSeqSeen);
            if (throwPressed) s.ThrowSeqSeen = cmd.ThrowSeq;

            // Whether the throw button is still down. This is the one place in the game that cares
            // about a button being HELD rather than about the edge, because the whole gesture is
            // press-to-pull-the-pin, release-to-throw.
            bool throwHeld = (cmd.Buttons & Buttons.Throw) != 0;

            switch (s.Carry)
            {
                case GrenadeCarry.Stowed:
                    // Not while you are swinging, vaulting or dragging something: both hands.
                    if (!drawPressed || s.GrenadesLeft == 0 || s.IsSwinging || s.Vaulting || s.Mantling) break;
                    s.Carry = GrenadeCarry.Drawing;
                    s.CarryTimer = MathK.Max(0.05f, g.drawTime);
                    ev.GrenadeDrawStarted = true;
                    break;

                case GrenadeCarry.Drawing:
                    s.CarryTimer -= dt;
                    // Pressing it again puts it away. Nothing has happened yet, so nothing is spent.
                    if (drawPressed) { s.Carry = GrenadeCarry.Stowed; s.CarryTimer = 0f; break; }
                    if (s.CarryTimer > 0f) break;
                    s.Carry = GrenadeCarry.Held;
                    s.CarryTimer = 0f;
                    ev.GrenadeInHand = true;
                    break;

                case GrenadeCarry.Held:
                    // In your hand with the pin in. Still reversible.
                    if (drawPressed) { s.Carry = GrenadeCarry.Stowed; s.CarryTimer = 0f; break; }
                    if (!throwPressed) break;

                    // A mouse button pulls the pin. From here the only ways out are throwing it or
                    // dying with it.
                    s.Carry = GrenadeCarry.Primed;
                    s.CarryTimer = MathK.Max(0.02f, g.primeTime);
                    s.ThrowHard = cmd.ThrowHard;
                    ev.GrenadePinPulled = true;
                    break;

                case GrenadeCarry.Primed:
                    s.CarryTimer = MathK.Max(0f, s.CarryTimer - dt);
                    // Whichever button is down decides the throw, right up until it is let go.
                    if (throwHeld) s.ThrowHard = cmd.ThrowHard;

                    // It leaves when the button comes up - or on its own if the button is somehow
                    // never released, because a live grenade welded to your hand is not a state the
                    // simulation should be able to reach.
                    bool released = !throwHeld && s.CarryTimer <= 0f;
                    if (!released) break;

                    s.Carry = GrenadeCarry.Stowed;
                    s.CarryTimer = 0f;
                    if (s.GrenadesLeft > 0) s.GrenadesLeft--;
                    ev.GrenadeReleased = true;
                    ev.GrenadeHard = s.ThrowHard;
                    break;
            }
        }

        // ------------------------------------------------------------------ melee
        /// <summary>
        /// A stock to the face. It commits you: no firing, no aiming, reduced movement, and a fresh
        /// press each time. The strike lands on one exact tick so the server has a single moment to
        /// test rather than a smear of frames.
        /// </summary>
        static void StepMelee(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt, ref SimEvents ev)
        {
            s.MeleeCooldown = MathK.Max(0f, s.MeleeCooldown - dt);

            bool fresh = InputCommand.Advanced(cmd.MeleeSeq, s.MeleeSeqSeen);
            if (fresh) s.MeleeSeqSeen = cmd.MeleeSeq;

            if (s.MeleeTimer > 0f)
            {
                float before = s.MeleeTimer;
                s.MeleeTimer += dt;
                if (before < t.meleeWindup && s.MeleeTimer >= t.meleeWindup) ev.MeleeStrike = true;
                if (s.MeleeTimer >= t.meleeWindup + t.meleeRecover)
                {
                    s.MeleeTimer = 0f;
                    s.MeleeCooldown = t.meleeCooldown;
                }
                return;
            }

            if (!fresh) return;
            if (s.MeleeCooldown > 0f || s.Mantling || s.Sliding) return;
            if (s.ArmStamina < t.meleeStaminaCost) return;

            s.MeleeTimer = 1e-5f;
            s.Ads = 0f;
            s.BlindFire = 0f;
            SpendArmStamina(ref s, t, t.meleeStaminaCost);
            ev.MeleeSwing = true;
        }

        // ------------------------------------------------------------------ aim
        static void StepAds(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w,
                            SightTuning sight, float dt, bool sprinting)
        {
            bool wantsAds = cmd.Has(Buttons.Ads) && !sprinting && !s.Mantling && s.BlindFire < 0.5f && !s.IsSwinging;
            float time = MathK.Max(0.02f, w != null ? w.adsTime : t.adsTime);
            if (sight != null) time *= MathK.Max(0.1f, sight.adsTimeMul);
            s.Ads = MathK.MoveTowards(s.Ads, wantsAds ? 1f : 0f, dt / time);
        }

        // ------------------------------------------------------------------ speed
        static Vec3 WishDirection(float yaw, InputCommand cmd)
        {
            Vec2 axis = new Vec2(cmd.MoveX, cmd.MoveY).ClampedToUnit();
            Vec3 fwd = ViewMath.FlatForward(yaw);
            Vec3 right = ViewMath.FlatRight(yaw);
            return (right * axis.x + fwd * axis.y);
        }

        static float TargetSpeed(ref PlayerSimState s, InputCommand cmd, MovementTuning t, bool sprinting)
        {
            if (s.Sliding) return 0f;   // a slide is not driven by the move keys

            float dial = cmd.Has(Buttons.WalkToggle) ? t.speedDialMin : MathK.Lerp(t.speedDialMin, 1f, MathK.Clamp01(cmd.SpeedDial));
            float speed = sprinting ? t.sprintSpeed : t.SpeedFor(s.Stance) * dial;

            Vec2 axis = new Vec2(cmd.MoveX, cmd.MoveY).ClampedToUnit();
            if (axis.y < -0.01f) speed *= MathK.Lerp(1f, t.backwardsSpeedMul, -axis.y);
            speed *= MathK.Lerp(1f, t.strafeSpeedMul, MathK.Abs(axis.x));

            if (!sprinting)
            {
                speed *= MathK.Lerp(1f, t.leanSpeedMul, MathK.Abs(s.Lean));
                speed *= MathK.Lerp(1f, t.adsSpeedMul, s.Ads);
                speed *= MathK.Lerp(1f, t.sideStepSpeedMul, MathK.Abs(s.SideStep));
                speed *= MathK.Lerp(1f, t.blindFireSpeedMul, s.BlindFire);
                if (s.IsSwinging) speed *= t.meleeSpeedMul;
            }

            // Dragging applies to a sprint too, unlike the rest of these: the sprint key is not a way
            // to run off with two hundred kilos.
            if (s.CarryMass > 0f)
            {
                speed /= 1f + s.CarryMass * MathK.Max(0f, t.carrySlowFactor);

                // You can never walk faster than the thing you are dragging. Without this you walk out
                // to the end of the grip and stay there, and the object trails at well under its own
                // drag speed: the headless drill covers 2.9 m in 4 s uncapped against 5.6 m in 5 s
                // capped. Letting the mass set your pace makes the object feel attached to your hands
                // rather than tethered to them.
                speed = MathK.Min(speed, PropSim.DragSpeed(s.CarryMass, t) * 0.95f);
            }

            if (IsChangingStance(in s, t)) speed *= t.stanceChangeSpeedMul;
            if (s.Exhausted) speed *= t.exhaustedSpeedMul;
            return MathK.Max(0f, speed);
        }

        static void Accelerate(ref PlayerSimState s, Vec3 wish, float targetSpeed, MovementTuning t, float dt, bool sprinting)
        {
            Vec3 flat = s.Velocity.Flat;
            float wishLen = wish.Magnitude;

            if (s.Sliding)
            {
                SlideAccelerate(ref s, wish, t, dt);
                return;
            }

            if (s.Grounded)
            {
                if (wishLen < 0.01f)
                {
                    flat = Vec3.MoveTowards(flat, Vec3.Zero, t.groundFriction * dt);
                }
                else
                {
                    Vec3 target = wish.Normalized * (targetSpeed * MathK.Min(1f, wishLen));

                    // Sprint is a switch, not a ramp: the moment it engages you are at whatever speed
                    // it allows. Leave the ceiling to targetSpeed - gear can lower that later without
                    // this having an opinion about it.
                    if (sprinting && t.sprintSnap > 0f && flat.Magnitude < target.Magnitude)
                        flat = Vec3.Lerp(flat, target, MathK.Clamp01(t.sprintSnap));

                    float accel = t.groundAccel;
                    if (Vec3.Dot(flat, target) < 0f) accel *= 1f + t.counterStrafeBoost;
                    // Above target speed (sprint release, landing hot) we bleed off at friction rate.
                    if (flat.Magnitude > target.Magnitude + 0.05f)
                        flat = Vec3.MoveTowards(flat, target, MathK.Max(accel, t.groundFriction) * dt);
                    else
                        flat = Vec3.MoveTowards(flat, target, accel * dt);
                }
            }
            else
            {
                if (wishLen > 0.01f)
                {
                    Vec3 target = wish.Normalized * (targetSpeed * MathK.Min(1f, wishLen));
                    Vec3 blended = Vec3.MoveTowards(flat, target, t.airAccel * dt);
                    flat = Vec3.LerpUnclamped(flat, blended, MathK.Clamp01(t.airControl));
                }
                if (t.airFriction > 0f)
                    flat = Vec3.MoveTowards(flat, Vec3.Zero, t.airFriction * dt);
            }

            s.Velocity = new Vec3(flat.x, s.Velocity.y, flat.z);
        }

        /// <summary>
        /// A slide is momentum you already had, bleeding away. You can lean on it and steer a little,
        /// and a downhill will feed it - but you cannot accelerate into one.
        /// </summary>
        static void SlideAccelerate(ref PlayerSimState s, Vec3 wish, MovementTuning t, float dt)
        {
            Vec3 flat = s.Velocity.Flat;
            float speed = flat.Magnitude;
            if (speed < 0.01f) { s.Velocity = new Vec3(0f, s.Velocity.y, 0f); return; }

            Vec3 forward = flat / speed;

            // Steering is sideways only: you cannot pump the stick to go faster.
            if (wish.SqrMagnitude > 0.01f)
            {
                Vec3 lateral = Vec3.ProjectOnPlane(wish.Normalized, forward);
                flat += lateral * (t.slideSteering * dt);
            }

            // Downhill feeds the slide, uphill kills it.
            Vec3 normal = s.GroundNormal.SqrMagnitude > 0.01f ? s.GroundNormal : Vec3.Up;
            if (normal.y < 0.999f)
            {
                Vec3 downhill = Vec3.ProjectOnPlane(Vec3.Down, normal).Flat;
                if (downhill.SqrMagnitude > 0.001f)
                    flat += downhill.Normalized * (t.slideSlopeAccel * (1f - normal.y) * dt);
            }

            flat = Vec3.MoveTowards(flat, Vec3.Zero, t.slideFriction * dt);
            s.Velocity = new Vec3(flat.x, s.Velocity.y, flat.z);
        }

        static void ApplyGravity(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt)
        {
            if (s.Grounded && s.Velocity.y <= 0f)
            {
                s.Velocity.y = -2f; // gentle stick so slopes and stairs do not launch you
                return;
            }
            float g = t.gravity;
            if (s.Velocity.y < 0f) g *= t.fallGravityMul;
            else if (!cmd.Has(Buttons.Jump)) g *= t.lowJumpGravityMul;
            s.Velocity.y -= g * dt;
            if (s.Velocity.y < -60f) s.Velocity.y = -60f;
        }

        // ------------------------------------------------------------------ ground / jump
        static void GroundCheck(ref PlayerSimState s, MovementTuning t, ICollisionWorld world, float dt, ref SimEvents ev)
        {
            float dist;
            Vec3 normal;
            bool wasGrounded = s.Grounded;
            float probe = s.Velocity.y > 0.1f ? SkinWidth * 2f : t.groundSnapDistance;

            bool found = world.GroundProbe(s.Position, t.radius, probe, out dist, out normal);
            bool slopeOk = found && normal.y >= MathK.Cos(MathK.Clamp(t.slopeLimit, 1f, 89f) * MathK.Deg2Rad);
            s.Grounded = found && slopeOk && s.Velocity.y <= 0.1f;
            s.GroundNormal = found ? normal : Vec3.Up;

            if (s.Grounded && !wasGrounded)
            {
                ev.Landed = true;
                ev.LandImpact = MathK.Abs(s.Velocity.y);
                s.TimeSinceLanded = 0f;
                Vec3 flat = s.Velocity.Flat * (1f - MathK.Clamp01(t.landSpeedLoss));
                s.Velocity = new Vec3(flat.x, s.Velocity.y, flat.z);
            }

            s.CoyoteTimer = s.Grounded ? t.coyoteTime : MathK.Max(0f, s.CoyoteTimer - dt);
        }

        static void StepJump(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world, float dt, ref SimEvents ev)
        {
            s.JumpCooldownTimer = MathK.Max(0f, s.JumpCooldownTimer - dt);
            s.JumpBufferTimer = cmd.Has(Buttons.Jump)
                ? t.jumpBuffer
                : MathK.Max(0f, s.JumpBufferTimer - dt);

            if (s.JumpBufferTimer <= 0f) return;
            if (s.Sliding) return;                       // StepSlide converts it into a slide jump
            if (s.CoyoteTimer <= 0f || s.JumpCooldownTimer > 0f) return;
            if (s.Stamina < t.jumpStaminaCost || s.Exhausted) return;
            if (IsChangingStance(in s, t)) return;
            if (s.Stance != Stance.Stand)
            {
                // Jump out of crouch/prone stands you up first.
                if (!world.CheckCapsule(s.Position, t.standHeight, t.radius))
                {
                    s.Stance = Stance.Stand;
                    s.Height = t.standHeight;
                }
                return;
            }

            s.Velocity.y = t.JumpVelocity;
            s.Grounded = false;
            s.CoyoteTimer = 0f;
            s.JumpBufferTimer = 0f;
            s.JumpCooldownTimer = t.jumpCooldown;
            SpendStamina(ref s, t, t.jumpStaminaCost);
            ev.Jumped = true;
        }

        static void GroundSnap(ref PlayerSimState s, MovementTuning t, ICollisionWorld world, MoveResult res, float dt)
        {
            if (s.Velocity.y > 0.1f) return;
            float dist;
            Vec3 normal;
            if (!world.GroundProbe(s.Position, t.radius, t.groundSnapDistance, out dist, out normal)) return;
            if (normal.y < MathK.Cos(MathK.Clamp(t.slopeLimit, 1f, 89f) * MathK.Deg2Rad)) return;
            if (dist > SkinWidth)
            {
                s.Position = new Vec3(s.Position.x, s.Position.y - (dist - SkinWidth), s.Position.z);
            }
            s.Grounded = true;
        }

        static void ResolveVelocityAgainstHits(ref PlayerSimState s, MoveResult res)
        {
            if ((res.Flags & MoveCollisionFlags.Above) != 0 && s.Velocity.y > 0f) s.Velocity.y = 0f;
            if ((res.Flags & MoveCollisionFlags.Below) != 0 && s.Velocity.y < 0f) s.Velocity.y = 0f;
            if ((res.Flags & MoveCollisionFlags.Sides) != 0)
            {
                Vec3 n = res.WallNormal.Flat.Normalized;
                if (n.SqrMagnitude > 0.01f)
                {
                    Vec3 flat = s.Velocity.Flat;
                    float into = Vec3.Dot(flat, n);
                    if (into < 0f) flat -= n * into;
                    s.Velocity = new Vec3(flat.x, s.Velocity.y, flat.z);
                }
            }
        }

        // ------------------------------------------------------------------ side step
        static Vec3 StepSideStep(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt, ref SimEvents ev)
        {
            s.SideStepCooldown = MathK.Max(0f, s.SideStepCooldown - dt);

            float target = 0f;
            if (cmd.Has(Buttons.StepLeft)) target -= 1f;
            if (cmd.Has(Buttons.StepRight)) target += 1f;
            if (s.Stance == Stance.Prone || s.Mantling) target = 0f;

            if (target != 0f && MathK.Abs(s.SideStep) < 0.05f)
            {
                if (s.SideStepCooldown > 0f || s.Stamina < t.sideStepStaminaCost)
                {
                    target = 0f;
                }
                else
                {
                    s.SideStepCooldown = t.sideStepCooldown + t.sideStepTime;
                    SpendStamina(ref s, t, t.sideStepStaminaCost);
                    ev.StartedSideStep = true;
                }
            }

            bool returning = MathK.Abs(target) < MathK.Abs(s.SideStep) || (target * s.SideStep) < 0f;
            float time = returning ? t.sideStepReturnTime : t.sideStepTime;
            float prev = s.SideStep;
            s.SideStep = MathK.MoveTowards(s.SideStep, target, dt / MathK.Max(0.01f, time));

            float delta = (s.SideStep - prev) * t.sideStepDistance;
            if (MathK.Abs(delta) < 1e-6f) return Vec3.Zero;
            return ViewMath.FlatRight(s.Yaw) * delta;
        }

        // ------------------------------------------------------------------ slide
        /// <summary>
        /// Crouching out of a sprint converts the speed you already had into a low, fast slide. You
        /// cannot accelerate into one and you cannot pump it - it is momentum being spent, which is why
        /// it has a cooldown and a stamina price. Jumping out of it keeps the speed.
        /// </summary>
        static void StepSlide(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world,
                              float dt, ref bool sprinting, ref SimEvents ev)
        {
            s.SlideCooldown = MathK.Max(0f, s.SlideCooldown - dt);

            if (s.Sliding)
            {
                sprinting = false;
                s.SlideTimer -= dt;

                bool jumped = cmd.Has(Buttons.Jump);
                float speed = s.Velocity.Flat.Magnitude;
                bool finished = s.SlideTimer <= 0f
                                || speed < t.slideMinExitSpeed
                                || !s.Grounded
                                || s.Mantling
                                || cmd.StanceRequest == Stance.Stand
                                || jumped;
                if (!finished) return;

                s.Sliding = false;
                s.SlideCooldown = t.slideCooldown;
                ev.EndedSlide = true;

                if (jumped && !world.CheckCapsule(s.Position, t.standHeight, t.radius))
                {
                    // Slide jump: stand up this tick so the normal jump fires, and keep the speed.
                    Vec3 boosted = s.Velocity.Flat * t.slideJumpBoost;
                    s.Velocity = new Vec3(boosted.x, s.Velocity.y, boosted.z);
                    s.Stance = Stance.Stand;
                    s.Height = t.standHeight;
                }
                return;
            }

            // A held crouch must not chain slides: it takes a fresh press each time.
            bool freshCrouch = cmd.StanceRequest == Stance.Crouch && s.LastStanceRequest != Stance.Crouch;

            if (t.slideEnabled < 0.5f || !sprinting || !s.Grounded || s.Mantling) return;
            if (!freshCrouch) return;
            if (s.SlideCooldown > 0f || s.Stamina < t.slideStaminaCost) return;

            float entrySpeed = s.Velocity.Flat.Magnitude;
            if (entrySpeed < t.slideMinSpeed) return;

            Vec3 launch = s.Velocity.Flat.Normalized * (entrySpeed * t.slideImpulse);
            s.Velocity = new Vec3(launch.x, s.Velocity.y, launch.z);
            s.Sliding = true;
            s.SlideTimer = t.slideDuration;
            s.Lean = 0f;
            s.SideStep = 0f;
            sprinting = false;
            SpendStamina(ref s, t, t.slideStaminaCost);
            ev.StartedSlide = true;
        }

        // ------------------------------------------------------------------ mantle and vault
        static void TryMantle(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world, ref SimEvents ev)
        {
            if (s.Mantling) return;
            if (!cmd.Has(Buttons.Mantle) && !(cmd.Has(Buttons.Jump) && !s.Grounded)) return;
            if (s.Stance == Stance.Prone || s.Sliding) return;

            bool canClimb = t.mantleEnabled >= 0.5f && s.Stamina >= t.mantleStaminaCost;
            bool canVault = t.vaultEnabled >= 0.5f && s.Stamina >= t.vaultStaminaCost;
            if (!canClimb && !canVault) return;

            Vec3 fwd = ViewMath.FlatForward(s.Yaw);
            float reachHeight = MathK.Max(t.mantleMaxHeight, t.vaultMaxHeight);

            // Something solid and upright in front of us?
            Vec3 chest = s.Position + Vec3.Up * (MathK.Min(t.mantleMinHeight, t.vaultMinHeight) * 0.9f);
            float wallDist;
            Vec3 wallNormal;
            if (!world.Raycast(chest, fwd, t.radius + t.mantleReach, out wallDist, out wallNormal)) return;
            if (MathK.Abs(wallNormal.y) > 0.5f) return;

            // Measure its top just past the face - a railing can be thinner than the capsule radius, so
            // probing a whole radius in would sail straight over it and find the floor beyond.
            Vec3 face = chest + fwd * wallDist;
            Vec3 topProbe = new Vec3(face.x, s.Position.y + reachHeight + 0.5f, face.z) + fwd * 0.06f;

            float downDist;
            Vec3 downNormal;
            if (!world.Raycast(topProbe, Vec3.Down, reachHeight + 0.6f, out downDist, out downNormal)) return;
            if (downNormal.y < 0.6f) return;

            float ledgeY = topProbe.y - downDist;
            float height = ledgeY - s.Position.y;

            // A railing is something with a floor well below its top on the far side: go OVER it.
            if (canVault && height >= t.vaultMinHeight && height <= t.vaultMaxHeight &&
                TryVaultBeyond(ref s, t, world, fwd, face, ledgeY, ref ev))
                return;

            if (!canClimb || height < t.mantleMinHeight || height > t.mantleMaxHeight) return;

            // To climb it, there has to be a surface at that height a full capsule further in.
            Vec3 target = s.Position + fwd * (wallDist + t.radius * 1.05f);
            float standDist;
            Vec3 standNormal;
            Vec3 standProbe = new Vec3(target.x, ledgeY + 0.6f, target.z);
            if (!world.Raycast(standProbe, Vec3.Down, 1.2f, out standDist, out standNormal)) return;
            if (standNormal.y < 0.6f) return;
            float standY = standProbe.y - standDist;
            if (MathK.Abs(standY - ledgeY) > 0.2f) return;      // nothing to stand on: it was a railing

            target = new Vec3(target.x, standY + SkinWidth, target.z);
            float clearHeight = world.CheckCapsule(target, t.standHeight, t.radius) ? t.crouchHeight : t.standHeight;
            if (world.CheckCapsule(target, clearHeight, t.radius)) return;

            BeginTraversal(ref s, t, target, ledgeY, false);
            if (clearHeight < t.standHeight) s.Stance = Stance.Crouch;
            SpendStamina(ref s, t, t.mantleStaminaCost);
            ev.StartedMantle = true;
        }

        /// <summary>
        /// Looks past the top of the obstacle for ground to land on. If the far side is meaningfully
        /// lower than the top, it is a railing or a window sill rather than a platform, and the right
        /// move is to swing over it and keep going.
        /// </summary>
        static bool TryVaultBeyond(ref PlayerSimState s, MovementTuning t, ICollisionWorld world,
                                   Vec3 forward, Vec3 face, float ledgeY, ref SimEvents ev)
        {
            Vec3 beyond = new Vec3(face.x, ledgeY + 1.4f, face.z) + forward * t.vaultReachBeyond;

            float farDist;
            Vec3 farNormal;
            bool foundFloor = world.Raycast(beyond, Vec3.Down, 12f, out farDist, out farNormal);
            float farY = foundFloor ? beyond.y - farDist : ledgeY - t.vaultMaxDrop - 1f;

            if (foundFloor && farNormal.y >= 0.6f && farY > ledgeY - t.vaultDropThreshold)
                return false;      // the far side is level with the top: it is a platform, climb it

            Vec3 landing;
            if (foundFloor && farNormal.y >= 0.6f && ledgeY - farY <= t.vaultMaxDrop)
            {
                landing = new Vec3(beyond.x, farY + SkinWidth, beyond.z);
            }
            else
            {
                // Long drop or nothing down there at all: go over the rail and fall the rest.
                landing = new Vec3(beyond.x, ledgeY + 0.05f, beyond.z);
            }

            float clearHeight = world.CheckCapsule(landing, t.standHeight, t.radius) ? t.crouchHeight : t.standHeight;
            if (world.CheckCapsule(landing, clearHeight, t.radius)) return false;

            BeginTraversal(ref s, t, landing, ledgeY, true);
            if (clearHeight < t.standHeight) s.Stance = Stance.Crouch;
            SpendStamina(ref s, t, t.vaultStaminaCost);
            ev.StartedVault = true;
            return true;
        }

        static void BeginTraversal(ref PlayerSimState s, MovementTuning t, Vec3 target, float ledgeY, bool vault)
        {
            s.Mantling = true;
            s.Vaulting = vault;
            s.MantleTimer = 0f;
            s.MantleStart = s.Position;
            s.MantleEnd = target;

            // The apex has to clear the obstacle, or a vault would clip straight through the railing.
            Vec3 mid = (s.Position + target) * 0.5f;
            s.MantlePeak = new Vec3(mid.x, ledgeY + t.radius * 0.9f, mid.z);

            s.Velocity = Vec3.Zero;
            s.Lean = 0f;
            s.SideStep = 0f;
            s.Sliding = false;
        }

        static void StepMantle(ref PlayerSimState s, MovementTuning t, float dt)
        {
            s.MantleTimer += dt;
            float duration = MathK.Max(0.05f, s.Vaulting ? t.vaultTime : t.mantleTime);
            float k = MathK.Clamp01(s.MantleTimer / duration);

            if (s.Vaulting)
            {
                // Quadratic arc through the apex: up, over the railing, down the far side.
                float e = MathK.SmoothStep(k);
                float inv = 1f - e;
                s.Position = s.MantleStart * (inv * inv)
                           + s.MantlePeak * (2f * inv * e)
                           + s.MantleEnd * (e * e);
            }
            else
            {
                // Up first, then across - reads as a pull-up rather than a slide.
                float up = MathK.SmoothStep(MathK.Clamp01(k * 1.5f));
                float across = MathK.SmoothStep(MathK.Clamp01((k - 0.25f) / 0.75f));
                s.Position = new Vec3(
                    MathK.Lerp(s.MantleStart.x, s.MantleEnd.x, across),
                    MathK.Lerp(s.MantleStart.y, s.MantleEnd.y, up),
                    MathK.Lerp(s.MantleStart.z, s.MantleEnd.z, across));
            }

            s.Velocity = Vec3.Zero;

            if (k < 1f) return;

            bool wasVault = s.Vaulting;
            s.Mantling = false;
            s.Vaulting = false;
            s.Grounded = true;
            s.CoyoteTimer = t.coyoteTime;
            s.TimeSinceLanded = 0f;

            // A vault is a traversal, not a stop: you come out of it still moving.
            if (!wasVault) return;
            Vec3 exit = (s.MantleEnd - s.MantleStart).Flat;
            if (exit.SqrMagnitude > 0.01f) s.Velocity = exit.Normalized * t.vaultExitSpeed;
        }

        // ------------------------------------------------------------------ stamina
        /// <summary>Leg-pool spend: sprint, jump, slide, side step, mantle, vault.</summary>
        static void SpendStamina(ref PlayerSimState s, MovementTuning t, float amount)
        {
            s.Stamina = MathK.Max(0f, s.Stamina - amount);
            s.StaminaDelayTimer = t.staminaRegenDelay;
        }

        /// <summary>Arm-pool spend: melee (aim and blind fire drain continuously in StepStamina).</summary>
        static void SpendArmStamina(ref PlayerSimState s, MovementTuning t, float amount)
        {
            s.ArmStamina = MathK.Max(0f, s.ArmStamina - amount);
            s.ArmStaminaDelay = t.staminaRegenDelay;
        }

        static void StepStamina(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt, bool sprinting, float leanAmount)
        {
            // Legs: locomotion effort. Aim NEVER touches this pool.
            float legDrain = 0f;
            if (sprinting) legDrain += t.sprintStaminaDrain;
            if (leanAmount > 0.05f) legDrain += t.leanStaminaDrain * leanAmount;

            if (legDrain > 0f)
            {
                s.Stamina = MathK.Max(0f, s.Stamina - legDrain * dt);
                s.StaminaDelayTimer = t.staminaRegenDelay;
            }
            else
            {
                s.StaminaDelayTimer = MathK.Max(0f, s.StaminaDelayTimer - dt);
                if (s.StaminaDelayTimer <= 0f)
                    s.Stamina = MathK.Min(t.staminaMax, s.Stamina + t.staminaRegen * dt);
            }

            // Arms: holding the weapon up. Sprinting drops the gun, so it does not drain here.
            float armDrain = 0f;
            if (s.Ads > 0.5f) armDrain += t.adsStaminaDrain;
            if (s.BlindFire > 0.05f) armDrain += t.blindFireStaminaDrain * s.BlindFire;

            if (armDrain > 0f)
            {
                s.ArmStamina = MathK.Max(0f, s.ArmStamina - armDrain * dt);
                s.ArmStaminaDelay = t.staminaRegenDelay;
            }
            else
            {
                s.ArmStaminaDelay = MathK.Max(0f, s.ArmStaminaDelay - dt);
                if (s.ArmStaminaDelay <= 0f)
                    s.ArmStamina = MathK.Min(t.staminaMax, s.ArmStamina + t.staminaRegen * dt);
            }

            // Hysteresis so you cannot machine-gun the sprint key at the exhaustion boundary. Exhaustion
            // tracks the legs - it gates sprint and slows you down.
            float low = t.staminaMax * t.exhaustionThreshold;
            float high = t.staminaMax * MathK.Min(0.95f, t.exhaustionThreshold + 0.15f);
            if (!s.Exhausted && s.Stamina <= low) s.Exhausted = true;
            else if (s.Exhausted && s.Stamina >= high) s.Exhausted = false;
        }

        // ------------------------------------------------------------------ weapon
        static void StepWeapon(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w, float dt, ref SimEvents ev)
        {
            if (w == null) return;

            s.Weapon.Sight = cmd.SightIndex;

            if (s.Weapon.Index != cmd.WeaponIndex)
            {
                s.Weapon.Index = cmd.WeaponIndex;
                s.Weapon.Ammo = (short)w.MagSizeInt;
                s.Weapon.ReloadTimer = 0f;
                s.Weapon.FireCooldown = MathK.Max(s.Weapon.FireCooldown, 0.35f);
                s.Weapon.Spread = 0f;
            }

            s.Weapon.FireCooldown = MathK.Max(0f, s.Weapon.FireCooldown - dt);

            if (s.Weapon.Reloading)
            {
                s.Weapon.ReloadTimer -= dt;
                if (s.Weapon.ReloadTimer <= 0f)
                {
                    s.Weapon.ReloadTimer = 0f;
                    s.Weapon.Ammo = (short)w.MagSizeInt;
                    ev.Reloaded = true;
                }
            }
            else if (cmd.Has(Buttons.Reload) && s.Weapon.Ammo < w.MagSizeInt)
            {
                s.Weapon.ReloadTimer = w.reloadTime;
            }

            bool trigger = cmd.Has(Buttons.Fire) && !s.Mantling && !s.IsSwinging;
            bool freshPull = trigger && !s.Weapon.TriggerHeld;

            if (trigger && !s.Weapon.Reloading)
            {
                int guard = 0;
                while (s.Weapon.FireCooldown <= 0f && s.Weapon.Ammo > 0 && guard++ < 4)
                {
                    if (!w.IsAutomatic && !freshPull) break;
                    s.Weapon.Ammo--;
                    if (ev.ShotsFired == 0) ev.FirstShotIndex = s.Weapon.ShotIndex;
                    s.Weapon.ShotIndex++;
                    ev.ShotsFired++;
                    s.Weapon.FireCooldown += w.ShotInterval;
                    s.Weapon.Spread = MathK.Min(w.spreadBase * 6f + 12f, s.Weapon.Spread + w.spreadPerShot);
                    freshPull = false;
                }

                if (s.Weapon.Ammo <= 0)
                {
                    // Running dry while holding the trigger starts the reload for you.
                    if (!s.Weapon.TriggerHeld) ev.DryFire = true;
                    s.Weapon.ReloadTimer = w.reloadTime;
                }
            }

            s.Weapon.TriggerHeld = trigger;
            s.Weapon.Spread = MathK.MoveTowards(s.Weapon.Spread, 0f, w.spreadRecovery * dt);
        }

        /// <summary>Total cone of fire in degrees. Client draws it, server validates with it.</summary>
        public static float CurrentSpread(in PlayerSimState s, MovementTuning t, WeaponTuning w)
        {
            return CurrentSpread(in s, t, w, null);
        }

        public static float CurrentSpread(in PlayerSimState s, MovementTuning t, WeaponTuning w, SightTuning sight)
        {
            float spread = w.spreadBase + s.Weapon.Spread;
            spread += s.Velocity.Flat.Magnitude * w.spreadMovePerSpeed;
            if (s.Stance == Stance.Crouch) spread *= w.spreadCrouchMul;
            else if (s.Stance == Stance.Prone) spread *= w.spreadProneMul;
            float adsSpread = w.spreadAdsMul;
            if (sight != null) adsSpread *= MathK.Max(0.05f, sight.spreadMul);
            spread *= MathK.Lerp(1f, adsSpread, s.Ads);
            // Tired ARMS shake the sights, not tired legs.
            if (s.ArmStamina <= t.staminaMax * t.exhaustionThreshold) spread *= 1.35f;
            if (!s.Grounded) spread *= 2.2f;
            if (s.BlindFire > 0.01f) spread *= MathK.Lerp(1f, MathK.Max(1f, t.blindFireSpreadMul), s.BlindFire);
            return MathK.Max(0f, spread);
        }
    }
}
