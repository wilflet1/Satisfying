namespace Satisfying.Shared
{
    public enum HitZone : byte
    {
        None = 0,
        Head = 1,
        Body = 2,
        Limb = 3
    }

    /// <summary>
    /// The shootable shape of a player, derived from the exact same state the simulation produces -
    /// including lean and side step, so peeking a corner really does expose your head and nothing else.
    /// </summary>
    public struct PlayerHitbox
    {
        public Vec3 BodyBottom;   // centre of the lower sphere of the body capsule
        public Vec3 BodyTop;      // centre of the upper sphere (leans with the torso)
        public float BodyRadius;
        public Vec3 HeadCenter;
        public float HeadRadius;
        public float LegTopY;     // below this height a body hit counts as a limb

        public static PlayerHitbox FromState(in PlayerSimState s, MovementTuning t)
        {
            PlayerHitbox h = new PlayerHitbox();
            float lean = s.EffectiveLean(t);
            Vec3 right = ViewMath.FlatRight(s.Yaw);

            h.BodyRadius = t.radius * 0.82f;
            h.HeadRadius = 0.14f;

            // The torso capsule stops at the neck. If it reached the crown its rounded cap would sit in
            // front of the head sphere and eat headshots, because the nearest surface wins.
            float top = MathK.Max(h.BodyRadius + 0.01f, s.Height - 0.42f);
            Vec3 leanShift = right * (lean * t.leanOffset) + Vec3.Down * (MathK.Abs(lean) * t.leanDrop);

            h.BodyBottom = s.Position + Vec3.Up * h.BodyRadius;
            h.BodyTop = s.Position + Vec3.Up * top + leanShift;
            h.HeadCenter = s.Position + Vec3.Up * (s.EyeHeight(t) + 0.05f) + leanShift;
            h.LegTopY = s.Position.y + s.Height * 0.42f;

            if (s.Stance == Stance.Prone)
            {
                // Prone spreads the body out along the facing direction instead of standing it up.
                Vec3 fwd = ViewMath.FlatForward(s.Yaw);
                h.BodyBottom = s.Position + Vec3.Up * h.BodyRadius - fwd * 0.35f;
                h.BodyTop = s.Position + Vec3.Up * h.BodyRadius + fwd * 0.35f + leanShift;
                h.HeadCenter = s.Position + Vec3.Up * (s.EyeHeight(t) + 0.02f) + fwd * 0.5f + leanShift;
                h.LegTopY = s.Position.y - 1f; // nothing counts as a leg when you are flat
            }

            return h;
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

        /// <summary>Head first, then body. Returns the nearest zone the ray enters.</summary>
        public static bool TestPlayer(Vec3 ro, Vec3 rd, in PlayerHitbox h, float maxDistance, out HitTestResult result)
        {
            result = new HitTestResult();
            float best = maxDistance;
            HitZone zone = HitZone.None;

            float tHead;
            if (Sphere(ro, rd, h.HeadCenter, h.HeadRadius, out tHead) && tHead >= 0f && tHead < best)
            {
                best = tHead;
                zone = HitZone.Head;
            }

            float tBody;
            if (Capsule(ro, rd, h.BodyBottom, h.BodyTop, h.BodyRadius, out tBody) && tBody >= 0f && tBody < best)
            {
                best = tBody;
                Vec3 p = ro + rd * tBody;
                zone = p.y < h.LegTopY ? HitZone.Limb : HitZone.Body;
            }

            if (zone == HitZone.None) return false;
            result.Zone = zone;
            result.Distance = best;
            result.Point = ro + rd * best;
            return true;
        }
    }
}
