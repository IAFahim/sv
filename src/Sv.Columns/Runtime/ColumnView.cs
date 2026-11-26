namespace Sv.Columns
{
    public unsafe struct ColumnView
    {
        internal long LaneData;
        internal long AliveWords;
        internal long ChunkTemplates;
        internal long Gates;
        internal int ChunkInts;
        internal int LaneCount;
        internal int SlotCount;
        internal int EventsLane;
        internal int EnteredLane;
        internal int EventCount;
        internal uint NoSourceFlags;

        public bool IsCreated => LaneData != 0;

        public int Capacity => SlotCount;

        public int Lanes => LaneCount;

        public bool IsAlive(int slot) => (uint)slot < (uint)SlotCount && ((((ulong*)AliveWords)[slot >> 6] >> (slot & 63)) & 1ul) != 0;

        public int TemplateOf(int slot) => IsAlive(slot) ? ((int*)ChunkTemplates)[slot >> 6] : -1;

        public int Get(int slot, int lane) =>
            IsAlive(slot) && (uint)lane < (uint)LaneCount ? ((int*)LaneData)[(long)(slot >> 6) * ChunkInts + lane * Law.Rows + (slot & 63)] : 0;

        public bool Event(int slot, int eventIndex) => (uint)eventIndex < (uint)EventCount && (((uint)Get(slot, EventsLane) >> eventIndex) & 1u) != 0;

        public bool Entered(int slot, int eventIndex) => (uint)eventIndex < (uint)EventCount && (((uint)Get(slot, EnteredLane) >> eventIndex) & 1u) != 0;

        public bool TryRecord(int target, int channel, long amount, int hits, out ulong record)
        {
            if ((uint)target >= (uint)SlotCount || (uint)channel >= RecordWriter.ChannelLimit)
            {
                record = 0;
                return false;
            }

            record = Record.Pack(target, channel, 0, NoSourceFlags, RecordWriter.AmountTimesHits(amount, hits));
            return true;
        }

        public bool TryRecordFrom(int source, int target, int channel, long amount, int hits, out ulong record)
        {
            if ((uint)target >= (uint)SlotCount || (uint)channel >= RecordWriter.ChannelLimit)
            {
                record = 0;
                return false;
            }

            var gate = IsAlive(source) ? ((SlotGate*)Gates)[source] : new SlotGate { Flags = NoSourceFlags << 8 };
            record = Record.Pack(target, channel, gate.Kind, gate.Flags >> 8, RecordWriter.AmountTimesHits(amount, hits));
            return true;
        }
    }
}
