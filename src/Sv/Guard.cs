using System;
using System.Diagnostics;

namespace Sv;

internal static unsafe class Guard
{
    public static void SlotLive(int slot, ulong* live, int capacity)
    {
        if (slot < 0 || slot >= capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"sv: slot {slot} is not live");
        }
    }

    [Conditional("SV_CHECKED")]
    public static void ChunkRange(int start, int count, int capacity)
    {
        if (start < 0 || count < 0 || start + count > capacity)
        {
            throw new ArgumentOutOfRangeException($"sv: chunk [{start}, {start + count}) exceeds capacity {capacity}");
        }
    }

    [Conditional("SV_CHECKED")]
    public static void EnabledRange(int offset, int capacity)
    {
        if (offset < 0 || offset + 64 > capacity)
        {
            throw new ArgumentOutOfRangeException($"sv: enabled window [{offset}, {offset + 64}) exceeds capacity {capacity}");
        }
    }
}
