using System;

namespace Sv
{
    public sealed unsafe partial class Board
    {
        internal struct WheelEntry
        {
            public int Record;

            public long Id;

            public long Expiry;

            public int Next;

            public int Prev;

            public int Cell;
        }

        const int WheelBuckets = 256;

        const int WheelSlots = 4 * WheelBuckets;

        const long WheelBulk = 1L << 16;

        WheelEntry* wheelEntries;

        int wheelEntryCount;

        int wheelEntryCap;

        int wheelEntryFree;

        int wheelEntryLive;

        int* wheelHeads;

        int* wheelTails;

        long wheelTick;

        int wheelOverdue;

        int* wheelDue;

        int* wheelDueTemp;

        int* wheelKeep;

        int* wheelKeepTemp;

        int wheelScratchCap;

        long* crossingDue;

        int crossingDueCount;

        int crossingDueCap;

        void EnsureWheel()
        {
            if (wheelEntries != null)
            {
                return;
            }

            wheelEntryCap = 16;
            wheelEntries = arena.Alloc<WheelEntry>(wheelEntryCap);
            wheelEntryFree = -1;
            wheelEntryCount = 0;
            wheelEntryLive = 0;
            wheelOverdue = -1;
            wheelTick = 0;
            wheelHeads = arena.Alloc<int>(WheelSlots);
            wheelTails = arena.Alloc<int>(WheelSlots);
            for (var i = 0; i < WheelSlots; i++)
            {
                wheelHeads[i] = -1;
                wheelTails[i] = -1;
            }
        }

        void FreeWheel()
        {
            if (wheelEntries == null)
            {
                return;
            }

            arena.Free(wheelEntries, wheelEntryCap);
            arena.Free(wheelHeads, WheelSlots);
            arena.Free(wheelTails, WheelSlots);
            if (wheelScratchCap > 0)
            {
                arena.Free(wheelDue, wheelScratchCap);
                arena.Free(wheelDueTemp, wheelScratchCap);
                arena.Free(wheelKeep, wheelScratchCap);
                arena.Free(wheelKeepTemp, wheelScratchCap);
            }

            if (crossingDueCap > 0)
            {
                arena.Free(crossingDue, crossingDueCap * 2);
            }
        }

        /// <summary>
        /// Schedules a drift crossing: the host-chosen 64-bit <paramref name="id"/> (generation-tagged per
        /// the modifier-id discipline — a stale generation drains and is dropped by the host) comes due at
        /// <paramref name="atTick"/> and is handed back by <see cref="TakeDueCrossings"/> during the
        /// <see cref="ExpireModifiers"/> pass that reaches that tick, in (tick, id) order with the
        /// expiries. Renumbering or loading state rebuilds the wheel from modifier records — reschedule
        /// crossings afterwards.
        /// </summary>
        public void ScheduleCrossing(long atTick, long id)
        {
            EnsureWheel();
            if (wheelEntryCount == wheelEntryCap)
            {
                var previous = wheelEntryCap;
                wheelEntryCap *= 2;
                wheelEntries = UndoGrow(UndoArrayId.WheelEntries, wheelEntries, previous, wheelEntryCap);
            }

            var entry = wheelEntryFree >= 0 ? TakeWheelEntry() : wheelEntryCount++;
            undo?.Note(wheelEntries + entry, sizeof(WheelEntry));
            wheelEntries[entry] = new WheelEntry { Record = -1, Id = id, Expiry = atTick, Next = -1, Prev = -1, Cell = -1 };
            LinkWheelEntry(entry, atTick, id);
            wheelEntryLive++;
        }

        void CollectCrossing(long tick, long id)
        {
            if (crossingDueCount == crossingDueCap)
            {
                var previous = crossingDue;
                var previousCap = crossingDueCap;
                crossingDueCap = crossingDueCap == 0 ? 16 : crossingDueCap * 2;
                var grown = arena.Alloc<long>(crossingDueCap * 2);
                if (previousCap > 0)
                {
                    Buffer.MemoryCopy(previous, grown, (nuint)crossingDueCap * 2 * sizeof(long), (nuint)previousCap * 2 * sizeof(long));
                    arena.Free(previous, previousCap * 2);
                }

                crossingDue = grown;
            }

            crossingDue[2 * crossingDueCount] = tick;
            crossingDue[2 * crossingDueCount + 1] = id;
            crossingDueCount++;
        }

