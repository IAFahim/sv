using System;
using System.Runtime.InteropServices;

namespace Sv;

public sealed unsafe partial class Board
{
    private const int PageShift = 12;

    private const int PageSlots = 1 << PageShift;

    private struct ModRecord
    {
        public int Id;

        public ModifierKind Kind;

        public float Value;

        public int Source;

        public long Expiry;

        public int Next;
    }

    private struct Expiry
    {
        public long Tick;

        public int Id;

        public int Slot;

        public int Stat;
    }

    private struct PairState
    {
        public int Set;

        public int Head;
    }

    private Expiry* expiries;

    private int expiryCount;

    private int expiryCap;

    private int statCount;

    private float* statBase;

    private float** outputColumn;

    private int* outputStride;

    private float* svColumns;

    private int* boundGeneration;

    private int** mirrors;

    private int* sourceOffset;

    private int* sourceStat;

    private float* sourceCoefficient;

    private int* dependentOffset;

    private int* dependentStat;

    private StatEntry* statScratch;

    private int* statScratchIds;

    private int statScratchCap;

    private ulong* changed;

    private int* changedTouched;

    private int changedTouchedCount;

    private ulong* acquired;

    private int* acquiredTouched;

    private int acquiredTouchedCount;

    private int* boundLength;

    private int bindingGeneration;

    private SetTable? table;

    private int pageCount;

    private PairState** metaPages;

    private ModRecord* records;

    private int recordCount;

    private int recordCap;

    private int recordFree;

    private uint* dirty;

    private int statMaskWords;

    private int* dirtyTouched;

    private int dirtyTouchedCount;

    private int dirtyTouchedCap;

    private uint* dependentsMask;

    private uint* allStatsMask;

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

