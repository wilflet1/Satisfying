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

            // A sink that records exactly what came out the other end, so an event's fields can be
            // checked rather than assumed. Every one of these has a client drawing something from it.
            TestRunner.Add("events/a hit confirm carries where the round landed", () =>
            {
                RecordingSink sink = new RecordingSink();
                GameEvents.Dispatch(
                    GameEvents.HitConfirm(3, HitZone.Neck, 41.5f, false, new Vec3(-12.5f, 1.75f, 30.25f)), sink);

                Assert.Equal(sink.HitTarget, 3, "target");
                Assert.True(sink.HitZone == HitZone.Neck, "zone");
                Assert.Near(sink.HitDamage, 41.5f, 0.2f, "damage");
                Assert.False(sink.HitKilled, "not a kill");
                Assert.Near(sink.HitPoint.x, -12.5f, 0.01f, "point x");
                Assert.Near(sink.HitPoint.y, 1.75f, 0.01f, "point y");
                Assert.Near(sink.HitPoint.z, 30.25f, 0.01f, "point z");
            });

            TestRunner.Add("events/being damaged says what did it", () =>
            {
                RecordingSink sink = new RecordingSink();
                GameEvents.Dispatch(GameEvents.Damaged(2, HitZone.Head, 2, 62f, false), sink);

                Assert.Equal(sink.DamagedBy, 2, "attacker");
                Assert.True(sink.DamagedZone == HitZone.Head, "zone");
                Assert.Equal(sink.DamagedWeapon, 2, "weapon index");
                Assert.False(sink.DamagedKilled, "survived");
            });

            TestRunner.Add("events/a shot says whether it landed on a man", () =>
            {
                RecordingSink sink = new RecordingSink();
                GameEvents.Dispatch(
                    GameEvents.Shot(1, Vec3.Zero, new Vec3(0f, 0f, 1f), 0, true, new Vec3(0f, 1.5f, 8f), true), sink);
                Assert.True(sink.ShotHitPlayer, "hit a player");

                RecordingSink wall = new RecordingSink();
                GameEvents.Dispatch(
                    GameEvents.Shot(1, Vec3.Zero, new Vec3(0f, 0f, 1f), 0, true, new Vec3(0f, 1.5f, 8f), false), wall);
                Assert.False(wall.ShotHitPlayer, "hit the wall");

                // A miss writes no point at all, and reading one that is not there is how a buffer
                // overrun becomes a mystery crash six months later.
                RecordingSink miss = new RecordingSink();
                GameEvents.Dispatch(
                    GameEvents.Shot(1, Vec3.Zero, new Vec3(0f, 0f, 1f), 0, false, Vec3.Zero, false), miss);
                Assert.False(miss.ShotHitPlayer, "a miss hit nobody");
            });

            TestRunner.Add("events/a head hit that kills you does not also concuss you", () =>
            {
                // The client gates the blur on this bit, so it is the bit that matters.
                RecordingSink sink = new RecordingSink();
                GameEvents.Dispatch(GameEvents.Damaged(1, HitZone.Head, 0, 200f, true), sink);
                Assert.True(sink.DamagedKilled, "killed");
            });

            TestRunner.Add("weapons/a rifle rings your helmet harder and longer than a pistol", () =>
            {
                WeaponTuning[] loadout = WeaponTuning.DefaultLoadout();
                WeaponTuning rifle = loadout[0];
                WeaponTuning pistol = loadout[2];

                Assert.True(rifle.concussionTime > pistol.concussionTime, "rifle lasts longer");
                Assert.True(rifle.concussionStrength > pistol.concussionStrength, "rifle is worse");
                Assert.True(pistol.concussionTime > 0f, "a pistol still does something");

                // And it has to be survivable at full health, or the effect never plays.
                float head = ShotSolver.Damage(rifle, HitZone.Head, 30f);
                Assert.True(head < 100f, "a head hit at range is survivable");
            });

            TestRunner.Add("weapons/a rifle carries further than a boot", () =>
            {
                WeaponTuning[] loadout = WeaponTuning.DefaultLoadout();
                Assert.True(loadout[0].soundCarry > loadout[2].soundCarry, "rifle over pistol");
                Assert.True(loadout[2].soundCarry > 25f, "a pistol still beats a footstep");
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
                // Sixteen, since grenades: a draw counter, a throw counter and the overarm bit are
                // seven more. Ten copies of every command ride in each packet, so a byte here is ten
                // bytes on the wire at 64 Hz - about 0.6 KB/s up, which is what a grenade costs.
                Assert.Less(packet.Length, 17f, "command packs into 16 bytes or fewer");

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

    /// <summary>Keeps whatever it was told, so a dispatched event can be inspected field by field.</summary>
    sealed class RecordingSink : IEventSink
    {
        public int HitTarget = -1;
        public HitZone HitZone;
        public float HitDamage;
        public bool HitKilled;
        public Vec3 HitPoint;

        public int DamagedBy = -1;
        public HitZone DamagedZone;
        public int DamagedWeapon = -1;
        public bool DamagedKilled;

        public bool ShotHitPlayer;

        public void OnPlayerJoined(int peerId, string name) { }
        public void OnPlayerLeft(int peerId, DisconnectReason reason) { }
        public void OnSpawn(int peerId, Vec3 position, float yaw) { }
        public void OnDeath(int victim, int killer, HitZone zone, float distance) { }
        public void OnTargetHit(HitZone zone, float distance) { }
        public void OnScore(int peerId, int kills, int deaths) { }
        public void OnMatchPhase(MatchPhase phase, float timer, int winner) { }

        public int ZoneIndex = -1;
        public float ZoneLeft;
        public int ZoneHolder;
        public int GrenadeId = -1;
        public Vec3 GrenadeAt;
        public float GrenadeFuse;
        public SurfaceKind GrenadeSurface;
        public Vec3 BlastAt;
        public int Blasts;

        public void OnGrenade(int id, int owner, Vec3 position, float fuse, int bounces, SurfaceKind surface)
        {
            GrenadeId = id; GrenadeAt = position; GrenadeFuse = fuse; GrenadeSurface = surface;
        }

        public void OnBlast(Vec3 position) { Blasts++; BlastAt = position; }

        public void OnZone(int zone, float secondsLeft, int holder)
        {
            ZoneIndex = zone; ZoneLeft = secondsLeft; ZoneHolder = holder;
        }
        public void OnTuning(string tuningText) { }
        public void OnWindowBroken(int windowIndex, Vec3 centre) { }

        public void OnHitConfirm(int target, HitZone zone, float damage, bool killed, Vec3 point)
        {
            HitTarget = target; HitZone = zone; HitDamage = damage; HitKilled = killed; HitPoint = point;
        }

        public void OnDamaged(int attacker, HitZone zone, byte weaponIndex, float damage, bool killed)
        {
            DamagedBy = attacker; DamagedZone = zone; DamagedWeapon = weaponIndex; DamagedKilled = killed;
        }

        public void OnRemoteShot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit,
                                 Vec3 hitPoint, bool hitPlayer)
        {
            ShotHitPlayer = hitPlayer;
        }
    }
}
