namespace Satisfying.Game
{
    /// <summary>
    /// Which build this is. Rewritten by BuildScript from git immediately before every build, so it
    /// cannot drift the way a hand-typed label does - the menu used to claim a date and a number
    /// that someone had to remember to edit, and by definition it was wrong more often than right.
    ///
    /// The values below are what an unbuilt working copy shows; a real build overwrites them.
    /// </summary>
    public static class BuildStamp
    {
        public const string Commit = "working copy";
        public const string Built = "not built yet";
        public const string Branch = "";

        /// <summary>
        /// One line for the menu. The protocol version is the half that decides whether two people
        /// can actually play together, so it is not buried: a mismatch is the quiet way a join fails.
        /// </summary>
        public static string Describe(int protocolVersion)
        {
            string where = string.IsNullOrEmpty(Branch) ? Commit : Commit + " (" + Branch + ")";
            return "build " + where + "   " + Built + "   protocol v" + protocolVersion;
        }
    }
}
