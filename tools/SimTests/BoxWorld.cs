using System.Collections.Generic;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// Analytic axis-aligned-box implementation of ICollisionWorld.
    /// This is the reference world for the headless tests - same interface Unity's capsule-cast
    /// implementation fulfils, so the movement code under test is byte-for-byte the shipping code.
    /// </summary>
    public sealed class BoxWorld : ICollisionWorld
    {
        public struct Box
        {
            public Vec3 Min;
            public Vec3 Max;
        }

        readonly List<Box> _boxes = new List<Box>();

        public void AddBox(Vec3 center, Vec3 size)
        {
            Box b;
            Vec3 h = size * 0.5f;
            b.Min = center - h;
            b.Max = center + h;
            _boxes.Add(b);
        }

        public static BoxWorld FlatGround(float halfSize = 60f)
        {
            BoxWorld w = new BoxWorld();
            w.AddBox(new Vec3(0f, -1f, 0f), new Vec3(halfSize * 2f, 2f, halfSize * 2f)); // top at y=0
            return w;
        }

        // ------------------------------------------------------------------ helpers
        static Vec3 ClosestOnBox(Box b, Vec3 p)
        {
            return new Vec3(
                MathK.Clamp(p.x, b.Min.x, b.Max.x),
                MathK.Clamp(p.y, b.Min.y, b.Max.y),
                MathK.Clamp(p.z, b.Min.z, b.Max.z));
        }

        static bool SphereVsBox(Box b, Vec3 center, float radius, out Vec3 normal, out float depth)
        {
            Vec3 closest = ClosestOnBox(b, center);
            Vec3 delta = center - closest;
            float d2 = delta.SqrMagnitude;
            if (d2 > radius * radius) { normal = Vec3.Up; depth = 0f; return false; }

            if (d2 > 1e-8f)
            {
                float d = MathK.Sqrt(d2);
                normal = delta * (1f / d);
                depth = radius - d;
                return true;
            }

            // Centre inside the box: push out of the nearest face.
            float[] dist = {
                center.x - b.Min.x, b.Max.x - center.x,
                center.y - b.Min.y, b.Max.y - center.y,
                center.z - b.Min.z, b.Max.z - center.z
            };
            Vec3[] dirs = { new Vec3(-1,0,0), new Vec3(1,0,0), new Vec3(0,-1,0), new Vec3(0,1,0), new Vec3(0,0,-1), new Vec3(0,0,1) };
            int best = 0;
            for (int i = 1; i < 6; i++) if (dist[i] < dist[best]) best = i;
            normal = dirs[best];
            depth = dist[best] + radius;
            return true;
        }

        const int SphereSamples = 6;

        static void CapsuleSpheres(Vec3 foot, float height, float radius, Vec3[] outPts, out float r)
        {
            r = MathK.Min(radius, height * 0.5f);
            float lo = foot.y + r;
            float hi = foot.y + MathK.Max(height - r, r);
            for (int i = 0; i < SphereSamples; i++)
            {
                float t = SphereSamples == 1 ? 0f : i / (float)(SphereSamples - 1);
                outPts[i] = new Vec3(foot.x, MathK.Lerp(lo, hi, t), foot.z);
            }
        }

        // ------------------------------------------------------------------ ICollisionWorld
        public bool CheckCapsule(Vec3 footPos, float height, float radius)
        {
            Vec3[] pts = new Vec3[SphereSamples];
            float r;
            CapsuleSpheres(footPos, height, radius, pts, out r);
            float shrunk = r - 0.005f;
            for (int i = 0; i < _boxes.Count; i++)
            {
                for (int k = 0; k < pts.Length; k++)
                {
                    Vec3 n; float d;
                    if (SphereVsBox(_boxes[i], pts[k], shrunk, out n, out d)) return true;
                }
            }
            return false;
        }

        public bool CheckSphere(Vec3 center, float radius)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                Vec3 n; float d;
                if (SphereVsBox(_boxes[i], center, radius, out n, out d)) return true;
            }
            return false;
        }

        /// <summary>
        /// Exact downward sphere cast of the capsule's bottom sphere. Using the same rounded-corner
        /// maths as the depenetration keeps "am I standing on it" consistent with "does it push me out",
        /// which is what stops a capsule perched on a ledge edge from flickering between heights.
        /// </summary>
        public bool GroundProbe(Vec3 footPos, float radius, float maxDistance, out float distance, out Vec3 normal)
        {
            distance = float.MaxValue;
            normal = Vec3.Up;
            bool found = false;

            Vec3 c = new Vec3(footPos.x, footPos.y + radius, footPos.z);
            float r2 = radius * radius;

            for (int i = 0; i < _boxes.Count; i++)
            {
                Box b = _boxes[i];
                float dx = MathK.Max(0f, MathK.Max(b.Min.x - c.x, c.x - b.Max.x));
                float dz = MathK.Max(0f, MathK.Max(b.Min.z - c.z, c.z - b.Max.z));
                float horiz = dx * dx + dz * dz;
                if (horiz >= r2) continue;

                float contactY = b.Max.y + MathK.Sqrt(r2 - horiz);
                float d = c.y - contactY;
                if (d < -0.05f) continue;          // we are inside / above the top face by a lot: not ground
                if (d < 0f) d = 0f;
                if (d <= maxDistance && d < distance)
                {
                    distance = d;
                    normal = Vec3.Up;
                    found = true;
                }
            }

            if (!found) distance = 0f;
            return found;
        }

        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out float distance, out Vec3 normal)
        {
            distance = maxDistance;
            normal = Vec3.Up;
            bool hit = false;
            Vec3 dir = direction.Normalized;
            for (int i = 0; i < _boxes.Count; i++)
            {
                float t;
                Vec3 n;
                if (RayVsBox(_boxes[i], origin, dir, maxDistance, out t, out n) && t < distance)
                {
                    distance = t;
                    normal = n;
                    hit = true;
                }
            }
            return hit;
        }

        static bool RayVsBox(Box b, Vec3 o, Vec3 d, float maxDist, out float t, out Vec3 normal)
        {
            float tmin = 0f, tmax = maxDist;
            normal = Vec3.Up;
            int axis = -1;
            float sign = 1f;

            for (int a = 0; a < 3; a++)
            {
                float od = a == 0 ? d.x : (a == 1 ? d.y : d.z);
                float oo = a == 0 ? o.x : (a == 1 ? o.y : o.z);
                float mn = a == 0 ? b.Min.x : (a == 1 ? b.Min.y : b.Min.z);
                float mx = a == 0 ? b.Max.x : (a == 1 ? b.Max.y : b.Max.z);

                if (MathK.Abs(od) < 1e-7f)
                {
                    if (oo < mn || oo > mx) { t = 0f; return false; }
                    continue;
                }
                float inv = 1f / od;
                float t1 = (mn - oo) * inv;
                float t2 = (mx - oo) * inv;
                float s = -1f;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; s = 1f; }
                if (t1 > tmin) { tmin = t1; axis = a; sign = s; }
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { t = 0f; return false; }
            }

            t = tmin;
            if (axis >= 0)
                normal = axis == 0 ? new Vec3(sign, 0, 0) : (axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign));
            return t >= 0f && t <= maxDist;
        }

        public MoveResult MoveCapsule(Vec3 footPos, float height, float radius, Vec3 displacement, float stepHeight, float slopeLimitDeg)
        {
            MoveResult res = new MoveResult();
            res.GroundNormal = Vec3.Up;
            res.WallNormal = Vec3.Zero;

            Vec3 pos = footPos;
            float len = displacement.Magnitude;
            int steps = 1 + (int)(len / MathK.Max(0.05f, radius * 0.5f));
            if (steps > 16) steps = 16;
            Vec3 stepDelta = displacement / steps;

            Vec3[] pts = new Vec3[SphereSamples];
            for (int s = 0; s < steps; s++)
            {
                Vec3 before = pos;
                pos += stepDelta;

                for (int iter = 0; iter < 4; iter++)
                {
                    float r;
                    CapsuleSpheres(pos, height, radius, pts, out r);
                    bool any = false;
                    for (int i = 0; i < _boxes.Count; i++)
                    {
                        for (int k = 0; k < pts.Length; k++)
                        {
                            Vec3 n; float depth;
                            if (!SphereVsBox(_boxes[i], pts[k], r, out n, out depth)) continue;
                            if (depth <= 0f) continue;
                            pos += n * depth;
                            any = true;

                            if (n.y > 0.7f) res.Flags |= MoveCollisionFlags.Below;
                            else if (n.y < -0.7f) res.Flags |= MoveCollisionFlags.Above;
                            else { res.Flags |= MoveCollisionFlags.Sides; res.WallNormal = n; }
                            break;
                        }
                    }
                    if (!any) break;
                }

                // Blocked sideways? Try stepping up over it.
                if ((res.Flags & MoveCollisionFlags.Sides) != 0 && stepHeight > 0f)
                {
                    Vec3 flatWanted = new Vec3(before.x + stepDelta.x, pos.y, before.z + stepDelta.z);
                    Vec3 raised = new Vec3(flatWanted.x, before.y + stepHeight, flatWanted.z);
                    if (!CheckCapsule(raised, height, radius))
                    {
                        float d; Vec3 n2;
                        if (GroundProbe(raised, radius, stepHeight + 0.05f, out d, out n2))
                        {
                            pos = new Vec3(raised.x, raised.y - d, raised.z);
                            res.Flags &= ~MoveCollisionFlags.Sides;
                            res.Flags |= MoveCollisionFlags.Below;
                        }
                    }
                }
            }

            res.Position = pos;
            return res;
        }
    }
}
