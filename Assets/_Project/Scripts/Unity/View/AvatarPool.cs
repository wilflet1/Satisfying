using System.Collections.Generic;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Who looks like what, when nobody has said.
    ///
    /// If the player has picked a character in the menu, that is what they wear. If they have not,
    /// everybody in the match - opponents and bots alike - is dealt a character from whatever avatars
    /// are on the machine, so a duel is not two identical mannequins.
    ///
    /// THE PICK IS A FUNCTION OF THE PEER ID, not a random number. That is the whole trick, and it
    /// buys something the replication does not currently provide: every client runs the same hash
    /// over the same id against the same sorted list, so both machines independently decide that
    /// peer 3 is wearing the same character, without a single byte crossing the wire. Shuffle it
    /// randomly and two players would be looking at different people.
    ///
    /// When there are no avatars at all it falls back to the blockout duellist, and varies THAT
    /// instead - a different palette per peer. What it never varies is anything the shape of a
    /// player: everyone is the same fifteen capsules and the same BodyPose, so a randomised
    /// character is a different thing to look at and exactly the same thing to shoot at.
    /// </summary>
    public sealed class AvatarPool
    {
        readonly AvatarLibrary _library;
        readonly List<string> _sources = new List<string>();
        float _scannedAt = -99f;

        public AvatarPool(AvatarLibrary library)
        {
            _library = library;
        }

        /// <summary>Set when the player has chosen one deliberately. Overrides the pool for everybody.</summary>
        public string Chosen;

        /// <summary>Everything on the machine, sorted so every client sees the same order.</summary>
        public List<string> Sources
        {
            get
            {
                Rescan();
                return _sources;
            }
        }

        void Rescan()
        {
            if (Time.realtimeSinceStartup - _scannedAt < 5f) return;
            _scannedAt = Time.realtimeSinceStartup;

            _sources.Clear();
            if (_library == null) return;

            List<string> found = _library.Available();
            for (int i = 0; i < found.Count; i++) _sources.Add(found[i]);

            // Sorted by NAME rather than by path: two machines have different folders but the same
            // avatar files, and the order has to come out the same on both.
            _sources.Sort(delegate(string a, string b)
            {
                return string.CompareOrdinal(AvatarLibrary.NameOf(a), AvatarLibrary.NameOf(b));
            });
        }

        /// <summary>
        /// The character this peer wears. Null means the blockout duellist - either because there are
        /// no avatars to deal out, or because this one has not finished loading yet.
        /// </summary>
        public string SourceFor(int peerId)
        {
            if (!string.IsNullOrEmpty(Chosen)) return Chosen;

            Rescan();
            if (_sources.Count == 0) return null;
            return _sources[(int)(Hash(peerId) % (uint)_sources.Count)];
        }

        /// <summary>
        /// Which look the blockout character gets when there is no avatar for this peer. Same hash,
        /// different salt, so a player's fallback appearance is stable for as long as they are here.
        /// </summary>
        public int VariantFor(int peerId)
        {
            return (int)(Hash(peerId ^ 0x5BF03635) & 0xFFFF);
        }

        /// <summary>
        /// A small integer hash. Deliberately written out rather than using GetHashCode: string and
        /// object hashes are allowed to differ between runtimes and between runs, and this has to give
        /// the same answer on both machines in the match, forever.
        /// </summary>
        static uint Hash(int value)
        {
            unchecked
            {
                uint x = (uint)value * 2654435761u + 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
