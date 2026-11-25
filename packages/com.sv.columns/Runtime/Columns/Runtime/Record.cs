using System.Runtime.CompilerServices;

namespace Sv.Columns
{
    public static class Record
    {
        public const int SlotBits = 22;
        public const int SlotLimit = 1 << SlotBits;
        public const int TargetShift = 42;
        public const int ChannelShift = 38;
        public const int KindShift = 36;
        public const int FlagsShift = 28;
        public const int AmountBits = 28;
        public const long AmountMax = (1L << (AmountBits - 1)) - 1;
        public const long AmountMin = -AmountMax;
        public const ulong AmountMask = (1ul << AmountBits) - 1;
        public const ulong AmountSign = 1ul << (AmountBits - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Pack(int target, int channel, int kind, uint flags, long amount)
        {
            amount = amount > AmountMax ? AmountMax : amount < AmountMin ? AmountMin : amount;
            return ((ulong)(uint)target << TargetShift)
                | ((ulong)(uint)(channel & 15) << ChannelShift)
                | ((ulong)(uint)(kind & 3) << KindShift)
                | ((ulong)(flags & 255u) << FlagsShift)
                | ((ulong)amount & AmountMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Target(ulong record) => (int)(record >> TargetShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Channel(ulong record) => (int)(record >> ChannelShift) & 15;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Kind(ulong record) => (int)(record >> KindShift) & 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Flags(ulong record) => (uint)(record >> FlagsShift) & 255u;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Amount(ulong record) => (long)((record & AmountMask) ^ AmountSign) - (long)AmountSign;
    }
}
