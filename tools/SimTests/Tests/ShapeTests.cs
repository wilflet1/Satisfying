using System.Collections.Generic;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// The character is generated geometry, and there is no renderer in this harness to look at it
    /// with. These are the three things that can be wrong with a generated body which you would never
    /// catch by reading the code: it is inside out, it has holes in it, or it is the wrong size.
    /// </summary>
    public static class ShapeTests
    {
        static readonly BodyShape[] All =
        {
            BodyShape.Limb, BodyShape.Torso, BodyShape.Head, BodyShape.Boot, BodyShape.Kit
        };

        public static void Register()
        {
            TestRunner.Add("shape/every shape fills the unit cube and stays inside it", () =>
            {
                // The whole rig scales these by (width, depth, length) in metres and expects to get a
                // limb that size. A shape that only reached 0.4 would quietly make every limb short.
                for (int i = 0; i < All.Length; i++)
                {
                    Vec3[] v;
                    int[] t;
                    ShapeCatalogue.Build(All[i], 0.8f, out v, out t);

                    Vec3 lo = v[0], hi = v[0];
                    for (int j = 1; j < v.Length; j++) { lo = Vec3.Min(lo, v[j]); hi = Vec3.Max(hi, v[j]); }

                    string what = All[i].ToString();
                    Assert.Less(hi.x, 0.5001f, what + " stays inside +X");
                    Assert.Less(hi.y, 0.5001f, what + " stays inside +Y");
                    Assert.Less(hi.z, 0.5001f, what + " stays inside +Z");
                    Assert.Greater(lo.x, -0.5001f, what + " stays inside -X");
                    Assert.Greater(lo.y, -0.5001f, what + " stays inside -Y");
                    Assert.Greater(lo.z, -0.5001f, what + " stays inside -Z");

                    // Z always spans the full cube; a taper means X and Y only have to reach it once.
                    Assert.Near(hi.z - lo.z, 1f, 0.0001f, what + " runs the length of the cube");
                    Assert.Greater(hi.x - lo.x, 0.90f, what + " is as wide as it claims");
                    Assert.Greater(hi.y - lo.y, 0.90f, what + " is as deep as it claims");
                }
            });

            TestRunner.Add("shape/every shape is a closed surface", () =>
            {
                // Each edge belongs to exactly two triangles, and they traverse it in opposite
                // directions. Anything else is a hole, a crack, or a face on backwards.
                for (int i = 0; i < All.Length; i++)
                {
                    Vec3[] v;
                    int[] t;
                    ShapeCatalogue.Build(All[i], 0.8f, out v, out t);
                    string what = All[i].ToString();

                    Dictionary<long, int> edges = new Dictionary<long, int>();
                    for (int j = 0; j < t.Length; j += 3)
                    {
                        Count(edges, t[j], t[j + 1]);
                        Count(edges, t[j + 1], t[j + 2]);
                        Count(edges, t[j + 2], t[j]);
                    }

                    foreach (KeyValuePair<long, int> edge in edges)
                    {
                        if (edge.Value == 0) continue;
                        int a = (int)(edge.Key >> 32);
                        int b = (int)(edge.Key & 0xFFFFFFFF);
                        Assert.True(false, what + " has an unpaired edge between vertex " + a + " and " + b);
                    }
                }
            });

            TestRunner.Add("shape/every face points outwards", () =>
            {
                // The bug this exists for: a loft wound the other way is invisible from outside and
                // solid from inside, and looks perfectly reasonable in the source.
                for (int i = 0; i < All.Length; i++)
                {
                    Vec3[] v;
                    int[] t;
                    ShapeCatalogue.Build(All[i], 0.8f, out v, out t);
                    string what = All[i].ToString();

                    int backwards = 0;
                    int degenerate = 0;
                    for (int j = 0; j < t.Length; j += 3)
                    {
                        Vec3 a = v[t[j]], b = v[t[j + 1]], c = v[t[j + 2]];
                        Vec3 normal = Loft.FaceNormal(a, b, c);
                        if (normal.Magnitude < 1e-7f) { degenerate++; continue; }

                        // These shapes are all convex enough about their own axis that "away from the
                        // centre line at this height" is the outward direction.
                        Vec3 centroid = (a + b + c) * (1f / 3f);
                        Vec3 outward = new Vec3(centroid.x, centroid.y, 0f);
                        if (outward.Magnitude < 1e-4f) outward = new Vec3(0f, 0f, centroid.z);

                        if (Vec3.Dot(normal.Normalized, outward.Normalized) < 0f) backwards++;
                    }

                    Assert.Equal(backwards, 0, what + " has faces wound inside out");
                    Assert.Equal(degenerate, 0, what + " has degenerate triangles");
                }
            });

            TestRunner.Add("shape/a limb really does taper", () =>
            {
                Vec3[] v;
                int[] t;
                ShapeCatalogue.Build(BodyShape.Limb, 0.7f, out v, out t);

                float nearWidth = WidthNear(v, -0.25f);
                float farWidth = WidthNear(v, 0.30f);
                Assert.Greater(nearWidth, farWidth + 0.05f, "the far end is narrower than the near one");

                // Both ends are drawn in, so a limb meets the next one at a joint rather than a stump.
                Assert.Less(WidthNear(v, -0.5f), nearWidth * 0.75f, "the near end is chamfered");
                Assert.Less(WidthNear(v, 0.5f), farWidth * 0.75f, "the far end is chamfered");
            });

            TestRunner.Add("shape/a torso is wider than it is deep", () =>
            {
                Vec3[] v;
                int[] t;
                ShapeCatalogue.Build(BodyShape.Torso, 0.76f, out v, out t);

                // Scaled to the chest's real size. Recognising a person at range is mostly recognising
                // that their shoulders are much wider than their front is thick.
                float width = 0f;
                float depth = 0f;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].z < 0.30f) continue;             // the shoulder end
                    float x = MathK.Abs(v[i].x) * 0.400f;
                    float y = MathK.Abs(v[i].y) * 0.262f;
                    if (x > width) width = x;
                    if (y > depth) depth = y;
                }
                Assert.Greater(width, depth * 1.4f, "shoulders read as shoulders");
            });
        }

        /// <summary>Counts an edge, cancelling it against the same edge traversed the other way.</summary>
        static void Count(Dictionary<long, int> edges, int a, int b)
        {
            long forward = ((long)a << 32) | (uint)b;
            long backward = ((long)b << 32) | (uint)a;

            int seen;
            if (edges.TryGetValue(backward, out seen) && seen > 0)
            {
                edges[backward] = seen - 1;
                return;
            }
            edges.TryGetValue(forward, out seen);
            edges[forward] = seen + 1;
        }

        /// <summary>Widest point of the shape within a slice around this height down the axis.</summary>
        static float WidthNear(Vec3[] vertices, float z)
        {
            float widest = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (MathK.Abs(vertices[i].z - z) > 0.02f) continue;
                float x = MathK.Abs(vertices[i].x);
                if (x > widest) widest = x;
            }
            return widest;
        }
    }
}
