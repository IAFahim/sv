using System;

namespace Sv.Columns
{
    public sealed unsafe class RecordWriter : IDisposable
    {
        readonly ColumnWorld world;
        ulong* records;
        int capacity;

        internal RecordWriter(ColumnWorld world, int initialCapacity)
        {
            this.world = world;
            capacity = Math.Max(initialCapacity, 64);
            records = Native.Zeroed<ulong>(capacity);
        }

        public int Count { get; private set; }

        internal ulong* Records => records;

        public void Send(int target, int channel, long amount, int hits = 1) =>
            Append(Record.Pack(target, channel, 0, world.NoSourceFlags, amount * hits));

        public void Send(int target, int source, int channel, long amount, int hits = 1)
        {
            var gate = world.GateOf(source);
            Append(Record.Pack(target, channel, gate.Kind, gate.Flags >> 8, amount * hits));
        }

        public void SendRecord(ulong record) => Append(record);

        void Append(ulong record)
        {
            if (Count == capacity)
            {
                Grow();
            }

            records[Count++] = record;
        }

        void Grow()
        {
            if (capacity >= int.MaxValue / 2)
            {
                throw new InvalidOperationException("a writer holds at most " + (int.MaxValue / 2) + " records per frame");
            }

            var grown = Native.Zeroed<ulong>((long)capacity * 2);
            Buffer.MemoryCopy(records, grown, (long)capacity * 8, (long)capacity * 8);
            Native.Free(records);
            records = grown;
            capacity *= 2;
        }

        internal void Clear() => Count = 0;

        internal void Load(ulong* source, int count)
        {
            while (capacity < count)
            {
                Grow();
            }

            Buffer.MemoryCopy(source, records, (long)capacity * 8, (long)count * 8);
            Count = count;
        }

        public void Dispose()
        {
            Native.Free(records);
            records = null;
        }
    }
}
