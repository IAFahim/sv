using System;
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
    private int setCount;

    private int setCapacity;

    private int bucketMask;

    private int* buckets;

    private int* setStat;

    private ulong* setHash;

    private float* setComputed;

    private int* setContentOffset;

    private int* setContentCount;

    private int* setMemberCount;

    private int* setMemberCap;

    private int** setMembers;

    private sbyte* setDirty;

    private sbyte* setFolded;

    private StatEntry* contentArena;

    private int contentUsed;

    private int contentCapacity;

    private int* dirtyQueue;

    private int dirtyHead;

    private int dirtyTail;

    private int dirtyCapacity;

    private bool disposed;

    public int LiveSetCount => setCount;

    internal long MemoryBytes
    {
        get
        {
            var bytes = (long)(bucketMask + 1) * sizeof(int)
                + (long)setCapacity * (sizeof(int) * 6 + sizeof(ulong) * 2 + sizeof(int*) + sizeof(sbyte) * 2)
                + sizeof(StatEntry) * (long)contentCapacity
                + sizeof(int) * (long)dirtyCapacity;
            for (var set = 0; set < setCount; set++)
            {
                bytes += setMemberCap[set] * sizeof(int);
            }

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
        setMemberCount = Alloc<int>(setCapacity);
        setMemberCap = Alloc<int>(setCapacity);
        setMembers = (int**)NativeMemory.Alloc((nuint)setCapacity * (nuint)sizeof(int*));
        setDirty = AllocZeroed<sbyte>(setCapacity);
        setFolded = AllocZeroed<sbyte>(setCapacity);
        contentArena = Alloc<StatEntry>(64);
        contentCapacity = 64;
        dirtyQueue = Alloc<int>(64);
        dirtyCapacity = 64;
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

            return hash;
        }
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
            setCapacity *= 2;
            setStat = Realloc(setStat, setCapacity);
            setHash = Realloc(setHash, setCapacity);
            setComputed = Realloc(setComputed, setCapacity);
            setContentOffset = Realloc(setContentOffset, setCapacity);
            setContentCount = Realloc(setContentCount, setCapacity);
            setMemberCount = Realloc(setMemberCount, setCapacity);
            setMemberCap = Realloc(setMemberCap, setCapacity);
            setMembers = (int**)NativeMemory.Realloc(setMembers, (nuint)setCapacity * (nuint)sizeof(int*));
            setDirty = Realloc(setDirty, setCapacity);
            setFolded = Realloc(setFolded, setCapacity);
        }

        while (contentUsed + count > contentCapacity)
        {
            contentCapacity *= 2;
            contentArena = Realloc(contentArena, contentCapacity);
        }

        var index = setCount++;
        setStat[index] = stat;
        setHash[index] = hash;
        setContentOffset[index] = contentUsed;
        setContentCount[index] = count;
        for (var i = 0; i < count; i++)
        {
            contentArena[contentUsed + i] = canonical[i];
        }

        contentUsed += count;
        setMemberCount[index] = 0;
        setMemberCap[index] = 2;
        setMembers[index] = Alloc<int>(2);
        setComputed[index] = 0f;
        setDirty[index] = 0;
        setFolded[index] = 0;
        return index;
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
            var slot = (int)(setHash[set] & (ulong)bucketMask);
            while (buckets[slot] != 0)
            {
                slot = (slot + 1) & bucketMask;
            }

            buckets[slot] = set + 1;
        }
    }

    public int AddMember(int set, int slot)
    {
        var count = setMemberCount[set];
        if (count == setMemberCap[set])
        {
            setMemberCap[set] *= 2;
            setMembers[set] = Realloc(setMembers[set], setMemberCap[set]);
        }

        setMembers[set][count] = slot;
        setMemberCount[set] = count + 1;
        return count;
    }

    public int RemoveMemberAt(int set, int position)
    {
        var members = setMembers[set];
        var last = setMemberCount[set] - 1;
        var moved = position == last ? -1 : members[last];
        members[position] = members[last];
        setMemberCount[set] = last;
        return moved;
    }

    public void RenumberMembers(ReadOnlySpan<int> oldToNew)
    {
        for (var set = 0; set < setCount; set++)
        {
            var members = setMembers[set];
            var count = setMemberCount[set];
            for (var i = 0; i < count; i++)
            {
                members[i] = oldToNew[members[i]];
            }
        }
    }

    internal int MemberTotal(int stat)
    {
        var total = 0;
        for (var set = 0; set < setCount; set++)
        {
            if (setStat[set] == stat)
            {
                total += setMemberCount[set];
            }
        }

        return total;
    }

    public int* Members(int set, out int count)
    {
        count = setMemberCount[set];
        return setMembers[set];
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

    public void MarkStatDirty(int stat)
    {
        for (var set = 0; set < setCount; set++)
        {
            if (setStat[set] == stat && setMemberCount[set] > 0)
            {
                MarkDirty(set);
            }
        }
    }

    public void MarkDirty(int set)
    {
        if (setDirty[set] != 0)
        {
            return;
        }

        if (dirtyTail == dirtyCapacity)
        {
            dirtyCapacity *= 2;
            dirtyQueue = Realloc(dirtyQueue, dirtyCapacity);
        }

        setDirty[set] = 1;
        dirtyQueue[dirtyTail++] = set;
    }

    public bool TryDequeueDirty(out int set)
    {
        if (dirtyHead == dirtyTail)
        {
            dirtyTail = 0;
            dirtyHead = 0;
            set = -1;
            return false;
        }

        set = dirtyQueue[dirtyHead++];
        if (dirtyHead == dirtyTail)
        {
            dirtyTail = 0;
            dirtyHead = 0;
        }

        setDirty[set] = 0;
        return true;
    }

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
        state.Write(setMemberCount, (long)setCount * sizeof(int));
        state.Write(setMemberCap, (long)setCount * sizeof(int));
        state.Write(setDirty, setCount);
        state.Write(setFolded, setCount);
        for (var set = 0; set < setCount; set++)
        {
            state.Write(setMembers[set], (long)setMemberCount[set] * sizeof(int));
        }

        state.Write(contentUsed);
        state.Write(contentCapacity);
        state.Write(contentArena, (long)contentUsed * sizeof(StatEntry));
        state.Write(dirtyHead);
        state.Write(dirtyTail);
        state.Write(dirtyCapacity);
        state.Write(dirtyQueue, (long)dirtyTail * sizeof(int));
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

        for (var set = count; set < setCount; set++)
        {
            NativeMemory.Free(setMembers[set]);
        }

        if (capacity != setCapacity)
        {
            setStat = Realloc(setStat, capacity);
            setHash = Realloc(setHash, capacity);
            setComputed = Realloc(setComputed, capacity);
            setContentOffset = Realloc(setContentOffset, capacity);
            setContentCount = Realloc(setContentCount, capacity);
            setMemberCount = Realloc(setMemberCount, capacity);
            setMemberCap = Realloc(setMemberCap, capacity);
            setMembers = (int**)NativeMemory.Realloc(setMembers, (nuint)capacity * (nuint)sizeof(int*));
            setDirty = Realloc(setDirty, capacity);
            setFolded = Realloc(setFolded, capacity);
        }

        var previous = setCount;
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
        state.Read(setMemberCount, (long)count * sizeof(int));
        var restoredCaps = (int*)NativeMemory.Alloc((nuint)Math.Max(count, 1) * sizeof(int));
        state.Read(restoredCaps, (long)count * sizeof(int));
        for (var set = 0; set < count; set++)
        {
            if (set >= previous)
            {
                setMembers[set] = Alloc<int>(restoredCaps[set]);
            }
            else if (setMemberCap[set] != restoredCaps[set])
            {
                setMembers[set] = Realloc(setMembers[set], restoredCaps[set]);
            }

            setMemberCap[set] = restoredCaps[set];
        }

        NativeMemory.Free(restoredCaps);
        state.Read(setDirty, count);
        state.Read(setFolded, count);
        for (var set = 0; set < count; set++)
        {
            state.Read(setMembers[set], (long)setMemberCount[set] * sizeof(int));
        }

        contentUsed = state.Read<int>();
        var arenaCapacity = state.Read<int>();
        if (arenaCapacity != contentCapacity)
        {
            contentArena = Realloc(contentArena, arenaCapacity);
            contentCapacity = arenaCapacity;
        }

        state.Read(contentArena, (long)contentUsed * sizeof(StatEntry));
        dirtyHead = state.Read<int>();
        dirtyTail = state.Read<int>();
        var queueCapacity = state.Read<int>();
        if (queueCapacity != dirtyCapacity)
        {
            dirtyQueue = Realloc(dirtyQueue, queueCapacity);
            dirtyCapacity = queueCapacity;
        }

        state.Read(dirtyQueue, (long)dirtyTail * sizeof(int));
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
        NativeMemory.Free(setMemberCount);
        NativeMemory.Free(setDirty);
        NativeMemory.Free(setFolded);
        NativeMemory.Free(contentArena);
        NativeMemory.Free(dirtyQueue);
        for (var set = 0; set < setCount; set++)
        {
            if (setMembers[set] != null)
            {
                NativeMemory.Free(setMembers[set]);
            }
        }

        NativeMemory.Free(setMemberCap);
        NativeMemory.Free(setMembers);
        disposed = true;
    }
}
