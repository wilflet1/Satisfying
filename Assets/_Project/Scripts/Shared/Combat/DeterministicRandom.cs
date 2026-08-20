namespace Satisfying.Shared
{
    /// <summary>
    /// Tiny xorshift PRNG. Every shot's spread is derived from (playerId, shotIndex, pellet) so the
    /// firing client and the authoritative server roll exactly the same numbers without syncing anything.
    /// </summary>
    public struct DeterministicRandom
    {
        uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        public static DeterministicRandom ForShot(int playerId, uint shotIndex, int pellet)
        {
            unchecked
            {
                uint s = (uint)playerId * 0x9E3779B9u;
                s ^= shotIndex * 0x85EBCA6Bu;
                s ^= (uint)(pellet + 1) * 0xC2B2AE35u;
                s ^= s >> 15;
                return new DeterministicRandom(s);
            }
        }

        public uint NextUInt()
        {
            unchecked
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }
        }

        /// <summary>0..1</summary>
        public float NextFloat()
        {
            return (NextUInt() & 0xFFFFFF) / 16777215f;
        }

        /// <summary>-1..1</summary>
        public float NextSigned()
        {
            return NextFloat() * 2f - 1f;
        }
    }
}
