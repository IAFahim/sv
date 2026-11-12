using System;
using System.Runtime.InteropServices;

namespace Sv;

public sealed unsafe partial class Board
{
    private const int PageShift = 12;

    private const int PageSlots = 1 << PageShift;

    private struct ModRecord
    {
        public long Id;

        public ModifierKind Kind;

        public float Value;

        public int Source;

        public long Expiry;

        public int Next;

        public int Slot;

        public int Stat;

        public int Wheel;
    }

    private struct SlotState
    {
        public int Block;

        public int Head;
    }

    private int statCount;

    private float* statBase;

    private float** outputColumn;

    private int* outputStride;

    private float** owned;

    private int* boundGeneration;

    private int** mirrors;

    private int* sourceOffset;

    private int* sourceStat;

    private float* sourceCoefficient;

    private int* sourceCurve;

    private float* curveData;

    private float* curveMin;

    private float* curveStep;

    private float* curveCeiling;

    private int* curveLength;

    private int curveTotal;

    private int* dependentOffset;

    private int* dependentStat;

    private StatEntry* statScratch;

    private long* statScratchIds;

    private int statScratchCap;

    private ulong* changed;

    private ulong digest;

    private bool digestPagesDirty;

    private bool digestRecomputing;

    private ulong* pageDigests;

    private int* changedTouched;

    private int* changedTouchedCounts;

    private ulong* acquired;

    private int* acquiredTouched;

    private int acquiredTouchedCount;

    private int* boundLength;

    private int bindingGeneration;

    private SetTable? table;

    private int pageCount;

    private SlotState** slotPages;

    private BlockTable? blocks;

    private int* tupleScratch;

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

    private int* sourceAnchor;

    private uint* allStatsMask;

    internal long nextInsertionId;

    internal int insertionGeneration;

    private StatEntry* scratch;

    private long* scratchIds;

    private int scratchCap;

    private void InitStats(Schema schema, int capacity)
    {
        statCount = schema.Stats.Count;
        if (statCount == 0)
        {
            return;
        }

        table = new SetTable(arena);
        statBase = arena.Alloc<float>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            statBase[i] = schema.Stats[i].Base;
        }

        outputColumn = (float**)arena.AllocBytes(statCount * sizeof(float*));
        NativeMemory.Clear(outputColumn, (nuint)statCount * (nuint)sizeof(float*));
        outputStride = arena.Alloc<int>(statCount);
        owned = (float**)arena.AllocZeroedBytes(statCount * sizeof(float*));
        boundGeneration = arena.AllocZeroed<int>(statCount);

        mirrors = (int**)arena.AllocZeroedBytes(statCount * sizeof(int*));
        statMaskWords = (statCount + 31) >> 5;
        dependentsMask = arena.AllocZeroed<uint>(statCount * statMaskWords);
        allStatsMask = arena.AllocZeroed<uint>(statMaskWords);
        for (var i = 0; i < statCount; i++)
        {
            allStatsMask[i >> 5] |= 1u << (i & 31);
        }

