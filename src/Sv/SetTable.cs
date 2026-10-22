using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv;

internal readonly struct StatEntry
{
    public readonly ModifierKind Kind;

    public readonly float Value;

    public StatEntry(ModifierKind kind, float value)
    {
        Kind = kind;
        Value = value;
    }
}

internal sealed unsafe class SetTable : IDisposable
{
    private const int ClassCount = 32;

    private const long ReclaimHorizon = 0;

    private int setCount;

    private int setCapacity;

    private int bucketMask;

    private int* buckets;

    private int* setStat;

    private ulong* setHash;

    private float* setComputed;

    private int* setContentOffset;

    private int* setContentCount;

    private int* setContentSpan;

    private int* setRefCount;

    private sbyte* setFolded;

    private int* setFreeNext;

    private long* setFreedAt;

    private int setFreeHead;

    private int liveSets;

    private long generation;

    private StatEntry* contentArena;

    private int contentUsed;

    private int contentCapacity;

    private int* contentClassHead;

    private bool disposed;

    public int LiveSetCount => liveSets;

    internal long MemoryBytes
    {
        get
        {
            var bytes = (long)(bucketMask + 1) * sizeof(int)
                + (long)setCapacity * (sizeof(int) * 7 + sizeof(ulong) + sizeof(long) + sizeof(sbyte))
                + sizeof(StatEntry) * (long)contentCapacity
                + ClassCount * sizeof(int);
            return bytes;
        }
    }

    public SetTable()
    {
        setCapacity = 16;
        bucketMask = 31;
        buckets = AllocZeroed<int>(32);
        setStat = Alloc<int>(setCapacity);
        setHash = Alloc<ulong>(setCapacity);
        setComputed = Alloc<float>(setCapacity);
        setContentOffset = Alloc<int>(setCapacity);
        setContentCount = Alloc<int>(setCapacity);
        setContentSpan = Alloc<int>(setCapacity);
        setRefCount = AllocZeroed<int>(setCapacity);
        setFolded = AllocZeroed<sbyte>(setCapacity);
        setFreeNext = AllocZeroed<int>(setCapacity);
        setFreedAt = AllocZeroed<long>(setCapacity);
        contentArena = Alloc<StatEntry>(64);
        contentCapacity = 64;
        contentClassHead = Alloc<int>(ClassCount);
        for (var i = 0; i < ClassCount; i++)
        {
            contentClassHead[i] = -1;
        }

        setFreeHead = -1;
    }

