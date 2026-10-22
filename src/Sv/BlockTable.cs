using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv;

internal sealed unsafe class BlockTable : IDisposable
{
    private readonly int statCount;

    private readonly NativeArena memory;

    private int blockCount;

    private int blockCapacity;

    private int bucketMask;

    private int* buckets;

    private int* blockSets;

    private ulong* blockHash;

    private int* blockRefCount;

    private int* blockFreeNext;

    private int blockFreeHead;

    private bool disposed;

    public BlockTable(NativeArena memory, int statCount)
    {
        this.memory = memory;
        this.statCount = statCount;
        blockCapacity = 16;
        bucketMask = 31;
        buckets = memory.AllocZeroed<int>(32);
        blockSets = memory.Alloc<int>(blockCapacity * statCount);
        blockHash = memory.Alloc<ulong>(blockCapacity);
        blockRefCount = memory.AllocZeroed<int>(blockCapacity);
        blockFreeNext = memory.AllocZeroed<int>(blockCapacity);
        blockFreeHead = -1;
    }

    static ulong TupleHash(int* tuple, int statCount)
    {
        unchecked
        {
            var hash = 14695981039346656037ul;
            for (var i = 0; i < statCount; i++)
            {
                hash ^= (ulong)tuple[i];
                hash *= 1099511628211ul;
            }

            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDul;
            hash ^= hash >> 33;
            hash *= 0xC4CEB9FE1A85EC53ul;
            hash ^= hash >> 33;
            return hash;
        }
    }

