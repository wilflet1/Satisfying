using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The maps, built from primitives at runtime. The duel arena is shaped around the movement set:
    /// hard corners to lean and side step around, window sills at crouch and prone height, ledges in
    /// the mantle band, and one long sightline so the marksman rifle has a reason to exist.
    /// Everything is mirrored so neither spawn has an advantage.
    /// </summary>
    public static class ArenaBuilder
    {
        /// <summary>A captioned drill station on the test range. The HUD names the nearest one.</summary>
        public struct Station
        {
            public Vector3 Position;
            public string Title;
            public string Hint;
        }

        public struct Result
        {
            public GameObject Root;
            public SpawnSet Spawns;
            public Bounds Bounds;
            public List<Station> Stations;
        }

        public static Result Build(MapId map, SpawnSet spawns, Palette palette, int worldLayer)
        {
            // Fill the caller's SpawnSet in place: the server holds a reference to it.
            spawns.Points.Clear();
            return map == MapId.TestRange
                ? BuildTestRange(spawns, palette, worldLayer)
                : BuildDuelArena(spawns, palette, worldLayer);
        }

        static Result BuildDuelArena(SpawnSet spawns, Palette palette, int worldLayer)
        {
            GameObject root = new GameObject("Arena");
            Transform t = root.transform;

            const float half = 30f;

            // ---------------------------------------------------------------- floor and perimeter
            Blockout.Box(t, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), palette.Ground, true, worldLayer);

            Wall(t, palette, worldLayer, new Vector3(0f, 3f, half), new Vector3(half * 2f, 6f, 1f));
            Wall(t, palette, worldLayer, new Vector3(0f, 3f, -half), new Vector3(half * 2f, 6f, 1f));
            Wall(t, palette, worldLayer, new Vector3(half, 3f, 0f), new Vector3(1f, 6f, half * 2f));
            Wall(t, palette, worldLayer, new Vector3(-half, 3f, 0f), new Vector3(1f, 6f, half * 2f));

            // ---------------------------------------------------------------- centre blockhouse
            // Two open doorways on the long sides and low windows on the short sides: every approach
            // gives you something to lean past.
            BuildBlockhouse(t, palette, worldLayer);

            // ---------------------------------------------------------------- mirrored halves
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f;

                // Corner L wall - the canonical lean/side step corner.
                Wall(t, palette, worldLayer, new Vector3(14f * s, 1.6f, 11f * s), new Vector3(9f, 3.2f, 0.6f));
                Wall(t, palette, worldLayer, new Vector3(18.2f * s, 1.6f, 15f * s), new Vector3(0.6f, 3.2f, 8.6f));

                // Stack of crates: 0.35 step, 1.05 mantle, 1.6 hard cover.
                Crate(t, palette, worldLayer, new Vector3(9.5f * s, 0.175f, -3.5f * s), new Vector3(2.4f, 0.35f, 2.4f));
                Crate(t, palette, worldLayer, new Vector3(11.6f * s, 0.525f, -3.5f * s), new Vector3(1.6f, 1.05f, 1.6f));
                Crate(t, palette, worldLayer, new Vector3(13.2f * s, 0.8f, -5.2f * s), new Vector3(1.8f, 1.6f, 1.8f));

                // Waist-high barricade with a gap: crouch behind it, prone under the gap, lean around the end.
                Wall(t, palette, worldLayer, new Vector3(3.5f * s, 0.55f, -12f * s), new Vector3(7f, 1.1f, 0.5f));
                Wall(t, palette, worldLayer, new Vector3(-3.5f * s, 0.55f, -12f * s), new Vector3(7f, 1.1f, 0.5f));
                Wall(t, palette, worldLayer, new Vector3(0f, 1.55f, -12f * s), new Vector3(1.6f, 0.9f, 0.5f));   // lintel: prone gap underneath

                // Pillars: pure lean furniture.
                Pillar(t, palette, worldLayer, new Vector3(6.5f * s, 0f, 6.5f * s));
                Pillar(t, palette, worldLayer, new Vector3(-8.5f * s, 0f, 5.0f * s));
                Pillar(t, palette, worldLayer, new Vector3(20f * s, 0f, -2f * s));

                // Ramp up to a catwalk over the long sightline.
                Ramp(t, palette, worldLayer, new Vector3(23f * s, 0f, 8f * s), 3.2f, 6f, s);
                Wall(t, palette, worldLayer, new Vector3(23f * s, 3.15f, 0f), new Vector3(4f, 0.3f, 16f));
                Wall(t, palette, worldLayer, new Vector3(24.9f * s, 3.75f, 0f), new Vector3(0.3f, 0.9f, 16f));   // catwalk rail: crouch cover at height

                spawns.Add(new Vec3(24f * s, 0f, -24f * s), s > 0f ? 315f : 135f);
                spawns.Add(new Vec3(6f * s, 0f, -22f * s), s > 0f ? 350f : 170f);
            }

            Result result;
            result.Root = root;
            result.Spawns = spawns;
            result.Bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 12f, half * 2f));
            result.Stations = new List<Station>();
            return result;
        }

        // ==================================================================== test range
        /// <summary>
        /// A drill course rather than a map: one lane per movement ability, sized against the tuning
        /// defaults so you can see immediately when a value stops working. Change slideHeight in the
        /// tuning panel and the slide tunnel will tell you about it.
        /// </summary>
        static Result BuildTestRange(SpawnSet spawns, Palette palette, int worldLayer)
        {
            GameObject root = new GameObject("Test Range");
            Transform t = root.transform;
            List<Station> stations = new List<Station>();

            const float half = 55f;
            Blockout.Box(t, "floor", new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), palette.Ground, true, worldLayer);
            Wall(t, palette, worldLayer, new Vector3(0f, 3f, half), new Vector3(half * 2f, 6f, 1f));
            Wall(t, palette, worldLayer, new Vector3(0f, 3f, -half), new Vector3(half * 2f, 6f, 1f));
            Wall(t, palette, worldLayer, new Vector3(half, 3f, 0f), new Vector3(1f, 6f, half * 2f));
            Wall(t, palette, worldLayer, new Vector3(-half, 3f, 0f), new Vector3(1f, 6f, half * 2f));

            // Central plaza with a raised kerb, so you always know which way is home.
            Blockout.Box(t, "plaza", new Vector3(0f, 0.06f, 0f), new Vector3(14f, 0.12f, 14f), palette.WallDark, true, worldLayer);

            BuildVaultLane(t, palette, worldLayer, stations);
            BuildSlideLane(t, palette, worldLayer, stations);
            BuildMantleLane(t, palette, worldLayer, stations);
            BuildLeanLane(t, palette, worldLayer, stations);
            BuildRangeLane(t, palette, worldLayer, stations);

            spawns.Add(new Vec3(0f, 0.15f, 0f), 0f);
            spawns.Add(new Vec3(4f, 0.15f, -4f), 315f);
            spawns.Add(new Vec3(-4f, 0.15f, 4f), 135f);

            Result result;
            result.Root = root;
            result.Spawns = spawns;
            result.Bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 14f, half * 2f));
            result.Stations = stations;
            return result;
        }

        static void AddStation(List<Station> stations, Vector3 position, string title, string hint)
        {
            Station s;
            s.Position = position;
            s.Title = title;
            s.Hint = hint;
            stations.Add(s);
        }

        /// <summary>Railings of rising height, thin enough that the game reads them as things to go over.</summary>
        static void BuildVaultLane(Transform t, Palette palette, int layer, List<Station> stations)
        {
            GameObject lane = new GameObject("vault lane");
            lane.transform.SetParent(t, false);
            Transform l = lane.transform;

            float[] heights = { 0.55f, 0.8f, 1.05f, 1.25f, 1.6f };
            for (int i = 0; i < heights.Length; i++)
            {
                float z = 14f + i * 6f;
                float h = heights[i];
                bool tooTall = h > 1.35f;
                Blockout.Box(l, "railing " + h, new Vector3(0f, h * 0.5f, z), new Vector3(7f, h, 0.16f),
                    tooTall ? palette.WallDark : palette.Accent, true, layer);
                // Posts, so the railing reads as a railing rather than a floating slab.
                Blockout.Box(l, "post", new Vector3(-3.4f, h * 0.5f, z), new Vector3(0.22f, h, 0.22f), palette.Metal, true, layer);
                Blockout.Box(l, "post", new Vector3(3.4f, h * 0.5f, z), new Vector3(0.22f, h, 0.22f), palette.Metal, true, layer);
            }

            // A balcony to vault off: over the rail is a two metre drop, which is a fall, not a landing.
            Blockout.Box(l, "balcony", new Vector3(0f, 1f, 46f), new Vector3(9f, 2f, 8f), palette.Wall, true, layer);
            Blockout.Box(l, "balcony rail", new Vector3(0f, 2.5f, 49.6f), new Vector3(9f, 1f, 0.16f), palette.Accent, true, layer);
            Ramp(l, palette, layer, new Vector3(0f, 0f, 41.5f), 2f, 4f, -1f);

            AddStation(stations, new Vector3(0f, 0f, 16f), "VAULT ROW",
                "Space at a railing. 0.55 / 0.80 / 1.05 / 1.25 clear, 1.60 does not.");
            AddStation(stations, new Vector3(0f, 2f, 46f), "BALCONY",
                "Vault the rail and you go over into the drop, not onto it.");
        }

        /// <summary>Runway, low tunnel, slope and a gap that only a slide jump clears.</summary>
        static void BuildSlideLane(Transform t, Palette palette, int layer, List<Station> stations)
        {
            GameObject lane = new GameObject("slide lane");
            lane.transform.SetParent(t, false);
            Transform l = lane.transform;

            // Runway walls, so it is obvious this is a sprint lane.
            Wall(l, palette, layer, new Vector3(-4.5f, 1f, -22f), new Vector3(0.5f, 2f, 26f));
            Wall(l, palette, layer, new Vector3(4.5f, 1f, -22f), new Vector3(0.5f, 2f, 26f));

            // Tunnel: underside at 1.0. A crouch is 1.22 and will not fit.
            Blockout.Box(l, "tunnel roof", new Vector3(0f, 1.9f, -20f), new Vector3(9f, 1.8f, 3f), palette.Accent, true, layer);
            Blockout.Box(l, "tunnel roof", new Vector3(0f, 1.9f, -27f), new Vector3(9f, 1.8f, 2f), palette.Accent, true, layer);

            // Slope to slide down, then a pit you have to jump out of the slide to clear.
            Ramp(l, palette, layer, new Vector3(0f, 0f, -33f), 2.4f, 7f, 1f);
            Blockout.Box(l, "slope top", new Vector3(0f, 1.2f, -38f), new Vector3(9f, 2.4f, 4f), palette.WallDark, true, layer);
            Blockout.Box(l, "landing", new Vector3(0f, 0.3f, -48f), new Vector3(9f, 0.6f, 6f), palette.WallDark, true, layer);
            // The pit between them is simply floor, four metres wide.

            AddStation(stations, new Vector3(0f, 0f, -14f), "SLIDE LANE",
                "Sprint, then tap crouch. The tunnels are 1.0 high: only a slide fits.");
            AddStation(stations, new Vector3(0f, 2.4f, -38f), "SLIDE JUMP",
                "Slide down the slope and jump out of it to clear the gap.");
        }

        /// <summary>Ledges to climb onto, plus a window to vault through.</summary>
        static void BuildMantleLane(Transform t, Palette palette, int layer, List<Station> stations)
        {
            GameObject lane = new GameObject("mantle lane");
            lane.transform.SetParent(t, false);
            Transform l = lane.transform;

            float[] heights = { 0.5f, 0.9f, 1.3f, 1.55f };
            for (int i = 0; i < heights.Length; i++)
            {
                float x = 14f + i * 5f;
                float h = heights[i];
                bool tooTall = h > 1.35f;
                Blockout.Box(l, "ledge " + h, new Vector3(x, h * 0.5f, 0f), new Vector3(3.5f, h, 7f),
                    tooTall ? palette.WallDark : palette.Wall, true, layer);
            }

            // A wall with a window sill at 1.05: thin, so it is a vault rather than a climb.
            Wall(l, palette, layer, new Vector3(36f, 0.52f, 0f), new Vector3(0.4f, 1.05f, 10f));
            Wall(l, palette, layer, new Vector3(36f, 2.6f, 0f), new Vector3(0.4f, 1.6f, 10f));
            Wall(l, palette, layer, new Vector3(36f, 1.8f, 4.2f), new Vector3(0.4f, 1.5f, 1.6f));
            Wall(l, palette, layer, new Vector3(36f, 1.8f, -4.2f), new Vector3(0.4f, 1.5f, 1.6f));

            // Rooftop you can reach by chaining the ledges.
            Blockout.Box(l, "roof", new Vector3(44f, 1.6f, 0f), new Vector3(10f, 0.4f, 10f), palette.Wall, true, layer);
            Blockout.Box(l, "roof rail", new Vector3(48.6f, 2.2f, 0f), new Vector3(0.16f, 0.8f, 10f), palette.Accent, true, layer);

            AddStation(stations, new Vector3(15f, 0f, 0f), "MANTLE STACK",
                "0.50 / 0.90 / 1.30 climb. 1.55 is above the band and will not.");
            AddStation(stations, new Vector3(34f, 0f, 0f), "WINDOW",
                "A 1.05 sill with floor beyond: the game vaults you through it.");
        }

        /// <summary>Corners to lean around, pillars to side step behind, a gap to crawl through.</summary>
        static void BuildLeanLane(Transform t, Palette palette, int layer, List<Station> stations)
        {
            GameObject lane = new GameObject("lean lane");
            lane.transform.SetParent(t, false);
            Transform l = lane.transform;

            for (int i = 0; i < 3; i++)
            {
                float x = -14f - i * 8f;
                Wall(l, palette, layer, new Vector3(x, 1.6f, 2.5f), new Vector3(0.6f, 3.2f, 9f));
                Dummy(l, palette, layer, new Vector3(x - 0.55f - i * 0.35f, 0f, 9f));
            }

            // Prone bar: 0.7 clearance, under a barricade.
            Wall(l, palette, layer, new Vector3(-30f, 1.35f, -6f), new Vector3(10f, 1.3f, 0.6f));
            Blockout.Box(l, "prone marker", new Vector3(-30f, 0.05f, -7.5f), new Vector3(10f, 0.1f, 1.5f), palette.Accent, true, layer);

            // Side step alley.
            for (int i = 0; i < 4; i++)
                Pillar(l, palette, layer, new Vector3(-16f - i * 4f, 0f, -14f + (i % 2) * 2.2f));
            Dummy(l, palette, layer, new Vector3(-34f, 0f, -14f));

            AddStation(stations, new Vector3(-14f, 0f, 6f), "LEAN GALLERY",
                "Q / E to peek. Alt+Q / Alt+E to creep it open. The dummies sit further out each time.");
            AddStation(stations, new Vector3(-30f, 0f, -6f), "PRONE BAR",
                "0.70 clearance: only prone gets under it.");
            AddStation(stations, new Vector3(-20f, 0f, -14f), "SIDE STEP ALLEY",
                "Alt+A / Alt+D moves your body without turning your aim.");
        }

        /// <summary>A long lane with distance markers and dummies, for weapon falloff and recoil.</summary>
        static void BuildRangeLane(Transform t, Palette palette, int layer, List<Station> stations)
        {
            GameObject lane = new GameObject("range lane");
            lane.transform.SetParent(t, false);
            Transform l = lane.transform;

            float[] distances = { 10f, 20f, 40f, 80f };
            for (int i = 0; i < distances.Length; i++)
            {
                float z = -8f;
                float x = 10f + distances[i] * 0.5f;
                Blockout.Box(l, "marker " + distances[i], new Vector3(x, 0.6f, z - 2.2f),
                    new Vector3(0.3f, 1.2f, 0.3f), palette.Accent, true, layer);
                Dummy(l, palette, layer, new Vector3(x, 0f, z));
            }
            Wall(l, palette, layer, new Vector3(10f, 1.6f, -12f), new Vector3(52f, 3.2f, 0.5f));

            AddStation(stations, new Vector3(12f, 0f, -8f), "SHOOTING RANGE",
                "Posts at 10 / 20 / 40 / 80 metres. 1 M4A1, 2 MP5, 3 USP45.");
        }

        /// <summary>A static target: blocks bullets so tracers and impacts land somewhere sensible.</summary>
        static void Dummy(Transform parent, Palette palette, int layer, Vector3 basePosition)
        {
            GameObject dummy = new GameObject("dummy");
            dummy.transform.SetParent(parent, false);
            dummy.transform.localPosition = basePosition;
            Blockout.Box(dummy.transform, "legs", new Vector3(0f, 0.4f, 0f), new Vector3(0.35f, 0.8f, 0.25f), palette.WallDark, true, layer);
            Blockout.Box(dummy.transform, "torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.7f, 0.28f), palette.Enemy, true, layer);
            Blockout.Box(dummy.transform, "head", new Vector3(0f, 1.68f, 0f), new Vector3(0.24f, 0.26f, 0.24f), palette.Enemy, true, layer);
            Blockout.Box(dummy.transform, "base", new Vector3(0f, 0.03f, 0f), new Vector3(0.7f, 0.06f, 0.7f), palette.Metal, true, layer);
        }

        static void BuildBlockhouse(Transform t, Palette palette, int layer)
        {
            GameObject house = new GameObject("blockhouse");
            house.transform.SetParent(t, false);
            Transform h = house.transform;

            const float w = 9f;      // half width (x)
            const float d = 6f;      // half depth (z)
            const float height = 3.4f;

            // Long walls with a central doorway.
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f;
                Wall(h, palette, layer, new Vector3(-(w * 0.5f + 0.75f) * 1f, height * 0.5f, d * s), new Vector3(w - 1.5f, height, 0.6f));
                Wall(h, palette, layer, new Vector3((w * 0.5f + 0.75f) * 1f, height * 0.5f, d * s), new Vector3(w - 1.5f, height, 0.6f));
                Wall(h, palette, layer, new Vector3(0f, height - 0.35f, d * s), new Vector3(3f, 0.7f, 0.6f));   // lintel over the door
            }

            // Short walls with a window: sill at 1.05 so you can lean over it, or go prone beneath.
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f;
                Wall(h, palette, layer, new Vector3(w * s, 0.55f, 0f), new Vector3(0.6f, 1.1f, d * 2f));
                Wall(h, palette, layer, new Vector3(w * s, height - 0.55f, 0f), new Vector3(0.6f, 1.1f, d * 2f));
                Wall(h, palette, layer, new Vector3(w * s, 1.8f, -(d * 0.5f + 0.9f)), new Vector3(0.6f, 1.4f, d - 1.8f));
                Wall(h, palette, layer, new Vector3(w * s, 1.8f, d * 0.5f + 0.9f), new Vector3(0.6f, 1.4f, d - 1.8f));
            }

            // Roof, reachable by mantling the interior crate then the sill.
            Wall(h, palette, layer, new Vector3(0f, height + 0.15f, 0f), new Vector3(w * 2f + 0.6f, 0.3f, d * 2f + 0.6f));
            Crate(h, palette, layer, new Vector3(w - 1.6f, 0.5f, d - 1.4f), new Vector3(1.4f, 1.0f, 1.4f));
            Crate(h, palette, layer, new Vector3(-(w - 1.6f), 0.5f, -(d - 1.4f)), new Vector3(1.4f, 1.0f, 1.4f));

            // Interior divider so the inside is not one open box.
            Wall(h, palette, layer, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.5f, height, d * 0.9f));

            // Roof parapet: crouch cover up top.
            for (int side = 0; side < 2; side++)
            {
                float s = side == 0 ? 1f : -1f;
                Wall(h, palette, layer, new Vector3(0f, height + 0.75f, (d + 0.15f) * s), new Vector3(w * 2f + 0.6f, 0.9f, 0.3f));
                Wall(h, palette, layer, new Vector3((w + 0.15f) * s, height + 0.75f, 0f), new Vector3(0.3f, 0.9f, d * 2f + 0.6f));
            }
        }

        static void Wall(Transform parent, Palette palette, int layer, Vector3 center, Vector3 size)
        {
            Blockout.Box(parent, "wall", center, size, palette.Wall, true, layer);
        }

        static void Crate(Transform parent, Palette palette, int layer, Vector3 center, Vector3 size)
        {
            Blockout.Box(parent, "crate", center, size, palette.Accent, true, layer);
        }

        static void Pillar(Transform parent, Palette palette, int layer, Vector3 basePosition)
        {
            Blockout.Box(parent, "pillar", basePosition + new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 3.2f, 0.9f), palette.WallDark, true, layer);
        }

        /// <summary>Stairs rather than a slope, so the step-up code gets a workout.</summary>
        static void Ramp(Transform parent, Palette palette, int layer, Vector3 basePosition, float height, float length, float facing)
        {
            int steps = Mathf.Max(2, Mathf.RoundToInt(height / 0.28f));
            float stepHeight = height / steps;
            float stepDepth = length / steps;
            for (int i = 0; i < steps; i++)
            {
                // Solid blocks from the floor up, not floating slabs - no gaps to fall into.
                float top = stepHeight * (i + 1);
                float z = basePosition.z + (stepDepth * (i + 0.5f)) * -facing;
                Blockout.Box(parent, "step", new Vector3(basePosition.x, top * 0.5f, z),
                    new Vector3(3.2f, top, stepDepth), palette.WallDark, true, layer);
            }
        }
    }
}