        /// <summary>Copies the ids that came due in the last <see cref="ExpireModifiers"/> pass, in (tick, id) order, and clears the ring. The ring is an event output, not state: it is not rolled back.</summary>
        public int TakeDueCrossings(Span<long> due)
        {
            for (var i = 1; i < crossingDueCount; i++)
            {
                var tick = crossingDue[2 * i];
                var id = crossingDue[2 * i + 1];
                var j = i - 1;
                while (j >= 0 && (crossingDue[2 * j] > tick || (crossingDue[2 * j] == tick && crossingDue[2 * j + 1] > id)))
                {
                    crossingDue[2 * (j + 1)] = crossingDue[2 * j];
                    crossingDue[2 * (j + 1) + 1] = crossingDue[2 * j + 1];
                    j--;
                }

                crossingDue[2 * (j + 1)] = tick;
                crossingDue[2 * (j + 1) + 1] = id;
            }

            if (due.Length < crossingDueCount)
            {
                throw new ArgumentException($"sv: due-crossing span of {due.Length} does not cover {crossingDueCount} due crossings");
            }

            for (var i = 0; i < crossingDueCount; i++)
            {
                due[i] = crossingDue[2 * i + 1];
            }

            var count = crossingDueCount;
            crossingDueCount = 0;
            return count;
        }

