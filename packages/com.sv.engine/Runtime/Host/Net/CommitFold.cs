using System.Runtime.CompilerServices;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.Host.Net
{
    public static class FoldLaw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }
    }

    public static unsafe class CommitFold
    {
        public static ulong Lane(Width width, int* value, int* delta, int* floor, int* ceiling, ulong rows)
        {
#if NET
            if (width == Width.Vector256)
            {
                return Lane256(value, delta, floor, ceiling, rows);
            }

            if (width == Width.Vector128)
            {
                return Lane128(value, delta, floor, ceiling, rows);
            }
#endif
            return Scalar(value, delta, floor, ceiling, rows);
        }

        public static ulong Scalar(int* value, int* delta, int* floor, int* ceiling, ulong rows)
        {
            var changed = 0ul;
            while (rows != 0ul)
            {
                var row = Bits.Tzcnt(rows);
                rows &= rows - 1ul;
                var before = value[row];
                var after = FoldLaw.Step(before, delta[row], floor[row], ceiling[row]);
                value[row] = after;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

#if NET
        public static ulong Lane256(int* value, int* delta, int* floor, int* ceiling, ulong rows)
        {
            var rowBits = Vector256.Create(1, 2, 4, 8, 16, 32, 64, 128);
            var top = Vector256.Create(int.MaxValue);
            var changed = 0ul;
            for (var at = 0; at < Lanes.Rows; at += 8)
            {
                var group = (int)(rows >> at) & 0xFF;
                if (group == 0)
                {
                    continue;
                }

                var selected = Vector256.Equals(Vector256.Create(group) & rowBits, rowBits);
                var before = Vector256.Load(value + at);
                var moved = Vector256.Load(delta + at);
                var sum = before + moved;
                var overflow = Vector256.LessThan((before ^ sum) & (moved ^ sum), Vector256<int>.Zero);
                var saturated = Vector256.ConditionalSelect(overflow, Vector256.ShiftRightArithmetic(before, 31) ^ top, sum);
                var high = Vector256.Load(ceiling + at);
                var low = Vector256.ConditionalSelect(Vector256.LessThan(Vector256.Load(floor + at), high), Vector256.Load(floor + at), high);
                var below = Vector256.ConditionalSelect(Vector256.GreaterThan(saturated, high), high, saturated);
                var clamped = Vector256.ConditionalSelect(Vector256.LessThan(saturated, low), low, below);
                var after = Vector256.ConditionalSelect(selected, clamped, before);
                after.Store(value + at);
                changed |= (ulong)(~Vector256.Equals(after, before)).ExtractMostSignificantBits() << at;
            }

            return changed;
        }

        public static ulong Lane128(int* value, int* delta, int* floor, int* ceiling, ulong rows)
        {
            var rowBits = Vector128.Create(1, 2, 4, 8);
            var top = Vector128.Create(int.MaxValue);
            var changed = 0ul;
            for (var at = 0; at < Lanes.Rows; at += 4)
            {
                var group = (int)(rows >> at) & 0xF;
                if (group == 0)
                {
                    continue;
                }

                var selected = Vector128.Equals(Vector128.Create(group) & rowBits, rowBits);
                var before = Vector128.Load(value + at);
                var moved = Vector128.Load(delta + at);
                var sum = before + moved;
                var overflow = Vector128.LessThan((before ^ sum) & (moved ^ sum), Vector128<int>.Zero);
                var saturated = Vector128.ConditionalSelect(overflow, Vector128.ShiftRightArithmetic(before, 31) ^ top, sum);
                var high = Vector128.Load(ceiling + at);
                var low = Vector128.ConditionalSelect(Vector128.LessThan(Vector128.Load(floor + at), high), Vector128.Load(floor + at), high);
                var below = Vector128.ConditionalSelect(Vector128.GreaterThan(saturated, high), high, saturated);
                var clamped = Vector128.ConditionalSelect(Vector128.LessThan(saturated, low), low, below);
                var after = Vector128.ConditionalSelect(selected, clamped, before);
                after.Store(value + at);
                changed |= (ulong)(~Vector128.Equals(after, before)).ExtractMostSignificantBits() << at;
            }

            return changed;
        }
#endif
    }
}
