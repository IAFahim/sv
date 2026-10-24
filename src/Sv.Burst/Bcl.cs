namespace Sv.Burst
{
    /// <summary>
    /// The BCL surface sv's unmanaged kernel path needs, re-expressed with no dependency Burst
    /// cannot compile: trailing-zero count as a compare sequence (no
    /// <c>System.Numerics.BitOperations</c>), and the clause-16 pinned float→int bound conversion.
    /// One source text, two compilers: plain .NET here, Unity Burst at <c>FloatMode.Strict</c> in
    /// the Unity leg.
    /// </summary>
    public static class Bcl
    {
        /// <summary>Equivalent to <c>System.Numerics.BitOperations.TrailingZeroCount(ulong)</c>: 64 for zero.</summary>
        public static int Tzcnt(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }

            var n = 1;
            if ((value & 0xFFFFFFFFul) == 0)
            {
                n += 32;
                value >>= 32;
            }

            if ((value & 0xFFFFul) == 0)
            {
                n += 16;
                value >>= 16;
            }

            if ((value & 0xFFul) == 0)
            {
                n += 8;
                value >>= 8;
            }

            if ((value & 0xFul) == 0)
            {
                n += 4;
                value >>= 4;
            }

            if ((value & 0x3ul) == 0)
            {
                n += 2;
                value >>= 2;
            }

            return n - ((int)value & 1);
        }

        /// <summary>
        /// Clause-16 pinned float→int conversion for stat bounds — identical math to
        /// <c>Sv.Clamp.BoundOf</c>: NaN → 0, saturate to [int.MinValue, int.MaxValue], otherwise
        /// truncate toward zero. Only float comparisons and the truncating cast appear.
        /// </summary>
        public static int BoundOf(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            if (value >= 2147483648.0f)
            {
                return int.MaxValue;
            }

            if (value < -2147483648.0f)
            {
                return int.MinValue;
            }

            return (int)value;
        }
    }
}
