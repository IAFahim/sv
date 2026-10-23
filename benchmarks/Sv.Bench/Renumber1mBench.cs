using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;
using static Program;

// renumber1m — the roadmap 3.3 receipt (issues/0093, Roadmap.md 4.6 "structural changes in
// O(moved)"): 2^20 entities, 16 stats, template-spawned to full, one churn pass, then per
// iteration 64 add/remove pairs leave pending dirty state, a deterministic map swaps 4096
// slot pairs (8192 moved rows), RenumberSlots, Recalculate. Reports p50 of each phase and
// fails the scenario if the renumber exceeds 20 ms or the post-renumber Recalculate leaves
// the microsecond range (the pre-atom behavior re-resolved every live pair: ~1 s at FF-1M).
// Values are verified bit-exact against per-row expectation arrays permuted through the
// same map — O(moved rows) per frame, the honest twin: cross-board digests are not
// comparable because digest keys embed slot indices and set ids are interned per history.

internal sealed unsafe class Renumber1mBench : IDisposable
{
    const int Entities = 1 << 20;

    const int Swaps = 4096;

    const int MovedRows = Swaps * 2;

    const int Churn = 1024;

    const int PendingDirty = 64;

    readonly Board board;

    readonly StatId stat0;

    readonly StatId stat1;

    readonly int* spawnSlots;

    readonly int[] map;

    readonly int[] pool;

    readonly float[] expect0;

    readonly float[] expect1;

    readonly ModifierHandle[] transient;

    readonly double[] renumberSamples;

    readonly double[] recalcSamples;

    uint randomState;

    int sampled;

    Renumber1mBench(Board board, StatId stat0, StatId stat1, int* spawnSlots, int[] map, int[] pool, float[] expect0, float[] expect1, ModifierHandle[] transient)
    {
        this.board = board;
        this.stat0 = stat0;
        this.stat1 = stat1;
        this.spawnSlots = spawnSlots;
        this.map = map;
        this.pool = pool;
        this.expect0 = expect0;
        this.expect1 = expect1;
        this.transient = transient;
        renumberSamples = new double[8192];
        recalcSamples = new double[8192];
        randomState = 0x9E3779B9u;
    }

    static Schema Schema()
    {
        var builder = SchemaBuilder.Create();
        for (var i = 0; i < 16; i++)
        {
            builder.Stat($"stat_{i:D2}", 100f + i * 3f);
        }

        return builder.Build();
    }

    internal static Renumber1mBench Create()
    {
        var schema = Schema();
        var board = Board.Create(schema, Entities);
        var stat0 = schema.Stat("stat_00");
        var stat1 = schema.Stat("stat_01");
        var template = board.Acquire();
        board.Recalculate();
        var spawnSlots = Allocator.New<int>(Entities);
        board.Spawn(template, Entities - 1, spawnSlots);
        for (var slot = 0; slot < Entities; slot++)
        {
            board.AddModifier(slot, stat0, new Modifier(ModifierKind.Added, slot % 97));
            board.AddModifier(slot, stat1, new Modifier(ModifierKind.Added, slot % 61));
        }

        board.Recalculate();
        var expect0 = new float[Entities];
        var expect1 = new float[Entities];
        var column0 = board.OutputPtr(stat0);
        var column1 = board.OutputPtr(stat1);
        for (var slot = 0; slot < Entities; slot++)
        {
            expect0[slot] = column0[slot];
            expect1[slot] = column1[slot];
        }

        var holes = new int[Churn];
        for (var k = 0; k < Churn; k++)
        {
            holes[k] = k * 1024 + 1 + (k % 1000);
        }

        for (var k = 0; k < Churn; k++)
        {
            board.Release(holes[k]);
        }

        board.Spawn(template, Churn, spawnSlots);
        for (var k = 0; k < Churn; k++)
        {
            expect0[holes[k]] = expect0[template];
            expect1[holes[k]] = expect1[template];
        }

        var pool = new int[Entities];
        for (var slot = 0; slot < Entities; slot++)
        {
            pool[slot] = slot;
        }

        return new Renumber1mBench(board, stat0, stat1, spawnSlots, new int[Entities], pool, expect0, expect1, new ModifierHandle[PendingDirty]);
    }

    uint NextRandom()
    {
        var state = randomState;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        randomState = state;
        return state;
    }

    internal string Verify()
    {
        if (board.LiveCount != Entities)
        {
            return $"FAIL live {board.LiveCount} != {Entities}";
        }

        var column0 = board.OutputPtr(stat0);
        var column1 = board.OutputPtr(stat1);
        var mismatch = -1;
        for (var slot = 0; slot < Entities; slot++)
        {
            if (BitConverter.SingleToUInt32Bits(column0[slot]) != BitConverter.SingleToUInt32Bits(expect0[slot])
                || BitConverter.SingleToUInt32Bits(column1[slot]) != BitConverter.SingleToUInt32Bits(expect1[slot]))
            {
                mismatch = slot;
                break;
            }
        }

        if (mismatch >= 0)
        {
            return $"FAIL row {mismatch}: {column0[mismatch]} / {column1[mismatch]} != {expect0[mismatch]} / {expect1[mismatch]}";
        }

        return $"ok rows={Entities:N0} moved={MovedRows:N0}/frame";
    }

