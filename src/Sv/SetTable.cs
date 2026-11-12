using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv
{
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

        internal long ReclaimHorizon;

        internal UndoLog? Undo;

        private readonly NativeArena memory;

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

        public SetTable(NativeArena memory)
        {
            this.memory = memory;
            setCapacity = 16;
            bucketMask = 31;
            buckets = memory.AllocZeroed<int>(32);
            setStat = memory.Alloc<int>(setCapacity);
            setHash = memory.Alloc<ulong>(setCapacity);
            setComputed = memory.Alloc<float>(setCapacity);
            setContentOffset = memory.Alloc<int>(setCapacity);
            setContentCount = memory.Alloc<int>(setCapacity);
            setContentSpan = memory.Alloc<int>(setCapacity);
            setRefCount = memory.AllocZeroed<int>(setCapacity);
            setFolded = memory.AllocZeroed<sbyte>(setCapacity);
            setFreeNext = memory.AllocZeroed<int>(setCapacity);
            setFreedAt = memory.AllocZeroed<long>(setCapacity);
            contentArena = memory.Alloc<StatEntry>(64);
            contentCapacity = 64;
            contentClassHead = memory.Alloc<int>(ClassCount);
            for (var i = 0; i < ClassCount; i++)
            {
                contentClassHead[i] = -1;
            }

            setFreeHead = -1;
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
                    Undo?.Note(buckets + slot, sizeof(int));
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

            var undo = Undo;
            undo?.Note(setStat + index, sizeof(int));
            setStat[index] = stat;
            undo?.Note(setHash + index, sizeof(ulong));
            setHash[index] = hash;
            var span = ClassSize(count);
            int offset;
            var cls = BitOperations.TrailingZeroCount(span);
            if (contentClassHead[cls] >= 0)
            {
                offset = contentClassHead[cls];
                undo?.Note(contentClassHead + cls, sizeof(int));
                contentClassHead[cls] = *(int*)(contentArena + offset);
            }
            else
            {
                while (contentUsed + span > contentCapacity)
                {
                    var previousContent = contentCapacity;
                    contentCapacity *= 2;
                    contentArena = GrowContent(contentArena, previousContent, contentCapacity);
                }

                offset = contentUsed;
                contentUsed += span;
            }

            undo?.Note(setContentOffset + index, sizeof(int));
            setContentOffset[index] = offset;
            undo?.Note(setContentSpan + index, sizeof(int));
            setContentSpan[index] = span;
            undo?.Note(setContentCount + index, sizeof(int));
            setContentCount[index] = count;
            undo?.NoteRange(contentArena + offset, count);
            for (var i = 0; i < count; i++)
            {
                contentArena[offset + i] = canonical[i];
            }

            undo?.Note(contentArena + offset + count, (span - count) * sizeof(StatEntry));
            NativeMemory.Clear(contentArena + offset + count, (nuint)(span - count) * (nuint)sizeof(StatEntry));
            undo?.Note(setRefCount + index, sizeof(int));
            setRefCount[index] = 0;
            undo?.Note(setComputed + index, sizeof(float));
            setComputed[index] = 0f;
            undo?.Note(setFolded + index, sizeof(sbyte));
            setFolded[index] = 0;
            undo?.Note(setFreedAt + index, sizeof(long));
            setFreedAt[index] = 0;
            undo?.Note(setFreeNext + index, sizeof(int));
            setFreeNext[index] = 0;
            return index;
        }

        StatEntry* GrowContent(StatEntry* old, int oldCount, int newCount)
        {
            return Undo == null ? memory.Realloc(old, oldCount, newCount) : Undo.ReallocTracked(memory, UndoArrayId.ContentArena, old, oldCount, newCount);
        }

        void GrowSetCapacity()
        {
            var previous = setCapacity;
            setCapacity *= 2;
            setStat = GrowArray(UndoArrayId.SetStat, setStat, previous, setCapacity);
            setHash = GrowArray(UndoArrayId.SetHash, setHash, previous, setCapacity);
            setComputed = GrowArray(UndoArrayId.SetComputed, setComputed, previous, setCapacity);
            setContentOffset = GrowArray(UndoArrayId.SetContentOffset, setContentOffset, previous, setCapacity);
            setContentCount = GrowArray(UndoArrayId.SetContentCount, setContentCount, previous, setCapacity);
            setContentSpan = GrowArray(UndoArrayId.SetContentSpan, setContentSpan, previous, setCapacity);
            setRefCount = GrowArray(UndoArrayId.SetRefCount, setRefCount, previous, setCapacity);
            setFolded = GrowArray(UndoArrayId.SetFolded, setFolded, previous, setCapacity);
            setFreeNext = GrowArray(UndoArrayId.SetFreeNext, setFreeNext, previous, setCapacity);
            setFreedAt = GrowArray(UndoArrayId.SetFreedAt, setFreedAt, previous, setCapacity);
            NativeMemory.Clear(setFreedAt + previous, (nuint)(setCapacity - previous) * (nuint)sizeof(long));
        }

        T* GrowArray<T>(UndoArrayId id, T* old, int oldCount, int newCount) where T : unmanaged
        {
            return Undo == null ? memory.Realloc(old, oldCount, newCount) : Undo.ReallocTracked(memory, id, old, oldCount, newCount);
        }

        public void AddRef(int set)
        {
            Undo?.Note(setRefCount + set, sizeof(int));
            setRefCount[set]++;
        }

        public void ReleaseRef(int set)
        {
            Undo?.Note(setRefCount + set, sizeof(int));
            var count = --setRefCount[set];
            if (count == 0)
            {
                FreeSet(set);
            }
        }

        void FreeSet(int set)
        {
            var undo = Undo;
            var span = setContentSpan[set];
            var cls = BitOperations.TrailingZeroCount(span);
            var node = (int*)(contentArena + setContentOffset[set]);
            undo?.Note(node, sizeof(int));
            *node = contentClassHead[cls];
            undo?.Note(contentClassHead + cls, sizeof(int));
            contentClassHead[cls] = setContentOffset[set];
            undo?.Note(setRefCount + set, sizeof(int));
            setRefCount[set] = 0;
            undo?.Note(setComputed + set, sizeof(float));
            setComputed[set] = 0f;
            undo?.Note(setFolded + set, sizeof(sbyte));
            setFolded[set] = 0;
            DeleteFromBuckets(set);
            undo?.Note(setFreedAt + set, sizeof(long));
            setFreedAt[set] = generation;
            undo?.Note(setFreeNext + set, sizeof(int));
            setFreeNext[set] = setFreeHead;
            setFreeHead = set;
            liveSets--;
        }

        void DeleteFromBuckets(int set)
        {
            var undo = Undo;
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
                    undo?.Note(buckets + hole, sizeof(int));
                    buckets[hole] = 0;
                    return;
                }

                undo?.Note(buckets + hole, sizeof(int));
                buckets[hole] = buckets[j];
                hole = j;
            }
        }

        void GrowBuckets()
        {
            var previousBuckets = bucketMask + 1;
            bucketMask = bucketMask * 2 + 1;
            buckets = GrowArray(UndoArrayId.SetBuckets, buckets, previousBuckets, bucketMask + 1);
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
            Undo?.Note(setComputed + set, sizeof(float));
            setComputed[set] = value;
            Undo?.Note(setFolded + set, sizeof(sbyte));
            setFolded[set] = 1;
        }

        public bool Folded(int set) => setFolded[set] != 0;

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            var count = setCount;
            var capacity = setCapacity;
            var mask = bucketMask;
            codec.Value(ref count);
            codec.Value(ref capacity);
            codec.Value(ref mask);
            if (TCodec.Reading)
            {
                if (count < 0 || capacity < count || mask < 0)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt set table state");
                }

                if (capacity != setCapacity)
                {
                    var previousCapacity = setCapacity;
                    setStat = memory.Realloc(setStat, previousCapacity, capacity);
                    setHash = memory.Realloc(setHash, previousCapacity, capacity);
                    setComputed = memory.Realloc(setComputed, previousCapacity, capacity);
                    setContentOffset = memory.Realloc(setContentOffset, previousCapacity, capacity);
                    setContentCount = memory.Realloc(setContentCount, previousCapacity, capacity);
                    setContentSpan = memory.Realloc(setContentSpan, previousCapacity, capacity);
                    setRefCount = memory.Realloc(setRefCount, previousCapacity, capacity);
                    setFolded = memory.Realloc(setFolded, previousCapacity, capacity);
                    setFreeNext = memory.Realloc(setFreeNext, previousCapacity, capacity);
                    setFreedAt = memory.Realloc(setFreedAt, previousCapacity, capacity);
                    if (capacity > previousCapacity)
                    {
                        NativeMemory.Clear(setFreedAt + previousCapacity, (nuint)(capacity - previousCapacity) * (nuint)sizeof(long));
                    }
                }

                if (mask != bucketMask)
                {
                    var previousBuckets = bucketMask + 1;
                    buckets = memory.Realloc(buckets, previousBuckets, mask + 1);
                    bucketMask = mask;
                }

                setCount = count;
                setCapacity = capacity;
            }

            codec.Bytes(buckets, (long)(bucketMask + 1) * sizeof(int));
            codec.Bytes(setStat, (long)count * sizeof(int));
            codec.Bytes(setHash, (long)count * sizeof(ulong));
            codec.Bytes(setComputed, (long)count * sizeof(float));
            codec.Bytes(setContentOffset, (long)count * sizeof(int));
            codec.Bytes(setContentCount, (long)count * sizeof(int));
            codec.Bytes(setContentSpan, (long)count * sizeof(int));
            codec.Bytes(setRefCount, (long)count * sizeof(int));
            codec.Bytes(setFolded, count);
            codec.Value(ref contentUsed);
            var arenaCapacity = contentCapacity;
            codec.Value(ref arenaCapacity);
            if (TCodec.Reading && arenaCapacity != contentCapacity)
            {
                contentArena = memory.Realloc(contentArena, contentCapacity, arenaCapacity);
                contentCapacity = arenaCapacity;
            }

            codec.Bytes(contentArena, (long)contentUsed * sizeof(StatEntry));
            codec.Bytes(contentClassHead, (long)ClassCount * sizeof(int));
            codec.Value(ref liveSets);
            codec.Value(ref setFreeHead);
            codec.Value(ref generation);
            codec.Bytes(setFreeNext, (long)count * sizeof(int));
            codec.Bytes(setFreedAt, (long)count * sizeof(long));
            if (TCodec.Reading && (liveSets < 0 || liveSets > count || setFreeHead < -1 || setFreeHead >= count))
            {
                throw new System.IO.InvalidDataException("sv: corrupt set table state");
            }
        }

        internal void CaptureUndo(ref Board.SetUndoFrame frame)
        {
            frame.SetCount = setCount;
            frame.SetCapacity = setCapacity;
            frame.BucketMask = bucketMask;
            frame.SetFreeHead = setFreeHead;
            frame.LiveSets = liveSets;
            frame.ContentUsed = contentUsed;
            frame.ContentCapacity = contentCapacity;
            frame.Generation = generation;
            frame.Buckets = buckets;
            frame.SetStat = setStat;
            frame.SetHash = setHash;
            frame.SetComputed = setComputed;
            frame.SetContentOffset = setContentOffset;
            frame.SetContentCount = setContentCount;
            frame.SetContentSpan = setContentSpan;
            frame.SetRefCount = setRefCount;
            frame.SetFolded = setFolded;
            frame.SetFreeNext = setFreeNext;
            frame.SetFreedAt = setFreedAt;
            frame.ContentArena = contentArena;
        }

        internal void RestoreUndo(ref Board.SetUndoFrame frame)
        {
            setCount = frame.SetCount;
            setCapacity = frame.SetCapacity;
            bucketMask = frame.BucketMask;
            setFreeHead = frame.SetFreeHead;
            liveSets = frame.LiveSets;
            contentUsed = frame.ContentUsed;
            contentCapacity = frame.ContentCapacity;
            generation = frame.Generation;
            buckets = (int*)frame.Buckets;
            setStat = (int*)frame.SetStat;
            setHash = (ulong*)frame.SetHash;
            setComputed = (float*)frame.SetComputed;
            setContentOffset = (int*)frame.SetContentOffset;
            setContentCount = (int*)frame.SetContentCount;
            setContentSpan = (int*)frame.SetContentSpan;
            setRefCount = (int*)frame.SetRefCount;
            setFolded = (sbyte*)frame.SetFolded;
            setFreeNext = (int*)frame.SetFreeNext;
            setFreedAt = (long*)frame.SetFreedAt;
            contentArena = (StatEntry*)frame.ContentArena;
        }

        internal void* UndoPointerOf(UndoArrayId id)
        {
            return id switch
            {
                UndoArrayId.SetBuckets => buckets,
                UndoArrayId.SetStat => setStat,
                UndoArrayId.SetHash => setHash,
                UndoArrayId.SetComputed => setComputed,
                UndoArrayId.SetContentOffset => setContentOffset,
                UndoArrayId.SetContentCount => setContentCount,
                UndoArrayId.SetContentSpan => setContentSpan,
                UndoArrayId.SetRefCount => setRefCount,
                UndoArrayId.SetFolded => setFolded,
                UndoArrayId.SetFreeNext => setFreeNext,
                UndoArrayId.SetFreedAt => setFreedAt,
                UndoArrayId.ContentArena => contentArena,
                _ => null,
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            memory.Free(buckets, bucketMask + 1);
            memory.Free(setStat, setCapacity);
            memory.Free(setHash, setCapacity);
            memory.Free(setComputed, setCapacity);
            memory.Free(setContentOffset, setCapacity);
            memory.Free(setContentCount, setCapacity);
            memory.Free(setContentSpan, setCapacity);
            memory.Free(setRefCount, setCapacity);
            memory.Free(setFolded, setCapacity);
            memory.Free(setFreeNext, setCapacity);
            memory.Free(setFreedAt, setCapacity);
            memory.Free(contentArena, contentCapacity);
            memory.Free(contentClassHead, ClassCount);
            disposed = true;
        }
    }
}
