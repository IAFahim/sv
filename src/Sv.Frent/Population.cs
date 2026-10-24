using System;
using System.Runtime.InteropServices;
using Frent;

namespace Sv.Frent;

/// <summary>
/// Fused chunk driver over a Frent world: owns the query, bind/unbind, and slot compaction.
/// Windows transparently take the contiguous fast path or the in-place row walk; after
/// <see cref="Reconcile"/> every archetype is contiguous again (at any slot offset — the
/// enabled windows carry their own tail bound), so the fast path is restored until the next
/// structural change.
/// </summary>
public sealed unsafe class Population : IDisposable
{
    public FrentBoard FrentBoard { get; }

    public Board Board => FrentBoard.Board;

    public World World { get; }

    public int FastPathChunks { get; internal set; }

    public int RemapChunks { get; internal set; }

    internal readonly int* permutation;

    public Population(World world, Schema schema, int capacity)
    {
        World = world;
        FrentBoard = new FrentBoard(schema, capacity);
        permutation = (int*)NativeMemory.Alloc((nuint)Board.Capacity * sizeof(int));
    }

    public int Bind(Entity entity) => FrentBoard.Bind(entity);

    public void Unbind(Entity entity) => FrentBoard.Unbind(entity);

    public bool TrySlot(Entity entity, out int slot) => FrentBoard.TrySlot(entity, out slot);

    /// <summary>
    /// Enumerates fused chunk windows; counters reset per enumeration. Apply reads/writes the
    /// Frent column in place. Do not nest enumerations — the counters are shared.
    /// </summary>
    public ChunkSource<TColumn> Chunks<TColumn>() where TColumn : unmanaged
    {
        FastPathChunks = 0;
        RemapChunks = 0;
        return new ChunkSource<TColumn>(this);
    }

    /// <summary>
    /// Compacts the slot space: reassigns SvSlot ids ascending per archetype (packed
    /// contiguously — enabled windows work at any offset), renumbers the board once, and
    /// returns the permutation for host-owned slot columns
    /// (<see cref="Sv.Columns.Permute{T}(T*, ReadOnlySpan{int}, int)"/>).
    /// Call at the drain step — the structural-change boundary. Every live board slot must be
    /// bound to an entity in this world; validation runs before any rewrite, so a failed reconcile
    /// leaves world and board exactly as they were.
    /// </summary>
    public ReadOnlySpan<int> Reconcile()
    {
        var capacity = Board.Capacity;
        var mapped = 0;
        var next = 0;
        for (var i = 0; i < capacity; i++)
        {
            permutation[i] = -1;
        }

        foreach (var chunk in World.Query<SvSlot>().EnumerateChunks<SvSlot>())
        {
            chunk.Deconstruct(out var slots);
            if (next + slots.Length > capacity)
            {
                throw new InvalidOperationException("sv.frent: reconcile exceeds board capacity");
            }

            next += slots.Length;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i].Slot;
                if (slot < 0 || slot >= capacity || !Board.IsLive(slot) || permutation[slot] == 0)
                {
                    throw new InvalidOperationException($"sv.frent: SvSlot {slot} is out of range, not live, or bound twice — reconcile rewrites nothing until every live slot is bound exactly once");
                }

                permutation[slot] = 0;
                mapped++;
            }
        }

        if (mapped != Board.LiveCount)
        {
            throw new InvalidOperationException($"sv.frent: reconcile found {mapped} bound slots for {Board.LiveCount} live board slots — every live slot must be bound before reconcile");
        }

        next = 0;
        foreach (var chunk in World.Query<SvSlot>().EnumerateChunks<SvSlot>())
        {
            chunk.Deconstruct(out var slots);
            for (var i = 0; i < slots.Length; i++)
            {
                var oldSlot = slots[i].Slot;
                slots[i] = new SvSlot(next);
                permutation[oldSlot] = next;
                next++;
            }
        }

        Board.RenumberSlots(new ReadOnlySpan<int>(permutation, capacity), Board.LiveCount);
        return new ReadOnlySpan<int>(permutation, capacity);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        FrentBoard.Dispose();
        NativeMemory.Free(permutation);
    }

    bool disposed;
}

public struct ChunkSource<TColumn> where TColumn : unmanaged
{
    readonly Population population;

    readonly global::Frent.Systems.ChunkQueryEnumerator<TColumn, SvSlot>.Enumerable source;

    internal ChunkSource(Population population)
    {
        this.population = population;
        source = population.World.Query<TColumn, SvSlot>().EnumerateChunks<TColumn, SvSlot>();
    }

    public ChunkCursor<TColumn> GetEnumerator() => new(source.GetEnumerator(), population);
}

