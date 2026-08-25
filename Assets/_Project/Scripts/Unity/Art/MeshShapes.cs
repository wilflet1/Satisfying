using System.Collections.Generic;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Turns the shape catalogue into engine meshes, and caches them.
    ///
    /// The geometry itself is in Shared/Math - ShapeCatalogue and Loft - because it is arithmetic and
    /// arithmetic can be tested, whereas a mesh can only be looked at. This file is the ten lines that
    /// need an engine: allocate a Mesh, fill it, work out normals.
    ///
    /// Every shape is normalised into the unit cube, so Blockout.Bone can scale one by
    /// (width, depth, length) and get exactly that. There is no per-character cost: eleven meshes exist
    /// in a match no matter how many duellists are in it.
    /// </summary>
    public static class MeshShapes
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>An arm, a leg or a neck: round, tapering to `tip` of its width at the far end.</summary>
        public static Mesh Limb(float tip) { return Get(BodyShape.Limb, tip); }

        /// <summary>A chest or a stomach, `waist` as wide at the near end as at the far one.</summary>
        public static Mesh Torso(float waist) { return Get(BodyShape.Torso, waist); }

        public static Mesh Head() { return Get(BodyShape.Head, 1f); }
        public static Mesh Boot() { return Get(BodyShape.Boot, 1f); }
        public static Mesh Kit() { return Get(BodyShape.Kit, 1f); }

        static Mesh Get(BodyShape shape, float taper)
        {
            string key = shape.ToString() + ":" + taper.ToString("0.00");
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null) return cached;

            Vec3[] points;
            int[] indices;
            ShapeCatalogue.Build(shape, taper, out points, out indices);

            Vector3[] vertices = new Vector3[points.Length];
            Vector2[] uv = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i] = points[i].ToUnity();
                // Cylindrical enough for a flat-coloured body, and there are no textures to line up.
                uv[i] = new Vector2(Mathf.Atan2(vertices[i].y, vertices[i].x) / (Mathf.PI * 2f) + 0.5f,
                                    vertices[i].z + 0.5f);
            }

            Mesh mesh = new Mesh();
            mesh.name = key;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }
    }
}