        outputColumn = (float**)NativeMemory.Alloc((nuint)statCount * (nuint)sizeof(float*));
        NativeMemory.Clear(outputColumn, (nuint)statCount * (nuint)sizeof(float*));
        outputStride = Alloc<int>(statCount);
        svColumns = (float*)NativeMemory.AllocZeroed((nuint)statCount * (nuint)capacity * sizeof(float));
        boundGeneration = Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundGeneration[i] = -1;
        }

        mirrors = (int**)NativeMemory.AllocZeroed((nuint)statCount * (nuint)sizeof(int*));
        statMaskWords = (statCount + 31) >> 5;
        dependentsMask = (uint*)NativeMemory.AllocZeroed((nuint)statCount * (nuint)statMaskWords * sizeof(uint));
        allStatsMask = (uint*)NativeMemory.AllocZeroed((nuint)statMaskWords * sizeof(uint));
        for (var i = 0; i < statCount; i++)
        {
            allStatsMask[i >> 5] |= 1u << (i & 31);
        }

        InitSources(schema);
        var changedWords = (nuint)statCount * (nuint)((capacity + 63) >> 6);
        changed = (ulong*)NativeMemory.AllocZeroed(changedWords * sizeof(ulong));
        changedTouched = (int*)NativeMemory.Alloc(changedWords * sizeof(int));
        acquired = (ulong*)NativeMemory.AllocZeroed(changedWords * sizeof(ulong));
        acquiredTouched = (int*)NativeMemory.Alloc(changedWords * sizeof(int));
        boundLength = Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundLength[i] = -1;
        }
        pageCount = (capacity + PageSlots - 1) >> PageShift;
        metaPages = (PairState**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairState*));
        recordCap = 16;
        records = Alloc<ModRecord>(recordCap);
        recordFree = -1;
        dirty = (uint*)NativeMemory.AllocZeroed((nuint)capacity * (nuint)statMaskWords * sizeof(uint));
        dirtyTouchedCap = 64;
        dirtyTouched = Alloc<int>(dirtyTouchedCap);
        scratchCap = 32;
        scratch = Alloc<StatEntry>(scratchCap);
        scratchIds = Alloc<int>(scratchCap);
        expiryCap = 16;
        expiries = Alloc<Expiry>(expiryCap);
    }

    void InitSources(Schema schema)
    {
        var total = 0;
        for (var i = 0; i < statCount; i++)
        {
            total += schema.Stats[i].Sources.Count;
        }

        sourceOffset = Alloc<int>(statCount + 1);
        sourceStat = Alloc<int>(Math.Max(total, 1));
        sourceCoefficient = Alloc<float>(Math.Max(total, 1));
        dependentOffset = (int*)NativeMemory.AllocZeroed((nuint)(statCount + 1) * sizeof(int));
        dependentStat = Alloc<int>(Math.Max(total, 1));
        var at = 0;
        for (var i = 0; i < statCount; i++)
        {
            sourceOffset[i] = at;
            foreach (var source in schema.Stats[i].Sources)
            {
                sourceStat[at] = source.Stat.Index;
                sourceCoefficient[at] = source.Coefficient;
                dependentOffset[source.Stat.Index + 1]++;
                at++;
            }
        }

        sourceOffset[statCount] = at;
        for (var i = 0; i < statCount; i++)
        {
            dependentOffset[i + 1] += dependentOffset[i];
        }

        var fill = Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            fill[i] = dependentOffset[i];
        }

        for (var i = 0; i < statCount; i++)
        {
            for (var k = sourceOffset[i]; k < sourceOffset[i + 1]; k++)
            {
                dependentStat[fill[sourceStat[k]]++] = i;
            }
        }

        for (var stat = 0; stat < statCount; stat++)
        {
            for (var d = dependentOffset[stat]; d < dependentOffset[stat + 1]; d++)
            {
                var dependent = dependentStat[d];
                dependentsMask[stat * statMaskWords + (dependent >> 5)] |= 1u << (dependent & 31);
            }
        }

        NativeMemory.Free(fill);
    }

    static T* Alloc<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T));
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

        FreePages(metaPages);
        table!.Dispose();
        NativeMemory.Free(statBase);
        NativeMemory.Free(outputColumn);
        NativeMemory.Free(outputStride);
        NativeMemory.Free(svColumns);
        NativeMemory.Free(boundGeneration);
        NativeMemory.Free(boundLength);
        for (var i = 0; i < statCount; i++)
        {
            if (mirrors[i] != null)
            {
                NativeMemory.Free(mirrors[i]);
            }
        }

        NativeMemory.Free(mirrors);
        NativeMemory.Free(sourceOffset);
        NativeMemory.Free(sourceStat);
        NativeMemory.Free(sourceCoefficient);
        NativeMemory.Free(dependentOffset);
        NativeMemory.Free(dependentStat);
        NativeMemory.Free(changed);
        NativeMemory.Free(changedTouched);
        NativeMemory.Free(acquired);
        NativeMemory.Free(acquiredTouched);
        NativeMemory.Free(records);
        NativeMemory.Free(dirty);
        NativeMemory.Free(dirtyTouched);
        NativeMemory.Free(dependentsMask);
        NativeMemory.Free(allStatsMask);
        NativeMemory.Free(scratch);
        NativeMemory.Free(scratchIds);
        NativeMemory.Free(statScratch);
        NativeMemory.Free(statScratchIds);
        NativeMemory.Free(expiries);
    }

    void FreePages(PairState** metas)
    {
        for (var page = 0; page < pageCount; page++)
        {
            if (metas[page] != null)
            {
                NativeMemory.Free(metas[page]);
            }
        }

        NativeMemory.Free(metas);
    }

    PairState* PairPage(PairState** metas, int slot)
    {
        var page = slot >> PageShift;
        if (metas[page] == null)
        {
            var width = PageSlots * statCount;
            var fresh = Alloc<PairState>(width);
            for (var i = 0; i < width; i++)
            {
                fresh[i] = new PairState { Set = -1, Head = -1 };
            }

            metas[page] = fresh;
        }

        return metas[page];
    }

    int AllocRecord()
    {
        if (recordFree >= 0)
        {
            var reuse = recordFree;
            recordFree = records[reuse].Next;
            return reuse;
        }

        if (recordCount == recordCap)
        {
            recordCap *= 2;
            records = Realloc(records, recordCap);
        }

        return recordCount++;
    }

    void FreeRecord(int index)
    {
        records[index].Next = recordFree;
        recordFree = index;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    ref PairState Pair(int slot, int stat)
    {
        return ref metaPages[slot >> PageShift][(slot & (PageSlots - 1)) * statCount + stat];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void EnsureSlotPages(int slot)
    {
        PairPage(metaPages, slot);
    }

    internal long PairBytes
    {
        get
        {
            long bytes = (long)pageCount * sizeof(PairState*) + (long)recordCap * sizeof(ModRecord);
            var pageWidth = PageSlots * statCount;
            for (var page = 0; page < pageCount; page++)
            {
                if (metaPages[page] != null)
                {
                    bytes += (long)pageWidth * sizeof(PairState);
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
    /// Rows of resolved pairs are written into the column immediately (dense refill).
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
        RefillColumn(index);
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
        RefillColumn(index);
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

    internal int OutputStrideAt(int statIndex) => outputStride[statIndex];

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

    /// <summary>
    /// Sv-owned int mirror of a stat's bound: <see cref="Clamp.BoundOf"/> of each row's broadcast
    /// value (clause-16 conversion), written by every broadcast from now on and filled from the
    /// current output column on first call. Capacity-sized, never moves. Host writes to a bound
    /// float column are not seen by the mirror. <see cref="Clamp.StatMirror"/> clamps against it.
    /// </summary>
    public int* MirrorBounds(StatId stat)
    {
        var index = StatIndex(stat);
        if (mirrors[index] != null)
        {
            return mirrors[index];
        }

        var mirror = (int*)NativeMemory.AllocZeroed((nuint)Capacity * sizeof(int));
        FillMirrorFromColumns(index, mirror);
        mirrors[index] = mirror;
        return mirror;
    }

    void FillMirrorFromColumns(int index, int* mirror)
    {
        var external = outputColumn[index];
        var stride = outputStride[index];
        var owned = svColumns + (nint)index * Capacity;
        for (var word = 0; word < LiveWordCount; word++)
        {
            var bits = live[word];
            while (bits != 0)
            {
                var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                mirror[slot] = Clamp.BoundOf(external == null ? owned[slot] : external[slot * stride]);
            }
        }
    }

    internal long MirrorBytes
    {
        get
        {
            long bytes = (long)statCount * sizeof(int*);
            for (var i = 0; i < statCount; i++)
            {
                if (mirrors[i] != null)
                {
                    bytes += (long)Capacity * sizeof(int);
                }
            }

            return bytes;
        }
    }

    public float* OutputColumn(StatId stat)
    {
        return outputColumn[StatIndex(stat)];
    }

    public int SetCount => table?.LiveSetCount ?? 0;

    internal int MemberTotal(StatId stat) => table!.MemberTotal(StatIndex(stat));

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier)
    {
        return AddModifier(slot, stat, modifier, 0, long.MaxValue);
    }

    /// <summary>
    /// Adds a modifier tagged with <paramref name="source"/> (see <see cref="RemoveSource"/>) that
    /// <see cref="ExpireModifiers"/> removes once its tick reaches <paramref name="expiresAtTick"/>
    /// (<see cref="long.MaxValue"/>: never). Like every modifier change it takes effect at the next
    /// <see cref="Recalculate"/>.
    /// </summary>
    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier, int source, long expiresAtTick = long.MaxValue)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var pair = ref Pair(slot, index);
        var id = ++nextInsertionId;
        var record = AllocRecord();
        records[record] = new ModRecord { Id = id, Kind = modifier.Kind, Value = modifier.Value, Source = source, Expiry = expiresAtTick, Next = pair.Head };
        pair.Head = record;
        MarkStatDirty(slot, index);
        if (expiresAtTick != long.MaxValue)
        {
            PushExpiry(new Expiry { Tick = expiresAtTick, Id = id, Slot = slot, Stat = index });
        }

        return new ModifierHandle(id);
    }

    /// <summary>
    /// Removes every modifier on <paramref name="slot"/> (all stats) carrying <paramref name="source"/>.
    /// Returns how many were removed.
    /// </summary>
    public int RemoveSource(int slot, int source)
    {
        Guard.SlotLive(slot, live, Capacity);
        var removed = 0;
        if (metaPages[slot >> PageShift] == null)
        {
            return removed;
        }

        for (var stat = 0; stat < statCount; stat++)
        {
            var before = removed;
            ref var pair = ref Pair(slot, stat);
            var previous = -1;
            for (var at = pair.Head; at >= 0;)
            {
                if (records[at].Source == source)
                {
                    var next = records[at].Next;
                    FreeRecord(at);
                    if (previous < 0)
                    {
                        pair.Head = next;
                    }
                    else
                    {
                        records[previous].Next = next;
                    }

                    at = next;
                    removed++;
                    continue;
                }

                previous = at;
                at = records[at].Next;
            }

            if (removed != before)
            {
                MarkStatDirty(slot, stat);
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes every modifier whose expiry tick is <= <paramref name="tick"/>, in (tick, insertion id)
    /// order. Modifiers already removed (by handle, source or slot release) are skipped. Returns how
    /// many were removed.
    /// </summary>
    public int ExpireModifiers(long tick)
    {
        var removed = 0;
        while (expiryCount > 0 && expiries[0].Tick <= tick)
        {
            var top = expiries[0];
            PopExpiry();
            if (top.Slot < 0 || (live[top.Slot >> 6] & (1ul << (top.Slot & 63))) == 0)
            {
                continue;
            }

            if (metaPages[top.Slot >> PageShift] == null)
            {
                continue;
            }

            ref var pair = ref Pair(top.Slot, top.Stat);
            var previous = -1;
            for (var at = pair.Head; at >= 0; at = records[at].Next)
            {
                if (records[at].Id == top.Id)
                {
                    var next = records[at].Next;
                    FreeRecord(at);
                    if (previous < 0)
                    {
                        pair.Head = next;
                    }
                    else
                    {
                        records[previous].Next = next;
                    }

                    MarkStatDirty(top.Slot, top.Stat);
                    removed++;
                    break;
                }

                previous = at;
            }
        }

        return removed;
    }

    public int PendingExpiries => expiryCount;

    static bool Before(in Expiry a, in Expiry b) => a.Tick < b.Tick || (a.Tick == b.Tick && a.Id < b.Id);

    void PushExpiry(Expiry entry)
    {
        if (expiryCount == expiryCap)
        {
            expiryCap *= 2;
            expiries = Realloc(expiries, expiryCap);
        }

        var at = expiryCount++;
        while (at > 0)
        {
            var parent = (at - 1) >> 1;
            if (!Before(entry, expiries[parent]))
            {
                break;
            }

            expiries[at] = expiries[parent];
            at = parent;
        }

        expiries[at] = entry;
    }

    void PopExpiry()
    {
        var last = expiries[--expiryCount];
        var at = 0;
        while (true)
        {
            var child = at * 2 + 1;
            if (child >= expiryCount)
            {
                break;
            }

            if (child + 1 < expiryCount && Before(expiries[child + 1], expiries[child]))
            {
                child++;
            }

            if (!Before(expiries[child], last))
            {
                break;
            }

            expiries[at] = expiries[child];
            at = child;
        }

        if (expiryCount > 0)
        {
            expiries[at] = last;
        }
    }

    public void RemoveModifier(int slot, StatId stat, ModifierHandle handle)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var pair = ref Pair(slot, index);
        var previous = -1;
        for (var at = pair.Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Id == handle.Id)
            {
                var next = records[at].Next;
                FreeRecord(at);
                if (previous < 0)
                {
                    pair.Head = next;
                }
                else
                {
                    records[previous].Next = next;
                }

                MarkStatDirty(slot, index);
                return;
            }

            previous = at;
        }

        throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
    }

    internal void MarkSlotStatsDirty(int slot)
    {
        var mask = dirty + (nint)slot * statMaskWords;
        var empty = true;
        for (var w = 0; w < statMaskWords; w++)
        {
            if (mask[w] != 0)
            {
                empty = false;
                break;
            }
        }

        for (var w = 0; w < statMaskWords; w++)
        {
            mask[w] = allStatsMask[w];
        }

        if (empty)
        {
            AppendTouched(slot);
        }
    }

    internal void ZeroSlotRows(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            svColumns[(nint)stat * Capacity + slot] = 0f;
            var mirror = mirrors[stat];
            if (mirror != null)
            {
                mirror[slot] = 0;
            }
        }
    }

    internal void MarkSlotAcquired(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            var index = stat * LiveWordCount + (slot >> 6);
            if (acquired[index] == 0)
            {
                acquiredTouched[acquiredTouchedCount++] = index;
            }

            acquired[index] |= 1ul << (slot & 63);
        }
    }

    internal void ClearChangedMasks()
    {
        for (var i = 0; i < changedTouchedCount; i++)
        {
            changed[changedTouched[i]] = 0;
        }

        changedTouchedCount = 0;
        for (var i = 0; i < acquiredTouchedCount; i++)
        {
            acquired[acquiredTouched[i]] = 0;
        }

        acquiredTouchedCount = 0;
    }

    internal void ClearSlotStats(int slot)
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            ref var pair = ref Pair(slot, stat);
            for (var at = pair.Head; at >= 0;)
            {
                var next = records[at].Next;
                FreeRecord(at);
                at = next;
            }

            pair.Head = -1;
            if (pair.Set >= 0)
            {
                table!.ReleaseRef(pair.Set);
                pair.Set = -1;
            }
        }
    }

    void MarkStatDirty(int slot, int stat)
    {
        var mask = dirty + (nint)slot * statMaskWords;
        var empty = true;
        for (var w = 0; w < statMaskWords; w++)
        {
            if (mask[w] != 0)
            {
                empty = false;
                break;
            }
        }

        mask[stat >> 5] |= 1u << (stat & 31);
        if (empty)
        {
            AppendTouched(slot);
        }
    }

    void AppendTouched(int slot)
    {
        if (dirtyTouchedCount == dirtyTouchedCap)
        {
            dirtyTouchedCap *= 2;
            dirtyTouched = Realloc(dirtyTouched, dirtyTouchedCap);
        }

        dirtyTouched[dirtyTouchedCount++] = slot;
    }

    void ClearDirty()
    {
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var mask = dirty + (nint)dirtyTouched[i] * statMaskWords;
            for (var w = 0; w < statMaskWords; w++)
            {
                mask[w] = 0;
            }
        }

        dirtyTouchedCount = 0;
    }

    static void SortTouched(int* items, int count)
    {
        for (var i = count / 2 - 1; i >= 0; i--)
        {
            SiftTouched(items, i, count);
        }

        for (var end = count - 1; end > 0; end--)
        {
            var top = items[0];
            items[0] = items[end];
            items[end] = top;
            SiftTouched(items, 0, end);
        }
    }

    static void SiftTouched(int* items, int at, int count)
    {
        while (true)
        {
            var child = at * 2 + 1;
            if (child >= count)
            {
                return;
            }

            if (child + 1 < count && items[child + 1] > items[child])
            {
                child++;
            }

            if (items[child] <= items[at])
            {
                return;
            }

            var swap = items[at];
            items[at] = items[child];
            items[child] = swap;
            at = child;
        }
    }

    public void Recalculate()
    {
        if (statCount == 0)
        {
            return;
        }

        ClearChangedMasks();
        table!.Tick();
        SortTouched(dirtyTouched, dirtyTouchedCount);
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var slot = dirtyTouched[i];
            var mask = dirty + (nint)slot * statMaskWords;
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                for (var k = 0; k < statMaskWords; k++)
                {
                    mask[k] = 0;
                }

                continue;
            }

            var w = 0;
            while (w < statMaskWords)
            {
                var word = mask[w];
                if (word == 0)
                {
                    w++;
                    continue;
                }

                var stat = (w << 5) + System.Numerics.BitOperations.TrailingZeroCount(word);
                mask[w] = word & (word - 1);
                ref var current = ref Pair(slot, stat).Set;
                var count = Gather(slot, stat);
                var set = table!.Resolve(stat, scratch, count);
                if (current != set)
                {
                    if (current >= 0)
                    {
                        table.ReleaseRef(current);
                    }

                    table.AddRef(set);
                    current = set;
                    var dependents = dependentsMask + (nint)stat * statMaskWords;
                    for (var k = 0; k < statMaskWords; k++)
                    {
                        mask[k] |= dependents[k];
                    }

                    if (!table.Folded(set))
                    {
                        var content = table.Content(set, out var contentCount);
                        table.SetComputed(set, CanonicalNaN(Fold(statBase[stat], content, contentCount)));
                    }
                }

                BroadcastOne(stat, slot, table.Computed(set));
            }
        }

        dirtyTouchedCount = 0;

    }

    void RefillColumn(int stat)
    {
        var column = outputColumn[stat];
        if (column == null)
        {
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            if (Pair(slot, stat).Set < 0)
            {
                continue;
            }

            Guard.BoundColumnFit(boundLength[stat], stride, slot);
            column[slot * stride] = SourceValue(slot, stat);
        }
    }

    int Gather(int slot, int stat)
    {
        var synthetic = sourceOffset[stat + 1] - sourceOffset[stat];
        var real = RecordCount(Pair(slot, stat).Head);
        var count = synthetic + real;
        if (count == 0)
        {
            return 0;
        }

        if (count > scratchCap)
        {
            scratchCap = count * 2;
            scratch = Realloc(scratch, scratchCap);
            scratchIds = Realloc(scratchIds, scratchCap);
        }

        return GatherInto(slot, stat, scratch, scratchIds);
    }

    int RecordCount(int head)
    {
        var count = 0;
        for (var at = head; at >= 0; at = records[at].Next)
        {
            count++;
        }

        return count;
    }

    int GatherInto(int slot, int stat, StatEntry* entries, int* entryIds)
    {
        var synthetic = sourceOffset[stat + 1] - sourceOffset[stat];
        var count = synthetic;
        for (var k = 0; k < synthetic; k++)
        {
            var at = sourceOffset[stat] + k;
            entries[k] = new StatEntry(ModifierKind.Added, sourceCoefficient[at] * SourceValue(slot, sourceStat[at]));
            entryIds[k] = int.MinValue + k;
        }

        for (var at = Pair(slot, stat).Head; at >= 0; at = records[at].Next)
        {
            entries[count] = new StatEntry(records[at].Kind, records[at].Value);
            entryIds[count] = records[at].Id;
            count++;
        }

        Canonicalize(entries, entryIds, count);
        return count;
    }

    float SourceValue(int slot, int stat)
    {
        var set = Pair(slot, stat).Set;
        return set >= 0 && table!.Folded(set) ? table.Computed(set) : statBase[stat];
    }

    void BroadcastOne(int stat, int slot, float computed)
    {
        var mirror = mirrors[stat];
        if (mirror != null)
        {
            mirror[slot] = Clamp.BoundOf(computed);
        }

        var column = outputColumn[stat];
        if (column == null)
        {
            ref var owned = ref svColumns[(nint)stat * Capacity + slot];
            MarkChanged(stat, slot, owned, computed);
            owned = computed;
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        Guard.BoundColumnFit(boundLength[stat], stride, slot);
        MarkChanged(stat, slot, column[slot * stride], computed);
        column[slot * stride] = computed;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void MarkChanged(int stat, int slot, float before, float after)
    {
        if (BitConverter.SingleToUInt32Bits(before) == BitConverter.SingleToUInt32Bits(after))
        {
            return;
        }

        var index = stat * LiveWordCount + (slot >> 6);
        if (changed[index] == 0)
        {
            changedTouched[changedTouchedCount++] = index;
        }

        changed[index] |= 1ul << (slot & 63);
    }

    /// <summary>
    /// Changed mask of the last <see cref="Recalculate"/> for a stat: bit i of word w is set iff row
    /// w*64+i was broadcast a value whose bits differ from what its output column held before.
    /// <see cref="LiveWordCount"/> words, valid until the next Recalculate — the clause-5 word-span
    /// shape, so it feeds <see cref="RunEnabledChunk(int*, int, in Clamp, ulong*, int, int, int*)"/> directly.
    /// </summary>
    public ulong* ChangedWords(StatId stat)
    {
        return changed + (nint)StatIndex(stat) * LiveWordCount;
    }

    /// <summary>
    /// Acquired mask of the slots acquired since the last <see cref="Recalculate"/>: bit i of word w
    /// marks row w*64+i. Set by <see cref="Acquire"/>, cleared when the next Recalculate begins —
    /// read it in the spawn-to-recalc window (clause-7 step 2 to step 3). Changed words carry value
    /// changes only, so a stat whose base is 0 spawns visibly here and never there.
    /// </summary>
    public ulong* AcquiredWords(StatId stat)
    {
        return acquired + (nint)StatIndex(stat) * LiveWordCount;
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

    internal static float CanonicalNaN(float value)
    {
        return float.IsNaN(value) ? BitConverter.UInt32BitsToSingle(0x7FC00000u) : value;
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
        var synthetic = sourceOffset[index + 1] - sourceOffset[index];
        var need = synthetic + RecordCount(Pair(slot, index).Head);
        if (statScratchCap < need)
        {
            statScratchCap = need * 2;
            statScratch = Realloc(statScratch, statScratchCap);
            statScratchIds = Realloc(statScratchIds, statScratchCap);
        }

        var count = GatherInto(slot, index, statScratch, statScratchIds);
        return CanonicalNaN(Fold(statBase[index], statScratch, count));
    }

    /// <summary>
    /// Compacts the slot space: live slots map through <paramref name="oldToNew"/> (negative entries
    /// are dead slots) to distinct targets in [0, capacity) — gaps are legal (64-alignment padding).
    /// Rewrites pair states (set ids are slot-independent, modifier records are pool-global — all
    /// live pairs dirty-marked — run <see cref="Recalculate"/> after), live bits, and sv-owned columns; external
    /// bindings are invalidated per the clause-12 doctrine. Modifier insertion ids ride along, so
    /// stat folds are preserved bit-exactly.
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
            ClearChangedMasks();
            ClearDirty();
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

    void WriteStats(SvState state)
    {
        var words = LiveWordCount;
        state.Write(nextInsertionId);
        state.Write(dirtyTouchedCount);
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var slot = dirtyTouched[i];
            state.Write(slot);
            state.Write(dirty + (nint)slot * statMaskWords, statMaskWords * sizeof(uint));
        }

        state.Write(changedTouchedCount);
        state.Write(changedTouched, (long)changedTouchedCount * sizeof(int));
        state.Write(changed, (long)statCount * words * sizeof(ulong));
        state.Write(acquiredTouchedCount);
        state.Write(acquiredTouched, (long)acquiredTouchedCount * sizeof(int));
        state.Write(acquired, (long)statCount * words * sizeof(ulong));
        state.Write(svColumns, (long)statCount * Capacity * sizeof(float));
        for (var stat = 0; stat < statCount; stat++)
        {
            var mirror = mirrors[stat];
            state.Write((byte)(mirror == null ? 0 : 1));
            if (mirror != null)
            {
                state.Write(mirror, (long)Capacity * sizeof(int));
            }
        }

        state.Write(expiryCount);
        state.Write(expiries, (long)expiryCount * sizeof(Expiry));
        state.Write(recordCount);
        state.Write(records, (long)recordCount * sizeof(ModRecord));
        state.Write(recordFree);
        var pageWidth = PageSlots * statCount;
        for (var page = 0; page < pageCount; page++)
        {
            var metas = metaPages[page];
            state.Write((byte)(metas == null ? 0 : 1));
            if (metas != null)
            {
                state.Write(metas, (long)pageWidth * sizeof(PairState));
            }
        }

        table!.Write(state);
    }

    void ReadStats(SvState state)
    {
        var words = LiveWordCount;
        nextInsertionId = state.Read<int>();
        var pending = state.Read<int>();
        if (pending < 0 || pending > Capacity)
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        if (pending > dirtyTouchedCap)
        {
            dirtyTouchedCap = pending;
            dirtyTouched = Realloc(dirtyTouched, dirtyTouchedCap);
        }

        ClearDirty();
        dirtyTouchedCount = pending;
        for (var i = 0; i < pending; i++)
        {
            var slot = state.Read<int>();
            if (slot < 0 || slot >= Capacity)
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            dirtyTouched[i] = slot;
            state.Read(dirty + (nint)slot * statMaskWords, statMaskWords * sizeof(uint));
        }

        changedTouchedCount = state.Read<int>();
        state.Read(changedTouched, (long)changedTouchedCount * sizeof(int));
        state.Read(changed, (long)statCount * words * sizeof(ulong));
        acquiredTouchedCount = state.Read<int>();
        state.Read(acquiredTouched, (long)acquiredTouchedCount * sizeof(int));
        state.Read(acquired, (long)statCount * words * sizeof(ulong));
        state.Read(svColumns, (long)statCount * Capacity * sizeof(float));
        for (var stat = 0; stat < statCount; stat++)
        {
            if (state.Read<byte>() == 0)
            {
                var mirror = mirrors[stat];
                if (mirror != null && outputColumn[stat] == null)
                {
                    FillMirrorFromColumns(stat, mirror);
                }

                continue;
            }

            if (mirrors[stat] == null)
            {
                mirrors[stat] = (int*)NativeMemory.AllocZeroed((nuint)Capacity * sizeof(int));
            }

            state.Read(mirrors[stat], (long)Capacity * sizeof(int));
        }

        var heap = state.Read<int>();
        if (heap > expiryCap)
        {
            expiryCap = heap;
            expiries = Realloc(expiries, expiryCap);
        }

        expiryCount = heap;
        state.Read(expiries, (long)heap * sizeof(Expiry));
        var poolCount = state.Read<int>();
        if (poolCount < 0 || poolCount > (1 << 24))
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        if (poolCount > recordCap)
        {
            recordCap = poolCount;
            records = Realloc(records, recordCap);
        }

        recordCount = poolCount;
        state.Read(records, (long)recordCount * sizeof(ModRecord));
        recordFree = state.Read<int>();
        if (recordFree < -1 || recordFree >= recordCount)
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        var pageWidth = PageSlots * statCount;
        for (var page = 0; page < pageCount; page++)
        {
            if (state.Read<byte>() == 0)
            {
                if (metaPages[page] != null)
                {
                    NativeMemory.Free(metaPages[page]);
                    metaPages[page] = null;
                }
            }
            else
            {
                if (metaPages[page] == null)
                {
                    metaPages[page] = Alloc<PairState>(pageWidth);
                    for (var i = 0; i < pageWidth; i++)
                    {
                        metaPages[page][i] = new PairState { Set = -1, Head = -1 };
                    }
                }

                state.Read(metaPages[page], (long)pageWidth * sizeof(PairState));
            }
        }

        table!.Read(state);
        RefillRestoredBindings();
    }

    void RefillRestoredBindings()
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            RefillColumn(stat);
        }
    }

    void RenumberStats(ReadOnlySpan<int> oldToNew)
    {
        var nextMetas = (PairState**)NativeMemory.AllocZeroed((nuint)pageCount * (nuint)sizeof(PairState*));
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            var to = (next & (PageSlots - 1)) * statCount;
            var metaPage = PairPage(nextMetas, next);
            for (var stat = 0; stat < statCount; stat++)
            {
                metaPage[to + stat] = Pair(slot, stat);
            }
        }

        FreePages(metaPages);
        metaPages = nextMetas;
        for (var i = 0; i < expiryCount; i++)
        {
            var slot = expiries[i].Slot;
            expiries[i].Slot = slot >= 0 && (live[slot >> 6] & (1ul << (slot & 63))) != 0 ? oldToNew[slot] : -1;
        }

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
            var mirror = mirrors[stat];
            if (mirror == null)
            {
                continue;
            }

            var mirrorRows = (int*)tempRows;
            NativeMemory.Clear(mirrorRows, (nuint)Capacity * sizeof(int));
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    mirrorRows[oldToNew[slot]] = mirror[slot];
                }
            }

            new ReadOnlySpan<int>(mirrorRows, Capacity).CopyTo(new Span<int>(mirror, Capacity));
        }

        NativeMemory.Free(tempRows);
    }
}
