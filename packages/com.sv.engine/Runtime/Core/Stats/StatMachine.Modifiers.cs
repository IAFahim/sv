using System;

namespace Sv
{
    public sealed unsafe partial class StatMachine
    {
        ModifierRecord* records;
        int recordCount;
        int recordCapacity;
        int recordFree;
        int liveRecords;
        int* heap;
        int heapCount;
        int heapCapacity;
        ulong* dirty;
        int dirtyCount;
        int dirtyCapacity;
        StatOp* opScratch;
        int opScratchCapacity;
        long nextId;
        ulong digest;
        long rejected;

        public int ModifierCount => liveRecords;

        public int PendingExpiries => heapCount;

        public long NextId => nextId;

        public long Rejected => rejected;

        public ulong Digest => digest;

        void InitModifiers()
        {
            recordFree = NoRecord;
            nextId = 0L;
        }

        void FreeModifiers()
        {
            if (records != null)
            {
                Memory.Free(records);
            }

            if (heap != null)
            {
                Memory.Free(heap);
            }

            if (dirty != null)
            {
                Memory.Free(dirty);
            }

            if (opScratch != null)
            {
                Memory.Free(opScratch);
            }
        }

        public void Step(long tick, Span<StatOp> ops)
        {
            opScratch = Grow(opScratch, 0, ref opScratchCapacity, ops.Length);
            fixed (StatOp* at = ops)
            {
                StatOps.Canonicalize(at, ops.Length, opScratch);
                Expire(tick);
                Apply(at, ops.Length);
            }

            Recalculate();
        }

        public void Apply(StatOp* ops, int count)
        {
            for (var at = 0; at < count; at++)
            {
                var op = ops[at];
                if (world.StateOf(op.Slot) != SlotState.Live)
                {
                    rejected++;
                    continue;
                }

                if (op.Op == StatOpKind.RemoveSource)
                {
                    RemoveSource(op.Slot, op.Source);
                    continue;
                }

                var ordinal = op.Op == StatOpKind.Add ? OrdinalOf(op.Stat) : NoOrdinal;
                if (ordinal < 0 || op.Kind > ShapeKind.Cap || op.Layer > StatLayer.External)
                {
                    rejected++;
                    continue;
                }

                AddRecord(op.Slot, ordinal, op.Kind, op.Value, op.Source, op.ExpiresAt, op.Layer);
            }
        }

        public ModifierHandle Add(int slot, int lane, ShapeKind kind, int value, int source, long expiresAt = long.MaxValue, StatLayer layer = StatLayer.Core)
        {
            var ordinal = OrdinalOf(lane);
            if (world.StateOf(slot) != SlotState.Live || ordinal < 0 || kind > ShapeKind.Cap || layer > StatLayer.External)
            {
                rejected++;
                return new ModifierHandle(0L);
            }

            return new ModifierHandle(AddRecord(slot, ordinal, kind, value, source, expiresAt, layer));
        }

        public int RemoveSource(int slot, int source)
        {
            var removed = 0;
            var at = HeadPeek(slot);
            while (at != NoRecord)
            {
                var next = records[at].Next;
                if (records[at].Source == source)
                {
                    RemoveRecord(at, true);
                    removed++;
                }

                at = next;
            }

            return removed;
        }

        public bool Remove(int slot, ModifierHandle handle)
        {
            for (var at = HeadPeek(slot); at != NoRecord; at = records[at].Next)
            {
                if (records[at].Id == handle.Id)
                {
                    RemoveRecord(at, true);
                    return true;
                }
            }

            return false;
        }

        public int Kill(int slot)
        {
            var removed = 0;
            var at = HeadPeek(slot);
            while (at != NoRecord)
            {
                var next = records[at].Next;
                RemoveRecord(at, false);
                removed++;
                at = next;
            }

            return removed;
        }

        public int Expire(long tick)
        {
            var removed = 0;
            while (heapCount > 0 && records[heap[0]].Expiry <= tick)
            {
                RemoveRecord(heap[0], true);
                removed++;
            }

            return removed;
        }

        public int CountOf(int slot, int lane)
        {
            var ordinal = OrdinalOf(lane);
            var count = 0;
            for (var at = HeadPeek(slot); at != NoRecord; at = records[at].Next)
            {
                count += records[at].Stat == ordinal ? 1 : 0;
            }

            return count;
        }

        long AddRecord(int slot, int ordinal, ShapeKind kind, int value, int source, long expiresAt, StatLayer layer)
        {
            nextId++;
            var record = new ModifierRecord
            {
                Id = nextId,
                Expiry = expiresAt,
                Value = value,
                Source = source,
                Slot = slot,
                Stat = (ushort)ordinal,
                Kind = kind,
                Layer = layer,
            };
            var at = Insert(record);
            LogUndo(StatUndoAction.Added, records[at]);
            MarkDirty(slot, ordinal);
            return nextId;
        }

