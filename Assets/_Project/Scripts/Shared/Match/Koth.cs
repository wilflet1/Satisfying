namespace Satisfying.Shared
{
    /// <summary>
    /// What the match is. Duel is the original: first to so many kills. King of the hill puts a room
    /// on the board instead, and moves it.
    /// </summary>
    public enum GameMode : byte
    {
        Duel = 0,
        KingOfTheHill = 1
    }

    /// <summary>
    /// A room worth standing in. Named because the HUD says which one is live and "go to zone 2" is
    /// not something anyone can act on at speed.
    /// </summary>
    public struct ZoneDef
    {
        public Box Bounds;
        public string Name;
    }

    [System.Serializable]
    public class KothTuning
    {
        [Tune("King of the hill", 15f, 240f, Tip = "Seconds before the hill moves to another room.")]
        public float rotateSeconds = 60f;

        [Tune("King of the hill", 10f, 400f, Tip = "Points needed to win. One a second while you hold it alone.")]
        public float pointsToWin = 100f;

        [Tune("King of the hill", 0.2f, 8f, Tip = "Points a second for holding the hill on your own.")]
        public float pointsPerSecond = 1f;

        [Tune("King of the hill", 0f, 1f, Tip = "1 stops the clock while both of you are in it; 0 lets you both score.")]
        public float contestedStops = 1f;

        [Tune("King of the hill", 0f, 20f, Tip = "Seconds of warning before the hill moves.")]
        public float warnSeconds = 8f;

        public int PointsToWinInt { get { return MathK.Max(1, MathK.RoundToInt(pointsToWin)); } }
        public bool ContestedStops { get { return contestedStops >= 0.5f; } }

        public KothTuning Clone() { return (KothTuning)MemberwiseClone(); }
    }

    /// <summary>
    /// The hill, and who is standing on it.
    ///
    /// Deliberately not a component of anything: it is a struct of numbers the server steps and the
    /// clients are told about, because the whole of this game's netcode is "the server owns the truth
    /// and says what changed". A player is in the zone when their FEET are, which is the position
    /// that is already replicated - testing the eye would mean you could hold a room from the floor
    /// below it by standing under the ceiling.
    /// </summary>
    public struct KothState
    {
        public int ActiveZone;
        public float RotateTimer;

        /// <summary>-1 for nobody, -2 for contested. Anything else is a peer id.</summary>
        public int Holder;

        public const int Nobody = -1;
        public const int Contested = -2;
    }

    public static class Koth
    {
        /// <summary>Feet inside the box. Height matters: an upstairs room is not held from below it.</summary>
        public static bool Inside(in ZoneDef zone, Vec3 footPosition)
        {
            Vec3 min = zone.Bounds.Min;
            Vec3 max = zone.Bounds.Max;
            return footPosition.x >= min.x && footPosition.x <= max.x &&
                   footPosition.y >= min.y - 0.35f && footPosition.y <= max.y &&
                   footPosition.z >= min.z && footPosition.z <= max.z;
        }

        /// <summary>
        /// Which room to move to. Never the one that is already live, and stepped rather than random
        /// so both machines and every future replay agree without replicating the choice.
        /// </summary>
        public static int NextZone(int current, int count, uint seed)
        {
            if (count <= 1) return 0;
            int step = 1 + (int)(seed % (uint)(count - 1));
            return (current + step) % count;
        }
    }
}
