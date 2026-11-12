using System.Runtime.CompilerServices;

namespace Sv
{
    public static class Condition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Below(int value, int whole, int numerator, int denominator) => (long)value * denominator < (long)whole * numerator;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Next(ulong state, ulong enter, ulong stay) => (~state & enter) | (state & stay);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Edges(ulong state, ulong next) => state ^ next;
    }
}