    static T* Alloc<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T));
    }

    static T* AllocZeroed<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.AllocZeroed((nuint)count * (nuint)sizeof(T));
    }

    static T* Realloc<T>(T* old, int count) where T : unmanaged
    {
        return (T*)NativeMemory.Realloc(old, (nuint)count * (nuint)sizeof(T));
    }

    static int ClassSize(int count)
    {
        var n = count < 1 ? 1 : count;
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        return n + 1;
    }

    static ulong ContentHash(int stat, StatEntry* entries, int count)
    {
        unchecked
        {
            var hash = 14695981039346656037ul;
            void Mix(ulong value)
            {
                hash ^= value;
                hash *= 1099511628211ul;
            }

            Mix((ulong)stat);
            for (var i = 0; i < count; i++)
            {
                Mix((ulong)entries[i].Kind | ((ulong)BitConverter.SingleToUInt32Bits(entries[i].Value) << 8));
            }

            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDul;
            hash ^= hash >> 33;
            hash *= 0xC4CEB9FE1A85EC53ul;
            hash ^= hash >> 33;
            return hash;
        }
    }

    internal ulong HashOf(int stat, StatEntry* entries, int count) => ContentHash(stat, entries, count);

    internal int BucketCount => bucketMask + 1;

    public void Tick()
    {
        generation++;
    }

    public int Resolve(int stat, StatEntry* canonical, int count)
    {
        var hash = ContentHash(stat, canonical, count);
        var bucket = (int)(hash & (ulong)bucketMask);
        for (var probe = 0; probe <= bucketMask; probe++)
        {
            var slot = (bucket + probe) & bucketMask;
            var existing = buckets[slot];
            if (existing == 0)
            {
                if ((liveSets + 1) * 2 > bucketMask + 1)
                {
                    GrowBuckets();
                    return Resolve(stat, canonical, count);
                }

                var set = CreateSet(stat, canonical, count, hash);
                buckets[slot] = set + 1;
                return set;
            }

            var index = existing - 1;
            if (setHash[index] == hash && setStat[index] == stat && setContentCount[index] == count &&
                ContentEquals(setContentOffset[index], canonical, count))
            {
                return index;
            }
        }

        GrowBuckets();
        return Resolve(stat, canonical, count);
    }

    bool ContentEquals(int offset, StatEntry* canonical, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (contentArena[offset + i].Kind != canonical[i].Kind ||
                BitConverter.SingleToUInt32Bits(contentArena[offset + i].Value) != BitConverter.SingleToUInt32Bits(canonical[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    int CreateSet(int stat, StatEntry* canonical, int count, ulong hash)
    {
        if (setCount == setCapacity)
        {
            GrowSetCapacity();
        }

        int index;
        if (setFreeHead >= 0 && generation - setFreedAt[setFreeHead] > ReclaimHorizon)
        {
            index = setFreeHead;
            setFreeHead = setFreeNext[index];
            liveSets++;
        }
        else
        {
            index = setCount++;
            liveSets++;
        }

        setStat[index] = stat;
        setHash[index] = hash;
        var span = ClassSize(count);
        int offset;
        var cls = BitOperations.TrailingZeroCount(span);
        if (contentClassHead[cls] >= 0)
        {
            offset = contentClassHead[cls];
            contentClassHead[cls] = *(int*)(contentArena + offset);
        }
        else
        {
            while (contentUsed + span > contentCapacity)
            {
                contentCapacity *= 2;
                contentArena = Realloc(contentArena, contentCapacity);
            }

            offset = contentUsed;
            contentUsed += span;
        }

        setContentOffset[index] = offset;
        setContentSpan[index] = span;
        setContentCount[index] = count;
        for (var i = 0; i < count; i++)
        {
            contentArena[offset + i] = canonical[i];
        }

        NativeMemory.Clear(contentArena + offset + count, (nuint)(span - count) * (nuint)sizeof(StatEntry));
        setRefCount[index] = 0;
        setComputed[index] = 0f;
        setFolded[index] = 0;
        setFreedAt[index] = 0;
        setFreeNext[index] = 0;
        return index;
    }

    void GrowSetCapacity()
    {
        var previous = setCapacity;
        setCapacity *= 2;
        setStat = Realloc(setStat, setCapacity);
        setHash = Realloc(setHash, setCapacity);
        setComputed = Realloc(setComputed, setCapacity);
        setContentOffset = Realloc(setContentOffset, setCapacity);
        setContentCount = Realloc(setContentCount, setCapacity);
        setContentSpan = Realloc(setContentSpan, setCapacity);
        setRefCount = Realloc(setRefCount, setCapacity);
        setFolded = Realloc(setFolded, setCapacity);
        setFreeNext = Realloc(setFreeNext, setCapacity);
        setFreedAt = Realloc(setFreedAt, setCapacity);
        NativeMemory.Clear(setFreedAt + previous, (nuint)(setCapacity - previous) * (nuint)sizeof(long));
    }

    public void AddRef(int set)
    {
        setRefCount[set]++;
    }

    public void ReleaseRef(int set)
    {
        var count = --setRefCount[set];
        if (count == 0)
        {
            FreeSet(set);
        }
    }

    void FreeSet(int set)
    {
        var span = setContentSpan[set];
        var cls = BitOperations.TrailingZeroCount(span);
        var node = (int*)(contentArena + setContentOffset[set]);
        *node = contentClassHead[cls];
        contentClassHead[cls] = setContentOffset[set];
        setRefCount[set] = 0;
        setComputed[set] = 0f;
        setFolded[set] = 0;
        DeleteFromBuckets(set);
        setFreedAt[set] = generation;
        setFreeNext[set] = setFreeHead;
        setFreeHead = set;
        liveSets--;
    }

    void DeleteFromBuckets(int set)
    {
        var hole = (int)(setHash[set] & (ulong)bucketMask);
        while (buckets[hole] != set + 1)
        {
            hole = (hole + 1) & bucketMask;
        }

        while (true)
        {
            var j = (hole + 1) & bucketMask;
            while (buckets[j] != 0)
            {
                var home = (int)(setHash[buckets[j] - 1] & (ulong)bucketMask);
                if (((j - home) & bucketMask) >= ((j - hole) & bucketMask))
                {
                    break;
                }

                j = (j + 1) & bucketMask;
            }

            if (buckets[j] == 0)
            {
                buckets[hole] = 0;
                return;
            }

            buckets[hole] = buckets[j];
            hole = j;
        }
    }

    void GrowBuckets()
    {
        bucketMask = bucketMask * 2 + 1;
        buckets = Realloc(buckets, bucketMask + 1);
        for (var i = 0; i <= bucketMask; i++)
        {
            buckets[i] = 0;
        }

        for (var set = 0; set < setCount; set++)
        {
            if (setFreedAt[set] != 0)
            {
                continue;
            }

            var slot = (int)(setHash[set] & (ulong)bucketMask);
            while (buckets[slot] != 0)
            {
                slot = (slot + 1) & bucketMask;
            }

            buckets[slot] = set + 1;
        }
    }

    internal int MemberTotal(int stat)
    {
        var total = 0;
        for (var set = 0; set < setCount; set++)
        {
            if (setStat[set] == stat)
            {
                total += setRefCount[set];
            }
        }

        return total;
    }

    public StatEntry* Content(int set, out int count)
    {
        count = setContentCount[set];
        return contentArena + setContentOffset[set];
    }

    public int SetStat(int set) => setStat[set];

    public float Computed(int set) => setComputed[set];

    public void SetComputed(int set, float value)
    {
        setComputed[set] = value;
        setFolded[set] = 1;
    }

    public bool Folded(int set) => setFolded[set] != 0;

    internal void Write(SvState state)
    {
        state.Write(setCount);
        state.Write(setCapacity);
        state.Write(bucketMask);
        state.Write(buckets, (long)(bucketMask + 1) * sizeof(int));
        state.Write(setStat, (long)setCount * sizeof(int));
        state.Write(setHash, (long)setCount * sizeof(ulong));
        state.Write(setComputed, (long)setCount * sizeof(float));
        state.Write(setContentOffset, (long)setCount * sizeof(int));
        state.Write(setContentCount, (long)setCount * sizeof(int));
        state.Write(setContentSpan, (long)setCount * sizeof(int));
        state.Write(setRefCount, (long)setCount * sizeof(int));
        state.Write(setFolded, setCount);
        state.Write(contentUsed);
        state.Write(contentCapacity);
        state.Write(contentArena, (long)contentUsed * sizeof(StatEntry));
        state.Write(contentClassHead, (long)ClassCount * sizeof(int));
        state.Write(liveSets);
        state.Write(setFreeHead);
        state.Write(generation);
        state.Write(setFreeNext, (long)setCount * sizeof(int));
        state.Write(setFreedAt, (long)setCount * sizeof(long));
    }

    internal void Read(SvState state)
    {
        var count = state.Read<int>();
        var capacity = state.Read<int>();
        var mask = state.Read<int>();
        if (count < 0 || capacity < count || mask < 0)
        {
            throw new System.IO.InvalidDataException("sv: corrupt set table state");
        }

        if (capacity != setCapacity)
        {
            var previousCapacity = setCapacity;
            setStat = Realloc(setStat, capacity);
            setHash = Realloc(setHash, capacity);
            setComputed = Realloc(setComputed, capacity);
            setContentOffset = Realloc(setContentOffset, capacity);
            setContentCount = Realloc(setContentCount, capacity);
            setContentSpan = Realloc(setContentSpan, capacity);
            setRefCount = Realloc(setRefCount, capacity);
            setFolded = Realloc(setFolded, capacity);
            setFreeNext = Realloc(setFreeNext, capacity);
            setFreedAt = Realloc(setFreedAt, capacity);
            if (capacity > previousCapacity)
            {
                NativeMemory.Clear(setFreedAt + previousCapacity, (nuint)(capacity - previousCapacity) * (nuint)sizeof(long));
            }
        }

        setCount = count;
        setCapacity = capacity;
        if (mask != bucketMask)
        {
            buckets = Realloc(buckets, mask + 1);
            bucketMask = mask;
        }

        state.Read(buckets, (long)(bucketMask + 1) * sizeof(int));
        state.Read(setStat, (long)count * sizeof(int));
        state.Read(setHash, (long)count * sizeof(ulong));
        state.Read(setComputed, (long)count * sizeof(float));
        state.Read(setContentOffset, (long)count * sizeof(int));
        state.Read(setContentCount, (long)count * sizeof(int));
        state.Read(setContentSpan, (long)count * sizeof(int));
        state.Read(setRefCount, (long)count * sizeof(int));
        state.Read(setFolded, count);
        contentUsed = state.Read<int>();
        var arenaCapacity = state.Read<int>();
        if (arenaCapacity != contentCapacity)
        {
            contentArena = Realloc(contentArena, arenaCapacity);
            contentCapacity = arenaCapacity;
        }

        state.Read(contentArena, (long)contentUsed * sizeof(StatEntry));
        state.Read(contentClassHead, (long)ClassCount * sizeof(int));
        liveSets = state.Read<int>();
        setFreeHead = state.Read<int>();
        generation = state.Read<long>();
        state.Read(setFreeNext, (long)count * sizeof(int));
        state.Read(setFreedAt, (long)count * sizeof(long));
        if (liveSets < 0 || liveSets > count || setFreeHead < -1 || setFreeHead >= count)
        {
            throw new System.IO.InvalidDataException("sv: corrupt set table state");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        NativeMemory.Free(buckets);
        NativeMemory.Free(setStat);
        NativeMemory.Free(setHash);
        NativeMemory.Free(setComputed);
        NativeMemory.Free(setContentOffset);
        NativeMemory.Free(setContentCount);
        NativeMemory.Free(setContentSpan);
        NativeMemory.Free(setRefCount);
        NativeMemory.Free(setFolded);
        NativeMemory.Free(setFreeNext);
        NativeMemory.Free(setFreedAt);
        NativeMemory.Free(contentArena);
        NativeMemory.Free(contentClassHead);
        disposed = true;
    }
}
