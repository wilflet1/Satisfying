using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// The aiming promise. "Crouched, the ADS lineup is bad and it is hard to see anything; prone is
    /// worse" was a bug report about arithmetic, not about art: the stance settle was added after the
    /// sights were lined up, so the further you got from standing the further the front post drifted
    /// out of the rear notch. These tests pin the rule that fixes it.
    /// </summary>
    public static class ViewmodelTests
    {
        // The M4's iron sight line, in weapon space: up on the rail and a long way back from the muzzle.
        static readonly Vec3 IronSight = new Vec3(0f, 0.084f, -0.055f);

        public static void Register()
        {
            TestRunner.Add("ads/the sight lands dead centre in every stance", () =>
            {
                MovementTuning move = Sim.Tuning();
                FeelTuning feel = new FeelTuning();
                BoxWorld world = BoxWorld.FlatGround();
                Vec3 hip = new Vec3(0.155f, -0.150f, 0.30f);

                Stance[] stances = new Stance[] { Stance.Stand, Stance.Crouch, Stance.Prone };
                for (int i = 0; i < stances.Length; i++)
                {
                    PlayerSimState s = Sim.Fresh(move, Vec3.Zero);
                    InputCommand c = InputCommand.Default(0);
                    c.StanceRequest = stances[i];
                    c.Buttons |= Buttons.Ads;
                    Sim.Run(ref s, c, move, world, 2f);

                    Assert.Equal((int)s.Stance, (int)stances[i], "reached " + stances[i]);
                    Assert.Near(s.Ads, 1f, 0.001f, "fully aimed in " + stances[i]);

                    ViewmodelPose pose = ViewmodelPose.Build(in s, move, feel, hip, IronSight, false);
                    Vec3 sight = pose.SightPoint(IronSight);

                    Assert.Near(sight.x, 0f, 0.0005f, "sight on the centreline in " + stances[i]);
                    Assert.Near(sight.y, 0f, 0.0005f, "sight on the eye line in " + stances[i]);
                    Assert.Near(sight.z, feel.adsSightDistance, 0.0005f, "sight at the aiming distance in " + stances[i]);
                    Assert.Near(pose.Euler.Magnitude, 0f, 0.0005f, "weapon square to the screen in " + stances[i]);
                }
            });

            TestRunner.Add("ads/nothing you can be doing knocks the sight off centre", () =>
            {
                MovementTuning move = Sim.Tuning();
                FeelTuning feel = new FeelTuning();
                Vec3 hip = new Vec3(0.155f, -0.150f, 0.30f);

                // A player's own viewmodel offsets used to be applied at a quarter strength while aimed,
                // which meant anyone who moved the gun to taste also moved their sights off the middle.
                feel.viewmodelX = 0.06f;
                feel.viewmodelY = -0.04f;
                feel.viewmodelZ = 0.03f;

                PlayerSimState s = new PlayerSimState();
                s.Ads = 1f;
                s.Stance = Stance.Prone;
                s.Sliding = true;
                s.CarryMass = 40f;
                s.MeleeTimer = 0.05f;

                ViewmodelPose pose = ViewmodelPose.Build(in s, move, feel, hip, IronSight, true);
                Vec3 sight = pose.SightPoint(IronSight);

                Assert.Near(sight.x, 0f, 0.0005f, "centreline");
                Assert.Near(sight.y, 0f, 0.0005f, "eye line");
                Assert.Near(pose.Euler.Magnitude, 0f, 0.0005f, "square to the screen");
            });

            TestRunner.Add("ads/hip fire still gets all of its character", () =>
            {
                MovementTuning move = Sim.Tuning();
                FeelTuning feel = new FeelTuning();
                Vec3 hip = new Vec3(0.155f, -0.150f, 0.30f);

                PlayerSimState idle = new PlayerSimState();
                idle.Stance = Stance.Stand;
                ViewmodelPose plain = ViewmodelPose.Build(in idle, move, feel, hip, IronSight, false);
                Assert.Near(Vec3.Distance(plain.Position, hip), 0f, 0.0005f, "the carry position is the carry position");

                PlayerSimState sprinting = idle;
                ViewmodelPose running = ViewmodelPose.Build(in sprinting, move, feel, hip, IronSight, true);
                Assert.Greater(Vec3.Distance(running.Position, hip), 0.05f, "sprinting moves the gun");
                Assert.Greater(running.Euler.Magnitude, 5f, "and tips it");

                PlayerSimState prone = idle;
                prone.Stance = Stance.Prone;
                ViewmodelPose settled = ViewmodelPose.Build(in prone, move, feel, hip, IronSight, false);
                Assert.Greater(Vec3.Distance(settled.Position, plain.Position), 0.02f, "prone settles the gun back");
            });

            TestRunner.Add("ads/aiming blends there instead of jumping", () =>
            {
                MovementTuning move = Sim.Tuning();
                FeelTuning feel = new FeelTuning();
                Vec3 hip = new Vec3(0.155f, -0.150f, 0.30f);

                PlayerSimState s = new PlayerSimState();
                s.Stance = Stance.Crouch;

                float previous = -1f;
                for (int i = 0; i <= 10; i++)
                {
                    s.Ads = i / 10f;
                    ViewmodelPose pose = ViewmodelPose.Build(in s, move, feel, hip, IronSight, false);
                    float off = OffCentre(pose.SightPoint(IronSight));
                    if (previous >= 0f) Assert.Less(off, previous + 0.0001f, "the sight only ever moves closer to centre");
                    previous = off;
                }
                Assert.Near(previous, 0f, 0.0005f, "and arrives");
            });
        }

        /// <summary>How far the sight sits from the middle of the screen. Depth is not an error.</summary>
        static float OffCentre(Vec3 sight)
        {
            return MathK.Sqrt(sight.x * sight.x + sight.y * sight.y);
        }
    }
}
