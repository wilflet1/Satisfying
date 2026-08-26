using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// The grenade's rules, which are mostly about time: how long it takes to get one out, when the
    /// fuse starts, and what happens if you die holding it. Every one of those is a place where a
    /// plausible-looking change quietly turns it into a different weapon.
    /// </summary>
    public static class GrenadeTests
    {
        static InputCommand Command(uint tick)
        {
            return InputCommand.Default(tick);
        }

        /// <summary>Runs the carry state machine forward without any of the rest of the simulation.</summary>
        static void Step(ref PlayerSimState s, InputCommand cmd, MovementTuning t, GrenadeTuning g,
                         BoxWorld world, ref SimEvents ev)
        {
            MovementCore.Step(ref s, cmd, t, WeaponTuning.DefaultLoadout()[0], null, g, Sim.Dt, world, ref ev);
        }

        /// <summary>The same command again, so its counters do not read as a fresh press.</summary>
        static InputCommand Hold(InputCommand previous)
        {
            InputCommand c = InputCommand.Default(0);
            c.GrenadeSeq = previous.GrenadeSeq;
            c.ThrowSeq = previous.ThrowSeq;
            c.ThrowHard = previous.ThrowHard;
            return c;
        }

        public static void Register()
        {
            TestRunner.Add("grenade/getting one out takes the time it says and pulls the pin at the end", () =>
            {
                MovementTuning t = new MovementTuning();
                GrenadeTuning g = new GrenadeTuning();
                BoxWorld world = BoxWorld.FlatGround();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);
                s.GrenadesLeft = 2;

                InputCommand draw = Command(1);
                draw.PressGrenade();

                SimEvents ev = new SimEvents();
                Step(ref s, draw, t, g, world, ref ev);
                Assert.True(s.Carry == GrenadeCarry.Drawing, "it starts coming out");
                Assert.True(ev.GrenadeDrawStarted, "and says so, for the noise");
                Assert.False(s.PinPulled, "the pin is still in");

                // Hold the same command - the counter has not advanced, so it is not a fresh press.
                InputCommand hold = Command(2);
                hold.GrenadeSeq = draw.GrenadeSeq;

                float elapsed = 0f;
                bool pinned = false;
                for (int i = 0; i < 400 && !pinned; i++)
                {
                    ev = new SimEvents();
                    Step(ref s, hold, t, g, world, ref ev);
                    elapsed += Sim.Dt;
                    if (ev.GrenadeInHand) pinned = true;
                }

                Assert.True(pinned, "it reaches your hand");
                Assert.Near(elapsed, g.drawTime, Sim.Dt * 2f, "after the draw time and not before");
                Assert.True(s.Carry == GrenadeCarry.Held, "and then it is in your hand");
                Assert.False(s.PinPulled, "the pin is still in - that takes a mouse button");
                Assert.Equal(s.GrenadesLeft, 2, "and nothing spent until it is thrown");
            });

            TestRunner.Add("grenade/it is not cookable - holding it costs nothing and buys nothing", () =>
            {
                MovementTuning t = new MovementTuning();
                GrenadeTuning g = new GrenadeTuning();
                BoxWorld world = BoxWorld.FlatGround();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);
                s.GrenadesLeft = 2;
                s.Carry = GrenadeCarry.Held;

                InputCommand idle = Command(1);
                SimEvents ev = new SimEvents();

                // Ten seconds with the pin out. A cookable grenade would have gone off long ago.
                for (int i = 0; i < 640; i++)
                {
                    ev = new SimEvents();
                    Step(ref s, idle, t, g, world, ref ev);
                    Assert.False(ev.GrenadeReleased, "it does not leave your hand on its own");
                }
                Assert.True(s.Carry == GrenadeCarry.Held, "still holding it after ten seconds");

                // And the fuse it gets is the whole fuse, not what is left of one.
                Assert.Near(g.fuse, 2.5f, 0.001f, "two and a half seconds, from leaving the hand");
            });

            TestRunner.Add("grenade/throwing spends one and starts the arm", () =>
            {
                MovementTuning t = new MovementTuning();
                GrenadeTuning g = new GrenadeTuning();
                BoxWorld world = BoxWorld.FlatGround();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);
                s.GrenadesLeft = 2;
                s.Carry = GrenadeCarry.Held;

                // Press: the pin comes out and the arm loads. The button stays down.
                InputCommand press = Command(1);
                press.PressThrow(true);
                press.Buttons |= Buttons.Throw;

                SimEvents ev = new SimEvents();
                Step(ref s, press, t, g, world, ref ev);
                Assert.True(s.Carry == GrenadeCarry.Primed, "the pin is out and the arm is loaded");
                Assert.True(ev.GrenadePinPulled, "and it says so, for the noise");
                Assert.Equal(s.GrenadesLeft, 2, "and it has not left yet");

                // Still holding it: nothing happens, however long you wait. Not cookable.
                InputCommand stillDown = Command(2);
                stillDown.ThrowSeq = press.ThrowSeq;
                stillDown.ThrowHard = true;
                stillDown.Buttons |= Buttons.Throw;
                for (int i = 0; i < 300; i++)
                {
                    ev = new SimEvents();
                    Step(ref s, stillDown, t, g, world, ref ev);
                    Assert.False(ev.GrenadeReleased, "holding the button does not throw it");
                }

                // Let go: that is the throw.
                InputCommand up = Command(3);
                up.ThrowSeq = press.ThrowSeq;
                up.ThrowHard = true;
                bool released = false;
                for (int i = 0; i < 200 && !released; i++)
                {
                    ev = new SimEvents();
                    Step(ref s, up, t, g, world, ref ev);
                    if (ev.GrenadeReleased) released = true;
                }

                Assert.True(released, "it leaves the hand");
                Assert.True(ev.GrenadeHard, "overarm, as thrown");
                Assert.Equal(s.GrenadesLeft, 1, "one spent");
                Assert.True(s.Carry == GrenadeCarry.Stowed, "and the weapon comes back up");
            });

            TestRunner.Add("grenade/a hard throw goes further and flatter than a lob", () =>
            {
                MovementTuning t = new MovementTuning();
                GrenadeTuning g = new GrenadeTuning();
                PlayerSimState s = PlayerSimState.Spawn(Vec3.Zero, 0f, t, WeaponTuning.DefaultLoadout()[0]);

                Vec3 hardAt, hardVelocity, softAt, softVelocity;
                GrenadeSim.Throw(in s, t, g, true, out hardAt, out hardVelocity);
                GrenadeSim.Throw(in s, t, g, false, out softAt, out softVelocity);

                Assert.True(hardVelocity.Magnitude > softVelocity.Magnitude, "harder is faster");
                Assert.True(hardVelocity.y < softVelocity.y, "and flatter");
                Assert.True(softVelocity.y > 0f, "a lob goes up");
                Assert.True(hardAt.y > 1f, "it leaves from about head height, not the floor");
            });

            TestRunner.Add("grenade/it bounces off the floor and comes to rest on it", () =>
            {
                GrenadeTuning g = new GrenadeTuning();
                BoxWorld world = BoxWorld.FlatGround();

                GrenadeState nade = new GrenadeState();
                nade.Active = true;
                nade.Position = new Vec3(0f, 3f, 0f);
                nade.Velocity = new Vec3(2f, 0f, 0f);
                nade.Fuse = 100f;

                int bounces = 0;
                for (int i = 0; i < 64 * 8; i++)
                {
                    bool bounced;
                    GrenadeSim.Step(ref nade, world, null, g, 22f, Sim.Dt, out bounced);
                    if (bounced) bounces++;
                    Assert.True(nade.Position.y > -0.5f, "it never goes through the floor");
                }

                Assert.True(bounces >= 1, "it bounced at least once");
                Assert.True(nade.Position.y < 0.4f, "and ended up on the floor, at " + nade.Position.y.ToString("0.00"));
                Assert.True(nade.Velocity.Magnitude < 1.5f, "and stopped rolling");
            });

            TestRunner.Add("grenade/a thrown one flies, is replicated, and goes off", () =>
            {
                NetHarness h = new NetHarness();
                h.Server.Tuning.match.warmupTime = 0f;
                h.Server.Tuning.match.spawnProtection = 0f;
                h.AddClient("thrower");
                h.AddClient("target");
                h.Advance(1.5f);

                // The thrower's whole input for the test: pull one out, wait, throw it overarm. The
                // counters only advance once each, which is exactly how a real press behaves.
                byte grenadeSeq = 1;
                byte throwSeq = 0;
                bool throwHeld = false;
                h.Bots[0].Behaviour = delegate(uint tick)
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.GrenadeSeq = grenadeSeq;
                    c.ThrowSeq = throwSeq;
                    c.ThrowHard = true;
                    if (throwHeld) c.Buttons |= Buttons.Throw;
                    return c;
                };

                // Long enough for the pin to come out, then let it go.
                h.Advance(h.Server.Tuning.grenade.drawTime + 0.4f);
                Assert.True(h.Clients[0].Predicted.Carry == GrenadeCarry.Held,
                    "it is in the hand after the draw, carry is " + h.Clients[0].Predicted.Carry);

                // Press and hold: pin out. Then let go, which is the throw.
                throwSeq = 1;
                throwHeld = true;
                h.Advance(0.4f);
                Assert.True(h.Clients[0].Predicted.PinPulled, "the pin is out while the button is down");

                throwHeld = false;
                h.Advance(0.6f);

                Assert.True(h.Sinks[1].GrenadeUpdates > 0,
                    "the other player is told where it is; got " + h.Sinks[1].GrenadeUpdates + " updates");

                h.Advance(h.Server.Tuning.grenade.fuse + 0.8f);
                Assert.True(h.Sinks[0].Blasts > 0, "it went off");
                Assert.True(h.Sinks[1].Blasts > 0, "and everyone heard it");
                Assert.Equal(h.Clients[0].Predicted.GrenadesLeft, h.Server.Tuning.grenade.CountInt - 1,
                    "one out of the pouch");
            });

            TestRunner.Add("grenade/dying with the pin out drops a live one", () =>
            {
                NetHarness h = new NetHarness();
                h.Server.Tuning.match.warmupTime = 0f;
                h.Server.Tuning.match.spawnProtection = 0f;
                h.Server.Tuning.match.respawnDelay = 30f;      // stay dead for the length of the test
                h.AddClient("victim");
                h.AddClient("watcher");
                h.Advance(1.5f);

                byte throwSeq = 0;
                h.Bots[0].Behaviour = delegate(uint tick)
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.GrenadeSeq = 1;                           // out, and left out
                    c.ThrowSeq = throwSeq;
                    if (throwSeq > 0) c.Buttons |= Buttons.Throw;    // and the button held down
                    return c;
                };

                h.Advance(h.Server.Tuning.grenade.drawTime + 0.4f);
                throwSeq = 1;
                h.Advance(0.4f);
                Assert.True(h.Clients[0].Predicted.PinPulled, "the pin is out");

                int before = h.Sinks[1].Blasts;

                // Killed outright, holding it. Reaching into the server is the point of the test:
                // there is no way to be shot by yourself and this is what dying looks like from the
                // inside.
                h.KillHolder(0);
                h.Advance(h.Server.Tuning.grenade.fuse + 1.0f);

                Assert.True(h.Sinks[1].Blasts > before,
                    "the grenade they were holding still goes off - that is the price of pulling it");
            });

            TestRunner.Add("grenade/the blast is flat inside the lethal radius and gone by the outer one", () =>
            {
                GrenadeTuning g = new GrenadeTuning();
                float health = new MatchTuning().maxHealth;

                Assert.True(g.DamageAt(0f) >= health, "on top of it is lethal");
                Assert.True(g.DamageAt(g.lethalRadius * 0.9f) >= health, "and anywhere inside the kill radius");
                Assert.True(g.DamageAt(g.radius + 0.01f) <= 0.01f, "and nothing at all past the outer one");

                float mid = g.DamageAt((g.lethalRadius + g.radius) * 0.5f);
                Assert.True(mid > 0f && mid < health, "halfway out it hurts without killing, at " + mid.ToString("0"));

                // Monotonic: further is never worse for you.
                float previous = float.MaxValue;
                for (float d = 0f; d <= g.radius + 1f; d += 0.25f)
                {
                    float here = g.DamageAt(d);
                    Assert.True(here <= previous + 0.001f, "damage never goes up with distance");
                    previous = here;
                }
            });
        }
    }
}
