using System;

namespace Satisfying.Shared
{
    /// <summary>
    /// Engine-free math helpers. The shared simulation must never reference UnityEngine so that
    /// the exact same source compiles (and is unit tested) outside the editor.
    /// </summary>
    public static class MathK
    {
        public const float PI = 3.14159265358979f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1e-6f;

        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Tan(float v) { return (float)Math.Tan(v); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Asin(float v) { return (float)Math.Asin(Clamp(v, -1f, 1f)); }
        public static float Acos(float v) { return (float)Math.Acos(Clamp(v, -1f, 1f)); }
        public static float Abs(float v) { return v < 0f ? -v : v; }
        public static int Abs(int v) { return v < 0 ? -v : v; }
        public static float Sign(float v) { return v < 0f ? -1f : 1f; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Exp(float a) { return (float)Math.Exp(a); }
        public static int FloorToInt(float v) { return (int)Math.Floor(v); }
        public static int RoundToInt(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }
        public static int CeilToInt(float v) { return (int)Math.Ceiling(v); }

        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }
        public static float InverseLerp(float a, float b, float v)
        {
            if (Abs(b - a) < Epsilon) return 0f;
            return Clamp01((v - a) / (b - a));
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (Abs(diff) <= maxDelta) return target;
            return current + Sign(diff) * maxDelta;
        }

        /// <summary>Frame-rate independent exponential smoothing. rate is "how much of the gap is closed per second".</summary>
        public static float ExpSmooth(float current, float target, float sharpness, float dt)
        {
            if (sharpness <= 0f) return target;
            float t = 1f - Exp(-sharpness * dt);
            return current + (target - current) * t;
        }

        public static float Repeat(float t, float length)
        {
            float r = t - (float)Math.Floor(t / length) * length;
            return Clamp(r, 0f, length);
        }

        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            if (delta > 180f) delta -= 360f;
            return delta;
        }

        public static float NormalizeAngle180(float angle)
        {
            float a = Repeat(angle + 180f, 360f) - 180f;
            return a;
        }

        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Critically damped spring step used by view/recoil springs. Stable at large dt.</summary>
        public static void Spring(ref float value, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            // Semi-implicit euler, sub-stepped so high stiffness never explodes.
            int steps = 1 + (int)(dt * 120f);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = (target - value) * stiffness - velocity * damping;
                velocity += accel * h;
                value += velocity * h;
            }
        }
    }
}
