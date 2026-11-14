using System;
using System.Runtime.CompilerServices;

namespace Sv
{
    public static unsafe class CellDigest
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Key(int column, int slot, uint bits)
        {
            unchecked
            {
                var key = ((ulong)(ushort)column << 48) ^ ((ulong)(uint)slot << 12) ^ (bits * 0x9E3779B97F4A7C15ul) ^ ((ulong)(ushort)column * 0xD6E8FEB86659FD93ul);
                key ^= key >> 33;
                key *= 0xFF51AFD7ED558CCDul;
                key ^= key >> 33;
                key *= 0xC4CEB9FE1A85EC53ul;
                return key ^ (key >> 33);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Move(ulong digest, int column, int slot, uint before, uint after)
        {
            unchecked
            {
                return before == after ? digest : digest - Key(column, slot, before) + Key(column, slot, after);
            }
        }

        public static ulong Sum(int column, int* values, int start, int count)
        {
            if (values == null && count != 0)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var sum = 0ul;
            unchecked
            {
                for (var row = 0; row < count; row++)
                {
                    sum += Key(column, start + row, (uint)values[start + row]);
                }
            }

            return sum;
        }

        public static ulong Sum(int column, int* values, int start, ulong* words, int wordCount)
        {
            if (values == null || words == null)
            {
                throw new ArgumentNullException(values == null ? nameof(values) : nameof(words));
            }

            var sum = 0ul;
            unchecked
            {
                for (var w = 0; w < wordCount; w++)
                {
                    var bits = words[w];
                    while (bits != 0)
                    {
                        var row = start + (w << 6) + Tzcnt(bits);
                        bits &= bits - 1;
                        sum += Key(column, row, (uint)values[row]);
                    }
                }
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Tzcnt(ulong value)
        {
#if NET
            return System.Numerics.BitOperations.TrailingZeroCount(value);
#else
            var count = 0;
            while ((value & 1ul) == 0ul && count < 64)
            {
                value >>= 1;
                count++;
            }

            return count;
#endif
        }
    }
}
