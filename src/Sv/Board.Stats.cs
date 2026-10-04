using System;
using System.Runtime.InteropServices;

namespace Sv;

public sealed unsafe partial class Board
{
    private struct PairMods
    {
        public StatEntry* Entries;

        public int* Ids;

        public int Count;

        public int Cap;
    }

    private int statCount;

    private float* statBase;

    private float** outputColumn;

    private int* outputStride;

    private float* svColumns;

    private int* boundGeneration;

    private int* boundLength;

    private int bindingGeneration;

    private SetTable? table;

    private PairMods* pairs;

    private int* pairSet;

    private sbyte* pairQueued;

    private int* pairQueue;

    private int pairQueueCount;

    private int pairQueueCap;

    private int nextInsertionId;

    private StatEntry* scratch;

    private int scratchCap;

    private void InitStats(Schema schema, int capacity)
    {
        statCount = schema.Stats.Count;
        if (statCount == 0)
        {
            return;
        }

        table = new SetTable();
        statBase = Alloc<float>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            statBase[i] = schema.Stats[i].Base;
        }

        outputColumn = (float**)NativeMemory.Alloc((nuint)statCount * (nuint)sizeof(float*), (nuint)sizeof(float*));
        NativeMemory.Clear(outputColumn, (nuint)statCount * (nuint)sizeof(float*));
        outputStride = Alloc<int>(statCount);
        svColumns = (float*)NativeMemory.AllocZeroed((nuint)statCount * (nuint)capacity * sizeof(float), sizeof(float));
        boundGeneration = Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundGeneration[i] = -1;
        }

        boundLength = Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundLength[i] = -1;
        }
        var pairTotal = statCount * capacity;
        pairs = Alloc<PairMods>(pairTotal);
        NativeMemory.Clear(pairs, (nuint)pairTotal * (nuint)sizeof(PairMods));
        pairSet = Alloc<int>(pairTotal);
        for (var i = 0; i < pairTotal; i++)
        {
            pairSet[i] = -1;
        }

        pairQueued = Alloc<sbyte>(pairTotal);
        NativeMemory.Clear(pairQueued, (nuint)pairTotal);
        pairQueueCap = 64;
        pairQueue = Alloc<int>(pairQueueCap);
        scratchCap = 32;
        scratch = Alloc<StatEntry>(scratchCap);
    }

    static T* Alloc<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T), (nuint)sizeof(T));
    }

    static T* Realloc<T>(T* old, int count) where T : unmanaged
    {
        return (T*)NativeMemory.Realloc(old, (nuint)count * (nuint)sizeof(T));
    }

    void FreeStats()
    {
        if (statCount == 0)
        {
            return;
        }

        var pairTotal = statCount * Capacity;
        for (var i = 0; i < pairTotal; i++)
        {
            if (pairs[i].Entries != null)
            {
                NativeMemory.Free(pairs[i].Entries);
                NativeMemory.Free(pairs[i].Ids);
            }
        }

        table!.Dispose();
        NativeMemory.Free(statBase);
        NativeMemory.Free(outputColumn);
        NativeMemory.Free(outputStride);
        NativeMemory.Free(svColumns);
        NativeMemory.Free(boundGeneration);
        NativeMemory.Free(boundLength);
        NativeMemory.Free(pairs);
        NativeMemory.Free(pairSet);
        NativeMemory.Free(pairQueued);
        NativeMemory.Free(pairQueue);
        NativeMemory.Free(scratch);
    }

    int StatIndex(StatId stat)
    {
        if (statCount == 0)
        {
            throw new InvalidOperationException("sv: schema declares no stats");
        }

        if (stat.Index < 0 || stat.Index >= statCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stat), "sv: stat id out of range");
        }

        return stat.Index;
    }

    /// <summary>
    /// Binds an external output column. The pointer is stable from this bind until the next
    /// <see cref="InvalidateBindings"/> or rebind; hosts owning growable storage must rebind at reconcile.
    /// A positive <paramref name="columnLength"/> is the column's element count; SV_CHECKED builds throw
    /// when a broadcast would write past it. -1 (the default) keeps length unchecked.
    /// </summary>
    public void BindOutput(StatId stat, float* column, int stride = 1, int columnLength = -1)
    {
        var index = StatIndex(stat);
        if (column == null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        if (stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "sv: stride must be >= 1");
        }

        if (columnLength < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnLength), "sv: columnLength must be -1 (unchecked) or >= 0");
        }

        outputColumn[index] = column;
        outputStride[index] = stride;
        boundLength[index] = columnLength;
        bindingGeneration++;
        boundGeneration[index] = bindingGeneration;
    }

    /// <summary>
    /// Binds an external output column and returns a handle capturing the binding generation;
    /// the handle reports stale after <see cref="InvalidateBindings"/> or a rebind of the same stat.
    /// The pointer doctrine matches <see cref="BindOutput"/>.
    /// </summary>
    public BindingHandle BindExternal(StatId stat, float* column, int stride = 1, int columnLength = -1)
    {
        var index = StatIndex(stat);
        if (column == null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        if (stride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "sv: stride must be >= 1");
        }

        if (columnLength < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnLength), "sv: columnLength must be -1 (unchecked) or >= 0");
        }

        outputColumn[index] = column;
        outputStride[index] = stride;
        boundLength[index] = columnLength;
        bindingGeneration++;
        boundGeneration[index] = bindingGeneration;
        return new BindingHandle(stat, column, stride, bindingGeneration);
    }

    /// <summary>
    /// Declares every external binding suspect (storage moved or slot space renumbered). Checked
    /// builds throw on the next stat broadcast or stat-bounds use; release builds keep writing
    /// through the recorded pointers. Rebind to re-establish freshness.
    /// </summary>
    public void InvalidateBindings()
    {
        bindingGeneration++;
        for (var stat = 0; stat < statCount; stat++)
        {
            if (outputColumn[stat] != null)
            {
                boundGeneration[stat] = -1;
            }
        }
    }

    public int BindingGeneration => bindingGeneration;

    internal int BoundGenerationAt(int statIndex) => boundGeneration[statIndex];

    /// <summary>
    /// Sv-owned output column for a stat: allocated at <see cref="Create"/>, fixed capacity, never
    /// moves, needs no bind. <see cref="Recalculate"/> broadcasts into it while no external column
    /// is bound.
    /// </summary>
    public float* OutputPtr(StatId stat)
    {
        var index = StatIndex(stat);
        return svColumns + (nint)index * Capacity;
    }

    public Span<float> Output(StatId stat) => new(OutputPtr(stat), Capacity);

    public float* OutputColumn(StatId stat)
    {
        return outputColumn[StatIndex(stat)];
    }

    public int SetCount => table?.LiveSetCount ?? 0;

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var pair = slot * statCount + index;
        ref var mods = ref pairs[pair];
        if (mods.Count == mods.Cap)
        {
            mods.Cap = mods.Cap == 0 ? 4 : mods.Cap * 2;
            mods.Entries = mods.Entries == null ? Alloc<StatEntry>(mods.Cap) : Realloc(mods.Entries, mods.Cap);
            mods.Ids = mods.Ids == null ? Alloc<int>(mods.Cap) : Realloc(mods.Ids, mods.Cap);
        }

        var id = ++nextInsertionId;
        mods.Entries[mods.Count] = new StatEntry(modifier.Kind, modifier.Value);
        mods.Ids[mods.Count] = id;
        mods.Count++;
        EnqueuePair(pair);
        return new ModifierHandle(id);
    }

    public void RemoveModifier(int slot, StatId stat, ModifierHandle handle)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var pair = slot * statCount + index;
        ref var mods = ref pairs[pair];
        for (var i = 0; i < mods.Count; i++)
        {
            if (mods.Ids[i] == handle.Id)
            {
                mods.Entries[i] = mods.Entries[mods.Count - 1];
                mods.Ids[i] = mods.Ids[mods.Count - 1];
                mods.Count--;
                EnqueuePair(pair);
                return;
            }
        }

        throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
    }

    internal void MarkSlotStatsDirty(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            EnqueuePair(slot * statCount + stat);
        }
    }

    internal void ClearSlotStats(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            var pair = slot * statCount + stat;
            pairs[pair].Count = 0;
            if (pairSet[pair] >= 0)
            {
                table!.RemoveMember(pairSet[pair], slot);
                pairSet[pair] = -1;
            }
        }
    }

    void EnqueuePair(int pair)
    {
        if (pairQueued[pair] != 0)
        {
            return;
        }

        if (pairQueueCount == pairQueueCap)
        {
            pairQueueCap *= 2;
            pairQueue = Realloc(pairQueue, pairQueueCap);
        }

        pairQueued[pair] = 1;
        pairQueue[pairQueueCount++] = pair;
    }

    public void Recalculate()
    {
        if (statCount == 0)
        {
            return;
        }

        var queueCount = pairQueueCount;
        var dequeueIndex = 0;
        while (dequeueIndex < queueCount)
        {
            var pair = pairQueue[dequeueIndex++];
            pairQueued[pair] = 0;
            var slot = pair / statCount;
            var stat = pair % statCount;
            ref var mods = ref pairs[pair];
            if (mods.Count > scratchCap)
            {
                scratchCap = mods.Count * 2;
                scratch = Realloc(scratch, scratchCap);
            }

            for (var i = 0; i < mods.Count; i++)
            {
                scratch[i] = mods.Entries[i];
            }

            Canonicalize(scratch, mods.Ids, mods.Count);
            var set = table!.Resolve(stat, scratch, mods.Count);
            if (pairSet[pair] != set)
            {
                if (pairSet[pair] >= 0)
                {
                    table.RemoveMember(pairSet[pair], slot);
                }

                table.AddMember(set, slot);
                pairSet[pair] = set;
            }

            table.MarkDirty(set);
        }

        pairQueueCount = 0;

        while (table!.TryDequeueDirty(out var dirty))
        {
            var stat = table.SetStat(dirty);
            var content = table.Content(dirty, out var contentCount);
            var computed = Fold(statBase[stat], content, contentCount);
            table.SetComputed(dirty, computed);
            var column = outputColumn[stat];
            var members = table.Members(dirty, out var memberCount);
            if (column == null)
            {
                var owned = svColumns + (nint)stat * Capacity;
                for (var i = 0; i < memberCount; i++)
                {
                    owned[members[i]] = computed;
                }

                continue;
            }

            Guard.BindingFresh(boundGeneration[stat]);
            var stride = outputStride[stat];
            for (var i = 0; i < memberCount; i++)
            {
                Guard.BoundColumnFit(boundLength[stat], stride, members[i]);
                column[members[i] * stride] = computed;
            }
        }
    }

    static void Canonicalize(StatEntry* entries, int* ids, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var entry = entries[i];
            var id = ids[i];
            var j = i - 1;
            while (j >= 0 && (entries[j].Kind > entry.Kind || (entries[j].Kind == entry.Kind && ids[j] > id)))
            {
                entries[j + 1] = entries[j];
                ids[j + 1] = ids[j];
                j--;
            }

            entries[j + 1] = entry;
            ids[j + 1] = id;
        }
    }

    internal static float Fold(float @base, StatEntry* entries, int count)
    {
        var value = @base;
        var additive = 1f;
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Added)
            {
                value = value + entries[i].Value;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Additive)
            {
                additive = additive + entries[i].Value;
            }
        }

        value = value * additive;
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Multi)
            {
                value = value * (1f + entries[i].Value);
            }
        }

        return value;
    }

    public float Stat(int slot, StatId stat)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var pair = slot * statCount + index;
        ref var mods = ref pairs[pair];
        if (mods.Count > scratchCap)
        {
            scratchCap = mods.Count * 2;
            scratch = Realloc(scratch, scratchCap);
        }

        for (var i = 0; i < mods.Count; i++)
        {
            scratch[i] = mods.Entries[i];
        }

        Canonicalize(scratch, mods.Ids, mods.Count);
        return Fold(statBase[index], scratch, mods.Count);
    }

    /// <summary>
    /// Compacts the slot space: live slots map through <paramref name="oldToNew"/> (negative entries
    /// are dead slots) to distinct targets in [0, capacity) — gaps are legal (64-alignment padding).
    /// Rewrites set members, modifier lists, the dirty queue (all live pairs re-enqueued — run
    /// <see cref="Recalculate"/> after), live bits, and sv-owned columns; external bindings are
    /// invalidated per the clause-12 doctrine. Modifier insertion ids ride along, so stat folds are
    /// preserved bit-exactly.
    /// </summary>
    public void RenumberSlots(ReadOnlySpan<int> oldToNew, int count)
    {
        if (count < 0 || count > Capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"sv: renumber count must be in [0, {Capacity}]");
        }

        if (count != LiveCount)
        {
            throw new InvalidOperationException($"sv: renumber count {count} != live count {LiveCount}");
        }

        if (oldToNew.Length != Capacity)
        {
            throw new ArgumentException($"sv: renumber map must cover capacity ({Capacity} entries)");
        }

        var words = (Capacity + 63) >> 6;
        var newLive = Alloc<ulong>(words);
        NativeMemory.Clear(newLive, (nuint)words * sizeof(ulong));
        var seen = Alloc<int>(Capacity);
        for (var i = 0; i < Capacity; i++)
        {
            seen[i] = -1;
        }

        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            if (next < 0 || next >= Capacity || seen[next] >= 0)
            {
                NativeMemory.Free(newLive);
                NativeMemory.Free(seen);
                throw new ArgumentException("sv: renumber map does not map live slots to distinct targets");
            }

            seen[next] = slot;
            newLive[next >> 6] |= 1ul << (next & 63);
        }

        if (statCount > 0)
        {
            RenumberStats(oldToNew);
        }

        new ReadOnlySpan<ulong>(newLive, words).CopyTo(new Span<ulong>(live, words));
        NativeMemory.Free(newLive);
        var freeCount = 0;
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                seen[freeCount++] = slot;
            }
        }

        freeTop = freeCount;
        for (var i = 0; i < freeCount; i++)
        {
            freeSlots[freeCount - 1 - i] = seen[i];
        }

        NativeMemory.Free(seen);

        if (statCount > 0)
        {
            NativeMemory.Clear(pairQueued, (nuint)statCount * (nuint)Capacity);
            pairQueueCount = 0;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    MarkSlotStatsDirty(slot);
                }
            }
        }

        InvalidateBindings();
    }

    void RenumberStats(ReadOnlySpan<int> oldToNew)
    {
        var pairTotal = statCount * Capacity;
        var tempPairs = Alloc<PairMods>(pairTotal);
        NativeMemory.Clear(tempPairs, (nuint)pairTotal * (nuint)sizeof(PairMods));
        var tempSet = Alloc<int>(pairTotal);
        for (var i = 0; i < pairTotal; i++)
        {
            tempSet[i] = -1;
        }

        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            for (var stat = 0; stat < statCount; stat++)
            {
                tempPairs[next * statCount + stat] = pairs[slot * statCount + stat];
                tempSet[next * statCount + stat] = pairSet[slot * statCount + stat];
            }
        }

        new ReadOnlySpan<PairMods>(tempPairs, pairTotal).CopyTo(new Span<PairMods>(pairs, pairTotal));
        new ReadOnlySpan<int>(tempSet, pairTotal).CopyTo(new Span<int>(pairSet, pairTotal));
        NativeMemory.Free(tempPairs);
        NativeMemory.Free(tempSet);
        table!.RenumberMembers(oldToNew);
        var tempRows = Alloc<float>(Capacity);
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = svColumns + (nint)stat * Capacity;
            NativeMemory.Clear(tempRows, (nuint)Capacity * sizeof(float));
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    tempRows[oldToNew[slot]] = column[slot];
                }
            }

            new ReadOnlySpan<float>(tempRows, Capacity).CopyTo(new Span<float>(column, Capacity));
        }

        NativeMemory.Free(tempRows);
    }
}
