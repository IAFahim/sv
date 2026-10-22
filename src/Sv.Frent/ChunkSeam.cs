using System;

namespace Sv.Frent;

public static unsafe class ChunkSeam
{
    public static bool TrySlotRange(ReadOnlySpan<SvSlot> slots, out int start, out int count)
    {
        if (slots.Length == 0)
        {
            start = 0;
            count = 0;
            return false;
        }

        start = slots[0].Slot;
        for (var i = 1; i < slots.Length; i++)
        {
            if (slots[i].Slot != start + i)
            {
                count = 0;
                return false;
            }
        }

        count = slots.Length;
        return true;
    }

    public static void RunChunk<TColumn>(
        Board board,
        Span<TColumn> rows,
        ReadOnlySpan<SvSlot> slots,
        int delta,
        in Clamp clamp,
        Span<int> applied) where TColumn : unmanaged
    {
        if (sizeof(TColumn) != sizeof(int))
        {
            throw new ArgumentException("sv.frent: RunChunk column must be int-sized");
        }

        if (!TrySlotRange(slots, out var start, out var count))
        {
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use the Population window driver");
        }

        if (!applied.IsEmpty && applied.Length < count)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the chunk's {count}");
        }

        fixed (TColumn* rowPin = rows)
        {
            var value = (int*)rowPin - start;
            fixed (int* appliedPin = applied)
            {
                var appliedView = applied.IsEmpty ? null : appliedPin - start;
                board.RunIntrinsicsChunk(value, delta, clamp, start, count, appliedView);
            }
        }
    }

    public static void RunChunk<TColumn, TDelta>(
        Board board,
        Span<TColumn> rows,
        Span<TDelta> deltas,
        ReadOnlySpan<SvSlot> slots,
        in Clamp clamp,
        Span<int> applied) where TColumn : unmanaged where TDelta : unmanaged
    {
        if (sizeof(TColumn) != sizeof(int) || sizeof(TDelta) != sizeof(int))
        {
            throw new ArgumentException("sv.frent: RunChunk columns must be int-sized");
        }

        if (!TrySlotRange(slots, out var start, out var count))
        {
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use the Population window driver");
        }

        if (deltas.Length < count)
        {
            throw new ArgumentException($"sv.frent: deltas span of {deltas.Length} rows does not cover the chunk's {count}");
        }

        if (!applied.IsEmpty && applied.Length < count)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the chunk's {count}");
        }

        fixed (TColumn* rowPin = rows)
        {
            var value = (int*)rowPin - start;
            fixed (TDelta* deltaPin = deltas)
            {
                var delta = (int*)deltaPin - start;
                fixed (int* appliedPin = applied)
                {
                    var appliedView = applied.IsEmpty ? null : appliedPin - start;
                    board.RunIntrinsicsChunk(value, delta, clamp, start, count, appliedView);
                }
            }
        }
    }

    public static void RunEnabledChunk<TColumn>(
        Board board,
        Span<TColumn> rows,
        ReadOnlySpan<SvSlot> slots,
        ulong enabled,
        int offsetSlot,
        int delta,
        in Clamp clamp,
        Span<int> applied) where TColumn : unmanaged
    {
        if (sizeof(TColumn) != sizeof(int))
        {
            throw new ArgumentException("sv.frent: RunEnabledChunk column must be int-sized");
        }

        if (!TrySlotRange(slots, out var start, out var count))
        {
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use the Population window driver");
        }

        if ((offsetSlot & 63) != 0 || offsetSlot < start || offsetSlot >= start + count)
        {
            throw new ArgumentException("sv.frent: enabled window must start 64-aligned inside the chunk");
        }

        if (!applied.IsEmpty && applied.Length < offsetSlot - start + 64)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the window's {offsetSlot - start + 64}");
        }

        Guard.EnabledTail(enabled, start + count - offsetSlot);
        fixed (TColumn* rowPin = rows)
        {
            var value = (int*)rowPin - start;
            fixed (int* appliedPin = applied)
            {
                var appliedView = applied.IsEmpty ? null : appliedPin - start;
                board.RunEnabledChunk(value, delta, clamp, enabled, offsetSlot, appliedView);
            }
        }
    }

    public static void RunEnabledChunk<TColumn>(
        Board board,
        Span<TColumn> rows,
        ReadOnlySpan<SvSlot> slots,
        ulong* words,
        int wordCount,
        int offsetSlot,
        int delta,
        in Clamp clamp,
        Span<int> applied) where TColumn : unmanaged
    {
        if (sizeof(TColumn) != sizeof(int))
        {
            throw new ArgumentException("sv.frent: RunEnabledChunk column must be int-sized");
        }

        if (!TrySlotRange(slots, out var start, out var count))
        {
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use the Population window driver");
        }

        if ((offsetSlot & 63) != 0 || offsetSlot < start || offsetSlot >= start + count)
        {
            throw new ArgumentException("sv.frent: enabled window must start 64-aligned inside the chunk");
        }

        var exactWords = (start + count - offsetSlot + 63) >> 6;
        if (wordCount != exactWords)
        {
            throw new ArgumentException($"sv.frent: enabled window of {wordCount} words must span exactly the chunk's remaining {start + count - offsetSlot} rows ({exactWords} words)");
        }

        if (!applied.IsEmpty && applied.Length < (long)offsetSlot - start + wordCount * 64)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the window's {offsetSlot - start + wordCount * 64}");
        }

        Guard.EnabledTail(words[wordCount - 1], start + count - offsetSlot - (wordCount - 1) * 64);
        fixed (TColumn* rowPin = rows)
        {
            var value = (int*)rowPin - start;
            fixed (int* appliedPin = applied)
            {
                var appliedView = applied.IsEmpty ? null : appliedPin - start;
                board.RunEnabledChunk(value, delta, clamp, words, wordCount, offsetSlot, appliedView);
            }
        }
    }
}
