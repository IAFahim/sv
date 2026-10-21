using System;
using System.Runtime.InteropServices;
using Frent;

namespace Sv.Frent;

/// <summary>
/// Fused chunk driver over a Frent world: owns the query, bind/unbind, and slot compaction.
/// Windows transparently take the contiguous fast path or the Pull/walk/Push remap; after
/// <see cref="Reconcile"/> every archetype is contiguous and 64-aligned again, so the fast path
/// is restored until the next structural change.
/// </summary>
public sealed unsafe class Population : IDisposable
{
    public FrentBoard FrentBoard { get; }

    public Board Board => FrentBoard.Board;

    public World World { get; }

    public int FastPathChunks { get; internal set; }

    public int RemapChunks { get; internal set; }

    internal readonly int* scratch;

    internal readonly int* permutation;

    public Population(World world, Schema schema, int capacity)
    {
        World = world;
        FrentBoard = new FrentBoard(schema, capacity);
        var rounded = Board.Capacity;
        scratch = (int*)NativeMemory.AllocZeroed((nuint)rounded * sizeof(int));
        permutation = (int*)NativeMemory.Alloc((nuint)rounded * sizeof(int));
    }

    public int Bind(Entity entity) => FrentBoard.Bind(entity);

    public void Unbind(Entity entity) => FrentBoard.Unbind(entity);

    public bool TrySlot(Entity entity, out int slot) => FrentBoard.TrySlot(entity, out slot);

    /// <summary>
    /// Enumerates fused chunk windows; counters reset per enumeration. Apply reads/writes the
    /// Frent column in place. Do not nest enumerations (they share the remap scratch).
    /// </summary>
    public ChunkSource<TColumn> Chunks<TColumn>() where TColumn : unmanaged
    {
        FastPathChunks = 0;
        RemapChunks = 0;
        return new ChunkSource<TColumn>(this);
    }

    /// <summary>
    /// Compacts the slot space: reassigns SvSlot ids ascending per archetype (each archetype block
    /// 64-aligned so enabled windows work), renumbers the board once, and returns the permutation
    /// for host-owned slot columns (<see cref="Sv.Columns.Permute{T}(T*, ReadOnlySpan{int}, int)"/>).
    /// Call at the drain step — the structural-change boundary. Every live board slot must be
    /// bound to an entity in this world; validation runs before any rewrite, so a failed reconcile
    /// leaves world and board exactly as they were.
    /// </summary>
    public ReadOnlySpan<int> Reconcile()
    {
        var capacity = Board.Capacity;
        var mapped = 0;
        var next = 0;
        foreach (var chunk in World.Query<SvSlot>().EnumerateChunks<SvSlot>())
        {
            chunk.Deconstruct(out var slots);
            next = (next + 63) & ~63;
            if (next + slots.Length > capacity)
            {
                throw new InvalidOperationException("sv.frent: reconcile exceeds board capacity (64-alignment padding)");
            }

            next += slots.Length;
            mapped += slots.Length;
        }

        if (mapped != Board.LiveCount)
        {
            throw new InvalidOperationException($"sv.frent: reconcile found {mapped} bound slots for {Board.LiveCount} live board slots — every live slot must be bound before reconcile");
        }

        for (var i = 0; i < capacity; i++)
        {
            permutation[i] = -1;
        }

        next = 0;
        foreach (var chunk in World.Query<SvSlot>().EnumerateChunks<SvSlot>())
        {
            chunk.Deconstruct(out var slots);
            next = (next + 63) & ~63;
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
        FrentBoard.Dispose();
        NativeMemory.Free(scratch);
        NativeMemory.Free(permutation);
    }
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
            return new ChunkWindow<TColumn>(rows, slots, population);
        }
    }

    public void Dispose() => enumerator.Dispose();
}

/// <summary>
/// One archetype window over a host column: contiguous chunks run the sv chunk verb zero-copy;
/// fragmented chunks pull into slot space, walk, and push back. Fragmentation never throws —
/// only unsupported column shapes do (non-int-sized columns).
/// </summary>
public readonly unsafe ref struct ChunkWindow<TColumn> where TColumn : unmanaged
{
    readonly Span<TColumn> rows;

    readonly Span<SvSlot> slots;

    readonly Population population;

    internal ChunkWindow(Span<TColumn> rows, Span<SvSlot> slots, Population population)
    {
        this.rows = rows;
        this.slots = slots;
        this.population = population;
    }

    public int RowCount => rows.Length;

    public bool IsContiguous => ChunkSeam.TrySlotRange(slots, out _, out _);

    public void Apply(Board board, int delta, in Clamp clamp, Span<int> applied)
    {
        GuardShape();
        if (!applied.IsEmpty && applied.Length < slots.Length)
        {
            throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the chunk's {slots.Length}");
        }

        fixed (TColumn* rowPin = rows)
        {
            if (ChunkSeam.TrySlotRange(slots, out var start, out var count))
            {
                population.FastPathChunks++;
                var value = (int*)rowPin - start;
                fixed (int* appliedPin = applied)
                {
                    var appliedView = applied.IsEmpty ? null : appliedPin - start;
                    board.RunIntrinsicsChunk(value, delta, clamp, start, count, appliedView);
                }

                return;
            }

            population.RemapChunks++;
            ChunkSeam.Pull(rows, slots, (TColumn*)population.scratch);
            var values = population.scratch;
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i].Slot;
                var before = values[slot];
                var after = clamp.Apply(Simd.SaturatingAdd(before, delta), slot);
                values[slot] = after;
                if (!applied.IsEmpty)
                {
                    applied[i] = after - before;
                }
            }

            ChunkSeam.Push((TColumn*)population.scratch, slots, rows);
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
            if (ChunkSeam.TrySlotRange(slots, out var start, out var count))
            {
                population.FastPathChunks++;
                if ((offset & 63) != 0 || offset < start || offset >= start + count)
                {
                    throw new ArgumentException("sv.frent: enabled window must start 64-aligned inside the chunk");
                }

                var exactWords = (start + count - offset + 63) >> 6;
                if (wordCount != exactWords)
                {
                    throw new ArgumentException($"sv.frent: enabled window of {wordCount} words must span exactly the chunk's remaining {start + count - offset} rows ({exactWords} words)");
                }

                if (!applied.IsEmpty && applied.Length < (long)offset - start + wordCount * 64)
                {
                    throw new ArgumentException($"sv.frent: applied span of {applied.Length} rows does not cover the window's {offset - start + wordCount * 64}");
                }

                Guard.EnabledTail(words[wordCount - 1], start + count - offset - (wordCount - 1) * 64);
                var value = (int*)rowPin - start;
                fixed (int* appliedPin = applied)
                {
                    var appliedView = applied.IsEmpty ? null : appliedPin - start;
                    board.RunEnabledChunk(value, delta, clamp, words, wordCount, offset, appliedView);
                }

                return;
            }

            population.RemapChunks++;
            ChunkSeam.Pull(rows, slots, (TColumn*)population.scratch);
            var values = population.scratch;
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

                var before = values[slot];
                var after = clamp.Apply(Simd.SaturatingAdd(before, delta), slot);
                values[slot] = after;
                if (!applied.IsEmpty)
                {
                    applied[i] = after - before;
                }
            }

            ChunkSeam.Push((TColumn*)population.scratch, slots, rows);
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
