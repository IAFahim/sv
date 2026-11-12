using System.Runtime.InteropServices;
using Sv;
using Xunit;

[Collection(HeavyAllocationCollection.Name)]
public unsafe class InboxLanesTests
{
    const int Rows = 65_536;

    static ulong Hash(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15ul;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ul;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBul;
            return x ^ (x >> 31);
        }
    }

    static (int Target, int Amount) Send(int row, int k, int hotPercent, int tick)
    {
        var h = Hash(((ulong)tick << 40) ^ ((ulong)row << 4) ^ (ulong)k);
        var amount = (h & 0xFF) == 0 ? ((h >> 8 & 1) == 0 ? int.MaxValue : int.MinValue) : (int)((h >> 8) % 41) - 20;
        var pick = (int)((h >> 20) % 100);
        var target = pick < hotPercent ? (int)((h >> 40) % 3) : (int)((h >> 32) % Rows);
        return (target, amount);
    }

    static ulong Run(int workers, int cells, int hotPercent, bool flushPerWorker, int shuffleSeed, int ticks)
    {
        using var inbox = new InboxBuffer(Rows);
        using var lanes = workers == 0 ? null : new InboxLanes(inbox, workers, cells);
        var value = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var delta = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var changed = (ulong*)NativeMemory.AlignedAlloc(Rows / 64 * sizeof(ulong), 64);
        try
        {
            for (var row = 0; row < Rows; row++)
            {
                value[row] = row % 200;
                delta[row] = 0;
            }

            var digest = 0ul;
            for (var tick = 0; tick < ticks; tick++)
            {
                if (lanes == null)
                {
                    var cellsPointer = inbox.Pointer;
                    for (var row = 0; row < Rows; row++)
                    {
                        for (var k = 0; k < 3; k++)
                        {
                            var (target, amount) = Send(row, k, hotPercent, tick);
                            Inbox.Send(cellsPointer, target, amount);
                        }
                    }
                }
                else
                {
                    var t = tick;
                    void Part(int part)
                    {
                        var lane = lanes.Lane(part);
                        var first = (int)((long)part * Rows / workers);
                        var last = (int)((long)(part + 1) * Rows / workers);
                        for (var row = first; row < last; row++)
                        {
                            for (var k = 0; k < 3; k++)
                            {
                                var (target, amount) = Send(row, k, hotPercent, t);
                                lane->Send(target, amount);
                            }
                        }

                        if (flushPerWorker)
                        {
                            lane->Flush();
                        }
                    }

                    if (shuffleSeed != 0)
                    {
                        var random = new Random(shuffleSeed + tick);
                        var order = Enumerable.Range(0, workers).OrderBy(_ => random.Next()).ToArray();
                        foreach (var part in order)
                        {
                            Part(part);
                        }
                    }
                    else
                    {
                        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, Part);
                    }

                    if (!flushPerWorker)
                    {
                        lanes.FlushAll();
                    }

                    for (var w = 0; w < workers; w++)
                    {
                        Assert.Equal(0, lanes.Lane(w)->Pending);
                    }
                }

                var inboxDigest = 0ul;
                var cellsAfter = inbox.Pointer;
                for (var slot = 0; slot < Rows; slot++)
                {
                    inboxDigest = unchecked(inboxDigest + CellDigest.Key(6, slot, (uint)cellsAfter[slot]) + CellDigest.Key(7, slot, (uint)(cellsAfter[slot] >> 32)));
                }

                Kernel.Fold(new Column<int>(value, Rows), Window.Range(0, Rows), new Column<int>(delta, Rows), Bounds.Constant(-1000, 1000), new Column<ulong>(changed, Rows / 64), inbox.Column);
                for (var slot = 0; slot < Rows; slot++)
                {
                    Assert.Equal(0L, cellsAfter[slot]);
                }

                digest = unchecked(digest * 31 + inboxDigest + CellDigest.Sum(1, value, 0, Rows));
            }

            return digest;
        }
        finally
        {
            NativeMemory.AlignedFree(value);
            NativeMemory.AlignedFree(delta);
            NativeMemory.AlignedFree(changed);
        }
    }

    [Fact]
    public void Lanes_EqualTheSerialAtomicPath_AtEveryWorkerCount()
    {
        foreach (var hot in new[] { 10, 0, 100 })
        {
            var serial = Run(0, 0, hot, false, 0, 3);
            foreach (var workers in new[] { 1, 2, 4, 11 })
            {
                Assert.Equal(serial, Run(workers, InboxLanes.DefaultCells, hot, workers % 2 == 1, 0, 3));
                Assert.Equal(serial, Run(workers, 16, hot, workers % 2 == 0, 0, 3));
            }

            Assert.Equal(serial, Run(11, 64, hot, false, 0x5EED, 3));
            Assert.Equal(serial, Run(7, 16, hot, true, 0xABC, 3));
        }
    }

    [Fact]
    public void Lanes_RejectBadShapes_AndHotSlotsStayInTheLane()
    {
        using var inbox = new InboxBuffer(128);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxLanes(inbox, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxLanes(inbox, 2, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxLanes(inbox, 2, 8));
        using var lanes = new InboxLanes(inbox, 2, 16);
        Assert.Throws<ArgumentOutOfRangeException>(() => lanes.Lane(2));
        var lane = lanes.Lane(1);
        for (var i = 0; i < 1000; i++)
        {
            lane->Send(5, 3);
        }

        Assert.Equal(0L, inbox.Pointer[5]);
        Assert.Equal(1, lane->Pending);
        lanes.FlushAll();
        Assert.Equal(3000L, inbox.Pointer[5]);
        Assert.Equal(0, lane->Pending);
    }

    [Fact]
    public void Lane_BypassesToAtomics_WhenTargetsSpread_AndReturnsAfterFlush()
    {
        using var inbox = new InboxBuffer(1 << 16);
        using var lanes = new InboxLanes(inbox, 1, 16);
        var lane = lanes.Lane(0);
        var expected = new long[1 << 16];
        for (var i = 0; i < InboxLane.ProbeSends + 10; i++)
        {
            var target = (int)((uint)(i * 40503) % (1 << 16));
            lane->Send(target, i % 5 - 2);
            expected[target] += i % 5 - 2;
        }

        Assert.True(lane->Bypassing);
        for (var i = 0; i < 1000; i++)
        {
            lane->Send(9, 1);
            expected[9] += 1;
        }

        lanes.FlushAll();
        Assert.False(lane->Bypassing);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], inbox.Pointer[i]);
        }

        for (var i = 0; i < InboxLane.ProbeSends * 2; i++)
        {
            lane->Send(3, 1);
        }

        Assert.False(lane->Bypassing);
        Assert.Equal(1, lane->Pending);
    }

    [Fact]
    public void LaneSends_AllocateZeroManagedBytes_Warm()
    {
        using var inbox = new InboxBuffer(4096);
        using var lanes = new InboxLanes(inbox, 1, 64);
        var lane = lanes.Lane(0);
        for (var i = 0; i < 10_000; i++)
        {
            lane->Send(i * 7 % 4096, 1);
        }

        lanes.FlushAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++)
        {
            lane->Send(i % 3 == 0 ? 1 : i * 13 % 4096, 1);
        }

        lanes.FlushAll();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var sum = 0L;
        for (var i = 0; i < 4096; i++)
        {
            sum += inbox.Pointer[i];
        }

        Assert.Equal(110_000L, sum);
    }
}
