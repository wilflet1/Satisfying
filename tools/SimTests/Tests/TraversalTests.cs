using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class TraversalTests
    {
        /// <summary>Sprints to top speed, then holds crouch to trigger the slide.</summary>
        static SimEvents SprintThenSlide(ref PlayerSimState s, MovementTuning t, ICollisionWorld w, float sprintSeconds, float slideSeconds, bool jumpOut = false)
        {
            SimEvents total = new SimEvents();
            WeaponTuning gun = Sim.Weapon();

            InputCommand run = InputCommand.Default(0);
            run.MoveY = 1f;
            run.Buttons |= Buttons.Sprint;

            int sprintTicks = MathK.RoundToInt(sprintSeconds / Sim.Dt);
            for (int i = 0; i < sprintTicks; i++)
            {
                SimEvents ev = new SimEvents();
                run.Tick = (uint)i;
                MovementCore.Step(ref s, run, t, gun, Sim.Dt, w, ref ev);
            }

            InputCommand slide = run;
            slide.StanceRequest = Stance.Crouch;

            int slideTicks = MathK.RoundToInt(slideSeconds / Sim.Dt);
            for (int i = 0; i < slideTicks; i++)
            {
                SimEvents ev = new SimEvents();
                slide.Tick = (uint)(sprintTicks + i);
                if (jumpOut && i == 12) slide.Buttons |= Buttons.Jump;
                else slide.Buttons &= ~Buttons.Jump;
                MovementCore.Step(ref s, slide, t, gun, Sim.Dt, w, ref ev);
                total.StartedSlide |= ev.StartedSlide;
                total.EndedSlide |= ev.EndedSlide;
                total.Jumped |= ev.Jumped;
                total.StartedVault |= ev.StartedVault;
                total.StartedMantle |= ev.StartedMantle;
            }
            return total;
        }

        public static void Register()
        {
            // ============================================================== slide
            TestRunner.Add("slide/crouching out of a sprint starts a slide", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                SimEvents ev = SprintThenSlide(ref s, t, w, 1.5f, 0.2f);
                Assert.True(ev.StartedSlide, "slide started");
                Assert.True(s.Sliding, "still sliding");
                Assert.Greater(s.Velocity.Flat.Magnitude, t.sprintSpeed, "the slide adds an impulse on entry");
                Assert.Less(s.Height, t.crouchHeight, "lower than a crouch");
            });

            TestRunner.Add("slide/needs real speed to trigger", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand c = Sim.Forward();
                c.StanceRequest = Stance.Crouch;          // walking pace: below the slide threshold
                SimEvents ev = Sim.Run(ref s, c, t, w, 1.5f);

                Assert.False(ev.StartedSlide, "no slide from a walk");
                Assert.False(s.Sliding, "walking into a crouch is just a crouch");
                Assert.Equal((int)s.Stance, (int)Stance.Crouch, "crouched instead");
            });

            TestRunner.Add("slide/covers ground and then gives up", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                float before = s.Position.z;
                SimEvents ev = SprintThenSlide(ref s, t, w, 1.5f, t.slideDuration + 0.3f);

                Assert.True(ev.StartedSlide && ev.EndedSlide, "slide ran and finished");
                Assert.False(s.Sliding, "not still sliding");
                Assert.Greater(s.Position.z - before, 4f, "it carried you a good distance");
                Assert.Greater(s.SlideCooldown, 0f, "and left a cooldown behind it");
            });

            TestRunner.Add("slide/fits under a gap a crouch cannot", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                // A tunnel from z=6 to z=8 with its underside at y=1.0: taller than a slide, shorter
                // than a crouch.
                w.AddBox(new Vec3(0f, 1.9f, 7f), new Vec3(10f, 1.8f, 2f));

                PlayerSimState sliding = Sim.Fresh(t, Vec3.Zero);
                SprintThenSlide(ref sliding, t, w, 0.6f, 1.2f);
                Assert.Greater(sliding.Position.z, 8.5f, "the slide went under the tunnel, z=" + sliding.Position.z);

                PlayerSimState crouching = Sim.Fresh(t, Vec3.Zero);
                InputCommand crouch = Sim.Forward();
                crouch.StanceRequest = Stance.Crouch;
                Sim.Run(ref crouching, crouch, t, w, 5f);
                Assert.Less(crouching.Position.z, 6f, "a crouch is stopped by it, z=" + crouching.Position.z);
            });

            TestRunner.Add("slide/jumping out keeps the speed", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                SimEvents ev = SprintThenSlide(ref s, t, w, 1.5f, 0.4f, true);
                Assert.True(ev.Jumped, "the jump fired out of the slide");
                Assert.False(s.Sliding, "slide released");
                Assert.Greater(s.Velocity.Flat.Magnitude, t.sprintSpeed * 0.9f, "and the speed survived");
            });

            TestRunner.Add("slide/steers sideways but cannot be pumped", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                WeaponTuning gun = Sim.Weapon();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand run = InputCommand.Default(0);
                run.MoveY = 1f;
                run.Buttons |= Buttons.Sprint;
                for (int i = 0; i < 96; i++)
                {
                    SimEvents ev = new SimEvents();
                    run.Tick = (uint)i;
                    MovementCore.Step(ref s, run, t, gun, Sim.Dt, w, ref ev);
                }

                float entrySpeed = s.Velocity.Flat.Magnitude;
                InputCommand steer = run;
                steer.StanceRequest = Stance.Crouch;
                steer.MoveX = 1f;
                float peak = 0f;
                for (int i = 0; i < 40; i++)
                {
                    SimEvents ev = new SimEvents();
                    steer.Tick = (uint)(96 + i);
                    MovementCore.Step(ref s, steer, t, gun, Sim.Dt, w, ref ev);
                    peak = MathK.Max(peak, s.Velocity.Flat.Magnitude);
                }

                Assert.Greater(s.Position.x, 0.3f, "steering moved you sideways");
                Assert.Less(peak, entrySpeed * t.slideImpulse * 1.15f, "but never above the entry impulse");
            });

            // ============================================================== vault
            TestRunner.Add("vault/goes over a railing and lands beyond it", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                // A thin railing: top at y=1.0, only 20cm deep, floor continues on the far side.
                w.AddBox(new Vec3(0f, 0.5f, 6f), new Vec3(10f, 1f, 0.2f));

                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 3f));
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Mantle;
                SimEvents ev = Sim.Run(ref s, c, t, w, 2.5f);

                Assert.True(ev.StartedVault, "it vaulted rather than climbed");
                Assert.False(ev.StartedMantle, "and did not treat the railing as a platform");
                Assert.Greater(s.Position.z, 6.2f, "landed past the railing, z=" + s.Position.z);
                Assert.Near(s.Position.y, 0f, 0.12f, "and back down on the floor");
            });

            TestRunner.Add("vault/carries momentum out the far side", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                w.AddBox(new Vec3(0f, 0.45f, 6f), new Vec3(10f, 0.9f, 0.2f));

                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 3f));
                WeaponTuning gun = Sim.Weapon();
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Mantle;

                bool vaulted = false;
                float speedOnExit = 0f;
                for (int i = 0; i < 200; i++)
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    bool wasVaulting = s.Vaulting;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    if (ev.StartedVault) vaulted = true;
                    if (wasVaulting && !s.Vaulting) { speedOnExit = s.Velocity.Flat.Magnitude; break; }
                }

                Assert.True(vaulted, "vaulted");
                Assert.Greater(speedOnExit, t.vaultExitSpeed * 0.9f, "came out moving, speed=" + speedOnExit);
            });

            TestRunner.Add("vault/a solid platform is climbed, not vaulted", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                // Same height as the railing, but twenty metres deep: there is nothing to land on beyond.
                w.AddBox(new Vec3(0f, 0.5f, 16f), new Vec3(10f, 1f, 20f));

                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 3f));
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Mantle;
                SimEvents ev = Sim.Run(ref s, c, t, w, 2.5f);

                Assert.True(ev.StartedMantle, "climbed onto it");
                Assert.False(ev.StartedVault, "did not try to vault a platform");
                Assert.Near(s.Position.y, 1f, 0.12f, "ended up standing on top");
            });

            TestRunner.Add("vault/a wall that is too tall is refused", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(120f);
                w.AddBox(new Vec3(0f, 1.1f, 6f), new Vec3(10f, 2.2f, 0.2f));

                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 3f));
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Mantle;
                SimEvents ev = Sim.Run(ref s, c, t, w, 2.5f);

                Assert.False(ev.StartedVault, "no vault");
                Assert.False(ev.StartedMantle, "no climb");
                Assert.Less(s.Position.z, 6f, "stopped at the wall");
            });

            TestRunner.Add("vault/is deterministic like everything else", () =>
            {
                MovementTuning t = Sim.Tuning();
                WeaponTuning gun = Sim.Weapon();

                PlayerSimState Play()
                {
                    BoxWorld w = BoxWorld.FlatGround(120f);
                    w.AddBox(new Vec3(0f, 0.5f, 6f), new Vec3(10f, 1f, 0.2f));
                    PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 3f));
                    for (int i = 0; i < 220; i++)
                    {
                        InputCommand c = InputCommand.Default((uint)i);
                        c.MoveY = 1f;
                        if (i > 40) c.Buttons |= Buttons.Sprint;
                        if (i % 50 == 0) c.Buttons |= Buttons.Mantle;
                        if (i > 150) c.StanceRequest = Stance.Crouch;
                        SimEvents ev = new SimEvents();
                        MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    }
                    return s;
                }

                PlayerSimState a = Play();
                PlayerSimState b = Play();
                Assert.True(a.Position == b.Position && a.Velocity == b.Velocity,
                    "slide and vault stay bit for bit reproducible: " + a.Position + " vs " + b.Position);
            });

            TestRunner.Add("slide/holding crouch does not chain slides", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround(160f);
                WeaponTuning gun = Sim.Weapon();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand c = InputCommand.Default(0);
                c.MoveY = 1f;
                c.Buttons |= Buttons.Sprint;

                int slides = 0;
                for (int i = 0; i < 64 * 6; i++)
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    if (i > 90) c.StanceRequest = Stance.Crouch;    // pressed once, then held
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    if (ev.StartedSlide) slides++;
                }

                Assert.Equal(slides, 1, "one press, one slide");
            });

        }
    }
}
