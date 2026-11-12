using System.Runtime.CompilerServices;

namespace Sv
{
    public static class IntrinsicLaw
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Applied(int before, int after) => Saturate((long)after - before);
    }

    public unsafe struct FoldWord
    {
        public int* Value;
        public int* Delta;
        public float* Floor;
        public float* Ceiling;
        public int FloorConstant;
        public int CeilingConstant;
    }

    public static unsafe class Fold
    {
        public static ulong Word(in FoldWord word, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var floor = word.Floor == null ? word.FloorConstant : Clamp.BoundOf(word.Floor[row]);
                var ceiling = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var before = word.Value[row];
                var after = IntrinsicLaw.Step(before, word.Delta[row], floor, ceiling);
                word.Value[row] = after;
                word.Delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }

        public static ulong Word(in FoldWord word, long* inbox, int rows)
        {
            var changed = 0ul;
            for (var row = 0; row < rows; row++)
            {
                var floor = word.Floor == null ? word.FloorConstant : Clamp.BoundOf(word.Floor[row]);
                var ceiling = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var before = word.Value[row];
                var delta = inbox == null ? word.Delta[row] : Inbox.Take(inbox, row, word.Delta[row]);
                var after = IntrinsicLaw.Step(before, delta, floor, ceiling);
                word.Value[row] = after;
                word.Delta[row] = 0;
                changed |= (after != before ? 1ul : 0ul) << row;
            }

            return changed;
        }
    }
}
