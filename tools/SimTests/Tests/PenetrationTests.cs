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

            TestRunner.Add("penetration/every weapon gets through the walls it is meant to", () =>
            {
                GameTuning tuning = new GameTuning();
                float scale;

                // A 140 mm plasterboard partition - the one the house is planned around. Everything
                // except the pistol goes through it.
                Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(0).penetration,
                    SurfaceKind.Drywall, 0.14f, out scale), "a rifle through a partition");
                Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(1).penetration,
                    SurfaceKind.Drywall, 0.14f, out scale), "an smg through a partition");
                Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(3).penetration,
                    SurfaceKind.Drywall, 0.14f, out scale), "a bolt gun through a partition");

                // 180 mm of floor. THIS is the one that was reported broken: wood cost more than any
                // weapon could pay, so nothing went through a floor or a fence.
                Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(0).penetration,
                    SurfaceKind.Wood, 0.18f, out scale), "a rifle through a floor");
                Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(3).penetration,
                    SurfaceKind.Wood, 0.18f, out scale), "a bolt gun through a floor");

                // A fence panel is 50 mm of board and everything goes through it, pistol included.
                for (int w = 0; w < 4; w++)
                    Assert.True(Penetration.Through(tuning.penetration, tuning.Weapon(w).penetration,
                        SurfaceKind.Wood, 0.05f, out scale), "weapon " + w + " through a fence");

                // And 300 mm of concrete still stops everything, or the map has no cover in it.
                for (int w = 0; w < 4; w++)
                    Assert.False(Penetration.Through(tuning.penetration, tuning.Weapon(w).penetration,
                        SurfaceKind.Concrete, 0.30f, out scale), "weapon " + w + " must not cross concrete");
            });

            TestRunner.Add("penetration/a round crosses a limb instead of stopping on it", () =>
            {
                // An arm is roughly 100 mm across. Crossing one has to be affordable for every weapon,
                // or an opponent with a forearm across their chest is wearing armour.
                GameTuning tuning = new GameTuning();
                float armThickness = 0.104f;
                float cost = armThickness * tuning.penetration.flesh;

                for (int w = 0; w < 4; w++)
                    Assert.True(tuning.Weapon(w).penetration > cost,
                        "weapon " + w + " must be able to cross an arm: budget " +
                        tuning.Weapon(w).penetration.ToString("0.000") + " vs " + cost.ToString("0.000"));

                // And what comes out the far side still matters.
                Assert.True(tuning.penetration.fleshExitScale > 0.4f, "a round through an arm still hurts");
                Assert.True(tuning.penetration.fleshExitScale < 1f, "but not as much as one that missed it");
            });

            TestRunner.Add("penetration/a capsule reports where the ray leaves it", () =>
            {
                // Straight across a horizontal capsule: in one side, out the other, one diameter apart.
                Vec3 a = new Vec3(-0.2f, 1.4f, 0f);
                Vec3 b = new Vec3(0.2f, 1.4f, 0f);
                float r = 0.052f;

                Vec3 origin = new Vec3(0f, 1.4f, -3f);
                Vec3 dir = new Vec3(0f, 0f, 1f);

                float entry;
                Assert.True(RayGeometry.Capsule(origin, dir, a, b, r, out entry), "it hits");

                float exit = RayGeometry.CapsuleExit(origin, dir, a, b, r, entry);
                Assert.True(exit > entry, "and comes out after going in");
                Assert.Near(exit - entry, r * 2f, 0.01f, "across the middle it is one diameter thick");

                // Down the length of it is much further, which is the case a fixed step would get wrong.
                Vec3 along = new Vec3(-3f, 1.4f, 0f);
                Vec3 alongDir = new Vec3(1f, 0f, 0f);
                float alongEntry;
                Assert.True(RayGeometry.Capsule(along, alongDir, a, b, r, out alongEntry), "lengthways hits");
                float alongExit = RayGeometry.CapsuleExit(along, alongDir, a, b, r, alongEntry);
                Assert.True(alongExit - alongEntry > r * 4f,
                    "lengthways is far thicker than across, at " + (alongExit - alongEntry).ToString("0.000"));
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
