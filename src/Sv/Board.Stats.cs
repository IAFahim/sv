using System;
using System.Runtime.InteropServices;

namespace Sv;

public sealed unsafe partial class Board
{
    private const int PageShift = 12;

    private const int PageSlots = 1 << PageShift;

    private struct PairMods
    {
        public StatEntry* Entries;

        public int Count;

        public int Cap;

        public readonly int* Ids => (int*)(Entries + Cap);
    }

    private struct PairMeta
    {
        public int Set;

        public int Pos;

        public int Queued;
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

    private int pageCount;

    private PairMods** modPages;

    private PairMeta** metaPages;

    private int* pairQueue;

    private int pairQueueCount;

    private int pairQueueCap;

    private int nextInsertionId;

    private StatEntry* scratch;

    private int* scratchIds;

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
        pageCount = (capacity + PageSlots - 1) >> PageShift;
        modPages = (PairMods**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairMods*));
        metaPages = (PairMeta**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairMeta*));
        pairQueueCap = 64;
        pairQueue = Alloc<int>(pairQueueCap);
        scratchCap = 32;
        scratch = Alloc<StatEntry>(scratchCap);
        scratchIds = Alloc<int>(scratchCap);
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

        FreePages(modPages, metaPages);
        table!.Dispose();
        NativeMemory.Free(statBase);
        NativeMemory.Free(outputColumn);
        NativeMemory.Free(outputStride);
        NativeMemory.Free(svColumns);
        NativeMemory.Free(boundGeneration);
        NativeMemory.Free(boundLength);
        NativeMemory.Free(pairQueue);
        NativeMemory.Free(scratch);
        NativeMemory.Free(scratchIds);
    }

    void FreePages(PairMods** mods, PairMeta** metas)
    {
        var pageWidth = PageSlots * statCount;
        for (var page = 0; page < pageCount; page++)
        {
            if (mods[page] != null)
            {
                for (var i = 0; i < pageWidth; i++)
                {
                    if (mods[page][i].Entries != null)
                    {
                        NativeMemory.Free(mods[page][i].Entries);
                    }
                }

                NativeMemory.Free(mods[page]);
            }

            if (metas[page] != null)
            {
                NativeMemory.Free(metas[page]);
            }
        }

        NativeMemory.Free(mods);
        NativeMemory.Free(metas);
    }

    PairMeta* MetaPage(PairMeta** metas, int slot)
    {
        var page = slot >> PageShift;
        if (metas[page] == null)
        {
            var width = PageSlots * statCount;
            var fresh = Alloc<PairMeta>(width);
            for (var i = 0; i < width; i++)
            {
                fresh[i] = new PairMeta { Set = -1 };
            }

            metas[page] = fresh;
        }

        return metas[page];
    }

    PairMods* ModPage(PairMods** mods, int slot)
    {
        var page = slot >> PageShift;
        if (mods[page] == null)
        {
            var bytes = (nuint)(PageSlots * statCount) * (nuint)sizeof(PairMods);
            mods[page] = (PairMods*)NativeMemory.AllocZeroed(bytes);
        }

        return mods[page];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    ref PairMeta Meta(int slot, int stat)
    {
        return ref metaPages[slot >> PageShift][(slot & (PageSlots - 1)) * statCount + stat];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    PairMods* ModsOrNull(int slot, int stat)
    {
        var page = modPages[slot >> PageShift];
        return page == null ? null : page + (slot & (PageSlots - 1)) * statCount + stat;
    }

    internal void EnsureSlotPages(int slot)
    {
        MetaPage(metaPages, slot);
    }

    internal long PairBytes
    {
        get
        {
            long bytes = (long)pageCount * (sizeof(PairMods*) + sizeof(PairMeta*));
            var pageWidth = PageSlots * statCount;
            for (var page = 0; page < pageCount; page++)
            {
                if (metaPages[page] != null)
                {
                    bytes += (long)pageWidth * sizeof(PairMeta);
                }

                if (modPages[page] != null)
                {
                    bytes += (long)pageWidth * sizeof(PairMods);
                    for (var i = 0; i < pageWidth; i++)
                    {
                        bytes += (long)modPages[page][i].Cap * (sizeof(StatEntry) + sizeof(int));
                    }
                }
            }

            return bytes;
        }
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
    /// The next <see cref="Recalculate"/> rebroadcasts every populated set of the stat into the column.
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
        table!.MarkStatDirty(index);
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
        table!.MarkStatDirty(index);
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

    internal int MemberTotal(StatId stat) => table!.MemberTotal(StatIndex(stat));

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var mods = ref ModPage(modPages, slot)[(slot & (PageSlots - 1)) * statCount + index];
        if (mods.Count == mods.Cap)
        {
            var cap = mods.Cap == 0 ? 4 : mods.Cap * 2;
            var block = (StatEntry*)NativeMemory.Alloc((nuint)cap * (nuint)(sizeof(StatEntry) + sizeof(int)));
            if (mods.Entries != null)
            {
                Buffer.MemoryCopy(mods.Entries, block, (long)cap * sizeof(StatEntry), (long)mods.Count * sizeof(StatEntry));
                Buffer.MemoryCopy(mods.Ids, block + cap, (long)cap * sizeof(int), (long)mods.Count * sizeof(int));
                NativeMemory.Free(mods.Entries);
            }

            mods.Entries = block;
            mods.Cap = cap;
        }

        var id = ++nextInsertionId;
        mods.Entries[mods.Count] = new StatEntry(modifier.Kind, modifier.Value);
        mods.Ids[mods.Count] = id;
        mods.Count++;
        EnqueuePair(slot, index);
        return new ModifierHandle(id);
    }

    public void RemoveModifier(int slot, StatId stat, ModifierHandle handle)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var mods = ModsOrNull(slot, index);
        var count = mods == null ? 0 : mods->Count;
        for (var i = 0; i < count; i++)
        {
            var ids = mods->Ids;
            if (ids[i] == handle.Id)
            {
                mods->Entries[i] = mods->Entries[count - 1];
                ids[i] = ids[count - 1];
                mods->Count--;
                EnqueuePair(slot, index);
                return;
            }
        }

        throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
    }

    internal void MarkSlotStatsDirty(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            EnqueuePair(slot, stat);
        }
    }

    internal void ClearSlotStats(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            var mods = ModsOrNull(slot, stat);
            if (mods != null)
            {
                mods->Count = 0;
            }

            ref var meta = ref Meta(slot, stat);
            if (meta.Set >= 0)
            {
                var moved = table!.RemoveMemberAt(meta.Set, meta.Pos);
                if (moved >= 0)
                {
                    Meta(moved, stat).Pos = meta.Pos;
                }

                meta.Set = -1;
            }
        }
    }

    void EnqueuePair(int slot, int stat)
    {
        ref var meta = ref Meta(slot, stat);
        if (meta.Queued != 0)
        {
            return;
        }

        if (pairQueueCount == pairQueueCap)
        {
            pairQueueCap *= 2;
            pairQueue = Realloc(pairQueue, pairQueueCap);
        }

        meta.Queued = 1;
        pairQueue[pairQueueCount++] = slot * statCount + stat;
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
            var slot = pair / statCount;
            var stat = pair - slot * statCount;
            ref var meta = ref Meta(slot, stat);
            meta.Queued = 0;
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var count = Gather(slot, stat);
            var set = table!.Resolve(stat, scratch, count);
            if (meta.Set != set)
            {
                if (meta.Set >= 0)
                {
                    var moved = table.RemoveMemberAt(meta.Set, meta.Pos);
                    if (moved >= 0)
                    {
                        Meta(moved, stat).Pos = meta.Pos;
                    }
                }

                meta.Pos = table.AddMember(set, slot);
                meta.Set = set;
            }

            if (!table.Folded(set))
            {
                table.MarkDirty(set);
                continue;
            }

            BroadcastOne(stat, slot, table.Computed(set));
        }

        pairQueueCount = 0;

        while (table!.TryDequeueDirty(out var dirty))
        {
            var stat = table.SetStat(dirty);
            float computed;
            if (table.Folded(dirty))
            {
                computed = table.Computed(dirty);
            }
            else
            {
                var content = table.Content(dirty, out var contentCount);
                computed = Fold(statBase[stat], content, contentCount);
                table.SetComputed(dirty, computed);
            }

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

    int Gather(int slot, int stat)
    {
        var mods = ModsOrNull(slot, stat);
        if (mods == null || mods->Count == 0)
        {
            return 0;
        }

        var count = mods->Count;
        if (count > scratchCap)
        {
            scratchCap = count * 2;
            scratch = Realloc(scratch, scratchCap);
            scratchIds = Realloc(scratchIds, scratchCap);
        }

        var ids = mods->Ids;
        for (var i = 0; i < count; i++)
        {
            scratch[i] = mods->Entries[i];
            scratchIds[i] = ids[i];
        }

        Canonicalize(scratch, scratchIds, count);
        return count;
    }

    void BroadcastOne(int stat, int slot, float computed)
    {
        var column = outputColumn[stat];
        if (column == null)
        {
            svColumns[(nint)stat * Capacity + slot] = computed;
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        Guard.BoundColumnFit(boundLength[stat], stride, slot);
        column[slot * stride] = computed;
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
        var count = Gather(slot, index);
        return Fold(statBase[index], scratch, count);
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
        var nextMods = (PairMods**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairMods*));
        var nextMetas = (PairMeta**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairMeta*));
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            var to = (next & (PageSlots - 1)) * statCount;
            var metaPage = MetaPage(nextMetas, next);
            for (var stat = 0; stat < statCount; stat++)
            {
                ref var meta = ref Meta(slot, stat);
                metaPage[to + stat] = new PairMeta { Set = meta.Set, Pos = meta.Pos };
                var mods = ModsOrNull(slot, stat);
                if (mods != null && mods->Entries != null)
                {
                    ModPage(nextMods, next)[to + stat] = *mods;
                    *mods = default;
                }
            }
        }

        FreePages(modPages, metaPages);
        modPages = nextMods;
        metaPages = nextMetas;
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
