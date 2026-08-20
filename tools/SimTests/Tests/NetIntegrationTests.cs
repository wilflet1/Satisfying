using System.Collections.Generic;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class NetIntegrationTests
    {
        static NetHarness Duel(float latencyMs, float jitterMs, float lossPercent, bool pinpointWeapons = true)
        {
            NetHarness h = new NetHarness();
            h.Server.Tuning.match.warmupTime = 0f;
            h.Server.Tuning.match.spawnProtection = 0f;
            h.Server.Tuning.match.killsToWin = 25f;
            if (pinpointWeapons)
            {
                for (int i = 0; i < h.Server.Tuning.weapons.Length; i++)
                {
                    WeaponTuning w = h.Server.Tuning.weapons[i];
                    w.spreadBase = 0f;
                    w.spreadPerShot = 0f;
                    w.spreadMovePerSpeed = 0f;
                    w.damage = 1f;          // keep both duellists alive for the whole test
                    w.headMultiplier = 1f;
                }
            }
            h.AddClient("alpha");
            h.AddClient("bravo");
            h.SetConditions(latencyMs, jitterMs, lossPercent);   // after the endpoints exist
            h.Advance(1.5f);
            return h;
        }

        static InputCommand Idle(uint tick) { return InputCommand.Default(tick); }

        /// <summary>Aims at where this client currently RENDERS the enemy - the whole point of lag compensation.</summary>
        static InputCommand AimAtEnemy(NetClient me, uint tick, bool fire)
        {
            InputCommand c = InputCommand.Default(tick);
            foreach (KeyValuePair<int, NetClient.RemotePlayer> kv in me.Remotes)
            {
                if (kv.Key == me.PeerId) continue;
                if (!kv.Value.HasRender || !kv.Value.Alive) continue;
                PlayerSimState shown = kv.Value.Render.ToDisplayState(100f);
                Vec3 target = shown.EyePosition(me.Tuning.move) + Vec3.Down * 0.25f;   // aim centre mass
                Vec3 eye = me.Predicted.EyePosition(me.Tuning.move);
                Vec3 dir = (target - eye).Normalized;
                c.Yaw = ViewMath.YawOf(dir);
                c.Pitch = ViewMath.PitchOf(dir);
                break;
            }
            if (fire) c.Buttons |= Buttons.Fire;
            return c;
        }

        public static void Register()
        {
            TestRunner.Add("net/two clients connect, spawn and see each other", () =>
            {
                NetHarness h = Duel(0f, 0f, 0f);
                Assert.True(h.Clients[0].Connected, "alpha connected");
                Assert.True(h.Clients[1].Connected, "bravo connected");
                Assert.Equal(h.Server.ActiveCount, 2, "server has both");
                Assert.True(h.Sinks[0].Spawns > 0, "alpha spawned");
                Assert.Equal(h.Clients[0].Remotes.Count, 1, "alpha sees one opponent");
                Assert.True(h.Clients[0].Remotes[h.Clients[1].PeerId].HasRender, "opponent is being rendered");
                Assert.True(h.Clients[0].Phase == MatchPhase.Live, "match went live");
            });

            TestRunner.Add("net/prediction is silent on a perfect link", () =>
            {
                NetHarness h = Duel(0f, 0f, 0f);
                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveY = 1f;
                    c.MoveX = MathK.Sin(tick * 0.07f);
                    c.Yaw = MathK.Sin(tick * 0.02f) * 40f;
                    if (tick % 64 == 0) c.Buttons |= Buttons.Jump;
                    return c;
                };
                int before = h.Clients[0].Corrections;
                h.Advance(3f);

                Assert.Less(h.ConvergenceError(h.Clients[0]), 0.02f, "client and server agree on the position");
                Assert.Less(h.Clients[0].Corrections - before, 3f, "almost no corrections on a clean link");
            });

            TestRunner.Add("net/prediction holds up at 150ms with 5% loss", () =>
            {
                NetHarness h = Duel(75f, 15f, 5f);
                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveY = 1f;
                    c.MoveX = MathK.Sin(tick * 0.05f);
                    c.LeanAxis = MathK.Sin(tick * 0.03f);
                    c.Yaw = MathK.Sin(tick * 0.01f) * 60f;
                    if (tick % 96 == 0) c.Buttons |= Buttons.Jump;
                    if (tick % 53 == 0) c.Buttons |= Buttons.StepRight;
                    return c;
                };
                h.Advance(4f);

                Assert.Less(h.ConvergenceError(h.Clients[0]), 0.06f, "still converged through loss and jitter");
                Assert.True(h.Clients[0].Rtt > 0.1f && h.Clients[0].Rtt < 0.35f, "round trip measured around 150ms, got " + h.Clients[0].Rtt);
                Assert.Less(h.Clients[0].Corrections, 100f, "corrections stay occasional rather than constant");
            });

            TestRunner.Add("net/input redundancy hides packet loss", () =>
            {
                NetHarness h = Duel(40f, 10f, 25f);
                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveY = 1f;
                    return c;
                };
                h.Advance(3f);
                NetServer.ServerPlayer sp = h.ServerPlayerOf(h.Clients[0]);
                Assert.Greater(sp.Sim.Velocity.Flat.Magnitude, 3.5f, "server still sees a player running at 25% loss");
                Assert.Less(h.ConvergenceError(h.Clients[0]), 0.1f, "converged despite quarter of the packets dying");
            });

            TestRunner.Add("net/remote players are rendered smoothly", () =>
            {
                NetHarness h = Duel(90f, 25f, 3f);
                h.Bots[1].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveX = 1f;
                    c.MoveY = MathK.Sin(tick * 0.04f);
                    return c;
                };

                NetClient watcher = h.Clients[0];
                int enemyId = h.Clients[1].PeerId;
                Vec3 last = Vec3.Zero;
                bool first = true;
                float worstJump = 0f;

                for (int i = 0; i < 240; i++)
                {
                    h.Advance(1f / 60f);
                    NetClient.RemotePlayer r = watcher.Remotes[enemyId];
                    if (!r.HasRender) continue;
                    Vec3 p = r.Render.Position;
                    if (!first)
                    {
                        float jump = Vec3.Distance(p, last);
                        if (jump > worstJump) worstJump = jump;
                    }
                    first = false;
                    last = p;
                }

                // At ~4.3 m/s a 60Hz frame covers 7cm; anything past 20cm is a visible pop.
                Assert.Less(worstJump, 0.2f, "no teleporting between frames");
            });

            TestRunner.Add("net/lag compensation registers hits on a strafing target", () =>
            {
                NetHarness h = Duel(70f, 10f, 0f);
                h.Bots[1].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveX = MathK.Sin(tick * 0.06f) > 0f ? 1f : -1f;
                    return c;
                };
                h.Bots[0].Behaviour = tick => AimAtEnemy(h.Clients[0], tick, tick % 12 == 0);
                h.Advance(4f);

                Assert.Greater(h.Sinks[0].HitsConfirmed, 8f, "shots at the rendered target actually land");
            });

            TestRunner.Add("net/turning lag compensation off makes those same shots miss", () =>
            {
                NetHarness h = Duel(70f, 10f, 0f);
                h.Server.LagCompensation = false;
                h.Bots[1].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveX = MathK.Sin(tick * 0.06f) > 0f ? 1f : -1f;
                    return c;
                };
                h.Bots[0].Behaviour = tick => AimAtEnemy(h.Clients[0], tick, tick % 12 == 0);
                h.Advance(4f);

                NetHarness compensated = Duel(70f, 10f, 0f);
                compensated.Bots[1].Behaviour = h.Bots[1].Behaviour;
                compensated.Bots[0].Behaviour = tick => AimAtEnemy(compensated.Clients[0], tick, tick % 12 == 0);
                compensated.Advance(4f);

                Assert.Less(h.Sinks[0].HitsConfirmed, compensated.Sinks[0].HitsConfirmed * 0.5f,
                    "without rewinding you have to lead the target: " + h.Sinks[0].HitsConfirmed + " vs " + compensated.Sinks[0].HitsConfirmed);
            });

            TestRunner.Add("net/lean is replicated to the other player", () =>
            {
                NetHarness h = Duel(60f, 0f, 0f);
                h.Bots[1].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.LeanAxis = 1f;
                    return c;
                };
                h.Advance(2f);

                NetClient.RemotePlayer r = h.Clients[0].Remotes[h.Clients[1].PeerId];
                Assert.True(r.HasRender, "opponent rendered");
                Assert.Near(r.Render.Lean, 1f, 0.05f, "full lean replicated");

                PlayerSimState shown = r.Render.ToDisplayState(100f);
                PlayerHitbox box = PlayerHitbox.FromState(in shown, h.Clients[0].Tuning.move);
                PlayerSimState centred = shown;
                centred.Lean = 0f;
                PlayerHitbox centredBox = PlayerHitbox.FromState(in centred, h.Clients[0].Tuning.move);
                Assert.Greater(Vec3.Distance(box.HeadCenter, centredBox.HeadCenter), 0.2f, "leaning really does move the head hitbox");
            });

            TestRunner.Add("net/host tuning changes reach the client", () =>
            {
                NetHarness h = Duel(50f, 0f, 0f);
                Assert.Near(h.Clients[0].Tuning.match.spawnProtection, 0f, 0.001f, "connect-time tuning applied");

                h.Server.Tuning.move.leanAngle = 44.5f;
                h.Server.Tuning.move.sideStepDistance = 1.4f;
                h.Server.PushTuning();
                h.Advance(1f);

                Assert.Near(h.Clients[0].Tuning.move.leanAngle, 44.5f, 0.001f, "lean angle synced live");
                Assert.Near(h.Clients[1].Tuning.move.sideStepDistance, 1.4f, 0.001f, "side step distance synced live");
            });

            TestRunner.Add("net/reliable events survive 30% packet loss", () =>
            {
                NetHarness h = Duel(60f, 20f, 30f, false);
                h.Server.Tuning.match.killsToWin = 25f;
                for (int i = 0; i < h.Server.Tuning.weapons.Length; i++)
                {
                    h.Server.Tuning.weapons[i].spreadBase = 0f;
                    h.Server.Tuning.weapons[i].spreadPerShot = 0f;
                    h.Server.Tuning.weapons[i].spreadMovePerSpeed = 0f;
                }
                h.Server.PushTuning();
                h.Advance(0.5f);

                h.Bots[0].Behaviour = tick => AimAtEnemy(h.Clients[0], tick, tick % 8 == 0);
                h.Advance(6f);

                Assert.Greater(h.Sinks[0].Kills, 0f, "kills reported through a badly lossy link");
                Assert.Greater(h.Sinks[1].Deaths, 0f, "victim was told it died");
                Assert.Greater(h.Sinks[1].Spawns, 1f, "victim respawned");
            });

            TestRunner.Add("net/first to the kill target wins the duel", () =>
            {
                NetHarness h = Duel(30f, 0f, 0f, false);
                h.Server.Tuning.match.killsToWin = 2f;
                h.Server.Tuning.match.respawnDelay = 0.4f;
                for (int i = 0; i < h.Server.Tuning.weapons.Length; i++)
                {
                    h.Server.Tuning.weapons[i].spreadBase = 0f;
                    h.Server.Tuning.weapons[i].spreadPerShot = 0f;
                    h.Server.Tuning.weapons[i].spreadMovePerSpeed = 0f;
                    h.Server.Tuning.weapons[i].damage = 200f;
                }
                h.Server.PushTuning();
                h.Advance(0.5f);

                h.Bots[0].Behaviour = tick => AimAtEnemy(h.Clients[0], tick, tick % 20 == 0);
                h.Advance(8f);

                Assert.True(h.Server.Phase == MatchPhase.Ended || h.Server.Winner == h.Clients[0].PeerId,
                    "match ended with a winner, phase=" + h.Server.Phase + " winner=" + h.Server.Winner);
            });

            TestRunner.Add("net/a leaving player is cleaned up", () =>
            {
                NetHarness h = Duel(20f, 0f, 0f);
                h.Clients[1].Disconnect();
                h.Advance(0.5f);
                Assert.Equal(h.Server.ActiveCount, 1, "server dropped the leaver");
                Assert.True(h.Sinks[0].Log.Contains("left " + h.Clients[1].PeerId), "the other client was told");
            });

            TestRunner.Add("net/bandwidth stays tiny for a 1v1", () =>
            {
                NetHarness h = Duel(20f, 0f, 0f);
                h.Bots[0].Behaviour = tick => { InputCommand c = InputCommand.Default(tick); c.MoveY = 1f; return c; };
                h.Bots[1].Behaviour = tick => { InputCommand c = InputCommand.Default(tick); c.MoveX = 1f; return c; };
                h.Advance(3f);

                Assert.Less(h.Clients[0].BytesInPerSecond, 12000f, "downstream under 12 KB/s, got " + h.Clients[0].BytesInPerSecond);
                Assert.Less(h.Clients[0].BytesOutPerSecond, 12000f, "upstream under 12 KB/s, got " + h.Clients[0].BytesOutPerSecond);
            });
        }
    }
}
