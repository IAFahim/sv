namespace Sv.Laws.Tests;

public sealed unsafe class InputLogCapLaws
{
    static void BuildCanonicalFrame(int[] perChunkExternal, int internalPerChunk, long seedOffset, out LoggedRecord* records, out int count, out int* start, out int* active, out int runs, out LoggedRecord[] expectedExternal)
    {
        var list = new List<(int Chunk, LoggedRecord Record)>();
        var externals = new List<LoggedRecord>();
        for (var chunk = 0; chunk < perChunkExternal.Length; chunk++)
        {
            var extCount = perChunkExternal[chunk];
            for (var i = 0; i < internalPerChunk; i++)
            {
                var target = chunk * Slot.Rows + (i % Slot.Rows);
                var key = EffectKey.Pack(target, 1, 0, 0, Origin.Reflected, (int)(seedOffset + i));
                list.Add((chunk, new LoggedRecord(key, 10 + i, 1)));
            }

            for (var i = 0; i < extCount; i++)
            {
                var target = chunk * Slot.Rows + (i % Slot.Rows);
                var origin = (i & 1) == 0 ? Origin.Direct : Origin.Timeline;
                var key = EffectKey.Pack(target, (int)(2 + ((seedOffset + i) % 32)), (byte)(i & 1), (byte)((i >> 1) & 3), origin, 0);
                var rec = new LoggedRecord(key, 5 + i + (int)(seedOffset % 17), 1 + (i & 1));
                list.Add((chunk, rec));
                externals.Add(rec);
            }
        }

        list.Sort((a, b) =>
        {
            var c = a.Chunk.CompareTo(b.Chunk);
            return c != 0 ? c : LoggedRecordLaw.Compare(a.Record, b.Record);
        });

        externals.Sort((a, b) => LoggedRecordLaw.Compare(a, b));
        expectedExternal = externals.ToArray();

        count = list.Count;
        records = (LoggedRecord*)Memory.Alloc((nuint)Math.Max(1, count), (nuint)sizeof(LoggedRecord));
        start = (int*)Memory.AllocZeroed((nuint)(perChunkExternal.Length + 1), sizeof(int));
        active = (int*)Memory.Alloc((nuint)perChunkExternal.Length, sizeof(int));
        runs = 0;
        var lastChunk = -1;
        for (var i = 0; i < count; i++)
        {
            records[i] = list[i].Record;
            var ch = list[i].Chunk;
            if (ch != lastChunk)
            {
                while (lastChunk < ch)
                {
                    lastChunk++;
                    start[lastChunk] = i;
                }

                active[runs++] = ch;
            }
        }

        for (var c = lastChunk + 1; c <= perChunkExternal.Length; c++)
        {
            start[c] = count;
        }
    }

    static LoggedRecord[] Snapshot(InputLog log, long frame)
    {
        var n = log.RecordCount(frame);
        var result = new LoggedRecord[n];
        if (n == 0)
        {
            return result;
        }

        fixed (LoggedRecord* dst = result)
        {
            var copied = log.CopyRecords(frame, dst, n);
            Assert.Equal(n, copied);
        }

        return result;
    }

    static void AssertEqualRecords(LoggedRecord[] expected, int expectedCount, LoggedRecord[] actual)
    {
        Assert.Equal(expectedCount, actual.Length);
        for (var i = 0; i < expectedCount; i++)
        {
            Assert.Equal(expected[i].Key, actual[i].Key);
            Assert.Equal(expected[i].Amount, actual[i].Amount);
            Assert.Equal(expected[i].Hits, actual[i].Hits);
        }
    }

    [Fact]
    public void PerFrameRecordCapIsDeterministicAcrossRecordAndParallelScan()
    {
        const int cap = 37;
        var chunks = new[] { 20, 15, 25, 10 };
        BuildCanonicalFrame(chunks, 3, 0, out var records, out var count, out var start, out var active, out var runs, out var expected);
        try
        {
            using var logDirect = new InputLog(8, chunks.Length, 0, cap);
            using var logSingle = new InputLog(8, chunks.Length, 0, cap);
            using var logMulti = new InputLog(8, chunks.Length, 0, cap);

            for (var frame = 0L; frame < 2L; frame++)
            {
                logDirect.Record(frame, records, count);

                var scan1 = logSingle.Scan(runs, 1);
                logSingle.ReservePart(0, count);
                LogScan.Examine(scan1, records, start, active, 0, runs, 0);
                logSingle.RecordScanned(frame, records, start, active, runs);

                var scan4 = logMulti.Scan(runs, 4);
                for (var part = 0; part < 4; part++)
                {
                    logMulti.ReservePart(part, count);
                }

                Parallel.For(0, 4, part =>
                {
                    var from = runs * part / 4;
                    var to = runs * (part + 1) / 4;
                    if (from < to)
                    {
                        LogScan.Examine(scan4, records, start, active, from, to, part);
                    }
                });
                logMulti.RecordScanned(frame, records, start, active, runs);

                var directOut = Snapshot(logDirect, frame);
                var singleOut = Snapshot(logSingle, frame);
                var multiOut = Snapshot(logMulti, frame);
                AssertEqualRecords(expected, cap, directOut);
                AssertEqualRecords(expected, cap, singleOut);
                AssertEqualRecords(expected, cap, multiOut);
            }

            Assert.Equal(66L, logDirect.DroppedRecords);
            Assert.Equal(66L, logSingle.DroppedRecords);
            Assert.Equal(66L, logMulti.DroppedRecords);
            Assert.Equal(2L, logDirect.CappedFrames);
            Assert.Equal(2L, logSingle.CappedFrames);
            Assert.Equal(2L, logMulti.CappedFrames);
        }
        finally
        {
            Memory.Free(records);
            Memory.Free(start);
            Memory.Free(active);
        }
    }

