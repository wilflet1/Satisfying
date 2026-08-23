using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class MeleeTests
    {
        public static void Register()
        {
            TestRunner.Add("melee/a swing lands on one exact tick", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = Sim.Weapon();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand c = InputCommand.Default(0);
                c.PressMelee();

                int swings = 0, strikes = 0;
                for (int i = 0; i < 64; i++)
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    if (ev.MeleeSwing) swings++;
                    if (ev.MeleeStrike) strikes++;
                }

                Assert.Equal(swings, 1, "one swing from a held button");
                Assert.Equal(strikes, 1, "and exactly one strike tick");
            });

            TestRunner.Add("melee/commits you: no firing, no aiming", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = Sim.Weapon();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand c = InputCommand.Default(0);
                c.PressMelee();
                c.Buttons |= Buttons.Fire | Buttons.Ads;

                int shots = 0;
                float peakAds = 0f;
                for (int i = 0; i < 24; i++)   // inside the swing
                {
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    shots += ev.ShotsFired;
                    peakAds = MathK.Max(peakAds, s.Ads);
                }

                Assert.True(s.IsSwinging, "still swinging");
                Assert.Equal(shots, 0, "the trigger does nothing mid swing");
                Assert.Less(peakAds, 0.05f, "and you cannot aim through it");
            });

            TestRunner.Add("melee/costs stamina and has a cooldown", () =>
            {
                MovementTuning t = Sim.Tuning();
                BoxWorld w = BoxWorld.FlatGround();
                WeaponTuning gun = Sim.Weapon();
                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);

                InputCommand c = InputCommand.Default(0);
                int strikes = 0;
                for (int i = 0; i < 64; i++)
                {
                    // Tap it every other tick: without a cooldown this would be a blender.
                    if (i % 2 == 0) c.PressMelee();
                    SimEvents ev = new SimEvents();
                    c.Tick = (uint)i;
                    MovementCore.Step(ref s, c, t, gun, Sim.Dt, w, ref ev);
                    if (ev.MeleeStrike) strikes++;
                }

                Assert.Less(strikes, 3f, "cooldown limits the rate, got " + strikes);
                Assert.Less(s.Stamina, t.staminaMax, "swings cost stamina");
            });

            TestRunner.Add("world/a ray finds the nearest intact pane and ignores broken ones", () =>
            {
                WorldModel model = new WorldModel();
                model.AddWindow(new Vec3(0f, 1.2f, 4f), new Vec3(2f, 1.4f, 0.08f));
                model.AddWindow(new Vec3(0f, 1.2f, 8f), new Vec3(2f, 1.4f, 0.08f));

                WorldState world = new WorldState();
                world.Reset(model);

                int index;
                float distance;
                Assert.True(world.RaycastWindows(model, new Vec3(0f, 1.2f, 0f), Vec3.Forward, 20f, out index, out distance),
                    "found glass");
                Assert.Equal(index, 0, "the near pane");
                Assert.Near(distance, 3.96f, 0.02f, "at the right distance");

                world.WindowBroken[0] = true;
                Assert.True(world.RaycastWindows(model, new Vec3(0f, 1.2f, 0f), Vec3.Forward, 20f, out index, out distance),
                    "still finds the far one");
                Assert.Equal(index, 1, "because the near one is gone");
            });

            TestRunner.Add("net/a stock to the glass breaks it for both players", () =>
            {
                WorldModel model = new WorldModel();
                model.AddWindow(new Vec3(0f, 1.2f, -6.5f), new Vec3(3f, 1.6f, 0.1f));

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, -5f), 180f);   // facing the pane
                spawns.Add(new Vec3(9f, 0f, 9f), 0f);

                NetHarness h = new NetHarness(BoxWorld.FlatGround(), spawns, model);
                h.Server.Tuning.match.warmupTime = 0f;
                h.AddClient("alpha");
                h.AddClient("bravo");
                h.SetConditions(50f, 10f, 0f);
                h.Advance(1.5f);

                bool swung = false;
                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.Yaw = 180f;
                    if (tick % 40 == 0) { c.PressMelee(); swung = true; }
                    return c;
                };
                h.Advance(2.5f);

                Assert.True(swung, "the bot swung");
                Assert.True(h.Server.World.WindowBroken[0], "the server broke the pane");
                Assert.True(h.Clients[1].World.WindowBroken[0], "and the other player was told");
                Assert.Greater(h.Sinks[1].WindowsBroken, 0f, "with an event to hang effects off");
            });
        }
    }
}
