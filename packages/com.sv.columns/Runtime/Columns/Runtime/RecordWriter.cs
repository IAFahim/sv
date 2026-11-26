using System;

namespace Sv.Columns
{
    public sealed unsafe class RecordWriter : IDisposable
    {
        public const int ChannelLimit = 16;
        readonly ColumnWorld world;
        readonly uint slotCount;
        ulong* records;
        int capacity;
        int limit;
        bool locked;
        long refusedAtSend;

        internal RecordWriter(ColumnWorld world, int initialCapacity)
        {
            this.world = world;
            slotCount = (uint)world.Capacity;
            capacity = Math.Max(initialCapacity, 64);
            records = Native.Zeroed<ulong>(capacity);
            limit = capacity;
        }

        public int Count { get; private set; }

        internal ulong* Records => records;

        public void Send(int target, int channel, long amount, int hits = 1)
        {
            if ((uint)target >= slotCount || (uint)channel >= ChannelLimit)
            {
                Refuse();
                return;
            }

            Append(Record.Pack(target, channel, 0, world.NoSourceFlags, AmountTimesHits(amount, hits)));
        }

        public void SendFrom(int source, int target, int channel, long amount, int hits = 1)
        {
            if (records == null)
            {
                throw new ObjectDisposedException(nameof(RecordWriter));
            }

            if ((uint)target >= slotCount || (uint)channel >= ChannelLimit)
            {
                Refuse();
                return;
            }

            var gate = world.SourceGateOf(source);
            Append(Record.Pack(target, channel, gate.Kind, gate.Flags >> 8, AmountTimesHits(amount, hits)));
        }

        public void SendRecord(ulong record) => Append(record);

        public static long AmountTimesHits(long amount, int hits)
        {
            var bounded = amount > Record.AmountMax ? Record.AmountMax : amount < Record.AmountMin ? Record.AmountMin : amount;
            return bounded * hits;
        }

        void Refuse()
        {
            if (records == null)
            {
                throw new ObjectDisposedException(nameof(RecordWriter));
            }

            Unlocked();
            refusedAtSend++;
        }

        internal long TakeRefused()
        {
            var refused = refusedAtSend;
            refusedAtSend = 0;
            return refused;
        }

        void Append(ulong record)
        {
            if (Count == limit)
            {
                Grow();
            }

            records[Count++] = record;
        }

        void Unlocked()
        {
            if (locked)
            {
                throw new InvalidOperationException("a frame is running: send before BeginFrame or OpenFrame, or after EndFrame or CloseFrame");
            }
        }

        void Grow()
        {
            if (records == null)
            {
                throw new ObjectDisposedException(nameof(RecordWriter));
            }

            Unlocked();
            if (Count < capacity)
            {
                limit = capacity;
                return;
            }

            if (capacity >= int.MaxValue / 2)
            {
                throw new InvalidOperationException("a writer holds at most " + (int.MaxValue / 2) + " records per frame");
            }

            var grown = Native.Zeroed<ulong>((long)capacity * 2);
            Buffer.MemoryCopy(records, grown, (long)capacity * 8, (long)capacity * 8);
            Native.Free(records);
            records = grown;
            capacity *= 2;
            limit = capacity;
        }

        internal void Lock()
        {
            locked = true;
            limit = Count;
        }

        internal void Unlock()
        {
            locked = false;
            limit = capacity;
        }

        internal void Clear()
        {
            Count = 0;
            limit = locked ? 0 : capacity;
        }

        internal void Replay(long refused)
        {
            refusedAtSend = refused;
            Clear();
        }

        public void Dispose()
        {
            Native.Free(records);
            records = null;
            capacity = 0;
            limit = 0;
            Count = 0;
        }
    }
}
