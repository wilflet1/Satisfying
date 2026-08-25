using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// How a sound gets from where it happened to your ear.
    ///
    /// The old version cast three lines and, if they were blocked, turned the volume down to a fifth
    /// and the treble off. That is wrong in both directions at once: a crate in an open room silenced
    /// a man walking behind it, and a wall did no more than the crate did. Neither is what happens.
    /// Sound bends round the edge of an obstacle - so a footstep behind a crate is not quieter so much
    /// as it arrives from the side of the crate - and it also passes straight through material, losing
    /// the top end and losing more of it the thicker the material is.
    ///
    /// So there are two paths, and both are worked out:
    ///
    ///   through   straight at you, attenuated by how much material is in the way and by how much
    ///             low end the sound had to start with. A rifle going off two rooms away still
    ///             reaches you; a boot on carpet does not.
    ///   around    the shortest bend the sound can take round the obstruction. It is placed at the
    ///             bend point, at the length of the whole path - so it is both quieter (further away)
    ///             and, more importantly, in the wrong direction: at the corner, which is where a
    ///             listener actually localises it.
    ///
    /// Whichever is louder decides where the voice goes; the two sum for the level.
    /// </summary>
    public struct SoundPath
    {
        /// <summary>Where to put the voice. Not the source position when the sound bends to reach you.</summary>
        public Vector3 Position;

        /// <summary>Extra multiplier on top of the distance rolloff.</summary>
        public float Gain;

        /// <summary>Low-pass corner. Material takes the top end off long before it takes the level.</summary>
        public float Cutoff;

        /// <summary>Diagnostics: how much material the straight line passes through.</summary>
        public float Thickness;

        /// <summary>Diagnostics: true when the level you hear came round a corner rather than through.</summary>
        public bool Bent;
    }

    public sealed class SoundPropagation
    {
        public int Mask;
        public Transform Listener;
        public FeelTuning Feel;

        /// <summary>
        /// Perpendicular directions tried for a way round, and how far out to try them. Four
        /// directions is enough for the shapes in this game - doorways, window openings, crates and
        /// pillars - and the radii stop at four and a half metres because a sound that has to detour
        /// further than that has lost to the through-the-wall path anyway.
        /// </summary>
        static readonly float[] Radii = { 0.7f, 1.6f, 3.0f, 4.5f };

        public SoundPath Solve(Vector3 source, float carry)
        {
            SoundPath path = new SoundPath();
            path.Position = source;
            path.Gain = 1f;
            path.Cutoff = 22000f;

            if (Listener == null || Mask == 0) return path;

            Vector3 ear = Listener.position;
            Vector3 delta = source - ear;
            float direct = delta.magnitude;
            if (direct < 0.5f) return path;

            Vector3 forward = delta / direct;

            // Nothing in the way is the common case and it costs one cast.
            RaycastHit first;
            if (!Physics.Raycast(ear, forward, out first, direct, Mask, QueryTriggerInteraction.Ignore))
                return path;

            float thickness = Thickness(ear, source, forward, direct, first.distance);
            path.Thickness = thickness;

            // ---------------------------------------------------------------- through
            // Low frequencies go through walls and high ones do not, and a weapon's carry is a decent
            // stand-in for how much low end it has: a rifle is felt through a wall, a footstep is not.
            float punch = Mathf.Clamp01(carry / 150f);
            float depth = Mathf.Max(0.05f, Feel.wallHalfDepth * Mathf.Lerp(0.6f, 1.7f, punch));
            float throughGain = Feel.wallTransmission * Mathf.Exp(-thickness / depth);
            float throughCutoff = Mathf.Lerp(2200f, 260f, Mathf.Clamp01(thickness / 1.2f));

            // ---------------------------------------------------------------- around
            Vector3 bend;
            float bentLength;
            bool found = FindBend(ear, source, forward, direct, out bend, out bentLength);

            float aroundGain = 0f;
            Vector3 aroundPosition = source;
            if (found)
            {
                // A sound that only just clips the edge of something loses far less than one that has
                // to go right round it, so the loss scales with how big a detour it had to make. A
                // flat penalty makes a doorway two metres away sound like a doorway across the map.
                float detour = Mathf.Clamp01((bentLength / direct - 1f) * 2.5f);
                aroundGain = (1f - Mathf.Clamp01(Feel.diffractionLoss)) * Mathf.Lerp(1f, 0.4f, detour);

                // The whole point: it arrives from the corner, at the length of the path it took to
                // get there. Putting it at the corner's *distance* instead would make a sound that
                // bent round a pillar louder than the same sound in the open.
                Vector3 toBend = bend - ear;
                float bendDistance = toBend.magnitude;
                Vector3 steer = bendDistance > 0.01f ? toBend / bendDistance : forward;
                steer = Vector3.Slerp(forward, steer, Mathf.Clamp01(Feel.diffractionSteering)).normalized;
                aroundPosition = ear + steer * bentLength;
            }

            // Compare them where they are actually heard - a path twice as long is not twice as quiet,
            // it is whatever the rolloff says it is - then keep both, because a sound arriving two ways
            // is louder than either.
            float throughHeard = throughGain * Rolloff(direct);
            float aroundHeard = aroundGain * Rolloff(bentLength);

            path.Bent = aroundHeard > throughHeard;
            path.Position = path.Bent ? aroundPosition : source;
            path.Cutoff = path.Bent ? Mathf.Lerp(9000f, 4200f, Mathf.Clamp01(bentLength / 30f)) : throughCutoff;

            // The voice sits at one place, so the level has to be expressed against that place's
            // rolloff or a bent path would be counted twice.
            float heard = Mathf.Sqrt(throughHeard * throughHeard + aroundHeard * aroundHeard);
            float atPosition = Rolloff(path.Bent ? bentLength : direct);
            path.Gain = atPosition > 0.0001f ? Mathf.Clamp01(heard / atPosition) : 0f;
            return path;
        }

        /// <summary>
        /// How much solid there is between two points, from the first surface going one way and the
        /// first surface going the other. One wall comes out exactly right. Two walls with a room
        /// between them come out as the whole span, which overstates it - and overstating is the
        /// side to be wrong on, because the alternative is hearing through a building.
        /// </summary>
        float Thickness(Vector3 ear, Vector3 source, Vector3 forward, float distance, float entry)
        {
            RaycastHit back;
            float exit = Physics.Raycast(source, -forward, out back, distance, Mask, QueryTriggerInteraction.Ignore)
                ? back.distance
                : 0f;
            return Mathf.Max(0f, distance - entry - exit);
        }

        /// <summary>
        /// The shortest way round. Candidate bend points are pushed out sideways and vertically from
        /// the middle of the blocked line; a point counts when both legs of the path are clear.
        /// Nearest radius wins per direction, so a doorway is found before a detour round the block.
        /// </summary>
        bool FindBend(Vector3 ear, Vector3 source, Vector3 forward, float distance,
                      out Vector3 bend, out float length)
        {
            bend = Vector3.zero;
            length = float.MaxValue;

            Vector3 right = Vector3.Cross(forward, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, forward).normalized;

            Vector3 middle = ear + forward * (distance * 0.5f);

            for (int axis = 0; axis < 4; axis++)
            {
                Vector3 offset = axis == 0 ? right : axis == 1 ? -right : axis == 2 ? up : -up;
                for (int r = 0; r < Radii.Length; r++)
                {
                    Vector3 candidate = middle + offset * Radii[r];
                    if (Physics.Linecast(ear, candidate, Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(candidate, source, Mask, QueryTriggerInteraction.Ignore)) continue;

                    float total = Vector3.Distance(ear, candidate) + Vector3.Distance(candidate, source);
                    if (total < length) { length = total; bend = candidate; }
                    break;      // this direction is open; a wider detour the same way is only longer
                }
            }

            return length < float.MaxValue;
        }

        /// <summary>
        /// The same linear curve the voices use, so the two paths are compared on the terms they will
        /// actually be played on.
        /// </summary>
        public float MinDistance = 3f;
        public float MaxDistance = 70f;

        float Rolloff(float distance)
        {
            if (distance <= MinDistance) return 1f;
            if (distance >= MaxDistance) return 0f;
            return 1f - (distance - MinDistance) / (MaxDistance - MinDistance);
        }
    }
}