    [Fact]
    public void PerFrameByteCapBoundsBlocksPerFrameAndAllocatesNothingWarm()
    {
        var blockBytes = (long)InputLog.BlockRecords * sizeof(LoggedRecord);
        const int maxBlocksPerFrame = 2;
        using var log = new InputLog(8, 32, 0, int.MaxValue, maxBlocksPerFrame * blockBytes);

        var sparseCounts = Enumerable.Repeat(1, 16).ToArray();
        var denseCounts = new[] { 300, 100 };

        BuildCanonicalFrame(denseCounts, 0, 1, out var denseRecs, out var denseTotal, out _, out _, out _, out var denseExpected);
        try
        {
            log.Record(0L, denseRecs, denseTotal);
            var denseKept = Snapshot(log, 0L);
            AssertEqualRecords(denseExpected, 300, denseKept);
            Assert.Equal(100L, log.DroppedRecords);
        }
        finally
        {
            Memory.Free(denseRecs);
        }

        BuildCanonicalFrame(sparseCounts, 0, 2, out var sparseRecs, out var sparseTotal, out _, out _, out _, out var sparseExpected);
        try
        {
            log.Record(1L, sparseRecs, sparseTotal);
            var sparseKept = Snapshot(log, 1L);
            AssertEqualRecords(sparseExpected, maxBlocksPerFrame, sparseKept);
            Assert.Equal(100L + 14L, log.DroppedRecords);
        }
        finally
        {
            Memory.Free(sparseRecs);
        }

        var bigDense = new[] { 600 };
        var framePointers = new IntPtr[3];
        var frameCounts = new int[3];
        for (var k = 0; k < 3; k++)
        {
            BuildCanonicalFrame(bigDense, 0, 100 + k, out var framePtr, out frameCounts[k], out _, out _, out _, out _);
            framePointers[k] = (IntPtr)framePtr;
        }

        try
        {
            for (var frame = 2L; frame < 18L; frame++)
            {
                var idx = (int)(frame % 3);
                log.Record(frame, (LoggedRecord*)framePointers[idx], frameCounts[idx]);
                Assert.Equal(maxBlocksPerFrame * InputLog.BlockRecords, log.RecordCount(frame));
            }

            var warm = long.MaxValue;
            var end = 18L;
            for (var window = 0; window < 3; window++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = end; frame < end + 32L; frame++)
                {
                    var idx = (int)(frame % 3);
                    log.Record(frame, (LoggedRecord*)framePointers[idx], frameCounts[idx]);
                }

                var after = GC.GetAllocatedBytesForCurrentThread();
                warm = Math.Min(warm, after - before);
                end += 32L;
            }

            Assert.Equal(0L, warm);
            for (var frame = end - 8L; frame < end; frame++)
            {
                Assert.Equal(maxBlocksPerFrame * InputLog.BlockRecords, log.RecordCount(frame));
            }
            Assert.Equal(64L * InputLog.BlockRecords * sizeof(LoggedRecord), log.Bytes);
        }
        finally
        {
            for (var k = 0; k < 3; k++)
            {
                Memory.Free((LoggedRecord*)framePointers[k]);
            }
        }
    }

    [Fact]
    public void InsertRespectsPerFrameCapDeterministically()
    {
        const int cap = 25;
        using var log = new InputLog(4, 8, 0, cap);

        BuildCanonicalFrame(new[] { 0, 10, 10, 5 }, 0, 0, out var initial, out var initialCount, out _, out _, out _, out _);
        BuildCanonicalFrame(new[] { 12 }, 0, 99, out var late, out var lateCount, out _, out _, out _, out _);
        try
        {
            log.Record(0L, initial, initialCount);
            Assert.Equal(25, log.RecordCount(0L));
            Assert.Equal(0L, log.DroppedRecords);

            log.Insert(0L, late, lateCount);
            Assert.Equal(cap, log.RecordCount(0L));
            Assert.Equal(12L, log.DroppedRecords);
            Assert.Equal(1L, log.CappedFrames);

            var expected = new LoggedRecord[cap];
            for (var i = 0; i < 12; i++)
            {
                expected[i] = late[i];
            }

            for (var i = 0; i < 13; i++)
            {
                expected[12 + i] = initial[i];
            }

            AssertEqualRecords(expected, cap, Snapshot(log, 0L));
        }
        finally
        {
            Memory.Free(initial);
            Memory.Free(late);
        }
    }
}