    static bool TupleEquals(int* left, int* right, int statCount)
    {
        for (var i = 0; i < statCount; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    public int Lookup(int* tuple)
    {
        var hash = TupleHash(tuple, statCount);
        var bucket = (int)(hash & (ulong)bucketMask);
        for (var probe = 0; probe <= bucketMask; probe++)
        {
            var slot = (bucket + probe) & bucketMask;
            var existing = buckets[slot];
            if (existing == 0)
            {
                return -1;
            }

            var index = existing - 1;
            if (blockHash[index] == hash && TupleEquals(blockSets + (nint)index * statCount, tuple, statCount))
            {
                return index;
            }
        }

        return -1;
    }

    public int Create(int* tuple)
    {
        if (blockCount == blockCapacity)
        {
            GrowBlockCapacity();
        }

        int index;
        if (blockFreeHead >= 0)
        {
            index = blockFreeHead;
            blockFreeHead = blockFreeNext[index];
        }
        else
        {
            index = blockCount++;
        }

        var hash = TupleHash(tuple, statCount);
        blockHash[index] = hash;
        var sets = blockSets + (nint)index * statCount;
        for (var i = 0; i < statCount; i++)
        {
            sets[i] = tuple[i];
        }

        blockRefCount[index] = 0;
        blockFreeNext[index] = 0;
        if ((blockCount + 1) * 2 > bucketMask + 1)
        {
            GrowBuckets();
        }

        var slot = (int)(hash & (ulong)bucketMask);
        while (buckets[slot] != 0)
        {
            slot = (slot + 1) & bucketMask;
        }

        buckets[slot] = index + 1;
        return index;
    }

    void GrowBlockCapacity()
    {
        var previous = blockCapacity;
        var previousSets = blockCapacity * statCount;
        blockCapacity *= 2;
        blockSets = memory.Realloc(blockSets, previousSets, blockCapacity * statCount);
        blockHash = memory.Realloc(blockHash, previous, blockCapacity);
        blockRefCount = memory.Realloc(blockRefCount, previous, blockCapacity);
        blockFreeNext = memory.Realloc(blockFreeNext, previous, blockCapacity);
        NativeMemory.Clear(blockRefCount + previous, (nuint)(blockCapacity - previous) * (nuint)sizeof(int));
        NativeMemory.Clear(blockFreeNext + previous, (nuint)(blockCapacity - previous) * (nuint)sizeof(int));
    }

    void GrowBuckets()
    {
        var previousBuckets = bucketMask + 1;
        bucketMask = bucketMask * 2 + 1;
        buckets = memory.Realloc(buckets, previousBuckets, bucketMask + 1);
        for (var i = 0; i <= bucketMask; i++)
        {
            buckets[i] = 0;
        }

        for (var block = 0; block < blockCount; block++)
        {
            if (blockRefCount[block] == 0)
            {
                continue;
            }

            var slot = (int)(blockHash[block] & (ulong)bucketMask);
            while (buckets[slot] != 0)
            {
                slot = (slot + 1) & bucketMask;
            }

            buckets[slot] = block + 1;
        }
    }

    public int Set(int block, int stat)
    {
        return blockSets[(nint)block * statCount + stat];
    }

    public void CopyTuple(int block, int* tuple)
    {
        var sets = blockSets + (nint)block * statCount;
        for (var i = 0; i < statCount; i++)
        {
            tuple[i] = sets[i];
        }
    }

    public int RefCount(int block) => blockRefCount[block];

    public void SetRefCount(int block, int count)
    {
        blockRefCount[block] = count;
    }

    public void AddRefMany(int block, int count)
    {
        blockRefCount[block] += count;
    }

    public bool ReleaseRef(int block)
    {
        return --blockRefCount[block] == 0;
    }

    public void Free(int block)
    {
        DeleteFromBuckets(block);
        blockRefCount[block] = 0;
        blockFreeNext[block] = blockFreeHead;
        blockFreeHead = block;
    }

    void DeleteFromBuckets(int block)
    {
        var hole = (int)(blockHash[block] & (ulong)bucketMask);
        while (buckets[hole] != block + 1)
        {
            hole = (hole + 1) & bucketMask;
        }

        while (true)
        {
            var j = (hole + 1) & bucketMask;
            while (buckets[j] != 0)
            {
                var home = (int)(blockHash[buckets[j] - 1] & (ulong)bucketMask);
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

    internal void Write(SvState state)
    {
        state.Write(blockCount);
        state.Write(blockCapacity);
        state.Write(bucketMask);
        state.Write(buckets, (long)(bucketMask + 1) * sizeof(int));
        state.Write(blockSets, (long)blockCount * statCount * sizeof(int));
        state.Write(blockHash, (long)blockCount * sizeof(ulong));
        state.Write(blockRefCount, (long)blockCount * sizeof(int));
        state.Write(blockFreeHead);
        state.Write(blockFreeNext, (long)blockCount * sizeof(int));
    }

    internal void Read(SvState state)
    {
        var count = state.Read<int>();
        var capacity = state.Read<int>();
        var mask = state.Read<int>();
        if (count < 0 || count > capacity || capacity > (1 << 24) || (mask & (mask + 1)) != 0)
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }

        if (capacity > blockCapacity)
        {
            var previous = blockCapacity;
            var previousSets = blockCapacity * statCount;
            blockCapacity = capacity;
            blockSets = memory.Realloc(blockSets, previousSets, blockCapacity * statCount);
            blockHash = memory.Realloc(blockHash, previous, blockCapacity);
            blockRefCount = memory.Realloc(blockRefCount, previous, blockCapacity);
            blockFreeNext = memory.Realloc(blockFreeNext, previous, blockCapacity);
        }

        if (mask != bucketMask)
        {
            var previousBuckets = bucketMask + 1;
            bucketMask = mask;
            buckets = memory.Realloc(buckets, previousBuckets, bucketMask + 1);
        }

        blockCount = count;
        state.Read(buckets, (long)(bucketMask + 1) * sizeof(int));
        state.Read(blockSets, (long)blockCount * statCount * sizeof(int));
        state.Read(blockHash, (long)blockCount * sizeof(ulong));
        state.Read(blockRefCount, (long)blockCount * sizeof(int));
        blockFreeHead = state.Read<int>();
        state.Read(blockFreeNext, (long)blockCount * sizeof(int));
        if (blockFreeHead < -1 || blockFreeHead >= blockCount)
        {
            throw new System.IO.InvalidDataException("sv: corrupt board state");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        memory.Free(buckets, bucketMask + 1);
        memory.Free(blockSets, blockCapacity * statCount);
        memory.Free(blockHash, blockCapacity);
        memory.Free(blockRefCount, blockCapacity);
        memory.Free(blockFreeNext, blockCapacity);
        disposed = true;
    }
}
