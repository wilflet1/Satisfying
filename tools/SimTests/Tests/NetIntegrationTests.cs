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
                PlayerHitbox box = PlayerHitbox.FromState(in shown, h.Clients[0].Tuning.move, h.Clients[0].Tuning.Weapon(shown.Weapon.Index));
                PlayerSimState centred = shown;
                centred.Lean = 0f;
                PlayerHitbox centredBox = PlayerHitbox.FromState(in centred, h.Clients[0].Tuning.move, h.Clients[0].Tuning.Weapon(centred.Weapon.Index));
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

            TestRunner.Add("net/a respawn does not cost a burst of corrections", () =>
            {
                // The server keeps acking inputs from the life that just ended for about half a round
                // trip. Those acks describe a body that no longer exists, so comparing the prediction
                // against them used to force a full snap on every packet until the ack caught up.
                NetHarness h = Duel(90f, 10f, 2f, false);
                h.Server.Tuning.match.killsToWin = 99f;
                h.Server.Tuning.match.respawnDelay = 0.6f;
                h.Server.Tuning.match.spawnProtection = 0f;
                for (int i = 0; i < h.Server.Tuning.weapons.Length; i++)
                {
                    h.Server.Tuning.weapons[i].spreadBase = 0f;
                    h.Server.Tuning.weapons[i].spreadPerShot = 0f;
                    h.Server.Tuning.weapons[i].spreadMovePerSpeed = 0f;
                    h.Server.Tuning.weapons[i].damage = 200f;
                }
                h.Server.PushTuning();
                h.Advance(0.6f);

                h.Bots[0].Behaviour = tick => { InputCommand c = InputCommand.Default(tick); c.MoveX = 0.6f; return c; };
                h.Bots[1].Behaviour = tick => AimAtEnemy(h.Clients[1], tick, tick % 24 == 0);
                h.Advance(1.5f);

                int before = h.Clients[0].Corrections;
                int deathsBefore = h.Sinks[0].Deaths;
                h.Advance(8f);

                Assert.Greater(h.Sinks[0].Deaths - deathsBefore, 1.5f, "the victim died more than once");
                Assert.Equal(h.Clients[0].HistoryMisses, 0, "no ack landed outside the prediction history");

                float perDeath = (h.Clients[0].Corrections - before) / (float)MathK.Max(1, h.Sinks[0].Deaths - deathsBefore);
                Assert.Less(perDeath, 4f, "corrections per death stayed low, got " + perDeath);
            });

            TestRunner.Add("net/someone joining a server that has been up a while can still move", () =>
            {
                // A client starts its tick counter at the server's, so a late joiner's first command is
                // numbered in the thousands. The server used to sanity check that against a counter
                // still sitting at zero and throw every one of them away: you connected, you saw the
                // map, and nothing you pressed did anything.
                NetHarness h = new NetHarness();
                h.Server.Tuning.match.warmupTime = 0f;
                h.Advance(45f);                                   // let the server get well under way
                Assert.Greater(h.Server.Tick, 2000f, "server has a high tick");

                NetClient late = h.AddClient("latecomer");
                Assert.True(h.WaitForConnect(), "connected");
                h.Advance(0.5f);

                Vec3 start = h.ServerPlayerOf(late).Sim.Position;
                h.Bots[0].Behaviour = tick => { InputCommand c = InputCommand.Default(tick); c.MoveY = 1f; return c; };
                h.Advance(2f);

                Assert.Greater(Vec3.Distance(h.ServerPlayerOf(late).Sim.Position, start), 2f,
                    "the server acted on their input");
                Assert.True(h.ServerPlayerOf(late).InputStarted, "and adopted their tick numbering");
            });

            TestRunner.Add("net/a client that never hears back gives up and says so", () =>
            {
                // Connecting had no timeout: it retried every 250 ms forever and reported nothing,
                // which is exactly what an endless loading screen is made of.
                NetHarness h = new NetHarness();
                NetClient client = h.AddClient("nobody home");

                // Everything the client sends falls on the floor, so the server never answers.
                h.ClientTransports[0].Conditions.lossPercent = 100f;
                h.Advance(Protocol.ConnectTimeoutSeconds + 2f);

                Assert.True(client.State == NetClient.Status.Disconnected, "gave up, got " + client.State);
                Assert.True(client.LastDisconnectReason == DisconnectReason.Timeout, "and said why");
                Assert.Greater(client.ConnectAttempts, 10f, "having actually tried, got " + client.ConnectAttempts);
                Assert.Equal(h.Server.ConnectAttemptsSeen, 0, "and the server saw none of them");
            });

            TestRunner.Add("net/the host can see that connection attempts are arriving", () =>
            {
                NetHarness h = new NetHarness();
                NetClient client = h.AddClient("knocker");
                Assert.True(h.WaitForConnect(), "connected");

                Assert.Greater(h.Server.ConnectAttemptsSeen, 0.5f, "the server counted the attempt");
                Assert.True(h.Server.LastConnectResult == "accepted", "and says what came of it");
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

            TestRunner.Add("net/a training bot plays a real game", () =>
            {
                NetHarness h = new NetHarness();
                h.Server.Tuning.match.warmupTime = 0f;
                h.Server.Tuning.match.spawnProtection = 0f;
                h.Server.Tuning.match.killsToWin = 50f;
                h.AddClient("alpha");
                h.Advance(1.5f);

                // Pinned seed: what this bot does must not depend on which id it happened to get.
                NetServer.ServerPlayer bot = h.Server.AddBot("training bot", 0.9f, 12345);
                Assert.True(bot != null, "bot joined");
                Vec3 spawn = bot.Sim.Position;

                h.Advance(10f);

                Assert.True(h.Server.Phase == MatchPhase.Live, "a bot makes the match live, phase=" + h.Server.Phase);
                Assert.Greater(Vec3.Distance(bot.Sim.Position, spawn), 2f, "the bot actually moved");
                Assert.True(h.Clients[0].Remotes.ContainsKey(bot.PeerId), "the client sees the bot");
                Assert.True(h.Clients[0].Remotes[bot.PeerId].HasRender, "and renders it");
                Assert.Greater(bot.Sim.Weapon.ShotIndex, 0f, "the bot took shots at the player");

                h.Server.RemoveBots();
                h.Advance(0.5f);
                Assert.Equal(h.Server.BotCount, 0, "bots cleared");
            });

            TestRunner.Add("net/a joining human takes a bot's slot rather than colliding", () =>
            {
                NetHarness h = new NetHarness();
                h.Server.Tuning.match.warmupTime = 0f;
                h.AddClient("alpha");
                h.Advance(1f);

                // Force the bot onto the id the next client will be handed.
                for (int i = 0; i < 6; i++) h.Server.AddBot("bot " + i, 0.4f);
                h.Advance(0.5f);

                h.AddClient("bravo");
                h.Advance(2f);

                Assert.True(h.Clients[1].Connected, "the second human still got in");
                Assert.True(h.Clients[0].PeerId != h.Clients[1].PeerId, "the two humans have distinct ids");
            });


            TestRunner.Add("net/a slide is replicated to the other player", () =>
            {
                NetHarness h = Duel(50f, 10f, 0f);
                h.Bots[1].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveY = 1f;
                    c.Buttons |= Buttons.Sprint;
                    // One crouch press, once the sprint is up to speed.
                    if (tick > 120 && tick < 200) c.StanceRequest = Stance.Crouch;
                    return c;
                };

                bool sawSlide = false;
                float lowestHeight = 99f;
                for (int i = 0; i < 200; i++)
                {
                    h.Advance(1f / 60f);
                    NetClient.RemotePlayer r = h.Clients[0].Remotes[h.Clients[1].PeerId];
                    if (!r.HasRender) continue;
                    if (r.Render.Sliding) sawSlide = true;
                    lowestHeight = MathK.Min(lowestHeight, r.Render.Height);
                }

                Assert.True(sawSlide, "the opponent's slide came over the wire");
                Assert.Less(lowestHeight, h.Server.Tuning.move.crouchHeight, "and they really did get lower than a crouch");
            });

            TestRunner.Add("net/a vault is replicated and lands both sides in the same place", () =>
            {
                BoxWorld world = BoxWorld.FlatGround(80f);
                world.AddBox(new Vec3(0f, 0.5f, 12f), new Vec3(12f, 1f, 0.2f));   // railing

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, 8f), 0f);
                spawns.Add(new Vec3(6f, 0f, 8f), 0f);

                NetHarness h = new NetHarness(world, spawns);
                h.Server.Tuning.match.warmupTime = 0f;
                h.AddClient("alpha");
                h.AddClient("bravo");
                h.SetConditions(60f, 10f, 2f);
                h.Advance(1.5f);

                h.Bots[0].Behaviour = tick =>
                {
                    InputCommand c = InputCommand.Default(tick);
                    c.MoveY = 1f;
                    c.Buttons |= Buttons.Mantle;
                    return c;
                };

                bool sawVault = false;
                for (int i = 0; i < 200; i++)
                {
                    h.Advance(1f / 60f);
                    NetClient.RemotePlayer r = h.Clients[1].Remotes[h.Clients[0].PeerId];
                    if (r.HasRender && r.Render.Vaulting) sawVault = true;
                }

                Assert.True(sawVault, "the opponent's vault came over the wire");
                NetServer.ServerPlayer sp = h.ServerPlayerOf(h.Clients[0]);
                Assert.Greater(sp.Sim.Position.z, 12.2f, "the server agrees they are past the railing");
                Assert.Less(h.ConvergenceError(h.Clients[0]), 0.06f, "and the client predicted the same traversal");
            });


            TestRunner.Add("net/a server fills every seat it says it has", () =>
            {
                // Bots were allocated ids counting down from MaxPlayers - 1, so id 6 was never handed
                // out and a server that advertised six players could only ever hold five. Found by
                // asking the Playground for five bots and getting four.
                NetHarness h = Duel(0f, 0f, 0f);
                int seats = Protocol.MaxPlayers - h.Server.ActiveCount;
                for (int i = 0; i < seats; i++)
                    Assert.True(h.Server.AddBot("bot " + i) != null, "seat " + i + " was free and was taken");

                Assert.Equal(h.Server.ActiveCount, Protocol.MaxPlayers, "every seat is full");
                Assert.True(h.Server.AddBot("one too many") == null, "and there is not another one");
            });
        }
    }
}
