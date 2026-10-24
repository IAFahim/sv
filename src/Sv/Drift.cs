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
/// One drain-fold op: a <see cref="ClampShift"/> aimed at a slot. Batches fold per slot in arrival
/// order (<see cref="DriftStore.ApplyBatch"/>); order is semantics — the monoid is not commutative.
/// </summary>
public readonly struct DriftOp
{
    public int Slot { get; }

    public ClampShift Op { get; }

    public DriftOp(int slot, ClampShift op)
    {
        Slot = slot;
        Op = op;
    }
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

    Board? board;

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

    /// <summary>Routes every later segment write through the board's undo window, so <see cref="Board.Rollback(int)"/> restores drift segments with the rest of the state. Renumbering closes the window — call <see cref="Renumber"/> after <see cref="Board.RenumberSlots"/>.</summary>
    public void AttachBoard(Board board)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        this.board = board;
    }

    void Note(int slot)
    {
        board?.ActiveUndo?.Note(segments + slot, sizeof(DriftSegment));
    }

    /// <summary>Establish or replace a slot's segment with an explicit value and rule.</summary>
    public void Capture(int slot, int value, long tick, ClampShift rule)
    {
        var s = Segment(slot);
        Note(slot);
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

        Note(slot);
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

        Note(slot);
        s->V0 = s->Rule.Repeat(tick - s->T0).Apply(s->V0);
        s->T0 = tick;
        s->Rule = rule;
    }

    /// <summary>
    /// The frame-step-3 bulk rebase: every row of <paramref name="window"/> whose rule changed rebases to
    /// <c>ClampShift.Step(BoundOf(rate[row]), BoundOf(floor[row]), BoundOf(ceiling[row]))</c> — the
    /// stat-broadcast columns are indexed by absolute row. Rows without a segment are skipped (they are
    /// not drifting); masked windows rebase enabled rows only.
    /// </summary>
    public void RebaseRules(Window window, long tick, float* rate, float* floor, float* ceiling)
    {
        if (rate == null || floor == null || ceiling == null)
        {
            throw new ArgumentException("sv: drift rule columns must not be null");
        }

        if (window.Words == null)
        {
            Guard.ChunkRange(window.Start, window.Count, capacity);
            for (var i = 0; i < window.Count; i++)
            {
                RebaseRow(window.Start + i, tick, rate, floor, ceiling);
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
                RebaseRow(window.Start + (w << 6) + bit, tick, rate, floor, ceiling);
            }
        }
    }

    void RebaseRow(int row, long tick, float* rate, float* floor, float* ceiling)
    {
        var s = segments + row;
        if (s->Active == 0)
        {
            return;
        }

        Note(row);
        s->V0 = s->Rule.Repeat(tick - s->T0).Apply(s->V0);
        s->T0 = tick;
        s->Rule = ClampShift.Step(Clamp.BoundOf(rate[row]), Clamp.BoundOf(floor[row]), Clamp.BoundOf(ceiling[row]));
    }

    /// <summary>Remaps segments through <paramref name="oldToNew"/> (negative entries deactivate) — the drift half of law L3: <c>read ∘ renumber = π ∘ read</c>. Call after <see cref="Board.RenumberSlots"/>; renumbering closes the undo window.</summary>
    public void Renumber(ReadOnlySpan<int> oldToNew)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (oldToNew.Length != capacity)
        {
            throw new ArgumentException($"sv: drift renumber map must cover capacity ({capacity} entries)");
        }

        var copy = (DriftSegment*)NativeMemory.Alloc((nuint)capacity, (nuint)sizeof(DriftSegment));
        var bytes = (nuint)capacity * (nuint)sizeof(DriftSegment);
        Buffer.MemoryCopy(segments, copy, bytes, bytes);
        NativeMemory.Clear(segments, bytes);
        for (var slot = 0; slot < capacity; slot++)
        {
            var next = oldToNew[slot];
            if (next >= 0)
            {
                if (next >= capacity)
                {
                    NativeMemory.Free(copy);
                    throw new ArgumentOutOfRangeException(nameof(oldToNew), $"sv: drift renumber maps slot {slot} to {next} outside 0..{capacity - 1}");
                }

                segments[next] = copy[slot];
            }
        }

        NativeMemory.Free(copy);
    }

    /// <summary>
    /// Folds a drain batch: ops are stably radix-sorted by slot, each slot's ops compose in arrival
    /// order through <see cref="ClampShift.Then"/>, and the composed map applies once to the value at
    /// the end of frame <paramref name="tick"/> &#8722; 1 — bit-equal to applying the k hits eagerly
    /// and then the frame's drift step. One write per touched slot. Requires every op's slot to hold
    /// a segment started before <paramref name="tick"/>.
    /// </summary>
    public unsafe void ApplyBatch(long tick, ReadOnlySpan<DriftOp> ops)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ops.IsEmpty)
        {
            return;
        }

        var count = ops.Length;
        var sorted = (DriftOp*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(DriftOp));
        var temp = (DriftOp*)NativeMemory.Alloc((nuint)count, (nuint)sizeof(DriftOp));
        ops.CopyTo(new Span<DriftOp>(sorted, count));
        var source = sorted;
        var target = temp;
        Span<int> counts = stackalloc int[256];
        for (var pass = 0; pass < 4; pass++)
        {
            counts.Clear();
            var shift = pass * 8;
            for (var i = 0; i < count; i++)
            {
                counts[(source[i].Slot >> shift) & 255]++;
            }

            var total = 0;
            for (var bucket = 0; bucket < 256; bucket++)
            {
                var size = counts[bucket];
                counts[bucket] = total;
                total += size;
            }

            for (var i = 0; i < count; i++)
            {
                var op = source[i];
                target[counts[(op.Slot >> shift) & 255]++] = op;
            }

            var swap = source;
            source = target;
            target = swap;
        }

        if (source != sorted)
        {
            Buffer.MemoryCopy(source, sorted, (nuint)count * (nuint)sizeof(DriftOp), (nuint)count * (nuint)sizeof(DriftOp));
        }

        var at = 0;
        while (at < count)
        {
            var slot = sorted[at].Slot;
            if ((uint)slot >= (uint)capacity)
            {
                NativeMemory.Free(sorted);
                NativeMemory.Free(temp);
                throw new ArgumentOutOfRangeException(nameof(ops), $"sv: drift op aims at slot {slot} outside 0..{capacity - 1}");
            }

            var composed = sorted[at].Op;
            at++;
            while (at < count && sorted[at].Slot == slot)
            {
                composed = composed.Then(sorted[at].Op);
                at++;
            }

            Write(slot, composed.Apply(Read(slot, tick - 1)), tick);
        }

        NativeMemory.Free(sorted);
        NativeMemory.Free(temp);
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

    internal bool SegmentActive(int slot) => Segment(slot)->Active != 0;

    internal void CopySegment(int slot, out ClampShift rule, out long t0, out int v0)
    {
        var s = Segment(slot);
        rule = s->Rule;
        t0 = s->T0;
        v0 = s->V0;
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