        InitSources(schema);
        var changedWords = statCount * ((capacity + 63) >> 6);
        changed = arena.AllocZeroed<ulong>(changedWords);
        pageDigests = arena.AllocZeroed<ulong>(statCount * ((capacity + 255) >> 8));
        changedTouched = arena.Alloc<int>(changedWords);
        changedTouchedCounts = arena.AllocZeroed<int>(statCount);
        acquired = arena.AllocZeroed<ulong>(changedWords);
        acquiredTouched = arena.Alloc<int>(changedWords);
        boundLength = arena.Alloc<int>(statCount);
        for (var i = 0; i < statCount; i++)
        {
            boundLength[i] = -1;
        }
        pageCount = (capacity + PageSlots - 1) >> PageShift;
        slotPages = (SlotState**)arena.AllocZeroedBytes(pageCount * sizeof(SlotState*));
        blocks = new BlockTable(arena, statCount);
        tupleScratch = arena.Alloc<int>(statCount);
        recordCap = 16;
        records = arena.Alloc<ModRecord>(recordCap);
        recordFree = -1;
        dirty = arena.AllocZeroed<uint>(capacity * statMaskWords);
        dirtyTouchedCap = 64;
        dirtyTouched = arena.Alloc<int>(dirtyTouchedCap);
        keyframeScratchCap = 32;
        keyframeScratch = arena.Alloc<KeyframeRecord>(keyframeScratchCap);
        scratchCap = 32;
        scratch = arena.Alloc<StatEntry>(scratchCap);
        scratchIds = arena.Alloc<long>(scratchCap);
    }

    void InitSources(Schema schema)
    {
        var total = 0;
        for (var i = 0; i < statCount; i++)
        {
            total += schema.Stats[i].Sources.Count;
        }

        sourceOffset = arena.Alloc<int>(statCount + 1);
        sourceStat = arena.Alloc<int>(Math.Max(total, 1));
        sourceCoefficient = arena.Alloc<float>(Math.Max(total, 1));
        sourceCurve = arena.Alloc<int>(Math.Max(total, 1));
        dependentOffset = arena.AllocZeroed<int>(statCount + 1);
        dependentStat = arena.Alloc<int>(Math.Max(total, 1));
        curveTotal = 0;
        for (var i = 0; i < statCount; i++)
        {
            foreach (var source in schema.Stats[i].Sources)
            {
                if (source.Curve != null)
                {
                    curveTotal += source.Curve.Length;
                }
            }
        }

        if (curveTotal > 0)
        {
            curveData = arena.Alloc<float>(curveTotal);
            curveMin = arena.Alloc<float>(Math.Max(total, 1));
            curveStep = arena.Alloc<float>(Math.Max(total, 1));
            curveCeiling = arena.Alloc<float>(Math.Max(total, 1));
            curveLength = arena.Alloc<int>(Math.Max(total, 1));
        }

        var at = 0;
        var dataAt = 0;
        for (var i = 0; i < statCount; i++)
        {
            sourceOffset[i] = at;
            foreach (var source in schema.Stats[i].Sources)
            {
                sourceStat[at] = source.Stat.Index;
                sourceCoefficient[at] = source.Coefficient;
                sourceCurve[at] = -1;
                if (source.Curve is { } curve)
                {
                    sourceCurve[at] = dataAt;
                    curveMin[at] = source.CurveMin;
                    curveStep[at] = source.CurveStep;
                    curveLength[at] = curve.Length;
                    curveCeiling[at] = source.CurveMin + (curve.Length - 1) * source.CurveStep;
                    for (var k = 0; k < curve.Length; k++)
                    {
                        curveData[dataAt + k] = curve[k];
                    }

                    dataAt += curve.Length;
                }

                dependentOffset[source.Stat.Index + 1]++;
                at++;
            }
        }

        sourceOffset[statCount] = at;
        var sourceTotal = Math.Max(at, 1);
        sourceAnchor = arena.Alloc<int>(sourceTotal);
        for (var i = 0; i < sourceTotal; i++)
        {
            sourceAnchor[i] = -1;
        }
        for (var i = 0; i < statCount; i++)
        {
            dependentOffset[i + 1] += dependentOffset[i];
        }

        var fill = arena.Alloc<int>(statCount);
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

        arena.Free(fill, statCount);
    }

    void FreeStats()
    {
        FreeWheel();
        if (statCount == 0)
        {
            return;
        }

        FreePages(slotPages);
        blocks!.Dispose();
        arena.Free(tupleScratch, statCount);
        table!.Dispose();
        arena.Free(statBase, statCount);
        arena.FreeBytes(outputColumn, statCount * sizeof(float*));
        arena.Free(outputStride, statCount);
        for (var i = 0; i < statCount; i++)
        {
            if (owned[i] != null)
            {
                arena.FreeAligned(owned[i], Capacity);
            }
        }

        arena.FreeBytes(owned, statCount * sizeof(float*));
        arena.Free(boundGeneration, statCount);
        arena.Free(boundLength, statCount);
        for (var i = 0; i < statCount; i++)
        {
            if (mirrors[i] != null)
            {
                arena.FreeAligned(mirrors[i], Capacity);
            }
        }

        arena.FreeBytes(mirrors, statCount * sizeof(int*));
        arena.Free(sourceOffset, statCount + 1);
        arena.Free(sourceStat, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceAnchor, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceCoefficient, Math.Max(sourceOffset[statCount], 1));
        arena.Free(sourceCurve, Math.Max(sourceOffset[statCount], 1));
        if (curveTotal > 0)
        {
            arena.Free(curveData, curveTotal);
            arena.Free(curveMin, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveStep, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveCeiling, Math.Max(sourceOffset[statCount], 1));
            arena.Free(curveLength, Math.Max(sourceOffset[statCount], 1));
        }
        arena.Free(dependentOffset, statCount + 1);
        arena.Free(dependentStat, Math.Max(sourceOffset[statCount], 1));
        var maskWords = statCount * ((Capacity + 63) >> 6);
        arena.Free(changed, maskWords);
        arena.Free(changedTouched, maskWords);
        arena.Free(changedTouchedCounts, statCount);
        arena.Free(acquired, maskWords);
        arena.Free(acquiredTouched, maskWords);
        arena.Free(records, recordCap);
        arena.Free(dirty, Capacity * statMaskWords);
        arena.Free(dirtyTouched, dirtyTouchedCap);
        arena.Free(keyframeScratch, keyframeScratchCap);
        arena.Free(dependentsMask, statCount * statMaskWords);
        arena.Free(allStatsMask, statMaskWords);
        arena.Free(scratch, scratchCap);
        arena.Free(scratchIds, scratchCap);
        arena.Free(statScratch, Math.Max(statScratchCap, 1));
        arena.Free(statScratchIds, Math.Max(statScratchCap, 1));
    }

    void FreePages(SlotState** pages)
    {
        for (var page = 0; page < pageCount; page++)
        {
            if (pages[page] != null)
            {
                arena.Free(pages[page], PageSlots);
            }
        }

        arena.FreeBytes(pages, pageCount * sizeof(SlotState*));
    }

    SlotState* SlotPage(SlotState** pages, int slot)
    {
        var page = slot >> PageShift;
        if (pages[page] == null)
        {
            var fresh = arena.Alloc<SlotState>(PageSlots);
            undo?.Note(pages + page, sizeof(SlotState*));
            pages[page] = fresh;
            undo?.QuarantineFresh(fresh, PageSlots * sizeof(SlotState));
            for (var i = 0; i < PageSlots; i++)
            {
                fresh[i] = new SlotState { Block = -1, Head = -1 };
            }
        }

        return pages[page];
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
            var previous = recordCap;
            recordCap *= 2;
            records = UndoGrow(UndoArrayId.Records, records, previous, recordCap);
        }

        return recordCount++;
    }

    void FreeRecord(int index)
    {
        undo?.Note(&records[index].Next, sizeof(int));
        records[index].Next = recordFree;
        recordFree = index;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    ref SlotState Slot(int slot)
    {
        return ref slotPages[slot >> PageShift][slot & (PageSlots - 1)];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    SlotState* SlotPtr(int slot)
    {
        return slotPages[slot >> PageShift] + (slot & (PageSlots - 1));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    int SetOf(int slot, int stat)
    {
        var block = Slot(slot).Block;
        return block >= 0 ? blocks!.Set(block, stat) : -1;
    }

    internal int PageCount => (Capacity + 255) >> 8;

    public ulong Digest
    {
        get
        {
            EnsureDigestCurrent();
            return digest;
        }
    }

    void EnsureDigestCurrent()
    {
        if (digestPagesDirty)
        {
            RecomputeDigest();
        }
    }

    static ulong DigestKey(int stat, int slot, uint bits)
    {
        unchecked
        {
            var key = ((ulong)(uint)stat << 40) ^ ((ulong)(uint)slot << 8) ^ (bits * 0x9E3779B97F4A7C15ul);
            key ^= key >> 33;
            key *= 0xFF51AFD7ED558CCDul;
            key ^= key >> 33;
            key *= 0xC4CEB9FE1A85EC53ul;
            return key ^ (key >> 33);
        }
    }

    void DigestKeyAdd(int slot, int stat, int set)
    {
        var key = DigestKey(stat, slot, BitConverter.SingleToUInt32Bits(table!.Computed(set)));
        digest += key;
        var cell = pageDigests + (nint)stat * PageCount + (slot >> 8);
        if (!digestRecomputing)
        {
            undo?.Note(cell, sizeof(ulong));
        }

        *cell += key;
    }

    void DigestMove(int slot, int stat, int oldSet, int newSet)
    {
        if (oldSet == newSet)
        {
            return;
        }

        var oldKey = oldSet >= 0 ? DigestKey(stat, slot, BitConverter.SingleToUInt32Bits(table!.Computed(oldSet))) : 0ul;
        var newKey = newSet >= 0 ? DigestKey(stat, slot, BitConverter.SingleToUInt32Bits(table!.Computed(newSet))) : 0ul;
        if (oldKey == newKey)
        {
            return;
        }

        var delta = newKey - oldKey;
        digest += delta;
        var cell = pageDigests + (nint)stat * PageCount + (slot >> 8);
        if (!digestRecomputing)
        {
            undo?.Note(cell, sizeof(ulong));
        }

        *cell += delta;
    }

    internal ulong RecomputeDigest()
    {
        digestPagesDirty = false;
        if (undo != null && undoFrames != null)
        {
            undoFrames[(nint)(undo.Top % undoFrames.Length)].PageDigestsRewritten = 1;
            digestRecomputing = true;
        }

        NativeMemory.Clear(pageDigests, (nuint)(statCount * PageCount) * sizeof(ulong));
        digest = 0;
        for (var word = 0; word < LiveWordCount; word++)
        {
            var bits = live[word];
            while (bits != 0)
            {
                var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                var block = Slot(slot).Block;
                if (block < 0)
                {
                    continue;
                }

                for (var stat = 0; stat < statCount; stat++)
                {
                    var set = blocks!.Set(block, stat);
                    if (set >= 0)
                    {
                        DigestKeyAdd(slot, stat, set);
                    }
                }
            }
        }

        digestRecomputing = false;
        return digest;
    }

    internal int FirstDivergentPage(Board other)
    {
        EnsureDigestCurrent();
        other.EnsureDigestCurrent();
        var total = statCount * PageCount;
        for (var i = 0; i < total; i++)
        {
            if (pageDigests[i] != other.pageDigests[i])
            {
                return i;
            }
        }

        return -1;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal void EnsureSlotPages(int slot)
    {
        SlotPage(slotPages, slot);
    }

    int InternTuple(int* tuple)
    {
        var existing = blocks!.Lookup(tuple);
        if (existing >= 0)
        {
            blocks.AddRefMany(existing, 1);
            return existing;
        }

        var block = blocks.Create(tuple);
        blocks.SetRefCount(block, 1);
        for (var stat = 0; stat < statCount; stat++)
        {
            if (tuple[stat] >= 0)
            {
                table!.AddRef(tuple[stat]);
            }
        }

        return block;
    }

    void DropBlockRef(int block)
    {
        if (!blocks!.ReleaseRef(block))
        {
            return;
        }

        for (var stat = 0; stat < statCount; stat++)
        {
            var set = blocks.Set(block, stat);
            if (set >= 0)
            {
                table!.ReleaseRef(set);
            }
        }

        blocks.Free(block);
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
    /// <summary>
    /// Points one declared source of <paramref name="stat"/> (by declaration ordinal) at
    /// <paramref name="anchorSlot"/>'s value of that source stat — the indirect-stat form of a
    /// population-wide buff: one modifier add on the anchor reaches every slot's fold, and identical
    /// outcomes intern to one set. Anchors ride the binding doctrine: they are not state —
    /// renumbering, loading or invalidating bindings clears them (rebind after), and releasing the
    /// anchor slot reverts the source to per-slot reads.
    /// </summary>
    public void BindSourceAnchor(StatId stat, int ordinal, int anchorSlot)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        var begin = sourceOffset[index];
        var total = sourceOffset[index + 1] - begin;
        if (ordinal < 0 || ordinal >= total)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"sv: stat has {total} declared sources");
        }

        if (anchorSlot < 0 || anchorSlot >= Capacity || (live[anchorSlot >> 6] & (1ul << (anchorSlot & 63))) == 0)
        {
            throw new ArgumentException($"sv: anchor slot {anchorSlot} is not live");
        }

        sourceAnchor[begin + ordinal] = anchorSlot;
    }

    internal int SourceAnchorOf(StatId stat, int ordinal)
    {
        var index = StatIndex(stat);
        return sourceAnchor[sourceOffset[index] + ordinal];
    }

    /// <summary>Reverts a source bound by <see cref="BindSourceAnchor"/> to per-slot reads.</summary>
    public void ClearSourceAnchor(StatId stat, int ordinal)
    {
        InvalidateUndo();
        var index = StatIndex(stat);
        var begin = sourceOffset[index];
        var total = sourceOffset[index + 1] - begin;
        if (ordinal < 0 || ordinal >= total)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), $"sv: stat has {total} declared sources");
        }

        sourceAnchor[begin + ordinal] = -1;
    }

    public void BindOutput(StatId stat, float* column, int stride = 1, int columnLength = -1)
    {
        InvalidateUndo();
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

        if (columnLength >= 0 && (long)columnLength < (long)(Capacity - 1) * stride + 1)
        {
            throw new ArgumentException($"sv: bound column of {columnLength} elements cannot cover capacity {Capacity} at stride {stride}");
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
        InvalidateUndo();
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

        if (columnLength >= 0 && (long)columnLength < (long)(Capacity - 1) * stride + 1)
        {
            throw new ArgumentException($"sv: bound column of {columnLength} elements cannot cover capacity {Capacity} at stride {stride}");
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
    /// Declares every external binding suspect (storage moved or slot space renumbered) and parks
    /// their writes: the recorded pointers are dropped in every build, so broadcasts write the
    /// sv-owned column (or nothing) until the host rebinds. Checked builds also throw on the next
    /// stat-bounds use. Rebind to re-establish freshness.
    /// </summary>
    public void InvalidateBindings()
    {
        InvalidateUndo();
        bindingGeneration++;
        for (var stat = 0; stat < statCount; stat++)
        {
            if (outputColumn[stat] != null)
            {
                outputColumn[stat] = null;
                boundGeneration[stat] = -1;
            }
        }

        if (statCount > 0)
        {
            var sourceTotal = Math.Max(sourceOffset[statCount], 1);
            for (var i = 0; i < sourceTotal; i++)
            {
                if (sourceAnchor[i] >= 0)
                {
                    MarkDependentsEverywhere(sourceStat[i]);
                }

                sourceAnchor[i] = -1;
            }
        }
    }

    /// <summary>How many stats have parked bindings awaiting a rebind after <see cref="InvalidateBindings"/>.</summary>
    public int PendingRebind
    {
        get
        {
            var count = 0;
            for (var stat = 0; stat < statCount; stat++)
            {
                if (boundGeneration[stat] == -1)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int BindingGeneration => bindingGeneration;

    internal int BoundGenerationAt(int statIndex) => boundGeneration[statIndex];

    internal int* BoundGenerationPtrAt(int statIndex) => boundGeneration + statIndex;

    internal int OutputStrideAt(int statIndex) => outputStride[statIndex];

    /// <summary>
    /// Sv-owned output column for a stat: allocates on first read (fixed capacity, never moves
    /// after), refilled from the set table, needs no bind. <see cref="Recalculate"/> broadcasts
    /// into it while no external column is bound; a stat nobody reads or binds pays no column.
    /// </summary>
    public float* OutputPtr(StatId stat)
    {
        var index = StatIndex(stat);
        return Materialize(index);
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

        var mirror = arena.AllocAlignedZeroed<int>(Capacity);
        undo?.QuarantineFreshAligned(mirror, Capacity * sizeof(int));
        FillMirrorFromColumns(index, mirror);
        undo?.Note(mirrors + index, sizeof(int*));
        mirrors[index] = mirror;
        return mirror;
    }

    void FillMirrorFromColumns(int index, int* mirror)
    {
        var external = outputColumn[index];
        var stride = outputStride[index];
        for (var word = 0; word < LiveWordCount; word++)
        {
            var bits = live[word];
            while (bits != 0)
            {
                var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                mirror[slot] = Clamp.BoundOf(external == null ? SourceValue(slot, index) : external[slot * stride]);
            }
        }
    }

    float* Materialize(int stat)
    {
        var column = owned[stat];
        if (column != null)
        {
            return column;
        }

        column = arena.AllocAlignedZeroed<float>(Capacity);
        undo?.QuarantineFreshAligned(column, Capacity * sizeof(float));
        if (outputColumn[stat] == null)
        {
            for (var word = 0; word < LiveWordCount; word++)
            {
                var bits = live[word];
                while (bits != 0)
                {
                    var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    if (SetOf(slot, stat) >= 0)
                    {
                        column[slot] = SourceValue(slot, stat);
                    }
                }
            }
        }

        undo?.Note(owned + stat, sizeof(float*));
        owned[stat] = column;
        return column;
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
    long NextInsertionId()
    {
        if (nextInsertionId >= (1L << 40) - 1)
        {
            insertionGeneration++;
            nextInsertionId = 0;
        }

        nextInsertionId++;
        return ((long)insertionGeneration << 40) | nextInsertionId;
    }

    public ModifierHandle AddModifier(int slot, StatId stat, in Modifier modifier, int source, long expiresAtTick = long.MaxValue)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var state = ref Slot(slot);
        var id = NextInsertionId();
        var record = AllocRecord();
        undo?.Note(records + record, sizeof(ModRecord));
        records[record] = new ModRecord { Id = id, Kind = modifier.Kind, Value = modifier.Value, Source = source, Expiry = expiresAtTick, Next = state.Head, Slot = slot, Stat = index, Wheel = -1 };
        undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
        state.Head = record;
        MarkStatDirty(slot, index);
        if (expiresAtTick != long.MaxValue)
        {
            WheelInsert(record, id, expiresAtTick);
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
        if (slotPages[slot >> PageShift] == null)
        {
            return removed;
        }

        ref var state = ref Slot(slot);
        var previous = -1;
        for (var at = state.Head; at >= 0;)
        {
            if (records[at].Source == source)
            {
                var next = records[at].Next;
                MarkStatDirty(slot, records[at].Stat);
                WheelUnlink(at);
                FreeRecord(at);
                if (previous < 0)
                {
                    undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
                    state.Head = next;
                }
                else
                {
                    undo?.Note(&records[previous].Next, sizeof(int));
                    records[previous].Next = next;
                }

                at = next;
                removed++;
                continue;
            }

            previous = at;
            at = records[at].Next;
        }

        return removed;
    }

    public void RemoveModifier(int slot, StatId stat, ModifierHandle handle)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        ref var state = ref Slot(slot);
        var previous = -1;
        for (var at = state.Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Id == handle.Id)
            {
                if (records[at].Stat != index)
                {
                    throw new ArgumentException($"sv: modifier handle {handle.Id} not found");
                }

                var next = records[at].Next;
                WheelUnlink(at);
                FreeRecord(at);
                if (previous < 0)
                {
                    undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
                    state.Head = next;
                }
                else
                {
                    undo?.Note(&records[previous].Next, sizeof(int));
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
        MarkKeyframeDirty(slot);
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

        undo?.Note(mask, statMaskWords * sizeof(uint));
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
            var column = owned[stat];
            if (column != null)
            {
                undo?.Note(column + slot, sizeof(float));
                column[slot] = 0f;
            }

            var mirror = mirrors[stat];
            if (mirror != null)
            {
                undo?.Note(mirror + slot, sizeof(int));
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
                undo?.Note(acquiredTouched + acquiredTouchedCount, sizeof(int));
                acquiredTouched[acquiredTouchedCount++] = index;
            }

            undo?.Note(acquired + index, sizeof(ulong));
            acquired[index] |= 1ul << (slot & 63);
        }
    }

    internal void MarkSlotsAcquired(int* slots, int count)
    {
        var words = LiveWordCount;
        var at = 0;
        while (at < count)
        {
            var word = slots[at] >> 6;
            ulong bits = 0;
            while (at < count && (slots[at] >> 6) == word)
            {
                bits |= 1ul << (slots[at] & 63);
                at++;
            }

            for (var stat = 0; stat < statCount; stat++)
            {
                var index = stat * words + word;
                if (acquired[index] == 0)
                {
                    undo?.Note(acquiredTouched + acquiredTouchedCount, sizeof(int));
                    acquiredTouched[acquiredTouchedCount++] = index;
                }

                undo?.Note(acquired + index, sizeof(ulong));
                acquired[index] |= bits;
            }
        }
    }

    /// <summary>
    /// Spawns <paramref name="count"/> entities copying <paramref name="templateSlot"/>'s resolved
    /// stat vector. Slots come from the same lowest-free-first order as <see cref="Acquire"/> and
    /// are written to <paramref name="slots"/>; every spawned slot shares the template's interned
    /// set tuple (one block refcount add), materialized columns and mirrors are filled with the
    /// template's values directly, and rows are marked in <see cref="AcquiredWords"/> — no pair
    /// resolves at the next <see cref="Recalculate"/>. The template's modifiers are folded into
    /// the shared set, not copied as records: the first re-resolve of a spawned pair (a later
    /// modifier on that entity) recomputes from the entity's own modifiers alone. A template that
    /// has never resolved falls back to plain acquire semantics. Throws at slot exhaustion before
    /// mutating anything.
    /// </summary>
    public void Spawn(int templateSlot, int count, int* slots)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(Board));
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "sv: spawn count must be >= 0");
        }

        if (count == 0)
        {
            return;
        }

        if (slots == null)
        {
            throw new ArgumentNullException(nameof(slots));
        }

        Guard.SlotLive(templateSlot, live, Capacity);
        if (LiveCount + count > Capacity)
        {
            throw new InvalidOperationException("sv: slot exhaustion");
        }

        var template = statCount > 0 ? Slot(templateSlot).Block : -1;
        if (template < 0)
        {
            for (var i = 0; i < count; i++)
            {
                slots[i] = Acquire();
            }

            return;
        }

        var word = freeHint;
        var words = LiveWordCount;
        SlotState* page = null;
        var pageAt = -1;
        for (var i = 0; i < count; i++)
        {
            while (live[word] == ulong.MaxValue)
            {
                word++;
            }

            var bits = ~live[word];
            var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
            undo?.Note(live + word, sizeof(ulong));
            live[word] = ~bits | (1ul << (slot & 63));
            slots[i] = slot;
            MarkKeyframeDirty(slot);
            if (slot >> PageShift != pageAt)
            {
                pageAt = slot >> PageShift;
                var freshPage = slotPages[pageAt] == null;
                page = SlotPage(slotPages, slot);
                if (freshPage)
                {
                    page[slot & (PageSlots - 1)] = new SlotState { Block = template, Head = -1 };
                    continue;
                }
            }

            undo?.Note(page + (slot & (PageSlots - 1)), sizeof(SlotState));
            page[slot & (PageSlots - 1)] = new SlotState { Block = template, Head = -1 };
        }

        if (live[word] == ulong.MaxValue)
        {
            word++;
        }

        freeHint = word;
        LiveCount += count;
        MarkSlotsAcquired(slots, count);
        blocks!.AddRefMany(template, count);
        if ((long)count * statCount > 65536)
        {
            digestPagesDirty = true;
        }
        else
        {
            for (var stat = 0; stat < statCount; stat++)
            {
                var set = blocks!.Set(template, stat);
                if (set < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    DigestKeyAdd(slots[i], stat, set);
                }
            }
        }
        var last = slots[count - 1];
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = outputColumn[stat];
            var own = owned[stat];
            var mirror = mirrors[stat];
            if (column == null && own == null && mirror == null)
            {
                continue;
            }

            var value = table!.Computed(blocks.Set(template, stat));
            var stride = outputStride[stat];
            var bounded = Clamp.BoundOf(value);
            if (column != null)
            {
                Guard.BindingFresh(boundGeneration[stat]);
                Guard.BoundColumnFit(boundLength[stat], stride, last);
            }

            for (var i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (column != null)
                {
                    undo?.Note(column + (nint)slot * stride, sizeof(float));
                    column[(nint)slot * stride] = value;
                }
                else if (own != null)
                {
                    undo?.Note(own + slot, sizeof(float));
                    own[slot] = value;
                }

                if (mirror != null)
                {
                    undo?.Note(mirror + slot, sizeof(int));
                    mirror[slot] = bounded;
                }
            }
        }
    }

    internal void ClearChangedMasks()
    {
        ResetSink();
        for (var stat = 0; stat < statCount; stat++)
        {
            var @base = stat * LiveWordCount;
            var count = changedTouchedCounts[stat];
            for (var i = 0; i < count; i++)
            {
                var word = @base + changedTouched[@base + i];
                undo?.Note(changed + word, sizeof(ulong));
                changed[word] = 0;
            }

            undo?.Note(changedTouchedCounts + stat, sizeof(int));
            changedTouchedCounts[stat] = 0;
        }

        for (var i = 0; i < acquiredTouchedCount; i++)
        {
            undo?.Note(acquired + acquiredTouched[i], sizeof(ulong));
            acquired[acquiredTouched[i]] = 0;
        }

        acquiredTouchedCount = 0;
    }

    internal void ClearSlotStats(int slot)
    {
        var sourceTotal = Math.Max(sourceOffset[statCount], 1);
        for (var i = 0; i < sourceTotal; i++)
        {
            if (sourceAnchor[i] == slot)
            {
                sourceAnchor[i] = -1;
                MarkDependentsEverywhere(sourceStat[i]);
            }
        }

        ref var state = ref Slot(slot);
        for (var at = state.Head; at >= 0;)
        {
            var next = records[at].Next;
            WheelUnlink(at);
            FreeRecord(at);
            at = next;
        }

        undo?.Note(&SlotPtr(slot)->Head, sizeof(int));
        state.Head = -1;
        if (state.Block >= 0)
        {
            var block = state.Block;
            for (var stat = 0; stat < statCount; stat++)
            {
                DigestMove(slot, stat, blocks!.Set(block, stat), -1);
            }

            undo?.Note(&SlotPtr(slot)->Block, sizeof(int));
            state.Block = -1;
            DropBlockRef(block);
        }
    }

    void MarkStatDirty(int slot, int stat)
    {
        MarkKeyframeDirty(slot);
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

        undo?.Note(mask + (stat >> 5), sizeof(uint));
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
            var previous = dirtyTouchedCap;
            dirtyTouchedCap *= 2;
            dirtyTouched = UndoGrow(UndoArrayId.DirtyTouched, dirtyTouched, previous, dirtyTouchedCap);
        }

        undo?.Note(dirtyTouched + dirtyTouchedCount, sizeof(int));
        dirtyTouched[dirtyTouchedCount++] = slot;
    }

    void ClearDirty()
    {
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var mask = dirty + (nint)dirtyTouched[i] * statMaskWords;
            undo?.Note(mask, statMaskWords * sizeof(uint));
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
        undo?.NoteRange(dirtyTouched, dirtyTouchedCount);
        ResolveAnchors();
        SortTouched(dirtyTouched, dirtyTouchedCount);
        for (var i = 0; i < dirtyTouchedCount; i++)
        {
            var slot = dirtyTouched[i];
            var mask = dirty + (nint)slot * statMaskWords;
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                undo?.Note(mask, statMaskWords * sizeof(uint));
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
                undo?.Note(mask + w, sizeof(uint));
                mask[w] = word & (word - 1);
                ResolvePair(slot, stat);
            }
        }

        dirtyTouchedCount = 0;
        ShrinkHighWater();
    }

    void ResolveAnchors()
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            for (var at = sourceOffset[stat]; at < sourceOffset[stat + 1]; at++)
            {
                var anchor = sourceAnchor[at];
                if (anchor < 0 || (live[anchor >> 6] & (1ul << (anchor & 63))) == 0)
                {
                    continue;
                }

                var source = sourceStat[at];
                var mask = dirty + (nint)anchor * statMaskWords;
                if ((mask[source >> 5] & (1u << (source & 31))) == 0)
                {
                    continue;
                }

                undo?.Note(mask + (source >> 5), sizeof(uint));
                mask[source >> 5] &= ~(1u << (source & 31));
                if (!ResolvePair(anchor, source))
                {
                    continue;
                }



                MarkDependentsEverywhere(source);
            }
        }
    }

    void MarkDependentsEverywhere(int source)
    {
        var dependents = dependentsMask + (nint)source * statMaskWords;
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            for (var w = 0; w < statMaskWords; w++)
            {
                var word = dependents[w];
                while (word != 0)
                {
                    var dependent = (w << 5) + System.Numerics.BitOperations.TrailingZeroCount(word);
                    word &= word - 1;
                    MarkStatDirty(slot, dependent);
                }
            }
        }
    }

    bool ResolvePair(int slot, int stat)
    {
        var block = Slot(slot).Block;
        var current = block >= 0 ? blocks!.Set(block, stat) : -1;
        var before = current >= 0 ? table!.Computed(current) : 0f;
        var count = Gather(slot, stat);
        var set = table!.Resolve(stat, scratch, count);
        if (!table.Folded(set))
        {
            var freshContent = table.Content(set, out var freshCount);
            table.SetComputed(set, CanonicalNaN(Fold(statBase[stat], freshContent, freshCount)));
        }

        if (current != set)
        {
            if (block >= 0)
            {
                blocks!.CopyTuple(block, tupleScratch);
            }
            else
            {
                for (var k = 0; k < statCount; k++)
                {
                    tupleScratch[k] = -1;
                }
            }

            tupleScratch[stat] = set;
            var next = InternTuple(tupleScratch);
            for (var k = 0; k < statCount; k++)
            {
                DigestMove(slot, k, k == stat ? current : tupleScratch[k], blocks!.Set(next, k));
            }

            if (block >= 0)
            {
                DropBlockRef(block);
            }

            undo?.Note(&SlotPtr(slot)->Block, sizeof(int));
            Slot(slot).Block = next;
            var dependents = dependentsMask + (nint)stat * statMaskWords;
            var mask = dirty + (nint)slot * statMaskWords;
            undo?.Note(mask, statMaskWords * sizeof(uint));
            for (var k = 0; k < statMaskWords; k++)
            {
                mask[k] |= dependents[k];
            }
        }

        var computed = table.Computed(set);
        BroadcastOne(stat, slot, computed, before);
        return before != computed;
    }

    void ShrinkHighWater()
    {
        if (recordCap > 16 && (long)recordCount * 4 <= (long)recordCap * 3)
        {
            var previous = recordCap;
            var target = recordCount + (recordCount >> 3);
            recordCap = target < 16 ? 16 : target;
            records = UndoGrow(UndoArrayId.Records, records, previous, recordCap);
        }

        if (dirtyTouchedCap > 64 && (long)dirtyTouchedCount * 4 <= (long)dirtyTouchedCap * 3)
        {
            var previous = dirtyTouchedCap;
            var target = dirtyTouchedCount + (dirtyTouchedCount >> 3);
            dirtyTouchedCap = target < 64 ? 64 : target;
            dirtyTouched = UndoGrow(UndoArrayId.DirtyTouched, dirtyTouched, previous, dirtyTouchedCap);
        }

        ShrinkWheel();
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

            if (SetOf(slot, stat) < 0)
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
        var real = RecordCount(slot, stat);
        var count = synthetic + real;
        if (count == 0)
        {
            return 0;
        }

        if (count > scratchCap)
        {
            var previous = scratchCap;
            scratchCap = count * 2;
            scratch = arena.Realloc(scratch, previous, scratchCap);
            scratchIds = arena.Realloc(scratchIds, previous, scratchCap);
        }

        return GatherInto(slot, stat, scratch, scratchIds);
    }

    int RecordCount(int slot, int stat)
    {
        var count = 0;
        for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Stat == stat)
            {
                count++;
            }
        }

        return count;
    }

    int CurveBucket(int at, float value)
    {
        if (value <= curveMin[at])
        {
            return 0;
        }

        if (value >= curveCeiling[at])
        {
            return curveLength[at] - 1;
        }

        return (int)((value - curveMin[at]) / curveStep[at]);
    }

    int GatherInto(int slot, int stat, StatEntry* entries, long* entryIds)
    {
        var synthetic = sourceOffset[stat + 1] - sourceOffset[stat];
        var count = synthetic;
        for (var k = 0; k < synthetic; k++)
        {
            var at = sourceOffset[stat] + k;
            var anchor = sourceAnchor[at];
            var sourceValue = SourceValue(anchor >= 0 ? anchor : slot, sourceStat[at]);
            var dataAt = sourceCurve[at];
            entries[k] = new StatEntry(ModifierKind.Added, dataAt < 0 ? sourceCoefficient[at] * sourceValue : curveData[dataAt + CurveBucket(at, sourceValue)]);
            entryIds[k] = long.MinValue + k;
        }

        for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
        {
            if (records[at].Stat != stat)
            {
                continue;
            }

            entries[count] = new StatEntry(records[at].Kind, records[at].Value);
            entryIds[count] = records[at].Id;
            count++;
        }

        Canonicalize(entries, entryIds, count);
        return count;
    }

    float SourceValue(int slot, int stat)
    {
        var set = SetOf(slot, stat);
        return set >= 0 && table!.Folded(set) ? table.Computed(set) : statBase[stat];
    }

    void BroadcastOne(int stat, int slot, float computed, float before)
    {
        var mirror = mirrors[stat];
        if (mirror != null)
        {
            undo?.Note(mirror + slot, sizeof(int));
            mirror[slot] = Clamp.BoundOf(computed);
        }

        var column = outputColumn[stat];
        if (column == null)
        {
            var own = owned[stat];
            if (own == null)
            {
                MarkChanged(stat, slot, before, computed);
                return;
            }

            MarkChanged(stat, slot, own[slot], computed);
            undo?.Note(own + slot, sizeof(float));
            own[slot] = computed;
            return;
        }

        Guard.BindingFresh(boundGeneration[stat]);
        var stride = outputStride[stat];
        Guard.BoundColumnFit(boundLength[stat], stride, slot);
        MarkChanged(stat, slot, column[(nint)slot * stride], computed);
        undo?.Note(column + (nint)slot * stride, sizeof(float));
        column[(nint)slot * stride] = computed;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void MarkChanged(int stat, int slot, float before, float after)
    {
        if (BitConverter.SingleToUInt32Bits(before) == BitConverter.SingleToUInt32Bits(after))
        {
            return;
        }

        var word = slot >> 6;
        var @base = stat * LiveWordCount;
        if (changed[@base + word] == 0)
        {
            undo?.Note(changedTouched + @base + changedTouchedCounts[stat], sizeof(int));
            changedTouched[@base + changedTouchedCounts[stat]] = word;
            undo?.Note(changedTouchedCounts + stat, sizeof(int));
            changedTouchedCounts[stat]++;
        }

        undo?.Note(changed + @base + word, sizeof(ulong));
        changed[@base + word] |= 1ul << (slot & 63);
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
    /// Sparse twin of <see cref="ChangedWords(StatId)"/>: the indices of that stat's nonzero changed
    /// words, <see cref="ChangedWordCount(StatId)"/> of them, each at most once in arrival order.
    /// A consumer walks O(changed words) instead of <see cref="LiveWordCount"/>; values are the same
    /// bits the dense mask holds, valid until the next <see cref="Recalculate"/>.
    /// </summary>
    public int* ChangedWordIndices(StatId stat)
    {
        return changedTouched + (nint)StatIndex(stat) * LiveWordCount;
    }

    /// <summary>
    /// Count of <see cref="ChangedWordIndices(StatId)"/> entries for the stat.
    /// </summary>
    public int ChangedWordCount(StatId stat)
    {
        return changedTouchedCounts[StatIndex(stat)];
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

    static void Canonicalize(StatEntry* entries, long* ids, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var entry = entries[i];
            var id = ids[i];
            var bits = BitConverter.SingleToUInt32Bits(entry.Value);
            var j = i - 1;
            while (j >= 0 && (entries[j].Kind > entry.Kind || (entries[j].Kind == entry.Kind && BitConverter.SingleToUInt32Bits(entries[j].Value) > bits)))
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

        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Cap)
            {
                value = value > entries[i].Value ? entries[i].Value : value;
            }
        }

        return value;
    }

    public float Stat(int slot, StatId stat)
    {
        var index = StatIndex(stat);
        Guard.SlotLive(slot, live, Capacity);
        var synthetic = sourceOffset[index + 1] - sourceOffset[index];
        var need = synthetic + RecordCount(slot, index);
        if (statScratchCap < need)
        {
            var previous = statScratchCap;
            statScratchCap = need * 2;
            statScratch = arena.Realloc(statScratch, previous, statScratchCap);
            statScratchIds = arena.Realloc(statScratchIds, previous, statScratchCap);
        }

        var count = GatherInto(slot, index, statScratch, statScratchIds);
        return CanonicalNaN(Fold(statBase[index], statScratch, count));
    }

    /// <summary>
    /// Compacts the slot space: live slots map through <paramref name="oldToNew"/> (negative entries
    /// are dead slots) to distinct targets in [0, capacity) — gaps are legal (64-alignment padding).
    /// Rewrites pair states and sv-owned columns; external bindings are invalidated per the
    /// clause-12 doctrine. Pending dirty state is remapped through <paramref name="oldToNew"/> —
    /// clean pairs stay clean (set ids are slot-independent, modifier records are pool-global) —
    /// so the next <see cref="Recalculate"/> resolves only the pairs that were already dirty.
    /// Modifier insertion ids ride along, so stat folds are preserved bit-exactly.
    /// </summary>
    public void RenumberSlots(ReadOnlySpan<int> oldToNew, int count)
    {
        InvalidateUndo();
        MarkAllKeyframesDirty();
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
        var newLive = arena.Alloc<ulong>(words);
        NativeMemory.Clear(newLive, (nuint)words * sizeof(ulong));
        var seen = arena.Alloc<int>(Capacity);
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
                arena.Free(newLive, words);
                arena.Free(seen, Capacity);
                throw new ArgumentException("sv: renumber map does not map live slots to distinct targets");
            }

            seen[next] = slot;
            newLive[next >> 6] |= 1ul << (next & 63);
        }

        if (statCount > 0)
        {
            RenumberStats(oldToNew, newLive);
            RemapDirty(oldToNew);
        }

        new ReadOnlySpan<ulong>(newLive, words).CopyTo(new Span<ulong>(live, words));
        arena.Free(newLive, words);
        var hint = 0;
        while (hint < words && live[hint] == ulong.MaxValue)
        {
            hint++;
        }

        freeHint = hint;
        arena.Free(seen, Capacity);

        if (statCount > 0)
        {
            ClearChangedMasks();
        }

        InvalidateBindings();
    }

    void VisitStats<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
    {
        var words = LiveWordCount;
        codec.Value(ref nextInsertionId);
        codec.Value(ref insertionGeneration);
        var pending = dirtyTouchedCount;
        codec.Value(ref pending);
        if (TCodec.Reading)
        {
            if (pending < 0 || pending > Capacity)
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            if (pending > dirtyTouchedCap)
            {
                var previous = dirtyTouchedCap;
                dirtyTouchedCap = pending;
                dirtyTouched = arena.Realloc(dirtyTouched, previous, dirtyTouchedCap);
            }

            ClearDirty();
            dirtyTouchedCount = pending;
        }

        for (var i = 0; i < pending; i++)
        {
            var slot = TCodec.Reading ? 0 : dirtyTouched[i];
            codec.Value(ref slot);
            if (TCodec.Reading)
            {
                if (slot < 0 || slot >= Capacity)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt board state");
                }

                dirtyTouched[i] = slot;
            }

            codec.Bytes(dirty + (nint)slot * statMaskWords, statMaskWords * sizeof(uint));
        }

        codec.Bytes(changedTouchedCounts, (long)statCount * sizeof(int));
        for (var stat = 0; stat < statCount; stat++)
        {
            var touched = changedTouchedCounts[stat];
            if (TCodec.Reading && (touched < 0 || touched > words))
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            codec.Bytes(changedTouched + (nint)stat * words, (long)touched * sizeof(int));
        }

        codec.Bytes(changed, (long)statCount * words * sizeof(ulong));
        var acquiredCount = acquiredTouchedCount;
        codec.Value(ref acquiredCount);
        if (TCodec.Reading)
        {
            acquiredTouchedCount = acquiredCount;
        }

        codec.Bytes(acquiredTouched, (long)acquiredCount * sizeof(int));
        codec.Bytes(acquired, (long)statCount * words * sizeof(ulong));
        var staleColumns = TCodec.Reading ? arena.Alloc<int>(statCount) : null;
        var staleCount = 0;
        for (var stat = 0; stat < statCount; stat++)
        {
            var hasOwned = (byte)(owned[stat] != null ? 1 : 0);
            codec.Value(ref hasOwned);
            if (hasOwned != 0)
            {
                if (TCodec.Reading && owned[stat] == null)
                {
                    owned[stat] = arena.AllocAlignedZeroed<float>(Capacity);
                }

                codec.Bytes(owned[stat], (long)Capacity * sizeof(float));
            }
            else if (TCodec.Reading && owned[stat] != null)
            {
                staleColumns![staleCount++] = stat;
            }
        }

        var staleMirrors = TCodec.Reading ? arena.Alloc<int>(statCount) : null;
        var staleMirrorCount = 0;
        for (var stat = 0; stat < statCount; stat++)
        {
            var hasMirror = (byte)(mirrors[stat] != null ? 1 : 0);
            codec.Value(ref hasMirror);
            if (hasMirror == 0)
            {
                if (TCodec.Reading && mirrors[stat] != null && outputColumn[stat] == null)
                {
                    staleMirrors![staleMirrorCount++] = stat;
                }

                continue;
            }

            if (TCodec.Reading && mirrors[stat] == null)
            {
                mirrors[stat] = arena.AllocAlignedZeroed<int>(Capacity);
            }

            codec.Bytes(mirrors[stat], (long)Capacity * sizeof(int));
        }

        var poolCount = recordCount;
        codec.Value(ref poolCount);
        if (TCodec.Reading)
        {
            if (poolCount < 0 || poolCount > (1 << 24))
            {
                throw new System.IO.InvalidDataException("sv: corrupt board state");
            }

            if (poolCount > recordCap)
            {
                var previous = recordCap;
                recordCap = poolCount;
                records = arena.Realloc(records, previous, recordCap);
            }

            recordCount = poolCount;
        }

        codec.Bytes(records, (long)poolCount * sizeof(ModRecord));
        codec.Value(ref recordFree);
        if (TCodec.Reading && (recordFree < -1 || recordFree >= poolCount))
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        for (var page = 0; page < pageCount; page++)
        {
            var states = slotPages[page];
            var hasPage = (byte)(states == null ? 0 : 1);
            codec.Value(ref hasPage);
            if (hasPage == 0)
            {
                if (TCodec.Reading && slotPages[page] != null)
                {
                    arena.Free(slotPages[page], PageSlots);
                    slotPages[page] = null;
                }

                continue;
            }

            if (TCodec.Reading && slotPages[page] == null)
            {
                slotPages[page] = arena.Alloc<SlotState>(PageSlots);
                for (var i = 0; i < PageSlots; i++)
                {
                    slotPages[page][i] = new SlotState { Block = -1, Head = -1 };
                }
            }

            codec.Bytes(slotPages[page], (long)PageSlots * sizeof(SlotState));
        }

        blocks!.VisitState(ref codec);
        table!.VisitState(ref codec);
        if (TCodec.Reading)
        {
            for (var i = 0; i < staleCount; i++)
            {
                var stat = staleColumns![i];
                NativeMemory.Clear(owned[stat], (nuint)Capacity * sizeof(float));
                if (outputColumn[stat] != null)
                {
                    continue;
                }

                for (var word = 0; word < LiveWordCount; word++)
                {
                    var bits = live[word];
                    while (bits != 0)
                    {
                        var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        if (SetOf(slot, stat) >= 0)
                        {
                            owned[stat][slot] = SourceValue(slot, stat);
                        }
                    }
                }
            }

            for (var i = 0; i < staleMirrorCount; i++)
            {
                var stat = staleMirrors![i];
                NativeMemory.Clear(mirrors[stat], (nuint)Capacity * sizeof(int));
                FillMirrorFromColumns(stat, mirrors[stat]);
            }

            arena.Free(staleColumns!, statCount);
            arena.Free(staleMirrors!, statCount);
            RefillRestoredBindings();
            wheelTick = 0;
            RebuildWheel();
            RecomputeDigest();
        }
    }

    void RefillRestoredBindings()
    {
        for (var stat = 0; stat < statCount; stat++)
        {
            RefillColumn(stat);
        }
    }

    void RemapDirty(ReadOnlySpan<int> oldToNew)
    {
        var total = dirtyTouchedCount;
        if (total == 0)
        {
            return;
        }

        var masks = arena.Alloc<uint>(total * statMaskWords);
        for (var i = 0; i < total; i++)
        {
            var source = dirty + (nint)dirtyTouched[i] * statMaskWords;
            new ReadOnlySpan<uint>(source, statMaskWords).CopyTo(new Span<uint>(masks + (nint)i * statMaskWords, statMaskWords));
            NativeMemory.Clear(source, (nuint)statMaskWords * sizeof(uint));
        }

        var count = 0;
        for (var i = 0; i < total; i++)
        {
            var slot = dirtyTouched[i];
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            dirtyTouched[count++] = next;
            new ReadOnlySpan<uint>(masks + (nint)i * statMaskWords, statMaskWords).CopyTo(new Span<uint>(dirty + (nint)next * statMaskWords, statMaskWords));
        }

        dirtyTouchedCount = count;
        arena.Free(masks, total * statMaskWords);
    }

    void RenumberStats(ReadOnlySpan<int> oldToNew, ulong* newLive)
    {
        EnsureDigestCurrent();
        var movedCap = LiveCount * 2;
        var moved = LiveCount > 0 ? arena.Alloc<int>(movedCap) : null;
        var movedCount = 0;
        for (var slot = 0; slot < Capacity; slot++)
        {
            if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                continue;
            }

            var next = oldToNew[slot];
            if (next != slot)
            {
                moved![movedCount * 2] = slot;
                moved[movedCount * 2 + 1] = next;
                movedCount++;
            }
        }

        if (movedCount == 0)
        {
            if (moved != null)
            {
                arena.Free(moved, movedCap);
            }

            return;
        }

        var touchedWords = pageCount + 63 >> 6;
        var touched = arena.AllocZeroed<ulong>(touchedWords);
        for (var i = 0; i < movedCount; i++)
        {
            touched[moved![i * 2] >> PageShift >> 6] |= 1ul << ((moved[i * 2] >> PageShift) & 63);
            touched[moved[i * 2 + 1] >> PageShift >> 6] |= 1ul << ((moved[i * 2 + 1] >> PageShift) & 63);
        }

        var nextPages = (SlotState**)arena.AllocZeroedBytes(pageCount * sizeof(SlotState*));
        for (var page = 0; page < pageCount; page++)
        {
            var old = slotPages[page];
            if (old == null)
            {
                continue;
            }

            var bit = 1ul << (page & 63);
            if ((touched[page >> 6] & bit) == 0)
            {
                nextPages[page] = old;
            }
            else
            {
                var copy = arena.Alloc<SlotState>(PageSlots);
                new ReadOnlySpan<SlotState>(old, PageSlots).CopyTo(new Span<SlotState>(copy, PageSlots));
                nextPages[page] = copy;
            }
        }

        for (var i = 0; i < movedCount; i++)
        {
            var slot = moved![i * 2];
            var next = moved[i * 2 + 1];
            var block = Slot(slot).Block;
            SlotPage(nextPages, next)[next & (PageSlots - 1)] = Slot(slot);
            for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
            {
                records[at].Slot = next;
            }

            if (block < 0)
            {
                continue;
            }

            for (var stat = 0; stat < statCount; stat++)
            {
                var set = blocks!.Set(block, stat);
                if (set < 0)
                {
                    continue;
                }

                var bits = BitConverter.SingleToUInt32Bits(table!.Computed(set));
                var oldKey = DigestKey(stat, slot, bits);
                var newKey = DigestKey(stat, next, bits);
                digest += newKey - oldKey;
                pageDigests[(nint)stat * PageCount + (slot >> 8)] -= oldKey;
                pageDigests[(nint)stat * PageCount + (next >> 8)] += newKey;
            }
        }

        var clearWords = LiveWordCount;
        var clearTail = Capacity & 63;
        for (var word = 0; word < clearWords; word++)
        {
            var bits = ~newLive[word];
            if (word == clearWords - 1 && clearTail != 0)
            {
                bits &= (1ul << clearTail) - 1;
            }

            while (bits != 0)
            {
                var deadSlot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                if (nextPages[deadSlot >> PageShift] != null)
                {
                    nextPages[deadSlot >> PageShift][deadSlot & (PageSlots - 1)] = new SlotState { Block = -1, Head = -1 };
                }
            }
        }

        for (var page = 0; page < pageCount; page++)
        {
            if (slotPages[page] != null && slotPages[page] != nextPages[page])
            {
                arena.Free(slotPages[page], PageSlots);
            }
        }

        arena.FreeBytes(slotPages, pageCount * sizeof(SlotState*));
        slotPages = nextPages;
        arena.Free(touched, touchedWords);

        var deadCount = Capacity - LiveCount;
        var dead = deadCount > 0 ? arena.Alloc<int>(deadCount) : null;
        var found = 0;
        var maskWords = LiveWordCount;
        var tail = Capacity & 63;
        for (var word = 0; word < maskWords; word++)
        {
            var bits = ~newLive[word];
            if (word == maskWords - 1 && tail != 0)
            {
                bits &= (1ul << tail) - 1;
            }

            while (bits != 0)
            {
                var slot = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                dead![found++] = slot;
            }
        }

        var tempRows = arena.Alloc<float>(Capacity);
        for (var stat = 0; stat < statCount; stat++)
        {
            var column = owned[stat];
            if (column == null)
            {
                continue;
            }

            NativeMemory.Copy(column, tempRows, (nuint)Capacity * sizeof(float));
            for (var j = 0; j < movedCount; j++)
            {
                tempRows[moved![j * 2 + 1]] = column[moved[j * 2]];
            }

            for (var j = 0; j < found; j++)
            {
                tempRows[dead![j]] = 0f;
            }

            new ReadOnlySpan<float>(tempRows, Capacity).CopyTo(new Span<float>(column, Capacity));
            var mirror = mirrors[stat];
            if (mirror == null)
            {
                continue;
            }

            var mirrorRows = (int*)tempRows;
            NativeMemory.Copy(mirror, mirrorRows, (nuint)Capacity * sizeof(int));
            for (var j = 0; j < movedCount; j++)
            {
                mirrorRows[moved![j * 2 + 1]] = mirror[moved[j * 2]];
            }

            for (var j = 0; j < found; j++)
            {
                mirrorRows[dead![j]] = 0;
            }

            new ReadOnlySpan<int>(mirrorRows, Capacity).CopyTo(new Span<int>(mirror, Capacity));
        }

        arena.Free(tempRows, Capacity);
        if (dead != null)
        {
            arena.Free(dead, deadCount);
        }

        arena.Free(moved!, movedCap);
    }
}
