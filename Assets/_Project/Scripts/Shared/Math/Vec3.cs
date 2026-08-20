using System;
using System.Globalization;

namespace Satisfying.Shared
{
    /// <summary>Engine-free 3D vector. Layout matches UnityEngine.Vector3 for cheap conversion.</summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 One = new Vec3(1f, 1f, 1f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Down = new Vec3(0f, -1f, 0f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public float SqrMagnitude { get { return x * x + y * y + z * z; } }
        public float Magnitude { get { return MathK.Sqrt(x * x + y * y + z * z); } }

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                if (m < 1e-5f) return Zero;
                float inv = 1f / m;
                return new Vec3(x * inv, y * inv, z * inv);
            }
        }

        /// <summary>Horizontal (XZ) component, Y zeroed.</summary>
        public Vec3 Flat { get { return new Vec3(x, 0f, z); } }
        public float FlatMagnitude { get { return MathK.Sqrt(x * x + z * z); } }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.x, -a.y, -a.z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return new Vec3(a.x * s, a.y * s, a.z * s); }
        public static Vec3 operator /(Vec3 a, float s) { return new Vec3(a.x / s, a.y / s, a.z / s); }
        public static bool operator ==(Vec3 a, Vec3 b) { return a.Equals(b); }
        public static bool operator !=(Vec3 a, Vec3 b) { return !a.Equals(b); }

        public static float Dot(Vec3 a, Vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);
        }

        public static float Distance(Vec3 a, Vec3 b) { return (a - b).Magnitude; }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = MathK.Clamp01(t);
            return new Vec3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vec3 LerpUnclamped(Vec3 a, Vec3 b, float t)
        {
            return new Vec3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vec3 MoveTowards(Vec3 current, Vec3 target, float maxDelta)
        {
            Vec3 diff = target - current;
            float m = diff.Magnitude;
            if (m <= maxDelta || m < 1e-6f) return target;
            return current + diff * (maxDelta / m);
        }

        public static Vec3 ClampMagnitude(Vec3 v, float max)
        {
            float sq = v.SqrMagnitude;
            if (sq <= max * max) return v;
            return v.Normalized * max;
        }

        /// <summary>Removes the component of v along normal (normal must be unit length).</summary>
        public static Vec3 ProjectOnPlane(Vec3 v, Vec3 normal)
        {
            float d = Dot(v, normal);
            return v - normal * d;
        }

        public static Vec3 Reflect(Vec3 v, Vec3 normal)
        {
            return v - normal * (2f * Dot(v, normal));
        }

        public static Vec3 Min(Vec3 a, Vec3 b)
        {
            return new Vec3(MathK.Min(a.x, b.x), MathK.Min(a.y, b.y), MathK.Min(a.z, b.z));
        }

        public static Vec3 Max(Vec3 a, Vec3 b)
        {
            return new Vec3(MathK.Max(a.x, b.x), MathK.Max(a.y, b.y), MathK.Max(a.z, b.z));
        }

        public bool Equals(Vec3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj) { return obj is Vec3 && Equals((Vec3)obj); }

        public override int GetHashCode()
        {
            unchecked { return (x.GetHashCode() * 397) ^ (y.GetHashCode() * 31) ^ z.GetHashCode(); }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", x, y, z);
        }
    }

    /// <summary>Engine-free 2D vector (input axes, screen space helpers).</summary>
    [Serializable]
    public struct Vec2
    {
        public float x;
        public float y;

        public Vec2(float x, float y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float SqrMagnitude { get { return x * x + y * y; } }
        public float Magnitude { get { return MathK.Sqrt(x * x + y * y); } }

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                if (m < 1e-5f) return Zero;
                return new Vec2(x / m, y / m);
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.x + b.x, a.y + b.y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.x - b.x, a.y - b.y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.x * s, a.y * s); }

        /// <summary>Clamps to the unit disc so diagonal input is never faster than cardinal input.</summary>
        public Vec2 ClampedToUnit()
        {
            float sq = SqrMagnitude;
            if (sq <= 1f) return this;
            float m = MathK.Sqrt(sq);
            return new Vec2(x / m, y / m);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", x, y);
        }
    }
}