        /// <summary>Crossings waiting to come due in the wheel (excluding modifier expiries).</summary>
        public int PendingCrossings
        {
            get
            {
                if (wheelEntries == null)
                {
                    return 0;
                }

                var count = 0;
                for (var at = wheelOverdue; at >= 0; at = wheelEntries[at].Next)
                {
                    if (wheelEntries[at].Record < 0)
                    {
                        count++;
                    }
                }

                for (var cell = 0; cell < WheelSlots; cell++)
                {
                    for (var at = wheelHeads[cell]; at >= 0; at = wheelEntries[at].Next)
                    {
                        if (wheelEntries[at].Record < 0)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        void WheelInsert(int record, long id, long expiry)
        {
            EnsureWheel();
            if (wheelEntryCount == wheelEntryCap)
            {
                var previous = wheelEntryCap;
                wheelEntryCap *= 2;
                wheelEntries = UndoGrow(UndoArrayId.WheelEntries, wheelEntries, previous, wheelEntryCap);
            }

            var entry = wheelEntryFree >= 0 ? TakeWheelEntry() : wheelEntryCount++;
            undo?.Note(wheelEntries + entry, sizeof(WheelEntry));
            wheelEntries[entry] = new WheelEntry { Record = record, Id = id, Expiry = expiry, Next = -1, Prev = -1, Cell = -1 };
            LinkWheelEntry(entry, expiry, id);
            undo?.Note(&records[record].Wheel, sizeof(int));
            records[record].Wheel = entry;
            wheelEntryLive++;
        }

        int TakeWheelEntry()
        {
            var reuse = wheelEntryFree;
            wheelEntryFree = wheelEntries[reuse].Next;
            return reuse;
        }

        void WheelUnlink(int record)
        {
            var index = records[record].Wheel;
            if (index < 0)
            {
                return;
            }

            var entry = wheelEntries[index];
            if (entry.Prev >= 0)
            {
                undo?.Note(&wheelEntries[entry.Prev].Next, sizeof(int));
                wheelEntries[entry.Prev].Next = entry.Next;
            }
            else if (entry.Cell < 0)
            {
                wheelOverdue = entry.Next;
            }
            else
            {
                undo?.Note(wheelHeads + entry.Cell, sizeof(int));
                wheelHeads[entry.Cell] = entry.Next;
            }

            if (entry.Next >= 0)
            {
                undo?.Note(&wheelEntries[entry.Next].Prev, sizeof(int));
                wheelEntries[entry.Next].Prev = entry.Prev;
            }
            else if (entry.Cell >= 0)
            {
                undo?.Note(wheelTails + entry.Cell, sizeof(int));
                wheelTails[entry.Cell] = entry.Prev;
            }

            undo?.Note(&records[record].Wheel, sizeof(int));
            records[record].Wheel = -1;
            FreeWheelEntry(index);
        }

        void LinkWheelEntry(int entry, long expiry, long id)
        {
            if (expiry <= wheelTick)
            {
                var at = wheelOverdue;
                var previous = -1;
                while (at >= 0)
                {
                    var key = wheelEntries[at].Expiry;
                    if (key > expiry || (key == expiry && wheelEntries[at].Id > id))
                    {
                        break;
                    }

                    previous = at;
                    at = wheelEntries[at].Next;
                }

                undo?.Note(&wheelEntries[entry].Next, 2 * sizeof(int));
                wheelEntries[entry].Next = at;
                wheelEntries[entry].Prev = previous;
                if (previous < 0)
                {
                    wheelOverdue = entry;
                }
                else
                {
                    undo?.Note(&wheelEntries[previous].Next, sizeof(int));
                    wheelEntries[previous].Next = entry;
                }

                if (at >= 0)
                {
                    undo?.Note(&wheelEntries[at].Prev, sizeof(int));
                    wheelEntries[at].Prev = entry;
                }

                return;
            }

            var delay = expiry - wheelTick;
            int cell;
            if (delay < 256)
            {
                cell = (int)(expiry & 255);
            }
            else if (delay < 65536)
            {
                cell = WheelBuckets + (int)((expiry >> 8) & 255);
            }
            else if (delay < (1L << 24))
            {
                cell = 2 * WheelBuckets + (int)((expiry >> 16) & 255);
            }
            else if (delay < (1L << 32))
            {
                cell = 3 * WheelBuckets + (int)((delay >> 24) & 255);
            }
            else
            {
                cell = 3 * WheelBuckets + WheelBuckets - 1;
            }

            WheelAppend(cell, entry);
        }

        void WheelAppend(int cell, int entry)
        {
            var tail = wheelTails[cell];
            if (tail < 0)
            {
                undo?.Note(wheelHeads + cell, sizeof(int));
                wheelHeads[cell] = entry;
            }
            else
            {
                undo?.Note(&wheelEntries[tail].Next, sizeof(int));
                wheelEntries[tail].Next = entry;
            }

            undo?.Note(&wheelEntries[entry].Prev, sizeof(int));
            wheelEntries[entry].Prev = tail;
            undo?.Note(&wheelEntries[entry].Cell, sizeof(int));
            wheelEntries[entry].Cell = cell;
            undo?.Note(wheelTails + cell, sizeof(int));
            wheelTails[cell] = entry;
        }

        WheelEntry WheelDrainHead(int cell)
        {
            var entry = wheelHeads[cell];
            var value = wheelEntries[entry];
            undo?.Note(wheelHeads + cell, sizeof(int));
            wheelHeads[cell] = value.Next;
            if (value.Next >= 0)
            {
                undo?.Note(&wheelEntries[value.Next].Prev, sizeof(int));
                wheelEntries[value.Next].Prev = -1;
            }
            else
            {
                undo?.Note(wheelTails + cell, sizeof(int));
                wheelTails[cell] = -1;
            }

            return value;
        }

        void FreeWheelEntry(int entry)
        {
            undo?.Note(&wheelEntries[entry].Next, sizeof(int));
            wheelEntries[entry].Next = wheelEntryFree;
            wheelEntryFree = entry;
            wheelEntryLive--;
        }

        int RemoveDueRecord(int record, long id)
        {
            if (record >= recordCount || records[record].Id != id)
            {
                return 0;
            }

            var slot = records[record].Slot;
            if (slot < 0 || slot >= Capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                return 0;
            }

            ref var state = ref Slot(slot);
            var previous = -1;
            for (var at = state.Head; at >= 0; at = records[at].Next)
            {
                if (records[at].Id == id)
                {
                    var next = records[at].Next;
                    MarkStatDirty(slot, records[at].Stat);
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

                    return 1;
                }

                previous = at;
            }

            return 0;
        }

        /// <summary>
        /// Removes every modifier whose expiry tick is &lt;= <paramref name="tick"/>, in (tick, insertion id)
        /// order, and collects the drift crossings due at or before it (see <see cref="ScheduleCrossing"/>).
        /// Modifiers removed by handle, source or slot release are already unlinked from the
        /// wheel. Returns how many modifiers were removed.
        /// </summary>
        public int ExpireModifiers(long tick)
        {
            EnsureWheel();
            DebugValidateWheel();
            var removed = 0;
            while (wheelOverdue >= 0)
            {
                var entry = wheelOverdue;
                var value = wheelEntries[entry];
                if (value.Record >= 0 && value.Record < recordCount && records[value.Record].Id == value.Id && value.Expiry > tick)
                {
                    break;
                }

                wheelOverdue = value.Next;
                if (value.Next >= 0)
                {
                    undo?.Note(&wheelEntries[value.Next].Prev, sizeof(int));
                    wheelEntries[value.Next].Prev = -1;
                }

                removed += RetireWheelEntry(value);
                FreeWheelEntry(entry);
            }

            if (tick > wheelTick)
            {
                removed += AdvanceWheel(tick);
            }

            ShrinkWheel();
            return removed;
        }

        int AdvanceWheel(long to)
        {
            var removed = 0;
            while (wheelTick < to)
            {
                if (to - wheelTick >= WheelBulk)
                {
                    removed += BulkWheelDrain(to);
                    break;
                }

                wheelTick++;
                if ((wheelTick & 255) == 0)
                {
                    CascadeWheel(1, (int)((wheelTick >> 8) & 255));
                    if (((wheelTick >> 8) & 255) == 0)
                    {
                        CascadeWheel(2, (int)((wheelTick >> 16) & 255));
                        if (((wheelTick >> 16) & 255) == 0)
                        {
                            CascadeWheel(3, (int)((wheelTick >> 24) & 255));
                        }
                    }
                }

                removed += FireBucket((int)(wheelTick & 255));
            }

            return removed;
        }

        int FireBucket(int bucket)
        {
            var removed = 0;
            while (wheelHeads[bucket] >= 0)
            {
                var index = wheelHeads[bucket];
                var value = WheelDrainHead(bucket);
                removed += RetireWheelEntry(value);
                FreeWheelEntry(index);
            }

            return removed;
        }

        void CascadeWheel(int level, int bucket)
        {
            var cell = level * WheelBuckets + bucket;
            var count = 0;
            for (var at = wheelHeads[cell]; at >= 0; at = wheelEntries[at].Next)
            {
                count++;
            }

            if (count == 0)
            {
                return;
            }

            EnsureWheelScratch(count);
            for (var i = 0; i < count; i++)
            {
                wheelKeep[i] = wheelHeads[cell];
                WheelDrainHead(cell);
            }

            for (var i = 0; i < count; i++)
            {
                var index = wheelKeep[i];
                var value = wheelEntries[index];
                undo?.Note(wheelEntries + index, sizeof(WheelEntry));
                wheelEntries[index] = new WheelEntry { Record = value.Record, Id = value.Id, Expiry = value.Expiry, Next = -1, Prev = -1, Cell = -1 };
                if (value.Expiry <= wheelTick)
                {
                    WheelAppend((int)(wheelTick & 255), index);
                }
                else
                {
                    LinkWheelEntry(index, value.Expiry, value.Id);
                }
            }
        }

        int BulkWheelDrain(long to)
        {
            wheelTick = to;
            var dueCount = 0;
            var keepCount = 0;
            EnsureWheelScratch(wheelEntryLive);
            while (wheelOverdue >= 0)
            {
                var index = wheelOverdue;
                var value = wheelEntries[index];
                wheelOverdue = value.Next;
                ClassifyWheelEntry(index, value, to, ref dueCount, ref keepCount);
            }

            for (var cell = 0; cell < WheelSlots; cell++)
            {
                while (wheelHeads[cell] >= 0)
                {
                    var index = wheelHeads[cell];
                    var value = WheelDrainHead(cell);
                    ClassifyWheelEntry(index, value, to, ref dueCount, ref keepCount);
                }
            }

            SortWheelIndices(wheelDue, wheelDueTemp, dueCount, false);
            SortWheelIndices(wheelKeep, wheelKeepTemp, keepCount, false);
            var removed = 0;
            for (var i = 0; i < dueCount; i++)
            {
                var index = wheelDue[i];
                var value = wheelEntries[index];
                removed += RetireWheelEntry(value);
                FreeWheelEntry(index);
            }

            for (var i = 0; i < keepCount; i++)
            {
                var index = wheelKeep[i];
                var value = wheelEntries[index];
                undo?.Note(wheelEntries + index, sizeof(WheelEntry));
                wheelEntries[index] = new WheelEntry { Record = value.Record, Id = value.Id, Expiry = value.Expiry, Next = -1, Prev = -1, Cell = -1 };
                LinkWheelEntry(index, value.Expiry, value.Id);
            }

            return removed;
        }

        void ClassifyWheelEntry(int index, in WheelEntry value, long to, ref int dueCount, ref int keepCount)
        {
            var expiry = value.Expiry;
            var due = expiry <= to || value.Record >= 0 && (value.Record >= recordCount || records[value.Record].Id != value.Id);
            if (due)
            {
                wheelDue[dueCount++] = index;
            }
            else
            {
                wheelKeep[keepCount++] = index;
            }
        }

        void EnsureWheelScratch(int count)
        {
            if (count <= wheelScratchCap)
            {
                return;
            }

            var capacity = wheelScratchCap == 0 ? 64 : wheelScratchCap;
            while (capacity < count)
            {
                capacity *= 2;
            }

            if (wheelScratchCap > 0)
            {
                arena.Free(wheelDue, wheelScratchCap);
                arena.Free(wheelDueTemp, wheelScratchCap);
                arena.Free(wheelKeep, wheelScratchCap);
                arena.Free(wheelKeepTemp, wheelScratchCap);
            }

            wheelDue = arena.Alloc<int>(capacity);
            wheelDueTemp = arena.Alloc<int>(capacity);
            wheelKeep = arena.Alloc<int>(capacity);
            wheelKeepTemp = arena.Alloc<int>(capacity);
            wheelScratchCap = capacity;
        }

        void SortWheelIndices(int* items, int* temp, int count, bool byRecord)
        {
            if (count < 2)
            {
                return;
            }

            var source = items;
            var target = temp;
            for (var width = 1; width < count; width <<= 1)
            {
                var at = 0;
                while (at < count)
                {
                    var left = at;
                    var middle = left + width;
                    if (middle > count)
                    {
                        middle = count;
                    }

                    var right = left + (width << 1);
                    if (right > count)
                    {
                        right = count;
                    }

                    var i = left;
                    var j = middle;
                    while (i < middle && j < right)
                    {
                        target[at++] = WheelBefore(source[i], source[j], byRecord) ? source[i++] : source[j++];
                    }

                    while (i < middle)
                    {
                        target[at++] = source[i++];
                    }

                    while (j < right)
                    {
                        target[at++] = source[j++];
                    }

                    at = right;
                }

                var swap = source;
                source = target;
                target = swap;
            }

            if (source != items)
            {
                for (var i = 0; i < count; i++)
                {
                    items[i] = source[i];
                }
            }
        }

        int RetireWheelEntry(in WheelEntry value)
        {
            if (value.Record < 0)
            {
                CollectCrossing(value.Expiry, value.Id);
                return 0;
            }

            return RemoveDueRecord(value.Record, value.Id);
        }

        bool WheelBefore(int left, int right, bool byRecord)
        {
            if (byRecord)
            {
                return records[left].Expiry < records[right].Expiry
                    || (records[left].Expiry == records[right].Expiry && records[left].Id < records[right].Id);
            }

            var a = wheelEntries[left];
            var b = wheelEntries[right];
            var ta = a.Expiry;
            var tb = b.Expiry;
            return ta < tb || (ta == tb && a.Id < b.Id);
        }

        internal void RebuildWheel()
        {
            var count = 0;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
                {
                    continue;
                }

                for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
                {
                    if (records[at].Expiry != long.MaxValue)
                    {
                        count++;
                    }
                }
            }

            if (wheelEntries == null && count == 0)
            {
                return;
            }

            EnsureWheel();
            undo?.NoteRange(wheelHeads, WheelSlots);
            undo?.NoteRange(wheelTails, WheelSlots);
            for (var i = 0; i < WheelSlots; i++)
            {
                wheelHeads[i] = -1;
                wheelTails[i] = -1;
            }

            wheelOverdue = -1;
            wheelEntryFree = -1;
            wheelEntryCount = 0;
            wheelEntryLive = 0;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
                {
                    continue;
                }

                for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
                {
                    records[at].Wheel = -1;
                }
            }

            EnsureWheelScratch(count);
            var fill = 0;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if ((live[slot >> 6] & (1ul << (slot & 63))) == 0)
                {
                    continue;
                }

                for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
                {
                    if (records[at].Expiry != long.MaxValue)
                    {
                        wheelDue[fill++] = at;
                    }
                }
            }

            SortWheelIndices(wheelDue, wheelDueTemp, count, true);
            for (var i = 0; i < count; i++)
            {
                var at = wheelDue[i];
                WheelInsert(at, records[at].Id, records[at].Expiry);
            }
        }

        public int PendingExpiries
        {
            get
            {
                EnsureWheel();
                return wheelEntryLive;
            }
        }

        static readonly bool wheelDebug = Environment.GetEnvironmentVariable("SV_WHEEL_DEBUG") == "1";

        internal void DebugValidateWheel()
        {
            if (!wheelDebug)
            {
                return;
            }

            var seen = new System.Collections.Generic.HashSet<int>();
            var at = wheelOverdue;
            var previous = -1;
            while (at >= 0)
            {
                if (!seen.Add(at)) throw new InvalidOperationException($"wheel: overdue cycle at {at}");
                if (wheelEntries[at].Prev != previous) throw new InvalidOperationException($"wheel: overdue prev broken at {at}");
                if (wheelEntries[at].Cell != -1) throw new InvalidOperationException($"wheel: overdue cell at {at}");
                if (wheelEntries[at].Record >= 0 && (wheelEntries[at].Record >= recordCount || records[wheelEntries[at].Record].Wheel != at)) throw new InvalidOperationException($"wheel: overdue backlink broken at {at}");
                previous = at;
                at = wheelEntries[at].Next;
            }

            for (var cell = 0; cell < WheelSlots; cell++)
            {
                at = wheelHeads[cell];
                previous = -1;
                while (at >= 0)
                {
                    if (!seen.Add(at)) throw new InvalidOperationException($"wheel: bucket {cell} cycle at {at}");
                    if (wheelEntries[at].Prev != previous) throw new InvalidOperationException($"wheel: bucket {cell} prev broken at {at}");
                    if (wheelEntries[at].Cell != cell) throw new InvalidOperationException($"wheel: bucket {cell} mismatch at {at}");
                    if (wheelEntries[at].Record >= 0 && (wheelEntries[at].Record >= recordCount || records[wheelEntries[at].Record].Wheel != at)) throw new InvalidOperationException($"wheel: bucket {cell} backlink broken at {at}");
                    previous = at;
                    at = wheelEntries[at].Next;
                }

                if (wheelHeads[cell] >= 0 && wheelTails[cell] != previous) throw new InvalidOperationException($"wheel: bucket {cell} tail broken");
                if (wheelHeads[cell] < 0 && wheelTails[cell] != -1) throw new InvalidOperationException($"wheel: bucket {cell} tail without head");
            }

            if (seen.Count != wheelEntryLive) throw new InvalidOperationException($"wheel: live {wheelEntryLive} != chained {seen.Count}");
        }

        void ShrinkWheel()
        {
            if (wheelEntryCap > 16 && (long)wheelEntryCount * 4 <= (long)wheelEntryCap * 3)
            {
                var previous = wheelEntryCap;
                var target = wheelEntryCount + (wheelEntryCount >> 3);
                wheelEntryCap = target < 16 ? 16 : target;
                wheelEntries = UndoGrow(UndoArrayId.WheelEntries, wheelEntries, previous, wheelEntryCap);
            }
        }
    }
}
