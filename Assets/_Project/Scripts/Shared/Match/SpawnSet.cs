using System.Collections.Generic;

namespace Satisfying.Shared
{
    public struct SpawnPoint
    {
        public Vec3 Position;
        public float Yaw;

        public SpawnPoint(Vec3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }
    }

    /// <summary>Spawn points, filled in by whoever builds the arena.</summary>
    public sealed class SpawnSet
    {
        public readonly List<SpawnPoint> Points = new List<SpawnPoint>();

        public void Add(Vec3 position, float yaw) { Points.Add(new SpawnPoint(position, yaw)); }

        public SpawnPoint Pick(int seed, IList<Vec3> avoid)
        {
            if (Points.Count == 0) return new SpawnPoint(Vec3.Zero, 0f);

            int best = seed % Points.Count;
            float bestScore = float.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                int idx = (seed + i) % Points.Count;
                float score = 1000f;
                if (avoid != null)
                {
                    for (int k = 0; k < avoid.Count; k++)
                    {
                        float d = Vec3.Distance(Points[idx].Position, avoid[k]);
                        if (d < score) score = d;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = idx;
                }
            }
            return Points[best];
        }
    }
}