public ref struct ChunkCursor<TColumn> where TColumn : unmanaged
{
    global::Frent.Systems.ChunkQueryEnumerator<TColumn, SvSlot> enumerator;

    readonly Population population;

    internal ChunkCursor(global::Frent.Systems.ChunkQueryEnumerator<TColumn, SvSlot> enumerator, Population population)
    {
        this.enumerator = enumerator;
        this.population = population;
    }

    public bool MoveNext() => enumerator.MoveNext();

    public ChunkWindow<TColumn> Current
    {
        get
        {
            enumerator.Current.Deconstruct(out var rows, out var slots);
            ChunkSeam.TrySlotRange(slots, out var start, out var count);
            return new ChunkWindow<TColumn>(rows, slots, population, start, count);
        }
    }

    public void Dispose() => enumerator.Dispose();
}

/// <summary>
/// One archetype window over a host column: contiguous chunks run the sv chunk verb zero-copy;
/// fragmented chunks walk their rows in place, reading the clamp's bounds by slot. Contiguity is
/// classified once per chunk visit, not per operation. Fragmentation never throws — only
/// unsupported column shapes do (non-int-sized columns).
/// </summary>
public readonly unsafe ref struct ChunkWindow<TColumn> where TColumn : unmanaged
{
    readonly Span<TColumn> rows;

    readonly Span<SvSlot> slots;

    readonly Population population;

    readonly int rangeStart;

    readonly int rangeCount;

    internal ChunkWindow(Span<TColumn> rows, Span<SvSlot> slots, Population population, int rangeStart, int rangeCount)
    {
        this.rows = rows;
        this.slots = slots;
        this.population = population;
        this.rangeStart = rangeStart;
        this.rangeCount = rangeCount;
    }

    public int RowCount => rows.Length;

    public bool IsContiguous => rangeCount != 0;

    public void Apply(Board board, int delta, in Clamp clamp, Span<int> applied)
    {
        GuardShape();
        if (!applied.IsEmpty && applied.Length < slots.Length)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the chunk's {slots.Length}");
        }

        fixed (TColumn* rowPin = rows)
        {
            if (rangeCount != 0)
            {
                population.FastPathChunks++;
                var value = (int*)rowPin - rangeStart;
                fixed (int* appliedPin = applied)
                {
                    var appliedView = applied.IsEmpty ? null : appliedPin - rangeStart;
                    board.RunIntrinsicsChunk(value, delta, clamp, rangeStart, rangeCount, appliedView);
                }

                return;
            }

            population.RemapChunks++;
            var values = (int*)rowPin;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i].Slot;
                var before = values[i];
                var after = clamp.Apply(Simd.SaturatingAdd(before, delta), slot);
                values[i] = after;
                if (!applied.IsEmpty)
                {
                    applied[i] = Simd.SaturatingSubtract(after, before);
                }
            }
        }
    }

    public void ApplyEnabled(Board board, ulong* words, int wordCount, int offset, int delta, in Clamp clamp, Span<int> applied)
    {
        GuardShape();
        if (words == null)
        {
            throw new ArgumentNullException(nameof(words));
        }

        fixed (TColumn* rowPin = rows)
        {
            if (rangeCount != 0)
            {
                population.FastPathChunks++;
                if (offset < rangeStart || offset >= rangeStart + rangeCount)
                {
                    throw new ArgumentException("sv.frent: enabled window must start inside the chunk");
                }

                var exactWords = (rangeStart + rangeCount - offset + 63) >> 6;
                if (wordCount != exactWords)
                {
                    throw new ArgumentException($"sv.frent: enabled window of {wordCount} words must span exactly the chunk's remaining {rangeStart + rangeCount - offset} rows ({exactWords} words)");
                }

                if (!applied.IsEmpty && applied.Length < (long)offset - rangeStart + wordCount * 64)
                {
                    throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the window's {offset - rangeStart + wordCount * 64}");
                }

                Guard.EnabledTail(words[wordCount - 1], rangeStart + rangeCount - offset - (wordCount - 1) * 64);
                var value = (int*)rowPin - rangeStart;
                fixed (int* appliedPin = applied)
                {
                    var appliedView = applied.IsEmpty ? null : appliedPin - rangeStart;
                    if (wordCount > 1)
                    {
                        board.RunEnabledChunk(value, delta, clamp, words, wordCount - 1, offset, appliedView);
                    }

                    board.RunEnabledChunk(value, delta, clamp, words[wordCount - 1], offset + (wordCount - 1) * 64, appliedView, rangeStart + rangeCount - offset - (wordCount - 1) * 64);
                }

                return;
            }

            population.RemapChunks++;
            var values = (int*)rowPin;
            var window = wordCount * 64;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i].Slot;
                var relative = slot - offset;
                if (relative < 0 || relative >= window || (words[relative >> 6] & (1ul << (relative & 63))) == 0)
                {
                    if (!applied.IsEmpty)
                    {
                        applied[i] = 0;
                    }

                    continue;
                }

                var before = values[i];
                var after = clamp.Apply(Simd.SaturatingAdd(before, delta), slot);
                values[i] = after;
                if (!applied.IsEmpty)
                {
                    applied[i] = Simd.SaturatingSubtract(after, before);
                }
            }
        }
    }

    void GuardShape()
    {
        if (sizeof(TColumn) != sizeof(int))
        {
            throw new ArgumentException("sv.frent: window column must be int-sized");
        }
    }
}
