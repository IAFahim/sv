using System;
using System.Diagnostics;
using Sv;

internal static class Statop10k
{
    const int Slots = 100_000;

    const int Touched = 5_000;

    const int Ops = Touched * 2;

    const int Reps = 41;

    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "statop10k")
        {
            return 0;
        }

        var schema = SchemaBuilder.Create().Stat("max_hp", 100f).Stat("armor", 10f).Stat("haste", 0f).Stat("power", 5f).Build();
        var stats = new[] { schema.Stat("max_hp"), schema.Stat("armor"), schema.Stat("haste"), schema.Stat("power") };
        var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi, ModifierKind.Cap };
        using var batchBoard = Board.Create(schema, Slots);
        using var loopBoard = Board.Create(schema, Slots);
        for (var i = 0; i < Slots; i++)
        {
            batchBoard.Acquire();
            loopBoard.Acquire();
        }

        batchBoard.Recalculate();
        loopBoard.Recalculate();
        var random = new Random(10_000);
        var slots = new int[Touched];
        for (var i = 0; i < Touched; i++)
        {
            slots[i] = random.Next(Slots);
        }

        var input = new StatOp[2][];
        for (var parity = 0; parity < 2; parity++)
        {
            var ops = new StatOp[Ops];
            for (var i = 0; i < Touched; i++)
            {
                var source = 0x40000000 | (i << 1) | parity;
                var stale = 0x40000000 | (i << 1) | (1 - parity);
                ops[i * 2] = StatOp.RemoveSource(slots[i], stale);
                ops[i * 2 + 1] = StatOp.Add(slots[i], stats[i & 3], kinds[(i >> 2) & 3], 1f + (i % 17) * 0.25f, source);
            }

            for (var i = ops.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (ops[i], ops[j]) = (ops[j], ops[i]);
            }

            input[parity] = ops;
        }

        var canonical = new StatOp[2][];
        var scratch = new StatOp[Ops];
        for (var parity = 0; parity < 2; parity++)
        {
            canonical[parity] = (StatOp[])input[parity].Clone();
            StatOps.Canonicalize(canonical[parity], scratch);
        }

        var work = new StatOp[Ops];
        var sortSamples = new double[Reps];
        var batchSamples = new double[Reps];
        var loopSamples = new double[Reps];
        var allocDelta = 0L;
        var verify = "ok";
        for (var rep = 0; rep < Reps; rep++)
        {
            var parity = rep & 1;
            input[parity].CopyTo(work, 0);
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            StatOps.Canonicalize(work, scratch);
            var sorted = Stopwatch.GetTimestamp();
            var changedBatch = batchBoard.Apply(work);
            var applied = Stopwatch.GetTimestamp();
            var changedLoop = 0;
            var ops = canonical[parity];
            for (var i = 0; i < ops.Length; i++)
            {
                var op = ops[i];
                if (op.Op == StatOpKind.RemoveSource)
                {
                    changedLoop += loopBoard.RemoveSource(op.Slot, op.Source) > 0 ? 1 : 0;
                }
                else
                {
                    loopBoard.AddModifier(op.Slot, stats[op.Stat], new Modifier(op.Kind, BitConverter.UInt32BitsToSingle(op.ValueBits)), op.Source, op.ExpiresAt);
                    changedLoop++;
                }
            }

            var looped = Stopwatch.GetTimestamp();
            allocDelta += GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            sortSamples[rep] = (sorted - start) * 1e9 / Stopwatch.Frequency / Ops;
            batchSamples[rep] = (applied - start) * 1e9 / Stopwatch.Frequency / Ops;
            loopSamples[rep] = (looped - applied) * 1e9 / Stopwatch.Frequency / Ops;
            if (changedBatch != changedLoop)
            {
                verify = $"FAIL batch changed {changedBatch} != loop {changedLoop}";
            }

            batchBoard.Recalculate();
            loopBoard.Recalculate();
        }

        for (var i = 0; i < Slots; i += 997)
        {
            for (var s = 0; s < stats.Length; s++)
            {
                if (BitConverter.SingleToUInt32Bits(batchBoard.Stat(i, stats[s])) != BitConverter.SingleToUInt32Bits(loopBoard.Stat(i, stats[s])))
                {
                    verify = $"FAIL batch and loop boards differ at slot {i} stat {s}";
                }
            }
        }

        if (allocDelta != 0)
        {
            verify = $"FAIL allocated {allocDelta} managed bytes";
        }

        Array.Sort(sortSamples, 1, Reps - 1);
        Array.Sort(batchSamples, 1, Reps - 1);
        Array.Sort(loopSamples, 1, Reps - 1);
        var sort = sortSamples[1 + (Reps - 1) / 2];
        var batch = batchSamples[1 + (Reps - 1) / 2];
        var loop = loopSamples[1 + (Reps - 1) / 2];
        var applyOnly = batch - sort;
        var line = $"statop10k  batch (canonicalize+apply) {batch:0.0} ns/op = sort {sort:0.0} + apply {applyOnly:0.0}; loop of calls {loop:0.0} ns/op  [{Ops:N0} ops (5k remove + 5k add) on {Slots:N0} live slots, 1 thread, median of {Reps - 1}]  target<=60:{(batch <= 60 ? "pass" : "miss")}  kill(apply slower than loop):{(applyOnly > loop ? "TRIPPED" : "clear")}";
        Console.WriteLine(verify.StartsWith("ok", StringComparison.Ordinal) ? line + "  alloc=0B" : $"statop10k  FAIL: {verify}");
        return verify.StartsWith("ok", StringComparison.Ordinal) ? 0 : 1;
    }
}
