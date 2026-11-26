namespace Sv.Columns
{
    public enum FrameKernel
    {
        Rows = 0,
        Scalar = 1,
    }

    public unsafe struct RecordBucket
    {
        public ulong* Records;
        public int* Starts;
    }

    public unsafe struct ColumnFrame
    {
        internal PackTables* Tables;
        internal int* Lanes;
        internal int* ChunkTemplates;
        internal ulong* Alive;
        internal SlotGate* Gates;
        internal byte* ScratchArea;
        internal int ScratchBytes;
        internal int ChunkCount;
        internal int ChunkInts;
        internal int ClassLongs;
        internal int PartCount;
        internal int SlotCount;
        internal long* PartLost;
        internal int WriterCount;
        internal ulong** WriterRecords;
        internal int* WriterCounts;
        internal RecordBucket* WriterBuckets;
        internal long* WriterLost;
        internal FrameKernel Kernel;
        internal int Number;

        public int Chunks => ChunkCount;

        public int Parts => PartCount;

        public int Writers => WriterCount;

        public int Frame => Number;

        public int StartsLength => ChunkCount + 2;

        public RecordBucket WriterBucket(int writer) => WriterBuckets[writer];

        public int WriterRecordCount(int writer) => WriterCounts[writer];

        public void BucketWriter(int writer)
        {
            var bucket = WriterBuckets[writer];
            WriterLost[writer * 8] = Bucket(WriterRecords[writer], WriterCounts[writer], bucket.Records, bucket.Starts);
        }

        public int Bucket(ulong* records, int count, ulong* sorted, int* starts)
        {
            var chunks = ChunkCount;
            var slots = (uint)SlotCount;
            for (var b = 0; b < chunks + 2; b++)
            {
                starts[b] = 0;
            }

            var outside = 0;
            for (var i = 0; i < count; i++)
            {
                var target = (uint)(records[i] >> Record.TargetShift);
                if (target >= slots)
                {
                    outside++;
                    continue;
                }

                starts[(target >> 6) + 2]++;
            }

            for (var b = 2; b < chunks + 2; b++)
            {
                starts[b] += starts[b - 1];
            }

            for (var i = 0; i < count; i++)
            {
                var record = records[i];
                var target = (uint)(record >> Record.TargetShift);
                if (target >= slots)
                {
                    continue;
                }

                sorted[starts[(target >> 6) + 1]++] = record;
            }

            return outside;
        }

        public long Resolve(int part, RecordBucket* buckets, int bucketCount)
        {
            var from = (int)((long)ChunkCount * part / PartCount);
            var to = (int)((long)ChunkCount * (part + 1) / PartCount);
            var area = new Scratch(ScratchArea + (long)part * ScratchBytes, Tables);
            var allAdmit = Tables->AllAdmit;
            var factors = Tables->Factors;
            var lost = 0L;
            for (var chunk = from; chunk < to; chunk++)
            {
                var chunkAlive = Alive[chunk];
                if (chunkAlive == 0)
                {
                    for (var b = 0; b < bucketCount; b++)
                    {
                        lost += buckets[b].Starts[chunk + 1] - buckets[b].Starts[chunk];
                    }

                    continue;
                }

                var totals = area.Totals;
                for (var i = 0; i < ClassLongs; i++)
                {
                    totals[i] = 0;
                }

                var chunkGates = Gates + (long)chunk * Law.Rows;
                for (var b = 0; b < bucketCount; b++)
                {
                    var bucket = buckets[b];
                    var first = bucket.Starts[chunk];
                    var end = bucket.Starts[chunk + 1];
                    if (first < end)
                    {
                        ColumnWorld.AddInChunk(bucket.Records, first, end, chunk, totals, chunkGates, chunkAlive, allAdmit, factors, out var missed);
                        lost += missed;
                    }
                }

                var template = Tables->Templates + ChunkTemplates[chunk];
                var chunkLanes = Lanes + (long)chunk * ChunkInts;
                if (Kernel == FrameKernel.Rows)
                {
                    RowOps.AddOps(Tables, template, chunkLanes, chunkGates, chunkAlive, area);
                }
                else
                {
                    Law.AddOps(Tables, template, chunkLanes, chunkGates, chunkAlive, totals, area.Preds);
                }

                for (var row = 0; row < Law.Rows; row++)
                {
                    if (((chunkAlive >> row) & 1ul) != 0)
                    {
                        Law.ResolveRow(Tables, chunkLanes, totals, area.Wide, area.Gain, row);
                        chunkGates[row] = Law.GateOf(Tables, chunkLanes, row);
                    }
                }
            }

            PartLost[part * 8] = lost;
            return lost;
        }
    }
}
