using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>Conversions between the engine-free simulation types and UnityEngine types.</summary>
    public static class UnityBridge
    {
        public static Vector3 ToUnity(this Vec3 v) { return new Vector3(v.x, v.y, v.z); }
        public static Vec3 ToSim(this Vector3 v) { return new Vec3(v.x, v.y, v.z); }
        public static Vector2 ToUnity(this Vec2 v) { return new Vector2(v.x, v.y); }
        public static Vec2 ToSim(this Vector2 v) { return new Vec2(v.x, v.y); }

        /// <summary>Camera rotation for a simulated view, including the lean roll.</summary>
        public static Quaternion ViewRotation(float yaw, float pitch, float roll)
        {
            return Quaternion.Euler(pitch, yaw, roll);
        }
    }
}
