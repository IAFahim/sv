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

    public void BindOutput(StatId stat, float* column, int stride = 1)
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

        outputColumn[index] = column;
        outputStride[index] = stride;
    }

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
            if (column == null)
            {
                continue;
            }

            var stride = outputStride[stat];
            var members = table.Members(dirty, out var memberCount);
            for (var i = 0; i < memberCount; i++)
            {
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
}
