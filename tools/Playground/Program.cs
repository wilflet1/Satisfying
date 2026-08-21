using System;
using System.Collections.Generic;
using Satisfying.Shared;
using Satisfying.Tests;

namespace Satisfying.Playground
{
    /// <summary>
    /// Runs the actual game with no renderer: a real authoritative server, a real predicting client,
    /// real training bots, over a link with real latency and loss. The clock is virtual, so twenty
    /// seconds of play takes a fraction of a second and is perfectly repeatable.
    ///
    ///   dotnet run --project tools/Playground -- --seconds 20 --latency 60 --loss 3 --bots 2
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            float seconds = ArgFloat(args, "--seconds", 20f);
            float latency = ArgFloat(args, "--latency", 60f);
            float jitter = ArgFloat(args, "--jitter", 12f);
            float loss = ArgFloat(args, "--loss", 3f);
            string mode = ArgString(args, "--mode", "drill");
            bool duel = mode == "duel";
            int bots = (int)ArgFloat(args, "--bots", duel ? 2f : 1f);

            Console.WriteLine();
            Console.WriteLine("  satisfying - headless launch");
            Console.WriteLine("  ----------------------------------------------------------------");
            Console.WriteLine(string.Format("  mode {0}   tick {1} Hz   link {2:0} ms one way, {3:0} ms jitter, {4:0}% loss   bots {5}",
                mode, Protocol.TickRate, latency, jitter, loss, bots));
            Console.WriteLine();

            SpawnSet spawns = new SpawnSet();
            spawns.Add(new Vec3(0f, 0f, 4f), 180f);        // on the drill lane, facing the slide tunnels
            spawns.Add(new Vec3(-6f, 0f, 6f), 180f);
            spawns.Add(new Vec3(6f, 0f, 6f), 180f);

            NetHarness harness = new NetHarness(BuildDrillCourse(), spawns);
            harness.Server.Tuning.match.warmupTime = 0f;
            harness.Server.Tuning.match.spawnProtection = 0.5f;
            harness.Server.Tuning.match.killsToWin = 99f;
            // In drill mode the runner is meant to finish the course, not win a firefight.
            if (!duel) harness.Server.Tuning.match.maxHealth = 600f;

            NetClient player = harness.AddClient("drill runner");
            harness.SetConditions(latency, jitter, loss);
            harness.Advance(1.2f);

            // Even the drill needs one opponent present, or the match never goes live and nothing shoots.
            for (int i = 0; i < bots; i++) harness.Server.AddBot("bot " + (i + 1), duel ? 0.55f : 0.15f);

            DrillRunner drill = new DrillRunner(player);
            DuelRunner duellist = new DuelRunner(player);
            harness.Bots[0].Behaviour = duel ? duellist.Think : (System.Func<uint, InputCommand>)drill.Think;

            Counters counters = new Counters();
            player.OnPredictedTick = (cmd, ev) => counters.Observe(in ev);

            Console.WriteLine("   time   phase      player                       speed  stance   event");
            Console.WriteLine("  ----------------------------------------------------------------------");

            float reportAt = 0f;
            string lastEvent = "spawned";
            player.OnPredictedTick = (cmd, ev) =>
            {
                counters.Observe(in ev);
                if (ev.StartedSlide) lastEvent = "slide";
                else if (ev.StartedVault) lastEvent = "VAULT";
                else if (ev.StartedMantle) lastEvent = "mantle";
                else if (ev.Jumped) lastEvent = "jump";
                else if (ev.ShotsFired > 0) lastEvent = "fire";
            };

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                harness.Advance(0.25f);
                elapsed += 0.25f;

                if (elapsed < reportAt) continue;
                reportAt = elapsed + 1f;

