using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public enum CommandKind : int
    {
        Spawn = 1,
        Kill = 2,
    }

    [StructLayout(LayoutKind.Sequential, Size = 12)]
    public readonly struct StructuralCommand
    {
        public readonly CommandKind Kind;
        public readonly int Template;
        public readonly int Slot;

        public StructuralCommand(CommandKind kind, int template, int slot)
        {
            Kind = kind;
            Template = template;
            Slot = slot;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public readonly struct LoggedRecord
    {
        public readonly ulong Key;
        public readonly int Amount;
        public readonly int Hits;

        public LoggedRecord(ulong key, int amount, int hits)
        {
            Key = key;
            Amount = amount;
            Hits = hits;
        }
    }

    public static class LoggedRecordLaw
    {
        public const int ChunkShift = 48;
        public const int OriginShift = 2;

        public static int Chunk(ulong key) => (int)(key >> ChunkShift);

        public static bool External(ulong key) => ((key >> OriginShift) & 3ul) < 2ul;

        public static int Compare(in LoggedRecord first, in LoggedRecord second) =>
            first.Key < second.Key ? -1 : first.Key > second.Key ? 1 : first.Amount < second.Amount ? -1 : first.Amount > second.Amount ? 1 : 0;

        public static int SaturatedHits(int first, int second)
        {
            var sum = (long)first + second;
            return sum > int.MaxValue ? int.MaxValue : sum < int.MinValue ? int.MinValue : (int)sum;
        }
    }

    public sealed unsafe class InputLog : IDisposable
    {
        public const int BlockRecords = 256;
        public const int BlocksPerPage = 64;

        struct Segment
        {
            public int Chunk;
            public int Head;
        }

        struct FrameSlot
        {
            public long Frame;
            public Segment* Segments;
            public int SegmentCount;
            public int SegmentCapacity;
            public StructuralCommand* Commands;
            public int CommandCount;
            public int CommandCapacity;
            public byte* StatOps;
            public int StatOpCount;
            public int StatOpCapacity;
        }

        readonly int span;
        readonly int chunkCapacity;
        readonly int statOpBytes;
        FrameSlot* frames;
        LoggedRecord** pages;
        int pageCount;
        int pageCapacity;
        int* next;
        int* used;
        int* refs;
        int* total;
        ulong* hash;
        int* freeBlocks;
        int freeCount;
        int* lastHead;
        LoggedRecord* scratch;
        int scratchCapacity;
        Segment* merged;
        int mergedCapacity;

        public InputLog(int frameSpan, int chunkCapacity, int statOpBytes)
        {
            if (frameSpan < 1 || chunkCapacity < 1 || chunkCapacity > Slot.MaxChunks || statOpBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameSpan));
            }

            span = frameSpan;
            this.chunkCapacity = chunkCapacity;
            this.statOpBytes = statOpBytes;
            frames = (FrameSlot*)Memory.AllocZeroed((nuint)span, (nuint)sizeof(FrameSlot));
            for (var at = 0; at < span; at++)
            {
                frames[at].Frame = -1L;
            }

            lastHead = (int*)Memory.Alloc((nuint)chunkCapacity, sizeof(int));
            for (var chunk = 0; chunk < chunkCapacity; chunk++)
            {
                lastHead[chunk] = -1;
            }
        }

        public static int SpanOf(World world) => world.RingEpochs * world.FramesPerEpoch + world.FramesPerEpoch;

        public int Span => span;

        public int StatOpBytes => statOpBytes;

        public long Bytes => (long)pageCount * BlocksPerPage * BlockRecords * sizeof(LoggedRecord);

        public bool Holds(long frame) => frame >= 0L && SlotOf(frame)->Frame == frame;

        public int RecordCount(long frame)
        {
            if (!Holds(frame))
            {
                return 0;
            }

            var slot = SlotOf(frame);
            var count = 0;
            for (var at = 0; at < slot->SegmentCount; at++)
            {
                count += total[slot->Segments[at].Head];
            }

            return count;
        }

        public int CopyRecords(long frame, LoggedRecord* destination, int capacity)
        {
            var count = RecordCount(frame);
            if (count == 0 || destination == null || capacity < count)
            {
                return count == 0 ? 0 : -count;
            }

            var slot = SlotOf(frame);
            var at = 0;
            for (var segment = 0; segment < slot->SegmentCount; segment++)
            {
                at += CopyChain(slot->Segments[segment].Head, destination + at);
            }

            return at;
        }

        public StructuralCommand* Commands(long frame, out int count)
        {
            if (!Holds(frame))
            {
                count = 0;
                return null;
            }

            var slot = SlotOf(frame);
            count = slot->CommandCount;
            return slot->Commands;
        }

        public byte* StatOps(long frame, out int count)
        {
            if (!Holds(frame))
            {
                count = 0;
                return null;
            }

            var slot = SlotOf(frame);
            count = slot->StatOpCount;
            return slot->StatOps;
        }

        public void Spawn(long frame, int template, int slot) => Command(frame, new StructuralCommand(CommandKind.Spawn, template, slot));

        public void Kill(long frame, int slot) => Command(frame, new StructuralCommand(CommandKind.Kill, -1, slot));

        void Command(long frame, StructuralCommand command)
        {
            var slot = Enter(frame);
            Ensure(ref slot->Commands, ref slot->CommandCapacity, slot->CommandCount + 1);
            slot->Commands[slot->CommandCount] = command;
            slot->CommandCount++;
        }

        public void AppendStatOps(long frame, byte* canonicalOps, int count)
        {
            if (count <= 0 || canonicalOps == null || statOpBytes == 0)
            {
                return;
            }

            var slot = Enter(frame);
            var needed = (slot->StatOpCount + count) * statOpBytes;
            if (needed > slot->StatOpCapacity)
            {
                var grown = Math.Max(needed, slot->StatOpCapacity * 2);
                slot->StatOps = (byte*)Memory.Realloc(slot->StatOps, (nuint)grown);
                slot->StatOpCapacity = grown;
            }

            Memory.Copy(canonicalOps, slot->StatOps + slot->StatOpCount * statOpBytes, (nuint)(count * statOpBytes));
            slot->StatOpCount += count;
        }

        public void Record(long frame, LoggedRecord* canonical, int count)
        {
            var slot = Enter(frame);
            ReleaseSegments(slot);
            var at = 0;
            while (at < count)
            {
                var chunk = LoggedRecordLaw.Chunk(canonical[at].Key);
                var external = 0;
                EnsureScratch(count - at);
                while (at < count && LoggedRecordLaw.Chunk(canonical[at].Key) == chunk)
                {
                    scratch[external] = canonical[at];
                    external += LoggedRecordLaw.External(canonical[at].Key) ? 1 : 0;
                    at++;
                }

                if (external > 0 && chunk < chunkCapacity)
                {
                    AddSegment(slot, chunk, Intern(chunk, scratch, external));
                }
            }
        }

        public void Insert(long frame, LoggedRecord* lateCanonical, int count)
        {
            var slot = Enter(frame);
            var existing = 0;
            var late = 0;
            var produced = 0;
            EnsureMerged(slot->SegmentCount + count);
            while (existing < slot->SegmentCount || late < count)
            {
                var segmentChunk = existing < slot->SegmentCount ? slot->Segments[existing].Chunk : int.MaxValue;
                var lateChunk = late < count ? LoggedRecordLaw.Chunk(lateCanonical[late].Key) : int.MaxValue;
                var chunk = Math.Min(segmentChunk, lateChunk);
                var lateEnd = late;
                while (lateEnd < count && LoggedRecordLaw.Chunk(lateCanonical[lateEnd].Key) == chunk)
                {
                    lateEnd++;
                }

                if (lateEnd == late)
                {
                    merged[produced] = slot->Segments[existing];
                    produced++;
                    existing++;
                    continue;
                }

                var oldHead = segmentChunk == chunk ? slot->Segments[existing].Head : -1;
                var oldCount = oldHead >= 0 ? total[oldHead] : 0;
                EnsureScratch(oldCount * 2 + (lateEnd - late) * 2);
                var old = scratch + oldCount + (lateEnd - late);
                if (oldHead >= 0)
                {
                    CopyChain(oldHead, old);
                }

                var length = Merge(old, oldCount, lateCanonical + late, lateEnd - late, scratch);
                if (oldHead >= 0)
                {
                    Release(oldHead);
                    existing++;
                }

                late = lateEnd;
                if (length > 0 && chunk < chunkCapacity)
                {
                    merged[produced].Chunk = chunk;
                    merged[produced].Head = Intern(chunk, scratch, length);
                    produced++;
                }
            }

            slot->SegmentCount = 0;
            Ensure(ref slot->Segments, ref slot->SegmentCapacity, produced);
            Memory.Copy(merged, slot->Segments, (nuint)produced * (nuint)sizeof(Segment));
            slot->SegmentCount = produced;
        }

        public void Truncate(long after)
        {
            for (var at = 0; at < span; at++)
            {
                if (frames[at].Frame > after)
                {
                    Clear(frames + at);
                }
            }
        }

        static int Merge(LoggedRecord* old, int oldCount, LoggedRecord* late, int lateCount, LoggedRecord* output)
        {
            var written = 0;
            var first = 0;
            var second = 0;
            while (first < oldCount || second < lateCount)
            {
                LoggedRecord next;
                if (second == lateCount || (first < oldCount && LoggedRecordLaw.Compare(old[first], late[second]) <= 0))
                {
                    next = old[first];
                    first++;
                }
                else
                {
                    next = late[second];
                    second++;
                    if (!LoggedRecordLaw.External(next.Key) || next.Amount == 0)
                    {
                        continue;
                    }
                }

                if (written > 0 && output[written - 1].Key == next.Key && output[written - 1].Amount == next.Amount)
                {
                    output[written - 1] = new LoggedRecord(next.Key, next.Amount, LoggedRecordLaw.SaturatedHits(output[written - 1].Hits, next.Hits));
                    continue;
                }

                output[written] = next;
                written++;
            }

            return written;
        }

        int Intern(int chunk, LoggedRecord* records, int count)
        {
            var digest = XxHash64.Hash((byte*)records, count * sizeof(LoggedRecord));
            var previous = lastHead[chunk];
            int head;
            if (previous >= 0 && total[previous] == count && hash[previous] == digest && SameContent(previous, records, count))
            {
                head = previous;
                refs[head]++;
            }
            else
            {
                head = Store(records, count, digest);
                if (previous >= 0)
                {
                    Release(previous);
                }

                refs[head] = 2;
                lastHead[chunk] = head;
            }

            return head;
        }

        int Store(LoggedRecord* records, int count, ulong digest)
        {
            var head = -1;
            var tail = -1;
            var at = 0;
            while (at < count)
            {
                var block = TakeBlock();
                var here = Math.Min(BlockRecords, count - at);
                Memory.Copy(records + at, BlockAt(block), (nuint)here * (nuint)sizeof(LoggedRecord));
                used[block] = here;
                next[block] = -1;
                if (tail >= 0)
                {
                    next[tail] = block;
                }
                else
                {
                    head = block;
                }

                tail = block;
                at += here;
            }

            refs[head] = 0;
            total[head] = count;
            hash[head] = digest;
            return head;
        }

        bool SameContent(int head, LoggedRecord* records, int count)
        {
            var at = 0;
            for (var block = head; block >= 0; block = next[block])
            {
                var cells = BlockAt(block);
                for (var row = 0; row < used[block]; row++)
                {
                    if (at >= count || cells[row].Key != records[at].Key || cells[row].Amount != records[at].Amount || cells[row].Hits != records[at].Hits)
                    {
                        return false;
                    }

                    at++;
                }
            }

            return at == count;
        }

        int CopyChain(int head, LoggedRecord* destination)
        {
            var at = 0;
            for (var block = head; block >= 0; block = next[block])
            {
                Memory.Copy(BlockAt(block), destination + at, (nuint)used[block] * (nuint)sizeof(LoggedRecord));
                at += used[block];
            }

            return at;
        }

        void Release(int head)
        {
            refs[head]--;
            if (refs[head] > 0)
            {
                return;
            }

            var block = head;
            while (block >= 0)
            {
                var following = next[block];
                freeBlocks[freeCount] = block;
                freeCount++;
                block = following;
            }
        }

        int TakeBlock()
        {
            if (freeCount == 0)
            {
                AddPage();
            }

            freeCount--;
            return freeBlocks[freeCount];
        }

        void AddPage()
        {
            if (pageCount == pageCapacity)
            {
                pageCapacity = Math.Max(4, pageCapacity * 2);
                pages = (LoggedRecord**)Memory.Realloc(pages, (nuint)pageCapacity * (nuint)sizeof(LoggedRecord*));
            }

            pages[pageCount] = (LoggedRecord*)Memory.AllocAligned((nuint)BlocksPerPage * BlockRecords * (nuint)sizeof(LoggedRecord), 64);
            var first = pageCount * BlocksPerPage;
            pageCount++;
            var capacity = pageCount * BlocksPerPage;
            next = (int*)Memory.Realloc(next, (nuint)capacity * sizeof(int));
            used = (int*)Memory.Realloc(used, (nuint)capacity * sizeof(int));
            refs = (int*)Memory.Realloc(refs, (nuint)capacity * sizeof(int));
            total = (int*)Memory.Realloc(total, (nuint)capacity * sizeof(int));
            hash = (ulong*)Memory.Realloc(hash, (nuint)capacity * sizeof(ulong));
            freeBlocks = (int*)Memory.Realloc(freeBlocks, (nuint)capacity * sizeof(int));
            for (var block = capacity - 1; block >= first; block--)
            {
                freeBlocks[freeCount] = block;
                freeCount++;
            }
        }

        LoggedRecord* BlockAt(int block) => pages[block / BlocksPerPage] + (block % BlocksPerPage) * BlockRecords;

        FrameSlot* SlotOf(long frame) => frames + (int)(frame % span);

        FrameSlot* Enter(long frame)
        {
            if (frame < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }

            var slot = SlotOf(frame);
            if (slot->Frame != frame)
            {
                Clear(slot);
                slot->Frame = frame;
            }

            return slot;
        }

        void Clear(FrameSlot* slot)
        {
            ReleaseSegments(slot);
            slot->CommandCount = 0;
            slot->StatOpCount = 0;
            slot->Frame = -1L;
        }

        void ReleaseSegments(FrameSlot* slot)
        {
            for (var at = 0; at < slot->SegmentCount; at++)
            {
                Release(slot->Segments[at].Head);
            }

            slot->SegmentCount = 0;
        }

        void AddSegment(FrameSlot* slot, int chunk, int head)
        {
            Ensure(ref slot->Segments, ref slot->SegmentCapacity, slot->SegmentCount + 1);
            slot->Segments[slot->SegmentCount].Chunk = chunk;
            slot->Segments[slot->SegmentCount].Head = head;
            slot->SegmentCount++;
        }

        void EnsureScratch(int count)
        {
            Ensure(ref scratch, ref scratchCapacity, count);
        }

        void EnsureMerged(int count)
        {
            Ensure(ref merged, ref mergedCapacity, count);
        }

        static void Ensure<T>(ref T* buffer, ref int capacity, int needed) where T : unmanaged
        {
            if (needed <= capacity)
            {
                return;
            }

            var grown = Math.Max(needed, Math.Max(16, capacity * 2));
            buffer = (T*)Memory.Realloc(buffer, (nuint)grown * (nuint)sizeof(T));
            capacity = grown;
        }

        public void Dispose()
        {
            if (frames == null)
            {
                return;
            }

            for (var at = 0; at < span; at++)
            {
                Memory.Free(frames[at].Segments);
                Memory.Free(frames[at].Commands);
                Memory.Free(frames[at].StatOps);
            }

            for (var page = 0; page < pageCount; page++)
            {
                Memory.FreeAligned(pages[page]);
            }

            Memory.Free(frames);
            Memory.Free(pages);
            Memory.Free(next);
            Memory.Free(used);
            Memory.Free(refs);
            Memory.Free(total);
            Memory.Free(hash);
            Memory.Free(freeBlocks);
            Memory.Free(lastHead);
            Memory.Free(scratch);
            Memory.Free(merged);
            frames = null;
            pages = null;
            pageCount = 0;
        }
    }
}
