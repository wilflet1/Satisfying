namespace Satisfying.Shared
{
    /// <summary>Axis aligned box helpers. Every piece of map geometry in this game is one.</summary>
    public struct Box
    {
        public Vec3 Center;
        public Vec3 Size;

        public Box(Vec3 center, Vec3 size)
        {
            Center = center;
            Size = size;
        }

        public Vec3 Min { get { return Center - Size * 0.5f; } }
        public Vec3 Max { get { return Center + Size * 0.5f; } }

        public bool Contains(Vec3 point)
        {
            Vec3 min = Min;
            Vec3 max = Max;
            return point.x >= min.x && point.x <= max.x
                && point.y >= min.y && point.y <= max.y
                && point.z >= min.z && point.z <= max.z;
        }

        public Vec3 ClosestPoint(Vec3 point)
        {
            Vec3 min = Min;
            Vec3 max = Max;
            return new Vec3(
                MathK.Clamp(point.x, min.x, max.x),
                MathK.Clamp(point.y, min.y, max.y),
                MathK.Clamp(point.z, min.z, max.z));
        }

        public float DistanceTo(Vec3 point)
        {
            return (point - ClosestPoint(point)).Magnitude;
        }
    }

    public static class BoxMath
    {
        /// <summary>Slab test. Direction must be normalised. Returns the entry distance.</summary>
        public static bool Raycast(in Box box, Vec3 origin, Vec3 direction, float maxDistance, out float distance)
        {
            distance = 0f;
            Vec3 min = box.Min;
            Vec3 max = box.Max;

            float tmin = 0f;
            float tmax = maxDistance;

            for (int axis = 0; axis < 3; axis++)
            {
                float d = axis == 0 ? direction.x : (axis == 1 ? direction.y : direction.z);
                float o = axis == 0 ? origin.x : (axis == 1 ? origin.y : origin.z);
                float lo = axis == 0 ? min.x : (axis == 1 ? min.y : min.z);
                float hi = axis == 0 ? max.x : (axis == 1 ? max.y : max.z);

                if (MathK.Abs(d) < 1e-7f)
                {
                    if (o < lo || o > hi) return false;
                    continue;
                }

                float inv = 1f / d;
                float t1 = (lo - o) * inv;
                float t2 = (hi - o) * inv;
                if (t1 > t2) { float swap = t1; t1 = t2; t2 = swap; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }

            distance = tmin;
            return tmin >= 0f && tmin <= maxDistance;
        }

        /// <summary>True when a sphere overlaps the box - used for melee arcs and prop clearance.</summary>
        public static bool OverlapsSphere(in Box box, Vec3 center, float radius)
        {
            return box.DistanceTo(center) <= radius;
        }
    }
}
