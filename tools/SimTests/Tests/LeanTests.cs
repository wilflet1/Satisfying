using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class LeanTests
    {
        public static void Register()
        {
            TestRunner.Add("lean/reaches full lean at the tuned rate", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 1f;

                Sim.Run(ref s, c, t, w, 1f / t.leanSpeed * 0.5f);
                Assert.Near(s.Lean, 0.5f, 0.08f, "half way after half the lean time");

                Sim.Run(ref s, c, t, w, 1f);
                Assert.Near(s.Lean, 1f, 0.001f, "fully leaned");
            });

            TestRunner.Add("lean/slow lean modifier is much slower", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();

                float LeanAfter(float seconds, bool slow)
                {
                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    InputCommand c = InputCommand.Default(0);
                    c.LeanAxis = 1f;
                    if (slow) c.Buttons |= Buttons.SlowLean;
                    Sim.Run(ref s, c, t, w, seconds);
                    return s.Lean;
                }

                float fast = LeanAfter(0.1f, false);
                float slow = LeanAfter(0.1f, true);
                Assert.Less(slow, fast * 0.5f, "slow lean is at least twice as slow");
                Assert.Greater(slow, 0f, "slow lean still moves");
            });

            TestRunner.Add("lean/slow lean latches where you leave it", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                // Dial out slowly, then let go of everything. It must stay put: this is a peek you hold,
                // not a spring you fight.
                InputCommand dial = InputCommand.Default(0);
                dial.LeanAxis = 1f;
                dial.Buttons |= Buttons.SlowLean;
                Sim.Run(ref s, dial, t, w, 0.5f);

                float held = s.Lean;
                Assert.Greater(held, 0.05f, "the slow dial moved");
                Assert.Less(held, 0.99f, "and has not run to the stop yet");
                Assert.True(s.LeanLatched, "it latched");

                Sim.Run(ref s, InputCommand.Default(0), t, w, 1.5f);
                Assert.Near(s.Lean, held, 0.02f, "released, it holds instead of recentring");
            });

            TestRunner.Add("lean/a normal lean takes control back from the latch", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand dial = InputCommand.Default(0);
                dial.LeanAxis = 1f;
                dial.Buttons |= Buttons.SlowLean;
                Sim.Run(ref s, dial, t, w, 0.5f);
                Assert.True(s.LeanLatched, "latched first");

                // Tap the lean key without the modifier and release: an ordinary lean must still recentre.
                InputCommand normal = InputCommand.Default(0);
                normal.LeanAxis = 1f;
                Sim.Run(ref s, normal, t, w, 0.15f);
                Assert.False(s.LeanLatched, "the manual press dropped the latch");

                Sim.Run(ref s, InputCommand.Default(0), t, w, 1.5f);
                Assert.Near(s.Lean, 0f, 0.02f, "so it springs back to centre");
            });

            TestRunner.Add("lean/sprinting drops a latched lean", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand dial = InputCommand.Default(0);
                dial.LeanAxis = 1f;
                dial.Buttons |= Buttons.SlowLean;
                Sim.Run(ref s, dial, t, w, 0.5f);
                Assert.True(s.LeanLatched, "latched first");

                InputCommand sprint = InputCommand.Default(0);
                sprint.MoveY = 1f;
                sprint.Buttons |= Buttons.Sprint;
                Sim.Run(ref s, sprint, t, w, 1f);

                Assert.False(s.LeanLatched, "you cannot sprint with a latched peek");
                Assert.Near(s.Lean, 0f, 0.02f, "and it recentres");
            });

            TestRunner.Add("lean/analog axis holds a partial lean", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 0.42f;
                Sim.Run(ref s, c, t, w, 1f);
                Assert.Near(s.Lean, 0.42f, 0.01f, "holds the analog target");
            });

            TestRunner.Add("lean/moves the eye position sideways", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                Vec3 centred = s.EyePosition(t);

                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = -1f;
                Sim.Run(ref s, c, t, w, 1f);
                Vec3 leaned = s.EyePosition(t);

                Assert.Near(leaned.x - centred.x, -t.leanOffset, 0.01f, "head displaced left");
                Assert.Near(leaned.y - centred.y, -t.leanDrop, 0.01f, "head drops slightly");
                Assert.Near(s.ViewRoll(t), t.leanAngle, 0.01f, "camera rolls with the lean");
            });

            TestRunner.Add("lean/prone lean is a reduced roll", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.StanceRequest = Stance.Prone;
                c.LeanAxis = 1f;
                Sim.Run(ref s, c, t, w, 1.5f);

                Assert.Equal((int)s.Stance, (int)Stance.Prone, "prone");
                Assert.Near(s.Lean, 1f, 0.01f, "lean input still holds");
                Assert.Near(s.EffectiveLean(t), t.proneLeanMul, 0.01f, "prone lean scaled down");
                Assert.Less(MathK.Abs(s.ViewRoll(t)), t.leanAngle, "prone roll is smaller than a standing lean");
            });

            TestRunner.Add("lean/aiming reduces the lean", () =>
            {
                MovementTuning t = Sim.Tuning();
                t.adsLeanMul = 0.5f;
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 1f;
                c.Buttons |= Buttons.Ads;
                Sim.Run(ref s, c, t, w, 1.5f);
                Assert.Near(s.Ads, 1f, 0.01f, "aimed in");
                Assert.Near(s.EffectiveLean(t), 0.5f, 0.02f, "lean halved while aiming");
            });

            TestRunner.Add("lean/is crushed by a wall next to your head", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                w.AddBox(new Vec3(1.0f, 1.5f, 0f), new Vec3(0.5f, 3f, 4f)); // face at x=0.75, beside the head
                PlayerSimState s = Sim.Fresh(t, new Vec3(0.35f, 0f, 0f));
                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 1f;
                Sim.Run(ref s, c, t, w, 1.5f);
                Assert.Less(s.Lean, 0.95f, "lean crushed by the wall");

                Vec3 eye = s.EyePosition(t);
                Assert.Less(eye.x, 0.75f, "head never ends up inside the wall");
            });

            TestRunner.Add("lean/sprinting cancels lean", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.LeanAxis = 1f;
                Sim.Run(ref s, c, t, w, 1f);
                Assert.Near(s.Lean, 1f, 0.01f, "leaned");

                c.MoveY = 1f;
                c.Buttons |= Buttons.Sprint;
                Sim.Run(ref s, c, t, w, 0.6f);
                Assert.Near(s.Lean, 0f, 0.01f, "lean cleared by sprint");
            });

            TestRunner.Add("lean/costs movement speed", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();

                float SpeedWithLean(float lean)
                {
                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    InputCommand c = Sim.Forward();
                    c.LeanAxis = lean;
                    Sim.Run(ref s, c, t, w, 2f);
                    return s.Velocity.Flat.Magnitude;
                }

                Assert.Near(SpeedWithLean(1f), SpeedWithLean(0f) * t.leanSpeedMul, 0.2f, "full lean applies the speed penalty");
            });
        }
    }
}
