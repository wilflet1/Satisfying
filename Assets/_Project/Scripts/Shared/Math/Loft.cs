namespace Satisfying.Shared
{
    /// <summary>One cross-section of a loft: where it sits down Z, and how wide and deep it is there.</summary>
    public struct LoftRing
    {
        public float z;
        public float x;
        public float y;

        public LoftRing(float z, float x, float y) { this.z = z; this.x = x; this.y = y; }
        public LoftRing(float z, float scale) { this.z = z; x = scale; y = scale; }
    }

    /// <summary>
    /// Sweeping an outline down an axis - the only modelling operation the game needs. A tapered limb,
    /// a chamfered torso, a head and a boot are all this with different numbers in them.
    ///
    /// It lives down here with the rest of the maths rather than up in the art layer for the same
    /// reason everything else does: there is no Unity in the container this was written in, so the one
    /// way to know that a generated body is not inside out, not full of holes and not the wrong size is
    /// to be able to run the numbers. The tests assert exactly those three things.
    ///
    /// Everything is normalised into the unit cube, centred on the origin, running -0.5 to +0.5 down Z.
    /// That is what lets a bone scale a shape by (width, depth, length) and get precisely that.
    /// </summary>
    public static class Loft
    {
        /// <summary>A circle, anticlockwise, sized to the unit cube.</summary>
        public static Vec2[] Circle(int sides)
        {
            if (sides < 3) sides = 3;
            Vec2[] outline = new Vec2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * MathK.PI * 2f;
                outline[i] = new Vec2(MathK.Cos(a) * 0.5f, MathK.Sin(a) * 0.5f);
            }
            return outline;
        }

        /// <summary>
        /// A square with rounded corners, anticlockwise, sized to the unit cube. corner is the radius as
        /// a fraction of the half-extent: 0 is a hard box, 1 is a circle.
        /// </summary>
        public static Vec2[] RoundedRect(float corner, int perCorner)
        {
            if (perCorner < 2) perCorner = 2;
            float r = MathK.Clamp01(corner) * 0.5f;
            float straight = 0.5f - r;

            Vec2[] centres =
            {
                new Vec2(straight, straight),
                new Vec2(-straight, straight),
                new Vec2(-straight, -straight),
                new Vec2(straight, -straight)
            };

            Vec2[] outline = new Vec2[perCorner * 4];
            int at = 0;
            for (int c = 0; c < 4; c++)
            {
                float start = c * MathK.PI * 0.5f;
                for (int i = 0; i < perCorner; i++)
                {
                    float a = start + i / (float)(perCorner - 1) * MathK.PI * 0.5f;
                    outline[at++] = new Vec2(centres[c].x + MathK.Cos(a) * r, centres[c].y + MathK.Sin(a) * r);
                }
            }
            return outline;
        }

        /// <summary>
        /// Sweeps the outline through the rings and caps both ends.
        ///
        /// Vertices are shared around the circumference so a renderer that averages normals smooths
        /// across the section and leaves the caps hard, which is what a limb wants. The winding is the
        /// part worth being careful about: get it backwards and the body is invisible from outside and
        /// solid from inside, which is not a thing you notice in a code review.
        /// </summary>
        public static void Build(Vec2[] outline, LoftRing[] rings, out Vec3[] vertices, out int[] triangles)
        {
            int sides = outline.Length;
            int ringCount = rings.Length;

            vertices = new Vec3[sides * ringCount + 2];
            for (int r = 0; r < ringCount; r++)
            {
                for (int i = 0; i < sides; i++)
                    vertices[r * sides + i] = new Vec3(outline[i].x * rings[r].x, outline[i].y * rings[r].y, rings[r].z);
            }

            int startCentre = sides * ringCount;
            int endCentre = startCentre + 1;
            vertices[startCentre] = new Vec3(0f, 0f, rings[0].z);
            vertices[endCentre] = new Vec3(0f, 0f, rings[ringCount - 1].z);

            triangles = new int[((ringCount - 1) * sides * 2 + sides * 2) * 3];
            int t = 0;

            for (int r = 0; r < ringCount - 1; r++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int next = (i + 1) % sides;
                    int a = r * sides + i;
                    int b = r * sides + next;
                    int c = (r + 1) * sides + i;
                    int d = (r + 1) * sides + next;

                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            }

            int last = (ringCount - 1) * sides;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                // The -Z cap faces backwards, so it winds the opposite way round from the +Z one.
                triangles[t++] = startCentre; triangles[t++] = next; triangles[t++] = i;
                triangles[t++] = endCentre; triangles[t++] = last + i; triangles[t++] = last + next;
            }
        }

        /// <summary>
        /// The face normal of one triangle, by the same convention the engine uses: the cross product of
        /// the first two edges. Positive against the outward direction means the face is the right way
        /// round.
        /// </summary>
        public static Vec3 FaceNormal(Vec3 a, Vec3 b, Vec3 c)
        {
            return Vec3.Cross(b - a, c - a);
        }
    }
}
