namespace Satisfying.Shared
{
    /// <summary>Deterministic spread + damage maths shared by the shooting client and the server.</summary>
    public static class ShotSolver
    {
        /// <summary>
        /// Cone sampled with a deterministic PRNG. The client draws the tracer along this exact ray and
        /// the server tests hits along it, so what you see is what gets registered.
        /// </summary>
        public static Vec3 PelletDirection(Vec3 aimDir, float spreadDegrees, int playerId, uint shotIndex, int pellet)
        {
            if (spreadDegrees <= 0.0001f) return aimDir.Normalized;

            DeterministicRandom rng = DeterministicRandom.ForShot(playerId, shotIndex, pellet);
            float angle = rng.NextFloat() * MathK.PI * 2f;
            // sqrt keeps the distribution even across the disc instead of clustering in the middle
            float radius = MathK.Sqrt(rng.NextFloat()) * (spreadDegrees * MathK.Deg2Rad);

            Vec3 fwd = aimDir.Normalized;
            Vec3 up = MathK.Abs(fwd.y) > 0.95f ? Vec3.Forward : Vec3.Up;
            Vec3 right = Vec3.Cross(up, fwd).Normalized;
            Vec3 realUp = Vec3.Cross(fwd, right);

            float sr = MathK.Sin(radius);
            Vec3 offset = right * (MathK.Cos(angle) * sr) + realUp * (MathK.Sin(angle) * sr);
            return (fwd * MathK.Cos(radius) + offset).Normalized;
        }

        public static float ZoneMultiplier(HitZone zone, WeaponTuning w)
        {
            switch (zone)
            {
                case HitZone.Head: return w.headMultiplier;
                case HitZone.Limb: return w.limbMultiplier;
                default: return 1f;
            }
        }

        public static float Damage(WeaponTuning w, HitZone zone, float distance)
        {
            return w.DamageAtRange(distance) * ZoneMultiplier(zone, w);
        }

        /// <summary>Recoil kick for one shot, applied to the shooter's own view (client authoritative aim).</summary>
        public static void RecoilKick(WeaponTuning w, int playerId, uint shotIndex, out float pitchKick, out float yawKick)
        {
            DeterministicRandom rng = DeterministicRandom.ForShot(playerId, shotIndex, 99);
            pitchKick = -w.recoilVertical * (0.75f + 0.25f * rng.NextFloat());
            yawKick = w.recoilHorizontal * rng.NextSigned();
        }
    }
}
