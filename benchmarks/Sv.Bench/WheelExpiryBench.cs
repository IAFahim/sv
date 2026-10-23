using System;
using System.Diagnostics;
using Sv;
using static Program;

// Issue 0091 pickup: a dedicated expiry scenario. Steady state holds ~262k
// pending expiries (the ff1m shape) with delays uniform in [1, 512] so both
// wheel levels stay populated and every L1 wrap cascades. The ring histogram
// predicts the exact due count per tick and integer accounting predicts the
// exact pending count, so Verify pins O(due) behavior, not just throughput.

internal sealed unsafe class WheelExpiryBench : BenchBase
{
    const int Entities = 1 << 20;

    const int Due = 1024;

    const int SpanTicks = 512;

    const int Ring = 1024;

    Board board;

    StatId power;

    uint seed = 0x9E3779B9u;

    long tick;

    long removedLast;

    long dueExpected;

    long addedTotal;

    long removedTotal;

    int[] dueRing = new int[Ring];

    internal long ExpireTicks;

    internal long Pending => board.PendingExpiries;

    long pendingExpected;

    long checksum;

    internal static WheelExpiryBench New()
    {
        return new WheelExpiryBench();
    }

    WheelExpiryBench()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
        power = schema.Stat("power");
        board = Board.Create(schema, Entities);
        for (var i = 0; i < Entities; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        for (var warm = 0; warm < SpanTicks * 2; warm++)
        {
            Tick();
        }
    }

    uint Next()
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return seed;
    }

    void Tick()
    {
        tick++;
        dueExpected = dueRing[(int)(tick % Ring)];
        var sw = Stopwatch.GetTimestamp();
        removedLast = board.ExpireModifiers(tick);
        ExpireTicks += Stopwatch.GetTimestamp() - sw;
        removedTotal += removedLast;
        for (var i = 0; i < Due; i++)
        {
            var slot = (int)(Next() % (uint)Entities);
            var delay = 1 + (int)(Next() % SpanTicks);
            board.AddModifier(slot, power, new Modifier(ModifierKind.Multi, 1f + (Next() % 3) * 0.25f), 0, tick + delay);
            dueRing[(int)((tick + delay) % Ring)]++;
            addedTotal++;
        }

        board.Recalculate();
        pendingExpected = addedTotal - removedTotal;
        dueRing[(int)(tick % Ring)] = 0;
    }

    public override long Frame(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            Tick();
            checksum = checksum * 31 + removedLast * 31 + (long)board.Digest;
        }

        return checksum;
    }

    public override string Verify()
    {
        Tick();
        if (removedLast != dueExpected)
        {
            return $"due mismatch at tick {tick}: expired {removedLast}, expected {dueExpected}";
        }

        if (board.PendingExpiries != pendingExpected)
        {
            return $"pending mismatch at tick {tick - 1}: {board.PendingExpiries}, expected {pendingExpected}";
        }

        return "ok";
    }

    public override int RowsPerFrame => 1;

    public override void Dispose()
    {
        board.Dispose();
    }
}

internal static class WheelExpiry
{
    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "wheel-expiry")
        {
            return 0;
        }

        using var bench = WheelExpiryBench.New();
        var verify = bench.Verify();
        bench.Frame(64);
        verify = bench.Verify();
        var sw = Stopwatch.GetTimestamp();
        bench.Frame(8);
        var perCall = Math.Max((Stopwatch.GetTimestamp() - sw) / 8.0 / Stopwatch.Frequency, 1e-9);
        var iterations = Math.Max(8, (int)(ms / 3000.0 / perCall));
        var batches = new double[5];
        var expireBatches = new double[5];
        for (var b = 0; b < batches.Length; b++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var expireBefore = bench.ExpireTicks;
            var batchSw = Stopwatch.GetTimestamp();
            var checksum = bench.Frame(iterations);
            var ticks = Stopwatch.GetTimestamp() - batchSw;
            if (GC.GetAllocatedBytesForCurrentThread() - before != 0 || checksum == long.MinValue)
            {
                verify = "FAIL allocated managed bytes on the warm frame";
            }

            batches[b] = ticks / (double)iterations / Stopwatch.Frequency * 1_000_000.0;
            expireBatches[b] = (bench.ExpireTicks - expireBefore) / (double)iterations / Stopwatch.Frequency * 1_000_000.0;
        }

        Array.Sort(batches);
        Array.Sort(expireBatches);
        verify = bench.Verify();
        if (!verify.StartsWith("ok", StringComparison.Ordinal))
        {
            Console.WriteLine(Markdown ? $"| wheel-expiry | FAIL: {verify} | — | — |" : $"wheel-expiry             FAIL: {verify}");
            return 1;
        }

        var expireP50 = expireBatches[2];
        var tickP50 = batches[2];
        var receipt = expireP50 <= 500.0 ? "ok" : $"FAIL expire p50 {expireP50:0} us > 500";
        if (tickP50 > 1500.0)
        {
            receipt = $"FAIL tick p50 {tickP50:0} us > 1500";
        }

        if (receipt != "ok")
        {
            Console.WriteLine(Markdown ? $"| wheel-expiry | FAIL: {receipt} | — | — |" : $"wheel-expiry             FAIL: {receipt}");
            return 1;
        }

        if (Markdown)
        {
            Console.WriteLine($"| wheel-expiry | {expireP50:0.0} | Expire µs · tick {tickP50:0.0} µs · {bench.Pending:N0} pending · {1024:N0} due/tick | 0 B |");
        }
        else
        {
            Console.WriteLine($"wheel-expiry            {expireP50,9:0.0} us Expire  {tickP50,9:0.0} us tick  ({bench.Pending:N0} pending, 1,024 due/tick)");
        }

        return 0;
    }
}