                PlayerSimState s = player.Predicted;
                Console.WriteLine(string.Format("  {0,5:0.0}s  {1,-9} ({2,6:0.0},{3,5:0.0},{4,6:0.0})  {5,6:0.0}  {6,-7}  {7}",
                    elapsed, player.Phase, s.Position.x, s.Position.y, s.Position.z,
                    s.Velocity.Flat.Magnitude, StanceLabel(in s),
                    lastEvent.Length > 0 ? lastEvent : (duel ? "fighting" : drill.CurrentLeg)));
                lastEvent = "";
            }

            Console.WriteLine();
            Console.WriteLine("  summary");
            Console.WriteLine("  ----------------------------------------------------------------");
            Report("server ticks", harness.Server.Tick.ToString());
            Report("slides", counters.Slides.ToString());
            Report("vaults", counters.Vaults.ToString());
            Report("mantles", counters.Mantles.ToString());
            Report("jumps", counters.Jumps.ToString());
            Report("shots fired", counters.Shots.ToString());
            Report("hits confirmed", harness.Sinks[0].HitsConfirmed + "  (" + harness.Sinks[0].HeadshotsConfirmed + " head)");
            Report("kills / deaths", harness.Sinks[0].Kills + " / " + harness.Sinks[0].Deaths);
            Report("round trip", (player.Rtt * 1000f).ToString("0") + " ms");
            Report("prediction corrections", player.Corrections + "  (last " + (player.LastCorrectionError * 100f).ToString("0.0") + " cm)");
            Report("bandwidth", (player.BytesInPerSecond / 1024f).ToString("0.0") + " KB/s down, " +
                                (player.BytesOutPerSecond / 1024f).ToString("0.0") + " KB/s up");
            Report("players on server", harness.Server.ActiveCount + " (" + harness.Server.BotCount + " bots)");

            bool healthy = player.Connected && (duel
                ? harness.Sinks[0].HitsConfirmed > 0
                : counters.Slides > 0 && counters.Vaults > 0);
            Console.WriteLine();
            Console.WriteLine(healthy
                ? "  the game ran: movement, abilities, shooting and netcode all live."
                : "  something did not fire - see the counters above.");
            Console.WriteLine();
            return healthy ? 0 : 1;
        }

        static string StanceLabel(in PlayerSimState s)
        {
            if (s.Sliding) return "slide";
            if (s.Vaulting) return "vault";
            if (s.Mantling) return "mantle";
            if (s.Stance == Stance.Prone) return "prone";
            if (s.Stance == Stance.Crouch) return "crouch";
            return "stand";
        }

        static void Report(string label, string value)
        {
            Console.WriteLine("  " + label.PadRight(26) + value);
        }

        static string ArgString(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        static float ArgFloat(string[] args, string name, float fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != name) continue;
                float value;
                if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value)) return value;
            }
            return fallback;
        }

        /// <summary>
        /// A stripped copy of the test range's drill lanes: a sprint runway into a low tunnel, and a
        /// row of railings. Same dimensions as the Unity map, so the numbers mean something.
        /// </summary>
        static BoxWorld BuildDrillCourse()
        {
            BoxWorld w = BoxWorld.FlatGround(70f);

            // Slide tunnels: undersides at 1.0, which only a slide fits under.
            w.AddBox(new Vec3(0f, 1.9f, -20f), new Vec3(9f, 1.8f, 3f));
            w.AddBox(new Vec3(0f, 1.9f, -27f), new Vec3(9f, 1.8f, 2f));

            // Vault row: thin railings of rising height with floor on both sides.
            float[] rails = { 0.55f, 0.8f, 1.05f, 1.25f };
            for (int i = 0; i < rails.Length; i++)
                w.AddBox(new Vec3(0f, rails[i] * 0.5f, 14f + i * 6f), new Vec3(7f, rails[i], 0.16f));

            // Something solid to climb, so mantling has somewhere to happen too.
            w.AddBox(new Vec3(14f, 0.45f, 0f), new Vec3(3.5f, 0.9f, 7f));

            return w;
        }
    }

    sealed class Counters
    {
        public int Slides, Vaults, Mantles, Jumps, Shots;

        public void Observe(in SimEvents ev)
        {
            if (ev.StartedSlide) Slides++;
            if (ev.StartedVault) Vaults++;
            if (ev.StartedMantle) Mantles++;
            if (ev.Jumped) Jumps++;
            Shots += ev.ShotsFired;
        }
    }

    /// <summary>
    /// Duel mode: hold an angle, lean out, shoot the nearest opponent, break off and reposition.
    /// Exercises aiming, lag compensation and the damage path rather than the movement course.
    /// </summary>
    sealed class DuelRunner
    {
        readonly NetClient _client;
        DeterministicRandom _rng = new DeterministicRandom(0xD0E1F2u);
        float _timer;
        float _strafe = 1f;
        float _lean;

        public DuelRunner(NetClient client) { _client = client; }

        public InputCommand Think(uint tick)
        {
            InputCommand c = InputCommand.Default(tick);
            PlayerSimState s = _client.Predicted;

            _timer -= Protocol.TickDt;
            if (_timer <= 0f)
            {
                _timer = 0.8f + _rng.NextFloat() * 1.6f;
                _strafe = _rng.NextFloat() > 0.5f ? 1f : -1f;
                float roll = _rng.NextFloat();
                _lean = roll < 0.3f ? -1f : (roll < 0.6f ? 1f : 0f);
            }

            c.MoveX = _strafe * 0.85f;
            c.LeanAxis = _lean;

            Vec3 eye = s.EyePosition(_client.Tuning.move);
            bool haveTarget = false;
            foreach (KeyValuePair<int, NetClient.RemotePlayer> kv in _client.Remotes)
            {
                if (kv.Key == _client.PeerId || !kv.Value.HasRender || !kv.Value.Alive) continue;
                PlayerSimState shown = kv.Value.Render.ToDisplayState(100f);
                Vec3 target = shown.EyePosition(_client.Tuning.move) + Vec3.Down * 0.22f;
                Vec3 dir = (target - eye).Normalized;
                c.Yaw = ViewMath.YawOf(dir);
                c.Pitch = ViewMath.PitchOf(dir);
                haveTarget = true;
                break;
            }

            if (haveTarget)
            {
                c.Buttons |= Buttons.Ads;
                if (tick % 16 < 6) c.Buttons |= Buttons.Fire;
            }
            else
            {
                c.MoveY = 1f;
            }

            if (s.Weapon.Ammo <= 0) c.Buttons |= Buttons.Reload;
            return c;
        }
    }

    /// <summary>
    /// Drives the client around the drill course on a loop as a series of legs: sprint the runway,
    /// slide the tunnels, vault the railing row both ways, climb the block, go home, repeat.
    /// </summary>
    sealed class DrillRunner
    {
        struct Leg
        {
            public Vec3 Target;
            public bool Sprint;
            public bool Slide;
            public bool Traverse;
            public float Timeout;
            public string Name;
        }

        static readonly Leg[] Course =
        {
            Leg2(new Vec3(0f, 0f, -14f), true,  false, false, 7f,  "runway"),
            Leg2(new Vec3(0f, 0f, -31f), true,  true,  false, 6f,  "slide tunnels"),
            Leg2(new Vec3(9f, 0f, -31f), true,  false, false, 5f,  "step off the lane"),
            Leg2(new Vec3(9f, 0f, 8f),   true,  false, false, 10f, "return"),
            Leg2(new Vec3(0f, 0f, 10f),  true,  false, false, 6f,  "line up"),
            Leg2(new Vec3(0f, 0f, 36f),  false, false, true,  12f, "vault row"),
            Leg2(new Vec3(0f, 0f, 8f),   false, false, true,  12f, "vault back"),
            Leg2(new Vec3(13f, 0f, 0f),  true,  false, false, 8f,  "approach the block"),
            Leg2(new Vec3(17f, 0f, 0f),  false, false, true,  6f,  "climb it"),
            Leg2(new Vec3(0f, 0f, 4f),   true,  false, false, 10f, "home")
        };

        static Leg Leg2(Vec3 target, bool sprint, bool slide, bool traverse, float timeout, string name)
        {
            Leg l;
            l.Target = target;
            l.Sprint = sprint;
            l.Slide = slide;
            l.Traverse = traverse;
            l.Timeout = timeout;
            l.Name = name;
            return l;
        }

        readonly NetClient _client;
        int _leg;
        float _legTime;
        float _stuckTime;
        bool _slidePressed;

        public DrillRunner(NetClient client) { _client = client; }

        public string CurrentLeg { get { return Course[_leg % Course.Length].Name; } }

        public InputCommand Think(uint tick)
        {
            InputCommand c = InputCommand.Default(tick);
            PlayerSimState s = _client.Predicted;
            Leg leg = Course[_leg % Course.Length];
            _legTime += Protocol.TickDt;

            Vec3 toTarget = (leg.Target - s.Position).Flat;
            float distance = toTarget.Magnitude;
            c.Yaw = distance > 0.2f ? ViewMath.YawOf(toTarget.Normalized) : s.Yaw;
            c.MoveY = 1f;
            if (leg.Sprint) c.Buttons |= Buttons.Sprint;
            if (leg.Traverse)
            {
                c.Buttons |= Buttons.Mantle;
                AimAtSomething(ref c, in s, c.Yaw, 40f);
                if (tick % 22 < 5) c.Buttons |= Buttons.Fire;
            }

            if (leg.Slide)
            {
                // One press, once we are actually fast enough for it to become a slide.
                if (!_slidePressed && s.Velocity.Flat.Magnitude >= _client.Tuning.move.slideMinSpeed)
                {
                    _slidePressed = true;
                    c.StanceRequest = Stance.Crouch;
                }
                else if (_slidePressed && s.Sliding)
                {
                    c.StanceRequest = Stance.Crouch;
                }
            }

            // Give up on a leg if we are wedged: a drill that can get stuck is not a useful smoke test.
            _stuckTime = s.Velocity.Flat.Magnitude < 0.3f && !s.Mantling ? _stuckTime + Protocol.TickDt : 0f;

            bool arrived = distance < 2.5f || _legTime > leg.Timeout || _stuckTime > 1.5f;
            if (!arrived) return c;

            _leg++;
            _legTime = 0f;
            _stuckTime = 0f;
            _slidePressed = false;
            return c;
        }

        /// <summary>
        /// Point at an opponent if one is being rendered and is close to the direction we are already
        /// running, so aiming never derails the drill.
        /// </summary>
        void AimAtSomething(ref InputCommand c, in PlayerSimState self, float preferredYaw, float maxDeviation)
        {
            Vec3 eye = self.EyePosition(_client.Tuning.move);
            foreach (KeyValuePair<int, NetClient.RemotePlayer> kv in _client.Remotes)
            {
                if (kv.Key == _client.PeerId || !kv.Value.HasRender || !kv.Value.Alive) continue;
                PlayerSimState shown = kv.Value.Render.ToDisplayState(100f);
                Vec3 target = shown.EyePosition(_client.Tuning.move) + Vec3.Down * 0.2f;
                if (Vec3.Distance(target, eye) > 45f) continue;

                Vec3 dir = (target - eye).Normalized;
                float yaw = ViewMath.YawOf(dir);
                if (MathK.Abs(MathK.DeltaAngle(preferredYaw, yaw)) > maxDeviation) continue;
                c.Yaw = yaw;
                c.Pitch = ViewMath.PitchOf(dir);
                return;
            }
        }
    }
}
