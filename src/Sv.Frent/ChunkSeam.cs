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

    public static void Pull<T>(ReadOnlySpan<T> rows, ReadOnlySpan<SvSlot> slots, T* dst) where T : unmanaged
    {
        for (var i = 0; i < rows.Length; i++)
        {
            dst[slots[i].Slot] = rows[i];
        }
    }

    public static void Push<T>(T* src, ReadOnlySpan<SvSlot> slots, Span<T> rows) where T : unmanaged
    {
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = src[slots[i].Slot];
        }
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
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use Pull/Push");
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
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use Pull/Push");
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
            throw new ArgumentException("sv.frent: chunk is not slot-contiguous; use Pull/Push");
        }

        if (offsetSlot != start)
        {
            throw new ArgumentException("sv.frent: enabled offset must equal the chunk slot base");
        }

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
}
