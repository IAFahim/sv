using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv;

public sealed unsafe class Board : IDisposable
{
    public Schema Schema { get; }

    public int Capacity { get; }

    public int LiveCount { get; private set; }

    private readonly int* freeSlots;

    private readonly ulong* live;

    private int freeTop;

    private bool disposed;

    Board(Schema schema, int capacity)
    {
        Schema = schema;
        Capacity = capacity;
        freeSlots = (int*)NativeMemory.Alloc((nuint)capacity, sizeof(int));
        var words = (nuint)((capacity + 63) >> 6);
        live = (ulong*)NativeMemory.Alloc(words, sizeof(ulong));
        for (var i = 0; i < capacity; i++)
        {
            freeSlots[i] = capacity - 1 - i;
        }

        freeTop = capacity;
    }

    public static Board Create(Schema schema, int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "sv: capacity must be positive");
        }

        return new Board(schema, capacity);
    }

    public int Acquire()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(Board));
        }

        if (freeTop == 0)
        {
            throw new InvalidOperationException("sv: slot exhaustion");
        }

        var slot = freeSlots[--freeTop];
        live[slot >> 6] |= 1ul << (slot & 63);
        LiveCount++;
        return slot;
    }

    public void Release(int slot)
    {
        if (slot < 0 || slot >= Capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
        {
            throw new ArgumentException($"sv: release of non-live slot {slot}");
        }

        live[slot >> 6] &= ~(1ul << (slot & 63));
        freeSlots[freeTop++] = slot;
        LiveCount--;
    }

    public void RunIntrinsicsChunk(int* value, int delta, in Clamp clamp, int start, int count, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.ChunkRange(start, count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var after = clamp.Apply(before + delta, slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = after - before;
            }
        }
    }

    public void RunIntrinsicsChunk(int* value, int* delta, in Clamp clamp, int start, int count, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.ChunkRange(start, count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var after = clamp.Apply(before + delta[slot], slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = after - before;
            }
        }
    }

    public void RunEnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.EnabledRange(offset, Capacity);
        var mask = enabled;
        while (mask != 0)
        {
            var bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var slot = offset + bit;
            var before = value[slot];
            var after = clamp.Apply(before + delta, slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = after - before;
            }
        }

        WriteSkippedZero(applied, enabled, offset);
    }

    public void RunEnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.EnabledRange(offset, Capacity);
        var mask = enabled;
        while (mask != 0)
        {
            var bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var slot = offset + bit;
            var before = value[slot];
            var after = clamp.Apply(before + delta[slot], slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = after - before;
            }
        }

        WriteSkippedZero(applied, enabled, offset);
    }

    static void WriteSkippedZero(int* applied, ulong enabled, int offset)
    {
        if (applied == null)
        {
            return;
        }

        var skipped = ~enabled;
        while (skipped != 0)
        {
            var bit = BitOperations.TrailingZeroCount(skipped);
            skipped &= skipped - 1;
            applied[offset + bit] = 0;
        }
    }

    public void RunIntrinsicsChunk(Span<int> value, int delta, in Clamp clamp, int start, int count, Span<int> applied = default)
    {
        Guard.ChunkRange(start, count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var after = clamp.Apply(before + delta, slot);
            value[slot] = after;
            if (!applied.IsEmpty)
            {
                applied[slot] = after - before;
            }
        }
    }

    public void RunIntrinsicsChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, int start, int count, Span<int> applied = default)
    {
        Guard.ChunkRange(start, count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var after = clamp.Apply(before + delta[slot], slot);
            value[slot] = after;
            if (!applied.IsEmpty)
            {
                applied[slot] = after - before;
            }
        }
    }

    public void RunEnabledChunk(Span<int> value, int delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
    {
        Guard.EnabledRange(offset, Capacity);
        var mask = enabled;
        while (mask != 0)
        {
            var bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var slot = offset + bit;
            var before = value[slot];
            var after = clamp.Apply(before + delta, slot);
            value[slot] = after;
            if (!applied.IsEmpty)
            {
                applied[slot] = after - before;
            }
        }

        if (!applied.IsEmpty)
        {
            var skipped = ~enabled;
            while (skipped != 0)
            {
                var bit = BitOperations.TrailingZeroCount(skipped);
                skipped &= skipped - 1;
                applied[offset + bit] = 0;
            }
        }
    }

    public void RunEnabledChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
    {
        Guard.EnabledRange(offset, Capacity);
        var mask = enabled;
        while (mask != 0)
        {
            var bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var slot = offset + bit;
            var before = value[slot];
            var after = clamp.Apply(before + delta[slot], slot);
            value[slot] = after;
            if (!applied.IsEmpty)
            {
                applied[slot] = after - before;
            }
        }

        if (!applied.IsEmpty)
        {
            var skipped = ~enabled;
            while (skipped != 0)
            {
                var bit = BitOperations.TrailingZeroCount(skipped);
                skipped &= skipped - 1;
                applied[offset + bit] = 0;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        NativeMemory.Free(freeSlots);
        NativeMemory.Free(live);
        disposed = true;
    }
}