    internal long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            for (var j = 0; j < PendingDirty; j++)
            {
                var slot = (int)(NextRandom() % (uint)Entities);
                transient[j] = board.AddModifier(slot, (j & 1) == 0 ? stat0 : stat1, new Modifier(ModifierKind.Multi, 1.5f));
                board.RemoveModifier(slot, (j & 1) == 0 ? stat0 : stat1, transient[j]);
            }

            for (var slot = 0; slot < Entities; slot++)
            {
                map[slot] = slot;
            }

            var probe = 0;
            for (var s = 0; s < Swaps * 2; s++)
            {
                var j = s + (int)(NextRandom() % (uint)(Entities - s));
                (pool[s], pool[j]) = (pool[j], pool[s]);
            }

            for (var s = 0; s < Swaps; s++)
            {
                var a = pool[s * 2];
                var b = pool[s * 2 + 1];
                map[a] = b;
                map[b] = a;
                (expect0[a], expect0[b]) = (expect0[b], expect0[a]);
                (expect1[a], expect1[b]) = (expect1[b], expect1[a]);
                probe = b;
            }

            var start = Stopwatch.GetTimestamp();
            board.RenumberSlots(map, board.LiveCount);
            var renumberTicks = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            board.Recalculate();
            var recalcTicks = Stopwatch.GetTimestamp() - start;
            var at = sampled % renumberSamples.Length;
            renumberSamples[at] = renumberTicks / (double)Stopwatch.Frequency * 1_000_000.0;
            recalcSamples[at] = recalcTicks / (double)Stopwatch.Frequency * 1_000_000.0;
            sampled++;
            checksum += (long)BitConverter.SingleToUInt32Bits(board.OutputPtr(stat0)[probe]);
        }

        return checksum;
    }

    internal (double RenumberP50, double RecalcP50) Percentiles()
    {
        var n = Math.Min(sampled, renumberSamples.Length);
        var renumber = new double[n];
        var recalc = new double[n];
        Array.Copy(renumberSamples, renumber, n);
        Array.Copy(recalcSamples, recalc, n);
        Array.Sort(renumber);
        Array.Sort(recalc);
        return (renumber[n / 2], recalc[n / 2]);
    }

    public void Dispose()
    {
        board.Dispose();
        NativeMemory.Free(spawnSlots);
    }
}

internal static class Renumber1m
{
    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "renumber1m")
        {
            return 0;
        }

        using var bench = Renumber1mBench.Create();
        var verify = bench.Verify();
        bench.Frame(16);
        verify = bench.Verify();
        var sw = Stopwatch.GetTimestamp();
        bench.Frame(8);
        var perCall = Math.Max((Stopwatch.GetTimestamp() - sw) / 8.0 / Stopwatch.Frequency, 1e-9);
        var iterations = Math.Max(8, (int)(ms / 3000.0 / perCall));
        for (var b = 0; b < 3; b++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var checksum = bench.Frame(iterations);
            if (GC.GetAllocatedBytesForCurrentThread() - before != 0 || checksum == long.MinValue)
            {
                verify = "FAIL allocated managed bytes on the warm frame";
            }
        }

        verify = bench.Verify();

        if (!verify.StartsWith("ok", StringComparison.Ordinal))
        {
            Console.WriteLine(Markdown ? $"| renumber1m | FAIL: {verify} | — | — |" : $"renumber1m              FAIL: {verify}");
            return 1;
        }

        var (renumberP50, recalcP50) = bench.Percentiles();
        var receipt = renumberP50 <= 20_000.0 ? "ok" : $"FAIL renumber p50 {renumberP50:0} us > 20000";
        if (recalcP50 > 1_000.0)
        {
            receipt = $"FAIL recalc p50 {recalcP50:0} us > 1000";
        }

        if (receipt != "ok")
        {
            Console.WriteLine(Markdown ? $"| renumber1m | FAIL: {receipt} | — | — |" : $"renumber1m              FAIL: {receipt}");
            return 1;
        }

        if (Markdown)
        {
            Console.WriteLine($"| renumber1m | {renumberP50:0.0} | renumber µs · recalc {recalcP50:0.0} µs · 1,048,576 rows · 8,192 moved | 0 B |");
        }
        else
        {
            Console.WriteLine($"renumber1m              {renumberP50,9:0.0} us RenumberSlots  {recalcP50,9:0.0} us Recalculate  (1,048,576 rows, 8,192 moved/frame)");
        }

        return 0;
    }
}
