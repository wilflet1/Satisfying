namespace Satisfying.Shared
{
    /// <summary>What a duellist is made of, as loft profiles.</summary>
    public enum BodyShape : byte
    {
        Limb = 0,       // arms, legs, neck - round, tapered, ends drawn in
        Torso = 1,      // chest and stomach - wide, shallow, lofting out towards the shoulders
        Head = 2,       // an ovoid
        Boot = 3,       // tall at the heel, flat and tapered at the toe
        Kit = 4         // pouches, plates, helmets - a box with the edges taken off
    }

    /// <summary>
    /// The shapes the character is built from, kept here rather than in the art layer so that the
    /// tests can check them. A generated body that is inside out, full of holes, or the wrong size by
    /// a factor is not something you can see in a diff, and there is no Unity in the harness to look
    /// at it with - so the numbers have to answer for themselves.
    ///
    /// Every profile is normalised into the unit cube, centred on the origin, running -0.5 to +0.5
    /// down Z. That is the contract the whole character rig rests on: a bone scales a shape by
    /// (width, depth, length) and gets exactly a limb that wide, that deep and that long.
    /// </summary>
    public static class ShapeCatalogue
    {
        public static Vec2[] Outline(BodyShape shape)
        {
            switch (shape)
            {
                case BodyShape.Torso: return Loft.RoundedRect(0.30f, 4);
                case BodyShape.Boot: return Loft.RoundedRect(0.34f, 4);
                case BodyShape.Kit: return Loft.RoundedRect(0.22f, 4);
                case BodyShape.Head: return Loft.Circle(14);
                default: return Loft.Circle(12);
            }
        }

        /// <summary>
        /// taper is the shape's one parameter: how much narrower the far end is than the near one for a
        /// limb, or how much narrower the waist is than the shoulders for a torso. Everything else
        /// ignores it.
        /// </summary>
        public static LoftRing[] Rings(BodyShape shape, float taper)
        {
            switch (shape)
            {
                case BodyShape.Limb:
                    return new LoftRing[]
                    {
                        new LoftRing(-0.500f, 0.58f),
                        new LoftRing(-0.455f, 0.94f),
                        new LoftRing(-0.250f, 1.00f),
                        new LoftRing( 0.300f, MathK.Lerp(1f, taper, 0.82f)),
                        new LoftRing( 0.455f, taper),
                        new LoftRing( 0.500f, taper * 0.58f)
                    };

                case BodyShape.Torso:
                    return new LoftRing[]
                    {
                        new LoftRing(-0.500f, taper * 0.80f, taper * 0.82f),
                        new LoftRing(-0.440f, taper, taper),
                        new LoftRing(-0.120f, MathK.Lerp(taper, 1f, 0.55f), MathK.Lerp(taper, 1f, 0.70f)),
                        new LoftRing( 0.330f, 1.00f, 1.00f),
                        new LoftRing( 0.455f, 0.96f, 0.90f),
                        new LoftRing( 0.500f, 0.74f, 0.66f)
                    };

                case BodyShape.Boot:
                    return new LoftRing[]
                    {
                        new LoftRing(-0.500f, 0.74f, 0.80f),
                        new LoftRing(-0.330f, 0.98f, 1.00f),
                        new LoftRing( 0.120f, 1.00f, 0.74f),
                        new LoftRing( 0.400f, 0.88f, 0.54f),
                        new LoftRing( 0.500f, 0.62f, 0.44f)
                    };

                case BodyShape.Kit:
                    return new LoftRing[]
                    {
                        new LoftRing(-0.500f, 0.86f),
                        new LoftRing(-0.430f, 1.00f),
                        new LoftRing( 0.430f, 1.00f),
                        new LoftRing( 0.500f, 0.86f)
                    };

                default:
                    // A sphere profile. The poles are single rings rather than points so the loft's own
                    // cap fans close them, which keeps the surface watertight like every other shape.
                    const int steps = 8;
                    LoftRing[] rings = new LoftRing[steps + 1];
                    for (int i = 0; i <= steps; i++)
                    {
                        float z = -0.5f + i / (float)steps;
                        float r = MathK.Sqrt(MathK.Max(0f, 1f - (z * 2f) * (z * 2f)));
                        rings[i] = new LoftRing(z, MathK.Max(0.08f, r));
                    }
                    return rings;
            }
        }

        /// <summary>Builds one shape's geometry. The art layer turns this into an engine mesh.</summary>
        public static void Build(BodyShape shape, float taper, out Vec3[] vertices, out int[] triangles)
        {
            Loft.Build(Outline(shape), Rings(shape, taper), out vertices, out triangles);
        }
    }
}
