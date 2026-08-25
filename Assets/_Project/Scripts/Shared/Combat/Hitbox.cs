namespace Satisfying.Shared
{
    /// <summary>
    /// What you hit. Seven parts and a miss, which is exactly three bits - the width the wire already
    /// spends on this, so a proper breakdown costs nothing to replicate.
    /// </summary>
    public enum HitZone : byte
    {
        None = 0,
        Head = 1,
        Neck = 2,
        Chest = 3,
        Stomach = 4,
        Arm = 5,
        Leg = 6,
        Foot = 7
    }

    /// <summary>
    /// The shootable shape of a player: fifteen capsules laid over BodyPose, which is the same skeleton
    /// the model is drawn from. Feet, shins, thighs, pelvis, stomach, chest, neck, head and both arms
    /// are all separate, so a leg peeking past a doorframe is a leg and nothing else.
    ///
    /// The old shape was a head sphere and one capsule from the ankles to the neck, which meant a shot
    /// at the gap between someone's legs hit them, and a shot at an arm held out over cover did not.
    /// </summary>
    public struct PlayerHitbox
    {
        /// <summary>World-space skeleton. Every segment below is two of its joints and a radius.</summary>
        public BodyPose Pose;

        /// <summary>Broad phase: one sphere round the lot, so a pellet that misses costs one dot product.</summary>
        public Vec3 BoundsCenter;
        public float BoundsRadius;

        public const int SegmentCount = 15;

        public Vec3 HeadCenter { get { return Pose.Head; } }
        public float HeadRadius { get { return Pose.HeadRadius; } }

        /// <summary>
        /// The weapon is not optional: it decides where the support arm is, and an arm is a hit zone.
        /// Two call sites disagreeing about which gun someone is holding would be two call sites
        /// disagreeing about where their left elbow is.
        /// </summary>
        public static PlayerHitbox FromState(in PlayerSimState s, MovementTuning t, WeaponTuning weapon)
        {
            PlayerHitbox h = new PlayerHitbox();
            h.Pose = BodyPose.Build(in s, t, weapon).ToWorld(s.Position, s.Yaw);

            Vec3 lo = h.Pose.Head;
            Vec3 hi = h.Pose.Head;
            float fat = 0f;
            for (int i = 0; i < SegmentCount; i++)
            {
                Vec3 a, b;
                float radius;
                HitZone zone;
                h.Segment(i, out a, out b, out radius, out zone);
                lo = Vec3.Min(lo, Vec3.Min(a, b));
                hi = Vec3.Max(hi, Vec3.Max(a, b));
                if (radius > fat) fat = radius;
            }

            h.BoundsCenter = (lo + hi) * 0.5f;
            h.BoundsRadius = (hi - lo).Magnitude * 0.5f + fat;
            return h;
        }

        /// <summary>
        /// One capsule. Indexed rather than stored in an array because this is rebuilt for every pellet
        /// of every shot against every rewound opponent, and an allocation there is an allocation in the
        /// server's hot loop.
        /// </summary>
        public void Segment(int index, out Vec3 a, out Vec3 b, out float radius, out HitZone zone)
        {
            switch (index)
            {
                case 0:  a = Pose.Head;          b = Pose.Head;         radius = Pose.HeadRadius;     zone = HitZone.Head;    return;
                case 1:  a = Pose.NeckBase;      b = Pose.Head;         radius = Pose.NeckRadius;     zone = HitZone.Neck;    return;
                case 2:  a = Pose.ChestBase;     b = Pose.ChestTop;     radius = Pose.ChestRadius;    zone = HitZone.Chest;   return;
                case 3:  a = Pose.Pelvis;        b = Pose.ChestBase;    radius = Pose.StomachRadius;  zone = HitZone.Stomach; return;
                case 4:  a = Pose.LeftShoulder;  b = Pose.LeftElbow;    radius = Pose.UpperArmRadius; zone = HitZone.Arm;     return;
                case 5:  a = Pose.LeftElbow;     b = Pose.LeftHand;     radius = Pose.ForearmRadius;  zone = HitZone.Arm;     return;
                case 6:  a = Pose.RightShoulder; b = Pose.RightElbow;   radius = Pose.UpperArmRadius; zone = HitZone.Arm;     return;
                case 7:  a = Pose.RightElbow;    b = Pose.RightHand;    radius = Pose.ForearmRadius;  zone = HitZone.Arm;     return;
                case 8:  a = Pose.LeftHip;       b = Pose.LeftKnee;     radius = Pose.ThighRadius;    zone = HitZone.Leg;     return;
                case 9:  a = Pose.LeftKnee;      b = Pose.LeftAnkle;    radius = Pose.ShinRadius;     zone = HitZone.Leg;     return;
                case 10: a = Pose.RightHip;      b = Pose.RightKnee;    radius = Pose.ThighRadius;    zone = HitZone.Leg;     return;
                case 11: a = Pose.RightKnee;     b = Pose.RightAnkle;   radius = Pose.ShinRadius;     zone = HitZone.Leg;     return;
                case 12: a = Pose.LeftAnkle;     b = Pose.LeftToe;      radius = Pose.FootRadius;     zone = HitZone.Foot;    return;
                case 13: a = Pose.RightAnkle;    b = Pose.RightToe;     radius = Pose.FootRadius;     zone = HitZone.Foot;    return;
                default: a = Pose.Pelvis;        b = Pose.Pelvis;       radius = Pose.StomachRadius;  zone = HitZone.Stomach; return;
            }
        }
    }

    public struct HitTestResult
    {
        public HitZone Zone;
        public float Distance;
        public Vec3 Point;
    }

    public static class RayGeometry
    {
        /// <summary>Ray vs sphere. Direction must be normalised.</summary>
        public static bool Sphere(Vec3 ro, Vec3 rd, Vec3 center, float radius, out float t)
        {
            Vec3 oc = ro - center;
            float b = Vec3.Dot(oc, rd);
            float c = oc.SqrMagnitude - radius * radius;
            float h = b * b - c;
            if (h < 0f) { t = 0f; return false; }
            h = MathK.Sqrt(h);
            float t0 = -b - h;
            float t1 = -b + h;
            t = t0 >= 0f ? t0 : t1;
            return t >= 0f;
        }

        /// <summary>Ray vs capsule defined by segment a..b with radius r. Direction must be normalised.</summary>
        public static bool Capsule(Vec3 ro, Vec3 rd, Vec3 a, Vec3 b, float r, out float t)
        {
            t = 0f;
            Vec3 ba = b - a;
            Vec3 oa = ro - a;
            float baba = Vec3.Dot(ba, ba);
            if (baba < 1e-8f) return Sphere(ro, rd, a, r, out t);

            float bard = Vec3.Dot(ba, rd);
            float baoa = Vec3.Dot(ba, oa);
            float rdoa = Vec3.Dot(rd, oa);
            float oaoa = Vec3.Dot(oa, oa);

            float A = baba - bard * bard;
            float B = baba * rdoa - baoa * bard;
            float C = baba * oaoa - baoa * baoa - r * r * baba;
            float h = B * B - A * C;

            if (h >= 0f && MathK.Abs(A) > 1e-8f)
            {
                float tt = (-B - MathK.Sqrt(h)) / A;
                float y = baoa + tt * bard;
                if (y > 0f && y < baba && tt >= 0f) { t = tt; return true; }
            }

            // Caps
            float tCap;
            bool hitA = Sphere(ro, rd, a, r, out tCap);
            float best = hitA ? tCap : float.MaxValue;
            float tCapB;
            bool hitB = Sphere(ro, rd, b, r, out tCapB);
            if (hitB && tCapB < best) best = tCapB;
            if (best == float.MaxValue) return false;
            t = best;
            return true;
        }

        /// <summary>
        /// Nearest part of the body the ray enters. Nearest wins outright - there is no priority list,
        /// because a rule that promotes a head over a nearer arm is a rule that lets you shoot through
        /// your opponent's own forearm.
        /// </summary>
        public static bool TestPlayer(Vec3 ro, Vec3 rd, in PlayerHitbox h, float maxDistance, out HitTestResult result)
        {
            result = new HitTestResult();

            float tBounds;
            if (!Sphere(ro, rd, h.BoundsCenter, h.BoundsRadius, out tBounds) || tBounds > maxDistance)
                return false;

            float best = maxDistance;
            HitZone zone = HitZone.None;

            for (int i = 0; i < PlayerHitbox.SegmentCount; i++)
            {
                Vec3 a, b;
                float radius;
                HitZone segmentZone;
                h.Segment(i, out a, out b, out radius, out segmentZone);

                float t;
                if (!Capsule(ro, rd, a, b, radius, out t)) continue;
                if (t < 0f || t >= best) continue;
                best = t;
                zone = segmentZone;
            }

            if (zone == HitZone.None) return false;
            result.Zone = zone;
            result.Distance = best;
            result.Point = ro + rd * best;
            return true;
        }
    }
}
