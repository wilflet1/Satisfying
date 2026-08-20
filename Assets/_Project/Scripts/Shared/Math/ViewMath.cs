namespace Satisfying.Shared
{
    /// <summary>
    /// Yaw/pitch to direction conversions that match Unity's convention
    /// (Y-up, left-handed, +Z forward, positive pitch looks DOWN) without needing quaternions.
    /// </summary>
    public static class ViewMath
    {
        public static Vec3 Forward(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * MathK.Deg2Rad;
            float p = pitchDeg * MathK.Deg2Rad;
            float cp = MathK.Cos(p);
            return new Vec3(MathK.Sin(y) * cp, -MathK.Sin(p), MathK.Cos(y) * cp);
        }

        public static Vec3 FlatForward(float yawDeg)
        {
            float y = yawDeg * MathK.Deg2Rad;
            return new Vec3(MathK.Sin(y), 0f, MathK.Cos(y));
        }

        public static Vec3 FlatRight(float yawDeg)
        {
            float y = yawDeg * MathK.Deg2Rad;
            return new Vec3(MathK.Cos(y), 0f, -MathK.Sin(y));
        }

        /// <summary>Signed yaw (degrees) of a flat direction vector.</summary>
        public static float YawOf(Vec3 dir)
        {
            return MathK.Atan2(dir.x, dir.z) * MathK.Rad2Deg;
        }

        public static float PitchOf(Vec3 dir)
        {
            Vec3 n = dir.Normalized;
            return -MathK.Asin(MathK.Clamp(n.y, -1f, 1f)) * MathK.Rad2Deg;
        }
    }
}
