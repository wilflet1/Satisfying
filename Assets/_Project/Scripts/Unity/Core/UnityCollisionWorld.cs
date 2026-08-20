using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// ICollisionWorld on top of Unity's physics queries: depenetrate, then collide-and-slide with
    /// capsule casts, with an explicit step-up pass. No CharacterController, because the simulation
    /// has to be able to run this same movement several times inside one frame when it replays
    /// predicted input after a server correction.
    /// </summary>
    public sealed class UnityCollisionWorld : ICollisionWorld
    {
        const float Skin = 0.015f;
        const int MaxSlides = 5;

        readonly int _mask;
        readonly Collider[] _overlap = new Collider[16];
        readonly CapsuleCollider _probe;
        readonly Transform _probeTransform;

        public UnityCollisionWorld(int layerMask, int probeLayer)
        {
            _mask = layerMask;

            GameObject go = new GameObject("~capsule probe");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = probeLayer;
            _probe = go.AddComponent<CapsuleCollider>();
            _probe.isTrigger = true;
            _probeTransform = go.transform;
        }

        public void Dispose()
        {
            if (_probeTransform != null) Object.Destroy(_probeTransform.gameObject);
        }

        // ------------------------------------------------------------------ helpers
        static float ClampRadius(float height, float radius)
        {
            return Mathf.Min(radius, Mathf.Max(0.02f, height * 0.5f - 0.001f));
        }

        static void Points(Vector3 foot, float height, float radius, out Vector3 p0, out Vector3 p1, out float r)
        {
            r = ClampRadius(height, radius);
            p0 = foot + Vector3.up * r;
            p1 = foot + Vector3.up * Mathf.Max(height - r, r);
        }

        bool Cast(Vector3 foot, float height, float radius, Vector3 dir, float distance, out RaycastHit hit)
        {
            Vector3 p0, p1;
            float r;
            Points(foot, height, radius, out p0, out p1, out r);
            return Physics.CapsuleCast(p0, p1, r - Skin * 0.5f, dir, out hit, distance, _mask, QueryTriggerInteraction.Ignore);
        }

        Vector3 Depenetrate(Vector3 foot, float height, float radius)
        {
            Vector3 p0, p1;
            float r;
            Points(foot, height, radius, out p0, out p1, out r);

            int count = Physics.OverlapCapsuleNonAlloc(p0, p1, r, _overlap, _mask, QueryTriggerInteraction.Ignore);
            if (count == 0) return foot;

            _probe.height = Mathf.Max(height, r * 2f);
            _probe.radius = r;
            _probe.center = new Vector3(0f, _probe.height * 0.5f, 0f);

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlap[i];
                if (other == null || other == _probe) continue;

                _probeTransform.position = foot;
                _probeTransform.rotation = Quaternion.identity;

                Vector3 dir;
                float dist;
                if (!Physics.ComputePenetration(_probe, foot, Quaternion.identity,
                        other, other.transform.position, other.transform.rotation, out dir, out dist))
                    continue;

                foot += dir * (dist + Skin * 0.5f);
            }
            return foot;
        }

        // ------------------------------------------------------------------ ICollisionWorld
        public MoveResult MoveCapsule(Vec3 footPos, float height, float radius, Vec3 displacement, float stepHeight, float slopeLimitDeg)
        {
            MoveResult result = new MoveResult();
            result.GroundNormal = Vec3.Up;

            Vector3 pos = Depenetrate(footPos.ToUnity(), height, radius);
            Vector3 remaining = displacement.ToUnity();
            float slopeCos = Mathf.Cos(Mathf.Clamp(slopeLimitDeg, 1f, 89f) * Mathf.Deg2Rad);

            for (int i = 0; i < MaxSlides; i++)
            {
                float distance = remaining.magnitude;
                if (distance < 1e-5f) break;
                Vector3 dir = remaining / distance;

                RaycastHit hit;
                if (!Cast(pos, height, radius, dir, distance + Skin, out hit))
                {
                    pos += remaining;
                    break;
                }

                float travel = Mathf.Max(0f, hit.distance - Skin);
                pos += dir * travel;
                remaining -= dir * travel;

                if (hit.normal.y >= slopeCos)
                {
                    result.Flags |= MoveCollisionFlags.Below;
                    result.GroundNormal = hit.normal.ToSim();
                }
                else if (hit.normal.y < -0.4f)
                {
                    result.Flags |= MoveCollisionFlags.Above;
                }
                else
                {
                    // A wall: try to walk up it before sliding along it.
                    Vector3 horizontal = new Vector3(remaining.x, 0f, remaining.z);
                    Vector3 stepped;
                    if (stepHeight > 0.01f && horizontal.sqrMagnitude > 1e-6f &&
                        TryStepUp(pos, height, radius, horizontal, stepHeight, slopeCos, out stepped))
                    {
                        pos = stepped;
                        remaining = Vector3.zero;
                        result.Flags |= MoveCollisionFlags.Below;
                        break;
                    }

                    result.Flags |= MoveCollisionFlags.Sides;
                    result.WallNormal = hit.normal.ToSim();
                }

                remaining = Vector3.ProjectOnPlane(remaining, hit.normal);
            }

            result.Position = Depenetrate(pos, height, radius).ToSim();
            return result;
        }

        bool TryStepUp(Vector3 foot, float height, float radius, Vector3 horizontal, float stepHeight, float slopeCos, out Vector3 result)
        {
            result = foot;
            RaycastHit hit;

            // Room above?
            if (Cast(foot, height, radius, Vector3.up, stepHeight + Skin, out hit)) return false;
            Vector3 raised = foot + Vector3.up * stepHeight;

            // Room forward at the raised height?
            float distance = horizontal.magnitude;
            Vector3 dir = horizontal / distance;
            if (Cast(raised, height, radius, dir, distance + Skin, out hit)) return false;
            Vector3 forward = raised + dir * distance;

            // Something to land on?
            if (!Cast(forward, height, radius, Vector3.down, stepHeight + Skin * 2f, out hit)) return false;
            if (hit.normal.y < slopeCos) return false;

            result = forward + Vector3.down * Mathf.Max(0f, hit.distance - Skin);
            return true;
        }

        public bool CheckCapsule(Vec3 footPos, float height, float radius)
        {
            Vector3 p0, p1;
            float r;
            Points(footPos.ToUnity(), height, radius, out p0, out p1, out r);
            return Physics.CheckCapsule(p0, p1, r - Skin, _mask, QueryTriggerInteraction.Ignore);
        }

        public bool CheckSphere(Vec3 center, float radius)
        {
            return Physics.CheckSphere(center.ToUnity(), radius, _mask, QueryTriggerInteraction.Ignore);
        }

        public bool GroundProbe(Vec3 footPos, float radius, float maxDistance, out float distance, out Vec3 normal)
        {
            distance = 0f;
            normal = Vec3.Up;

            float r = Mathf.Max(0.02f, radius * 0.92f);
            Vector3 origin = footPos.ToUnity() + Vector3.up * (r + Skin);

            RaycastHit hit;
            if (!Physics.SphereCast(origin, r, Vector3.down, out hit, maxDistance + Skin * 2f, _mask, QueryTriggerInteraction.Ignore))
                return false;

            distance = Mathf.Max(0f, hit.distance - Skin);
            normal = hit.normal.sqrMagnitude > 0.01f ? hit.normal.ToSim() : Vec3.Up;
            return distance <= maxDistance;
        }

        public bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out float distance, out Vec3 normal)
        {
            distance = maxDistance;
            normal = Vec3.Up;
            RaycastHit hit;
            if (!Physics.Raycast(origin.ToUnity(), direction.ToUnity(), out hit, maxDistance, _mask, QueryTriggerInteraction.Ignore))
                return false;
            distance = hit.distance;
            normal = hit.normal.ToSim();
            return true;
        }

        /// <summary>Convenience for effects that also want to know what they hit.</summary>
        public bool RaycastDetailed(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
        {
            return Physics.Raycast(origin, direction, out hit, maxDistance, _mask, QueryTriggerInteraction.Ignore);
        }
    }
}
