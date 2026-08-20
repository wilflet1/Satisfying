using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class SerializationTests
    {
        public static void Register()
        {
            TestRunner.Add("net/bit buffer round trips mixed data", () =>
            {
                NetBuffer b = new NetBuffer(256);
                b.ResetWrite();
                b.WriteBits(5u, 3);
                b.WriteBool(true);
                b.WriteBool(false);
                b.WriteUInt(0xDEADBEEF);
                b.WriteFloat(-12.375f);
                b.WriteQ(0.25f, 0f, 1f, 8);
                b.WriteString("duel");
                b.WriteVec3(new Vec3(1.5f, -2.25f, 3f));

                byte[] packet = b.ToArray();
                NetBuffer r = new NetBuffer(256);
                r.ResetRead(packet, packet.Length);
                Assert.Equal((int)r.ReadBits(3), 5, "bits");
                Assert.True(r.ReadBool(), "true bool");
                Assert.False(r.ReadBool(), "false bool");
                Assert.True(r.ReadUInt() == 0xDEADBEEF, "uint");
                Assert.Near(r.ReadFloat(), -12.375f, 0f, "float");
                Assert.Near(r.ReadQ(0f, 1f, 8), 0.25f, 0.005f, "quantised");
                Assert.True(r.ReadString() == "duel", "string");
                Assert.True(r.ReadVec3() == new Vec3(1.5f, -2.25f, 3f), "vector");
            });

            TestRunner.Add("net/input command round trips inside quantisation error", () =>
            {
                InputCommand c = InputCommand.Default(1000);
                c.MoveX = -0.5f;
                c.MoveY = 1f;
                c.Yaw = 271.3f;
                c.Pitch = -37.5f;
                c.LeanAxis = 0.62f;
                c.SpeedDial = 0.5f;
                c.StanceRequest = Stance.Prone;
                c.WeaponIndex = 2;
                c.Buttons = Buttons.Fire | Buttons.Ads | Buttons.SlowLean | Buttons.StepLeft;
                c.RenderTick = 993.5f;

                NetBuffer b = new NetBuffer(64);
                b.ResetWrite();
                c.Write(b, 1002);
                byte[] packet = b.ToArray();
                Assert.Less(packet.Length, 16f, "command packs into 15 bytes or fewer");

                NetBuffer r = new NetBuffer(64);
                r.ResetRead(packet, packet.Length);
                InputCommand d = InputCommand.Read(r, 1002);

                Assert.Equal((int)d.Tick, 1000, "tick");
                Assert.Near(d.MoveX, c.MoveX, 0.01f, "moveX");
                Assert.Near(d.MoveY, c.MoveY, 0.01f, "moveY");
                Assert.Near(d.Yaw, c.Yaw, 0.01f, "yaw");
                Assert.Near(d.Pitch, c.Pitch, 0.01f, "pitch");
                Assert.Near(d.LeanAxis, c.LeanAxis, 0.01f, "lean");
                Assert.Near(d.SpeedDial, c.SpeedDial, 0.02f, "speed dial");
                Assert.Equal((int)d.StanceRequest, (int)Stance.Prone, "stance");
                Assert.Equal(d.WeaponIndex, 2, "weapon");
                Assert.True(d.Buttons == c.Buttons, "buttons");
                Assert.Near(d.RenderTick, c.RenderTick, 0.02f, "render tick");
            });

            TestRunner.Add("tuning/text round trip preserves every field", () =>
            {
                GameTuning a = new GameTuning();
                a.move.walkSpeed = 5.55f;
                a.move.leanAngle = 31.25f;
                a.move.sideStepDistance = 1.11f;
                a.match.killsToWin = 7f;
                a.weapons[1].rpm = 1111f;

                string text = TuningSerializer.ToText(a);
                GameTuning b = new GameTuning();
                TuningSerializer.FromText(b, text);

                Assert.Near(b.move.walkSpeed, 5.55f, 0f, "walk speed survived");
                Assert.Near(b.move.leanAngle, 31.25f, 0f, "lean angle survived");
                Assert.Near(b.move.sideStepDistance, 1.11f, 0f, "side step survived");
                Assert.Near(b.match.killsToWin, 7f, 0f, "match rule survived");
                Assert.Near(b.weapons[1].rpm, 1111f, 0f, "weapon field survived");
            });

            TestRunner.Add("tuning/exposes every knob to the in-game panel", () =>
            {
                GameTuning g = new GameTuning();
                var fields = TuningSerializer.Collect(g);
                Assert.Greater(fields.Count, 60f, "plenty of live knobs");

                bool foundLean = false, foundWeapon = false;
                foreach (var f in fields)
                {
                    if (f.Path == "move.leanAngle") { foundLean = true; Assert.True(f.Category == "Lean", "lean category"); }
                    if (f.Path.StartsWith("weapons[0].")) foundWeapon = true;
                    Assert.True(f.Max > f.Min, "sane slider range for " + f.Path);
                }
                Assert.True(foundLean, "lean angle is editable at runtime");
                Assert.True(foundWeapon, "weapon fields are editable at runtime");
            });

            TestRunner.Add("tuning/set through reflection clamps to the slider range", () =>
            {
                GameTuning g = new GameTuning();
                var fields = TuningSerializer.Collect(g);
                foreach (var f in fields)
                {
                    if (f.Path != "move.leanAngle") continue;
                    f.Set(9999f);
                    Assert.Near(g.move.leanAngle, f.Max, 0f, "clamped to max");
                    f.Set(-9999f);
                    Assert.Near(g.move.leanAngle, f.Min, 0f, "clamped to min");
                }
            });
        }
    }
}
