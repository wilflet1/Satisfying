using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The duel arena, built from primitives at runtime. It is shaped around the movement set:
    /// hard corners to lean and side step around, window sills at crouch and prone height, ledges in
    /// the mantle band, and one long sightline so the marksman rifle has a reason to exist.
    /// Everything is mirrored so neither spawn has an advantage.
    /// </summary>
    public static class ArenaBuilder
    {
        public struct Result
        {
            public GameObject Root;
            public SpawnSet Spawns;
            public Bounds Bounds;
        }

        public static Result Build(Palette palette, int worldLayer)
        {
            GameObject root = new GameObject("Arena");
            Transform t = root.transform;
            SpawnSet spawns = new SpawnSet();

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
            return result;
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
