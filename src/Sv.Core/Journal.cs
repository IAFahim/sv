using System;

namespace Sv
{
    public unsafe struct EpochRecord
    {
        public uint Epoch;
        public long Begin;
        public JournalEntry* Entries;
        public int Count;
        public int Capacity;
        public int* Kills;
        public int KillCount;
        public int KillCapacity;
    }

    public sealed unsafe class Journal : IDisposable
    {
        public const int DefaultRing = 32;

        EpochRecord* ring;
        readonly int length;
        uint current;

        public Journal(int ringEpochs)
        {
            if (ringEpochs < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(ringEpochs), "sv: the ring holds at least two epochs");
            }

            length = ringEpochs;
            ring = (EpochRecord*)Memory.AllocZeroed((nuint)length, (nuint)sizeof(EpochRecord));
            current = 1u;
            Open(1u, 0L);
        }

        public int Ring => length;

        public uint Current => current;

        public uint Oldest => current >= (uint)length ? current - (uint)length + 1u : 1u;

        public bool Holds(uint epoch) => epoch >= Oldest && epoch <= current && RecordOf(epoch)->Epoch == epoch;

        public long BeginOf(uint epoch) => Holds(epoch) ? RecordOf(epoch)->Begin : -1L;

        public int Count(uint epoch) => Holds(epoch) ? RecordOf(epoch)->Count : 0;

        public JournalEntry* Entries(uint epoch) => Holds(epoch) ? RecordOf(epoch)->Entries : null;

        public uint AtOrBefore(long frame)
        {
            for (var epoch = current; epoch >= Oldest && epoch != 0u; epoch--)
            {
                if (RecordOf(epoch)->Epoch == epoch && RecordOf(epoch)->Begin <= frame)
                {
                    return epoch;
                }
            }

            return 0u;
        }

        public long Entered
        {
            get
            {
                var total = 0L;
                for (var at = 0; at < length; at++)
                {
                    total += ring[at].Count;
                }

                return total;
            }
        }

        public void Absorb(ref LaneWriter writer)
        {
            if (writer.JournalCount == 0)
            {
                return;
            }

            var record = RecordOf(current);
            Ensure(ref record->Entries, ref record->Capacity, record->Count + writer.JournalCount);
            Memory.Copy(writer.Journal, record->Entries + record->Count, (nuint)writer.JournalCount * (nuint)sizeof(JournalEntry));
            record->Count += writer.JournalCount;
            writer.JournalCount = 0;
        }

        public void NoteKill(uint epoch, int chunk)
        {
            if (!Holds(epoch))
            {
                return;
            }

            var record = RecordOf(epoch);
            if (record->KillCount > 0 && record->Kills[record->KillCount - 1] == chunk)
            {
                return;
            }

            Ensure(ref record->Kills, ref record->KillCapacity, record->KillCount + 1);
            record->Kills[record->KillCount] = chunk;
            record->KillCount++;
        }

        public int Expiring(out int* chunks)
        {
            var record = RecordOf(current + 1u);
            if (record->Epoch == 0u)
            {
                chunks = null;
                return 0;
            }

            chunks = record->Kills;
            return record->KillCount;
        }

        public void Advance(long frame, in WorldView world, LanePool pool)
        {
            var next = current + 1u;
            var record = RecordOf(next);
            if (record->Epoch != 0u)
            {
                for (var at = 0; at < record->Count; at++)
                {
                    var released = Lanes.Expired(world, record->Entries[at]);
                    if (released != Lanes.NoLane)
                    {
                        pool.Give(released);
                    }
                }
            }

            current = next;
            Open(next, frame);
        }

        public void Rewind(uint epoch, in WorldView world, LanePool pool)
        {
            if (!Holds(epoch))
            {
                throw new ArgumentOutOfRangeException(nameof(epoch), "sv: the epoch is outside the ring");
            }

            for (var undone = current; undone >= epoch; undone--)
            {
                var record = RecordOf(undone);
                for (var at = record->Count - 1; at >= 0; at--)
                {
                    var released = Lanes.Restore(world, record->Entries[at]);
                    if (released != Lanes.NoLane)
                    {
                        pool.Give(released);
                    }
                }

                record->Count = 0;
                record->KillCount = 0;
                if (undone != epoch)
                {
                    record->Epoch = 0u;
                }

                if (undone == 0u)
                {
                    break;
                }
            }

            current = epoch;
        }

        EpochRecord* RecordOf(uint epoch) => ring + (int)(epoch % (uint)length);

        void Open(uint epoch, long frame)
        {
            var record = RecordOf(epoch);
            record->Epoch = epoch;
            record->Begin = frame;
            record->Count = 0;
            record->KillCount = 0;
        }

        static void Ensure<T>(ref T* buffer, ref int capacity, int needed) where T : unmanaged
        {
            if (needed <= capacity)
            {
                return;
            }

            var grown = Math.Max(needed, Math.Max(64, capacity * 2));
            buffer = (T*)Memory.Realloc(buffer, (nuint)grown * (nuint)sizeof(T));
            capacity = grown;
        }

        public void Dispose()
        {
            if (ring == null)
            {
                return;
            }

            for (var at = 0; at < length; at++)
            {
                Memory.Free(ring[at].Entries);
                Memory.Free(ring[at].Kills);
            }

            Memory.Free(ring);
            ring = null;
        }
    }
}
