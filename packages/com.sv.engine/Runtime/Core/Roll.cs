namespace Sv
{
    public static class Roll
    {
        public const int Always = 10000;

        public static ulong Mix(ulong value)
        {
            unchecked
            {
                var z = value + 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        public static ulong Hash(ulong seed, long frame, ulong key, int amount, int rule, int ordinal)
        {
            unchecked
            {
                var hash = Mix(seed ^ (ulong)frame);
                hash = Mix(hash ^ key);
                hash = Mix(hash ^ (uint)amount);
                hash = Mix(hash ^ (uint)rule);
                return Mix(hash ^ (uint)ordinal);
            }
        }

        public static int BasisPointOf(ulong hash) => (int)(((hash >> 32) * (ulong)Always) >> 32);

        public static bool Passes(ulong seed, long frame, ulong key, int amount, int rule, int ordinal, int basisPoints) =>
            BasisPointOf(Hash(seed, frame, key, amount, rule, ordinal)) < basisPoints;
    }
}
