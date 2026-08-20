namespace Satisfying.Shared
{
    [System.Flags]
    public enum MoveCollisionFlags : byte
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4
    }

    public struct MoveResult
    {
        public Vec3 Position;
        public MoveCollisionFlags Flags;
        public Vec3 GroundNormal;
        public Vec3 WallNormal;
    }

    /// <summary>
    /// The simulation's only contact with geometry. Unity implements it with capsule casts;
    /// the headless test harness implements it with analytic boxes, which is how the movement
    /// code can be unit tested without an engine.
    /// All positions are FOOT positions (bottom centre of the capsule).
    /// </summary>
    public interface ICollisionWorld
    {
        MoveResult MoveCapsule(Vec3 footPos, float height, float radius, Vec3 displacement, float stepHeight, float slopeLimitDeg);

        /// <summary>True if a capsule at this position would overlap geometry (used for stand-up checks).</summary>
        bool CheckCapsule(Vec3 footPos, float height, float radius);

        /// <summary>True if a sphere overlaps geometry (used to crush lean against walls).</summary>
        bool CheckSphere(Vec3 center, float radius);

        /// <summary>Downward probe from the feet. Returns false when nothing is within maxDistance.</summary>
        bool GroundProbe(Vec3 footPos, float radius, float maxDistance, out float distance, out Vec3 normal);

        bool Raycast(Vec3 origin, Vec3 direction, float maxDistance, out float distance, out Vec3 normal);
    }
}
