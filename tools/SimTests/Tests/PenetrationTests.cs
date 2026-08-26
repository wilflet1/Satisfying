using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// Shooting through things, and the rooms worth standing in. Both are arithmetic over boxes, so
    /// both can be settled here rather than by walking around the map hoping.
    /// </summary>
    public static class PenetrationTests
    {
        public static void Register()
        {
            TestRunner.Add("penetration/a slab reports where the ray goes in and comes out", () =>
            {
                Box wall = new Box(new Vec3(0f, 1.5f, 4f), new Vec3(6f, 3f, 0.12f));

                float entry, exit;
                Assert.True(Penetration.Slab(wall, Vec3.Zero, new Vec3(0f, 0f, 1f), 20f, out entry, out exit),
                    "the ray crosses it");
                Assert.Near(entry, 3.94f, 0.01f, "in at the near face");
                Assert.Near(exit, 4.06f, 0.01f, "out at the far face");
                Assert.Near(exit - entry, 0.12f, 0.005f, "and the thickness is the wall's");

                // A ray that misses it entirely.
                float missEntry, missExit;
                Assert.False(Penetration.Slab(wall, new Vec3(0f, 9f, 0f), new Vec3(0f, 0f, 1f), 20f,
                    out missEntry, out missExit), "over the top is a miss");
            });

            TestRunner.Add("penetration/a rifle goes through plasterboard and not through concrete", () =>
            {
                GameTuning tuning = new GameTuning();
                WeaponTuning rifle = tuning.Weapon(0);

                float scale;
                Assert.True(Penetration.Through(tuning.penetration, rifle.penetration, SurfaceKind.Drywall, 0.12f, out scale),
                    "120 mm of plasterboard");
                Assert.True(scale > 0.4f, "and it still hurts, at " + scale.ToString("0.00"));

                Assert.False(Penetration.Through(tuning.penetration, rifle.penetration, SurfaceKind.Concrete, 0.2f, out scale),
                    "200 mm of concrete stops it");

                // The bolt gun gets through what the rifle does not, which is what its budget is for.
                WeaponTuning bolt = tuning.Weapon(3);
                Assert.True(Penetration.Through(tuning.penetration, bolt.penetration, SurfaceKind.Wood, 0.12f, out scale),
                    "the bolt gun goes through a floor");
                Assert.True(Penetration.Through(tuning.penetration, bolt.penetration, SurfaceKind.Drywall, 0.12f, out scale),
                    "and a partition");
                Assert.True(scale > 0.7f, "barely slowed by plasterboard, at " + scale.ToString("0.00"));

                // A pistol should not be punching through the map.
                WeaponTuning pistol = tuning.Weapon(2);
                Assert.False(Penetration.Through(tuning.penetration, pistol.penetration, SurfaceKind.Wood, 0.12f, out scale),
                    "a pistol does not get through a floor");
            });

            TestRunner.Add("penetration/thicker costs more, and the damage floor holds", () =>
            {
                GameTuning tuning = new GameTuning();
                float budget = 0.5f;

                float thin, thick;
                Assert.True(Penetration.Through(tuning.penetration, budget, SurfaceKind.Drywall, 0.05f, out thin), "thin");
                Assert.True(Penetration.Through(tuning.penetration, budget, SurfaceKind.Drywall, 0.45f, out thick), "thick");
                Assert.True(thin > thick, "thicker takes more out of it");
                Assert.True(thick >= tuning.penetration.exitDamageFloor - 0.001f, "and never below the floor");
                Assert.True(thin <= 1f, "and never above what went in");
            });

            TestRunner.Add("penetration/the panel in front is the one that is hit", () =>
            {
                // A soft panel BEHIND a hard wall must not let a round through the hard wall. The
                // caller passes the distance the round actually stopped at, and only a panel at that
                // distance counts.
                WorldModel model = new WorldModel();
                model.AddPanel(new Vec3(0f, 1.5f, 8f), new Vec3(6f, 3f, 0.12f), SurfaceKind.Drywall);

                PanelDef panel;
                float thickness, exit;

                // Stopped at 4 m - nowhere near the panel at 8.
                Assert.False(Penetration.PanelAt(model, Vec3.Zero, new Vec3(0f, 0f, 1f), 4f, 0.08f,
                    out panel, out thickness, out exit), "a panel further on is not what stopped the round");

                // Stopped at the panel itself.
                Assert.True(Penetration.PanelAt(model, Vec3.Zero, new Vec3(0f, 0f, 1f), 7.95f, 0.08f,
                    out panel, out thickness, out exit), "the panel at the stopping distance is found");
                Assert.True(panel.Kind == SurfaceKind.Drywall, "and it knows what it is made of");
                Assert.Near(thickness, 0.12f, 0.01f, "and how thick");
            });

            TestRunner.Add("hill/feet decide who holds a room, not eyes", () =>
            {
                ZoneDef zone;
                zone.Name = "the bedroom";
                zone.Bounds = new Box(new Vec3(0f, 4.35f, 0f), new Vec3(6f, 2.6f, 5f));

                Assert.True(Koth.Inside(zone, new Vec3(0f, 3.15f, 0f)), "standing in it");
                Assert.True(Koth.Inside(zone, new Vec3(2.9f, 3.15f, 2.4f)), "in the corner of it");
                Assert.False(Koth.Inside(zone, new Vec3(0f, 0f, 0f)),
                    "the room below it is not the room - this is why it tests the feet");
                Assert.False(Koth.Inside(zone, new Vec3(4f, 3.15f, 0f)), "outside the wall");
            });

            TestRunner.Add("hill/it always moves somewhere else", () =>
            {
                for (uint seed = 0; seed < 40; seed++)
                {
                    for (int from = 0; from < 3; from++)
                    {
                        int next = Koth.NextZone(from, 3, seed);
                        Assert.True(next != from, "the hill has to move");
                        Assert.True(next >= 0 && next < 3, "and stay on the map");
                    }
                }

                Assert.Equal(Koth.NextZone(0, 1, 7), 0, "one room is always the room");
            });
        }
    }
}
