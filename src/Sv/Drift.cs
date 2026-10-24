using System;
using System.Runtime.InteropServices;

namespace Sv;

[StructLayout(LayoutKind.Sequential)]
internal struct DriftSegment
{
    public ClampShift Rule;

    public long T0;

    public int V0;

    public int Active;
}

/// <summary>
/// Lazy intrinsic segments (roadmap 4.2): one per slot, <c>(v&#8320;, t&#8320;, rule)</c> where the
/// segment holds the value at the end of frame <c>t&#8320;</c>'s step 3 and the frozen
/// <see cref="ClampShift"/> since. <see cref="Read"/> evaluates the closed-form power, so n frames
/// of regen cost one evaluation instead of n passes. Segment convention: a
/// <see cref="Write"/> in frame t's step 2 lands before that frame's drift step (the segment
/// becomes <c>(value, t &#8722; 1)</c>); a <see cref="Rebase"/> in frame t's step 3 folds everything
/// up to and including frame t into v&#8320; and swaps the rule. Rules enter only through
/// <see cref="ClampShift.Step"/> or composition, so floor &#8804; ceiling holds everywhere.
/// </summary>
public sealed unsafe class DriftStore : IDisposable
{
    DriftSegment* segments;

    int capacity;

    bool disposed;

    DriftStore(DriftSegment* segments, int capacity)
    {
        this.segments = segments;
        this.capacity = capacity;
    }

    public static DriftStore Create(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentException($"sv: drift store of {capacity} slots");
        }

        var segments = (DriftSegment*)NativeMemory.AllocZeroed((nuint)capacity, (nuint)sizeof(DriftSegment));
        return new DriftStore(segments, capacity);
    }

    public int Capacity => capacity;

    /// <summary>Establish or replace a slot's segment with an explicit value and rule.</summary>
    public void Capture(int slot, int value, long tick, ClampShift rule)
    {
        var s = Segment(slot);
        s->Rule = rule;
        s->T0 = tick;
        s->V0 = value;
        s->Active = 1;
    }

    /// <summary>A step-2 write of frame <paramref name="tick"/>: the raw value lands, then the frame's drift step applies — requires an existing segment and a tick after its start.</summary>
    public void Write(int slot, int value, long tick)
    {
        var s = Segment(slot);
        if (s->Active == 0)
        {
            throw new InvalidOperationException($"sv: drift write to slot {slot} with no segment");
        }

        if (tick <= s->T0)
        {
            throw new InvalidOperationException($"sv: drift write at tick {tick} not after the segment start {s->T0}");
        }

        s->V0 = value;
        s->T0 = tick - 1;
    }

    /// <summary>A step-3 rebase of frame <paramref name="tick"/>: v&#8320; folds <c>rule.Repeat(tick &#8722; t&#8320;)</c>, then the rule swaps.</summary>
    public void Rebase(int slot, long tick, ClampShift rule)
    {
        var s = Segment(slot);
        if (s->Active == 0)
        {
            throw new InvalidOperationException($"sv: drift rebase of slot {slot} with no segment");
        }

        if (tick < s->T0)
        {
            throw new InvalidOperationException($"sv: drift rebase at tick {tick} before the segment start {s->T0}");
        }

        s->V0 = s->Rule.Repeat(tick - s->T0).Apply(s->V0);
        s->T0 = tick;
        s->Rule = rule;
    }

    /// <summary>The slot's value at the end of frame <paramref name="tick"/>'s step 3; a tick at or before the segment start reads v&#8320;.</summary>
    public int Read(int slot, long tick)
    {
        var s = Segment(slot);
        if (s->Active == 0)
        {
            throw new InvalidOperationException($"sv: drift read of slot {slot} with no segment");
        }

        return s->Rule.Repeat(tick - s->T0).Apply(s->V0);
    }

    /// <summary>Bulk <see cref="Read"/> over a window into <paramref name="destination"/> (indexed by absolute row); masked windows write enabled rows only.</summary>
    public void Materialize(Window window, int* destination, long tick)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (window.Words == null)
        {
            Guard.ChunkRange(window.Start, window.Count, capacity);
            for (var i = 0; i < window.Count; i++)
            {
                destination[window.Start + i] = Read(window.Start + i, tick);
            }

            return;
        }

        Guard.EnabledRange(window.Start, window.WordCount, capacity);
        for (var w = 0; w < window.WordCount; w++)
        {
            var word = window.Words[w];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                word &= word - 1;
                var row = window.Start + (w << 6) + bit;
                destination[row] = Read(row, tick);
            }
        }
    }

    DriftSegment* Segment(int slot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((uint)slot >= (uint)capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"sv: drift slot {slot} outside 0..{capacity - 1}");
        }

        return segments + slot;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        NativeMemory.Free(segments);
        segments = null;
    }
}
