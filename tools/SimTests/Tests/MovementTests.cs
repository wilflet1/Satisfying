using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class Sim
    {
        public const float Dt = 1f / 64f;

        public static MovementTuning Tuning() { return new MovementTuning(); }
        public static WeaponTuning Weapon() { return WeaponTuning.DefaultLoadout()[0]; }

        public static PlayerSimState Fresh(MovementTuning t, Vec3 pos)
        {
            return PlayerSimState.Spawn(pos, 0f, t, Weapon());
        }

        /// <summary>Runs the real shipping simulation for a duration and returns the accumulated events.</summary>
        public static SimEvents Run(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld world, float seconds)
        {
            SimEvents total = new SimEvents();
            int ticks = MathK.Max(1, MathK.RoundToInt(seconds / Dt));
            WeaponTuning w = Weapon();
            for (int i = 0; i < ticks; i++)
            {
                SimEvents ev = new SimEvents();
                cmd.Tick = (uint)i;
                MovementCore.Step(ref s, cmd, t, w, Dt, world, ref ev);
                total.Jumped |= ev.Jumped;
                total.Landed |= ev.Landed;
                total.StanceChanged |= ev.StanceChanged;
                total.StartedSideStep |= ev.StartedSideStep;
                total.StartedMantle |= ev.StartedMantle;
                total.ShotsFired += ev.ShotsFired;
            }
            return total;
        }

        public static InputCommand Forward()
        {
            InputCommand c = InputCommand.Default(0);
            c.MoveY = 1f;
            return c;
        }
    }

    public static class MovementTests
    {
        public static void Register()
        {
            TestRunner.Add("movement/reaches walk speed", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                Sim.Run(ref s, Sim.Forward(), t, w, 1.5f);
                Assert.Near(s.Velocity.Flat.Magnitude, t.walkSpeed, 0.15f, "walk speed");
                Assert.Near(s.Position.y, 0f, 0.05f, "stays on the ground");
            });

            TestRunner.Add("movement/analog speed dial scales walk", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = Sim.Forward();
                c.SpeedDial = 0f;
                Sim.Run(ref s, c, t, w, 1.5f);
                Assert.Near(s.Velocity.Flat.Magnitude, t.walkSpeed * t.speedDialMin, 0.15f, "dial at minimum");
            });

            TestRunner.Add("movement/stops within 0.25s", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                Sim.Run(ref s, Sim.Forward(), t, w, 1f);
                Sim.Run(ref s, InputCommand.Default(0), t, w, 0.25f);
                Assert.Less(s.Velocity.Flat.Magnitude, 0.2f, "stopped");
            });

            TestRunner.Add("movement/sprint is faster than walk", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Sprint;
                Sim.Run(ref s, c, t, w, 1.2f);
                Assert.Near(s.Velocity.Flat.Magnitude, t.sprintSpeed, 0.2f, "sprint speed");
                Assert.Less(s.Stamina, t.staminaMax, "sprint drains stamina");
            });

            TestRunner.Add("movement/exhaustion blocks sprint", () =>
            {
                MovementTuning t = Sim.Tuning();
                t.staminaMax = 20f;
                t.sprintStaminaDrain = 40f;
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Sprint;
                Sim.Run(ref s, c, t, w, 2f);
                Assert.True(s.Exhausted, "went exhausted");
                Assert.Less(s.Velocity.Flat.Magnitude, t.walkSpeed + 0.1f, "no sprint while winded");
            });

            TestRunner.Add("movement/jump apex matches tuned height", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Jump;
                float apex = 0f;
                WeaponTuning wt = Sim.Weapon();
                for (int i = 0; i < 64; i++)
                {
                    SimEvents ev = new SimEvents();
                    MovementCore.Step(ref s, c, t, wt, Sim.Dt, w, ref ev);
                    if (s.Position.y > apex) apex = s.Position.y;
                }
                Assert.Near(apex, t.jumpHeight, 0.15f, "apex height");
            });

            TestRunner.Add("movement/short hop is lower than full jump", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning wt = Sim.Weapon();

                float Apex(bool hold)
                {
                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    InputCommand c = InputCommand.Default(0);
                    c.Buttons |= Buttons.Jump;
                    float apex = 0f;
                    for (int i = 0; i < 64; i++)
                    {
                        if (!hold && i > 3) c.Buttons &= ~Buttons.Jump;
                        SimEvents ev = new SimEvents();
                        MovementCore.Step(ref s, c, t, wt, Sim.Dt, w, ref ev);
                        if (s.Position.y > apex) apex = s.Position.y;
                    }
                    return apex;
                }

                Assert.Less(Apex(false), Apex(true) - 0.1f, "variable jump height");
            });

            TestRunner.Add("movement/coyote time allows late jump", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = new BoxWorld();
                w.AddBox(new Vec3(0f, -0.5f, 0f), new Vec3(4f, 1f, 4f)); // platform top y=0, edge at z=2
                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 1.5f));
                WeaponTuning wt = Sim.Weapon();
                InputCommand c = Sim.Forward();

                bool jumped = false;
                for (int i = 0; i < 64; i++)
                {
                    SimEvents ev = new SimEvents();
                    if (!s.Grounded && !jumped) c.Buttons |= Buttons.Jump;
                    MovementCore.Step(ref s, c, t, wt, Sim.Dt, w, ref ev);
                    if (ev.Jumped) jumped = true;
                }
                Assert.True(jumped, "jump fired after leaving the ledge");
            });

            TestRunner.Add("movement/wall stops forward motion", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(0f, 1.5f, 3f), new Vec3(8f, 3f, 0.5f)); // face at z=2.75
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                Sim.Run(ref s, Sim.Forward(), t, w, 2f);
                Assert.Less(s.Position.z, 2.75f - t.radius + 0.08f, "stopped at the wall");
                Assert.Greater(s.Position.z, 2.0f, "actually reached the wall");
            });

            TestRunner.Add("movement/steps up a low ledge", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(0f, 0.15f, 14f), new Vec3(8f, 0.3f, 24f)); // top y=0.3, front face z=2
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                Sim.Run(ref s, Sim.Forward(), t, w, 2f);
                Assert.Near(s.Position.y, 0.3f, 0.06f, "climbed the step");
                Assert.Greater(s.Position.z, 2.2f, "kept moving forward");
            });

            TestRunner.Add("movement/cannot stand under a low ceiling", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(0f, 1.55f, 0f), new Vec3(6f, 0.4f, 6f)); // underside at y=1.35
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand crouch = InputCommand.Default(0);
                crouch.StanceRequest = Stance.Crouch;
                Sim.Run(ref s, crouch, t, w, 0.6f);
                Assert.Equal((int)s.Stance, (int)Stance.Crouch, "crouched");

                Sim.Run(ref s, InputCommand.Default(0), t, w, 0.6f);
                Assert.Equal((int)s.Stance, (int)Stance.Crouch, "stayed crouched under the ceiling");
            });

            TestRunner.Add("movement/prone is slow and turns slowly", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = Sim.Forward();
                c.StanceRequest = Stance.Prone;
                Sim.Run(ref s, c, t, w, 2f);
                Assert.Equal((int)s.Stance, (int)Stance.Prone, "went prone");
                Assert.Near(s.Height, t.proneHeight, 0.02f, "prone height");
                Assert.Less(s.Velocity.Flat.Magnitude, t.proneSpeed + 0.1f, "prone speed cap");

                c.Yaw = 170f;
                PlayerSimState before = s;
                SimEvents ev = new SimEvents();
                MovementCore.Step(ref s, c, t, Sim.Weapon(), Sim.Dt, w, ref ev);
                float turned = MathK.Abs(MathK.DeltaAngle(before.Yaw, s.Yaw));
                Assert.Less(turned, t.proneYawRateLimit * Sim.Dt + 0.01f, "prone yaw rate limited");
            });

            TestRunner.Add("movement/side step shifts you sideways", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.StepRight;
                SimEvents ev = Sim.Run(ref s, c, t, w, 0.4f);
                Assert.True(ev.StartedSideStep, "side step started");
                Assert.Near(s.SideStep, 1f, 0.02f, "side step fully extended");
                Assert.Near(s.Position.x, t.sideStepDistance, 0.06f, "moved laterally by the tuned distance");

                Sim.Run(ref s, InputCommand.Default(0), t, w, 0.5f);
                Assert.Near(s.Position.x, 0f, 0.08f, "returns to centre on release");
            });

            TestRunner.Add("movement/side step is blocked by a wall", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(1.2f, 1.5f, 0f), new Vec3(0.5f, 3f, 8f)); // face at x=0.95
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.StepRight;
                Sim.Run(ref s, c, t, w, 0.5f);
                Assert.Less(s.Position.x, 0.95f - t.radius + 0.08f, "wall stopped the step");
            });

            TestRunner.Add("movement/mantles a waist-high ledge", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(0f, 0.5f, 10f), new Vec3(6f, 1f, 16f)); // top y=1.0, face z=2
                PlayerSimState s = Sim.Fresh(t, new Vec3(0f, 0f, 1f));
                InputCommand c = Sim.Forward();
                c.Buttons |= Buttons.Mantle;
                SimEvents ev = Sim.Run(ref s, c, t, w, 1.6f);
                Assert.True(ev.StartedMantle, "mantle triggered");
                Assert.Near(s.Position.y, 1f, 0.1f, "ended up on top of the ledge");
            });

            TestRunner.Add("movement/is deterministic for identical inputs", () =>
            {
                MovementTuning t = Sim.Tuning();
                WeaponTuning wt = Sim.Weapon();

                PlayerSimState Play()
                {
                    BoxWorld w = BoxWorld.FlatGround();
                    w.AddBox(new Vec3(0f, 0.2f, 5f), new Vec3(6f, 0.4f, 3f));
                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    for (int i = 0; i < 256; i++)
                    {
                        InputCommand c = InputCommand.Default((uint)i);
                        c.MoveY = 1f;
                        c.MoveX = MathK.Sin(i * 0.11f);
                        c.Yaw = i * 0.7f;
                        c.Pitch = MathK.Sin(i * 0.05f) * 20f;
                        c.LeanAxis = MathK.Sin(i * 0.03f);
                        if (i % 37 == 0) c.Buttons |= Buttons.Jump;
                        if (i % 11 == 0) c.Buttons |= Buttons.StepRight;
                        if (i > 120) c.StanceRequest = Stance.Crouch;
                        SimEvents ev = new SimEvents();
                        MovementCore.Step(ref s, c, t, wt, Sim.Dt, w, ref ev);
                    }
                    return s;
                }

                PlayerSimState a = Play();
                PlayerSimState b = Play();
                Assert.True(a.Position.x == b.Position.x && a.Position.y == b.Position.y && a.Position.z == b.Position.z,
                    "positions match bit for bit: " + a.Position + " vs " + b.Position);
                Assert.True(a.Velocity == b.Velocity, "velocities match bit for bit");
                Assert.True(a.Lean == b.Lean && a.SideStep == b.SideStep, "lean/step match bit for bit");
            });
        }
    }
}
