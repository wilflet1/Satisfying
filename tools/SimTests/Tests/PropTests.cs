using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class PropTests
    {
        static void Run(ref PlayerSimState s, InputCommand cmd, MovementTuning t, ICollisionWorld w,
                        WorldModel model, WorldState world, float seconds, ref SimEvents total)
        {
            WeaponTuning gun = Sim.Weapon();
            int ticks = MathK.Max(1, MathK.RoundToInt(seconds / Sim.Dt));
            for (int i = 0; i < ticks; i++)
            {
                SimEvents ev = new SimEvents();
                cmd.Tick = (uint)i;
                MovementCore.Step(ref s, cmd, t, gun, Sim.Dt, w, ref ev);
                PropSim.Step(1, ref s, cmd, t, model, world, w, Sim.Dt, ref ev);
                total.GrabbedProp |= ev.GrabbedProp;
                total.ReleasedProp |= ev.ReleasedProp;
            }
        }

        static void Setup(out MovementTuning t, out BoxWorld w, out WorldModel model, out WorldState world,
                          float mass, Vec3 propPosition)
        {
            t = Sim.Tuning();
            w = BoxWorld.FlatGround(80f);
            model = new WorldModel();
            model.AddProp(propPosition, new Vec3(0.9f, 0.9f, 0.9f), mass);
            world = new WorldState();
            world.Reset(model);
        }

        public static void Register()
        {
            TestRunner.Add("props/grabbing takes hold of what you are looking at", () =>
            {
                MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                Setup(out t, out w, out model, out world, 25f, new Vec3(0f, 0f, 1.6f));

                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Grab;

                SimEvents total = new SimEvents();
                Run(ref s, c, t, w, model, world, 0.2f, ref total);

                Assert.True(total.GrabbedProp, "took hold of it");
                Assert.Equal(world.Props[0].Grabber, 1, "and the world records who has it");
                Assert.Greater(s.CarryMass, 0f, "the player knows they are carrying weight");
            });

            TestRunner.Add("props/something behind you is not grabbable", () =>
            {
                MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                Setup(out t, out w, out model, out world, 25f, new Vec3(0f, 0f, -1.6f));

                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Grab;

                SimEvents total = new SimEvents();
                Run(ref s, c, t, w, model, world, 0.3f, ref total);

                Assert.False(total.GrabbedProp, "nothing grabbed");
                Assert.False(world.Props[0].IsHeld, "it stays where it is");
            });

            TestRunner.Add("props/a dragged object follows you", () =>
            {
                MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                Setup(out t, out w, out model, out world, 20f, new Vec3(0f, 0f, 1.6f));

                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Grab;
                SimEvents total = new SimEvents();
                Run(ref s, c, t, w, model, world, 0.2f, ref total);

                // Now walk sideways and it should come with you.
                c.MoveX = 1f;
                c.Yaw = 0f;
                Run(ref s, c, t, w, model, world, 2f, ref total);

                Assert.Greater(world.Props[0].Position.x, 1f, "the object came along, x=" + world.Props[0].Position.x);
                Assert.Less(Vec3.Distance(world.Props[0].Position.Flat, s.Position.Flat), t.grabBreakDistance,
                    "and stayed within arm's reach");
            });

            TestRunner.Add("props/heavier is slower, for it and for you", () =>
            {
                float DragDistance(float mass, out float playerSpeed)
                {
                    MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                    Setup(out t, out w, out model, out world, mass, new Vec3(0f, 0f, 1.6f));

                    PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                    InputCommand c = InputCommand.Default(0);
                    c.Buttons |= Buttons.Grab;
                    SimEvents total = new SimEvents();
                    Run(ref s, c, t, w, model, world, 0.2f, ref total);

                    Vec3 start = world.Props[0].Position;
                    c.MoveX = 1f;
                    Run(ref s, c, t, w, model, world, 1.5f, ref total);
                    playerSpeed = s.Velocity.Flat.Magnitude;
                    return Vec3.Distance(world.Props[0].Position, start);
                }

                float lightSpeed, heavySpeed;
                float light = DragDistance(15f, out lightSpeed);
                float heavy = DragDistance(140f, out heavySpeed);

                Assert.Greater(light, heavy * 1.5f, "the light one travels much further: " + light + " vs " + heavy);
                Assert.Greater(lightSpeed, heavySpeed * 1.3f, "and you move faster with it");
            });

            TestRunner.Add("props/pressing again lets go", () =>
            {
                MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                Setup(out t, out w, out model, out world, 25f, new Vec3(0f, 0f, 1.6f));

                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand hold = InputCommand.Default(0);
                hold.Buttons |= Buttons.Grab;
                SimEvents total = new SimEvents();
                Run(ref s, hold, t, w, model, world, 0.2f, ref total);
                Assert.True(world.Props[0].IsHeld, "held");

                // Release the key, then press it again.
                InputCommand idle = InputCommand.Default(0);
                Run(ref s, idle, t, w, model, world, 0.1f, ref total);
                Run(ref s, hold, t, w, model, world, 0.1f, ref total);

                Assert.False(world.Props[0].IsHeld, "let go on the second press");
                Assert.Near(s.CarryMass, 0f, 0.001f, "and the weight came off you");
            });

            TestRunner.Add("props/the grip breaks if you walk off with it stuck", () =>
            {
                MovementTuning t; BoxWorld w; WorldModel model; WorldState world;
                Setup(out t, out w, out model, out world, 400f, new Vec3(0f, 0f, 1.6f));
                t.dragSpeedBase = 0.2f;      // effectively immovable

                PlayerSimState s = Sim.Fresh(t, Vec3.Zero);
                InputCommand c = InputCommand.Default(0);
                c.Buttons |= Buttons.Grab;
                SimEvents total = new SimEvents();
                Run(ref s, c, t, w, model, world, 0.2f, ref total);
                Assert.True(world.Props[0].IsHeld, "held it");

                c.MoveY = -1f;               // walk away from it
                Run(ref s, c, t, w, model, world, 8f, ref total);

                Assert.False(world.Props[0].IsHeld, "the grip broke");
                Assert.True(total.ReleasedProp, "and said so");
            });

            TestRunner.Add("net/a dragged object is replicated to the other player", () =>
            {
                WorldModel model = new WorldModel();
                model.AddProp(new Vec3(0f, 0f, -6.4f), new Vec3(0.9f, 0.9f, 0.9f), 30f);

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, -8f), 0f);
                spawns.Add(new Vec3(10f, 0f, 10f), 0f);

                NetHarness h = new NetHarness(BoxWorld.FlatGround(), spawns, model);
                h.Server.Tuning.match.warmupTime = 0f;
                h.AddClient("alpha");
                h.AddClient("bravo");
                h.SetConditions(50f, 10f, 3f);
                h.Advance(1.5f);

                Vec3 start = h.Server.World.Props[0].Position;
                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.Yaw = 0f;
                    if (tick > 100) c.Buttons |= Buttons.Grab;      // grab, then walk sideways
                    if (tick > 140) c.MoveX = 1f;
                    return c;
                };
                h.Advance(4f);

                Assert.Greater(Vec3.Distance(h.Server.World.Props[0].Position, start), 1f,
                    "the server moved it");
                Assert.Less(Vec3.Distance(h.Clients[1].World.Props[0].Position, h.Server.World.Props[0].Position), 0.2f,
                    "and the other player sees it in the same place");
                Assert.Equal(h.Clients[1].World.Props[0].Grabber, h.Clients[0].PeerId, "and knows who has it");
            });
        }
    }
}
