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
            ev.Clear();
            if (dt <= 0f) return;

            StepView(ref s, cmd, t, dt);

            if (s.Mantling)
            {
                s.BlindFire = MathK.MoveTowards(s.BlindFire, 0f, dt / MathK.Max(0.02f, t.blindFireBlendTime));
                StepMantle(ref s, t, dt);
                StepWeapon(ref s, cmd, t, w, dt, ref ev);
                StepStamina(ref s, cmd, t, dt, false, 0f);
                return;
            }

            bool wantsSprint = ResolveSprint(ref s, cmd, t, world);
            StepBlindFire(ref s, cmd, t, dt, wantsSprint);
            StepStance(ref s, cmd, t, world, dt, wantsSprint, ref ev);
            StepLean(ref s, cmd, t, world, dt, wantsSprint);
            StepAds(ref s, cmd, t, w, dt, wantsSprint);

            Vec3 wish = WishDirection(s.Yaw, cmd);
            float targetSpeed = TargetSpeed(ref s, cmd, t, wantsSprint);

            GroundCheck(ref s, t, world, dt, ref ev);
            StepJump(ref s, cmd, t, world, dt, ref ev);
            Accelerate(ref s, wish, targetSpeed, t, dt);
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
            float target = sprinting ? 0f : MathK.Clamp(cmd.LeanAxis, -1f, 1f);

            bool returning = MathK.Abs(target) < MathK.Abs(s.Lean) || (target * s.Lean) < 0f;
            float rate = returning ? t.leanReturnSpeed : t.leanSpeed;
            if (cmd.Has(Buttons.SlowLean)) rate *= t.slowLeanSpeedMul;

            float next = MathK.MoveTowards(s.Lean, target, rate * dt);

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
            bool wants = cmd.Has(Buttons.BlindFire) && !sprinting && !s.Mantling && s.Stamina > 0f;
            float rate = dt / MathK.Max(0.02f, t.blindFireBlendTime);
            s.BlindFire = MathK.MoveTowards(s.BlindFire, wants ? 1f : 0f, rate);
            s.BlindAngle = MathK.MoveTowards(s.BlindAngle, MathK.Clamp(cmd.BlindAngle, -1f, 1f), rate * 2f);
        }

        // ------------------------------------------------------------------ aim
        static void StepAds(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w, float dt, bool sprinting)
        {
            bool wantsAds = cmd.Has(Buttons.Ads) && !sprinting && !s.Mantling && s.BlindFire < 0.5f;
            float time = MathK.Max(0.02f, w != null ? w.adsTime : t.adsTime);
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
            }
            if (IsChangingStance(in s, t)) speed *= t.stanceChangeSpeedMul;
            if (s.Exhausted) speed *= t.exhaustedSpeedMul;
            return MathK.Max(0f, speed);
        }

        static void Accelerate(ref PlayerSimState s, Vec3 wish, float targetSpeed, MovementTuning t, float dt)
        {
            Vec3 flat = s.Velocity.Flat;
            float wishLen = wish.Magnitude;

            if (s.Grounded)
            {
                if (wishLen < 0.01f)
                {
                    flat = Vec3.MoveTowards(flat, Vec3.Zero, t.groundFriction * dt);
                }
                else
                {
                    Vec3 target = wish.Normalized * (targetSpeed * MathK.Min(1f, wishLen));
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

        // ------------------------------------------------------------------ mantle
        static void TryMantle(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world, ref SimEvents ev)
        {
            if (t.mantleEnabled < 0.5f || s.Mantling) return;
            if (!cmd.Has(Buttons.Mantle) && !(cmd.Has(Buttons.Jump) && !s.Grounded)) return;
            if (s.Stance == Stance.Prone) return;
            if (s.Stamina < t.mantleStaminaCost) return;

            Vec3 fwd = ViewMath.FlatForward(s.Yaw);
            Vec3 chest = s.Position + Vec3.Up * (t.mantleMinHeight * 0.9f);
            float wallDist;
            Vec3 wallNormal;
            if (!world.Raycast(chest, fwd, t.radius + t.mantleReach, out wallDist, out wallNormal)) return;
            if (MathK.Abs(wallNormal.y) > 0.5f) return;

            Vec3 ledgeProbe = s.Position + fwd * (wallDist + t.radius * 0.9f) + Vec3.Up * (t.mantleMaxHeight + 0.4f);
            float downDist;
            Vec3 downNormal;
            if (!world.Raycast(ledgeProbe, Vec3.Down, t.mantleMaxHeight + 0.5f, out downDist, out downNormal)) return;
            if (downNormal.y < 0.6f) return;

            float ledgeY = ledgeProbe.y - downDist;
            float height = ledgeY - s.Position.y;
            if (height < t.mantleMinHeight || height > t.mantleMaxHeight) return;

            Vec3 target = new Vec3(ledgeProbe.x, ledgeY + SkinWidth, ledgeProbe.z);
            float clearHeight = world.CheckCapsule(target, t.standHeight, t.radius) ? t.crouchHeight : t.standHeight;
            if (world.CheckCapsule(target, clearHeight, t.radius)) return;

            s.Mantling = true;
            s.MantleTimer = 0f;
            s.MantleStart = s.Position;
            s.MantleEnd = target;
            s.Velocity = Vec3.Zero;
            s.Lean = 0f;
            s.SideStep = 0f;
            if (clearHeight < t.standHeight) { s.Stance = Stance.Crouch; }
            SpendStamina(ref s, t, t.mantleStaminaCost);
            ev.StartedMantle = true;
        }

        static void StepMantle(ref PlayerSimState s, MovementTuning t, float dt)
        {
            s.MantleTimer += dt;
            float k = MathK.Clamp01(s.MantleTimer / MathK.Max(0.05f, t.mantleTime));

            // Up first, then across - reads as a pull-up rather than a slide.
            float up = MathK.SmoothStep(MathK.Clamp01(k * 1.5f));
            float across = MathK.SmoothStep(MathK.Clamp01((k - 0.25f) / 0.75f));

            float y = MathK.Lerp(s.MantleStart.y, s.MantleEnd.y, up);
            float x = MathK.Lerp(s.MantleStart.x, s.MantleEnd.x, across);
            float z = MathK.Lerp(s.MantleStart.z, s.MantleEnd.z, across);
            s.Position = new Vec3(x, y, z);
            s.Velocity = Vec3.Zero;

            if (k >= 1f)
            {
                s.Mantling = false;
                s.Grounded = true;
                s.CoyoteTimer = t.coyoteTime;
                s.TimeSinceLanded = 0f;
            }
        }

        // ------------------------------------------------------------------ stamina
        static void SpendStamina(ref PlayerSimState s, MovementTuning t, float amount)
        {
            s.Stamina = MathK.Max(0f, s.Stamina - amount);
            s.StaminaDelayTimer = t.staminaRegenDelay;
        }

        static void StepStamina(ref PlayerSimState s, InputCommand cmd, MovementTuning t, float dt, bool sprinting, float leanAmount)
        {
            float drain = 0f;
            if (sprinting) drain += t.sprintStaminaDrain;
            if (leanAmount > 0.05f) drain += t.leanStaminaDrain * leanAmount;
            if (s.Ads > 0.5f) drain += t.adsStaminaDrain;
            if (s.BlindFire > 0.05f) drain += t.blindFireStaminaDrain * s.BlindFire;

            if (drain > 0f)
            {
                s.Stamina = MathK.Max(0f, s.Stamina - drain * dt);
                s.StaminaDelayTimer = t.staminaRegenDelay;
            }
            else
            {
                s.StaminaDelayTimer = MathK.Max(0f, s.StaminaDelayTimer - dt);
                if (s.StaminaDelayTimer <= 0f)
                    s.Stamina = MathK.Min(t.staminaMax, s.Stamina + t.staminaRegen * dt);
            }

            // Hysteresis so you cannot machine-gun the sprint key at the exhaustion boundary.
            float low = t.staminaMax * t.exhaustionThreshold;
            float high = t.staminaMax * MathK.Min(0.95f, t.exhaustionThreshold + 0.15f);
            if (!s.Exhausted && s.Stamina <= low) s.Exhausted = true;
            else if (s.Exhausted && s.Stamina >= high) s.Exhausted = false;
        }

        // ------------------------------------------------------------------ weapon
        static void StepWeapon(ref PlayerSimState s, InputCommand cmd, MovementTuning t, WeaponTuning w, float dt, ref SimEvents ev)
        {
            if (w == null) return;

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

            bool trigger = cmd.Has(Buttons.Fire) && !s.Mantling;
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
            float spread = w.spreadBase + s.Weapon.Spread;
            spread += s.Velocity.Flat.Magnitude * w.spreadMovePerSpeed;
            if (s.Stance == Stance.Crouch) spread *= w.spreadCrouchMul;
            else if (s.Stance == Stance.Prone) spread *= w.spreadProneMul;
            spread *= MathK.Lerp(1f, w.spreadAdsMul, s.Ads);
            if (s.Exhausted) spread *= 1.35f;
            if (!s.Grounded) spread *= 2.2f;
            if (s.BlindFire > 0.01f) spread *= MathK.Lerp(1f, MathK.Max(1f, t.blindFireSpreadMul), s.BlindFire);
            return MathK.Max(0f, spread);
        }
    }
}