        void RemoveRecord(int at, bool dirtyStat)
        {
            LogUndo(StatUndoAction.Removed, records[at]);
            if (dirtyStat)
            {
                MarkDirty(records[at].Slot, records[at].Stat);
            }

            Unlink(at);
        }

        int Insert(ModifierRecord value)
        {
            int at;
            if (recordFree != NoRecord)
            {
                at = recordFree;
                recordFree = records[at].Next;
            }
            else
            {
                records = Grow(records, recordCount, ref recordCapacity, recordCount + 1);
                at = recordCount++;
            }

            var head = HeadOf(value.Slot);
            value.Previous = NoRecord;
            value.Next = *head;
            value.Heap = NoRecord;
            records[at] = value;
            if (*head != NoRecord)
            {
                records[*head].Previous = at;
            }

            *head = at;
            if (value.Expiry != long.MaxValue)
            {
                HeapPush(at);
            }

            liveRecords++;
            digest += KeyOf(value);
            return at;
        }

        void Unlink(int at)
        {
            var record = records[at];
            if (record.Heap != NoRecord)
            {
                HeapRemove(record.Heap);
            }

            if (record.Previous != NoRecord)
            {
                records[record.Previous].Next = record.Next;
            }
            else
            {
                *HeadOf(record.Slot) = record.Next;
            }

            if (record.Next != NoRecord)
            {
                records[record.Next].Previous = record.Previous;
            }

            digest -= KeyOf(record);
            liveRecords--;
            records[at].Next = recordFree;
            records[at].Previous = NoRecord;
            records[at].Heap = NoRecord;
            recordFree = at;
        }

        int HeadPeek(int slot)
        {
            if ((uint)slot >= (uint)(headPageCount << HeadPageShift))
            {
                return NoRecord;
            }

            var page = headPages[slot >> HeadPageShift];
            return page == null ? NoRecord : page[slot & (HeadPageSlots - 1)];
        }

        int* HeadOf(int slot)
        {
            var index = slot >> HeadPageShift;
            var page = headPages[index];
            if (page == null)
            {
                page = (int*)Memory.Alloc(HeadPageSlots, sizeof(int));
                for (var at = 0; at < HeadPageSlots; at++)
                {
                    page[at] = NoRecord;
                }

                headPages[index] = page;
            }

            return page + (slot & (HeadPageSlots - 1));
        }

        void MarkDirty(int slot, int ordinal)
        {
            dirty = Grow(dirty, dirtyCount, ref dirtyCapacity, dirtyCount + 1);
            dirty[dirtyCount++] = ((ulong)(uint)slot << 16) | (uint)ordinal;
        }

        static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 33;
                value *= 0xFF51AFD7ED558CCDul;
                value ^= value >> 33;
                value *= 0xC4CEB9FE1A85EC53ul;
                return value ^ (value >> 33);
            }
        }

        static ulong KeyOf(in ModifierRecord record)
        {
            unchecked
            {
                var key = Mix((ulong)record.Id);
                key = Mix(key ^ (ulong)record.Expiry);
                key = Mix(key ^ (((ulong)(uint)record.Slot << 32) | (uint)record.Value));
                return Mix(key ^ (((ulong)(uint)record.Source << 32) | ((ulong)record.Stat << 16) | ((ulong)record.Kind << 8) | (byte)record.Layer));
            }
        }

        bool HeapBefore(int first, int second)
        {
            var a = records[first];
            var b = records[second];
            return a.Expiry < b.Expiry || (a.Expiry == b.Expiry && a.Id < b.Id);
        }

        void HeapPush(int record)
        {
            heap = Grow(heap, heapCount, ref heapCapacity, heapCount + 1);
            var at = heapCount++;
            heap[at] = record;
            records[record].Heap = at;
            HeapUp(at);
        }

        void HeapRemove(int at)
        {
            var last = --heapCount;
            records[heap[at]].Heap = NoRecord;
            if (at == last)
            {
                return;
            }

            heap[at] = heap[last];
            records[heap[at]].Heap = at;
            HeapUp(at);
            HeapDown(records[heap[at]].Heap);
        }

        void HeapUp(int at)
        {
            while (at > 0)
            {
                var parent = (at - 1) >> 1;
                if (!HeapBefore(heap[at], heap[parent]))
                {
                    return;
                }

                HeapSwap(at, parent);
                at = parent;
            }
        }

        void HeapDown(int at)
        {
            while (true)
            {
                var child = at * 2 + 1;
                if (child >= heapCount)
                {
                    return;
                }

                if (child + 1 < heapCount && HeapBefore(heap[child + 1], heap[child]))
                {
                    child++;
                }

                if (!HeapBefore(heap[child], heap[at]))
                {
                    return;
                }

                HeapSwap(at, child);
                at = child;
            }
        }

        void HeapSwap(int first, int second)
        {
            var swap = heap[first];
            heap[first] = heap[second];
            heap[second] = swap;
            records[heap[first]].Heap = first;
            records[heap[second]].Heap = second;
        }
    }
}
