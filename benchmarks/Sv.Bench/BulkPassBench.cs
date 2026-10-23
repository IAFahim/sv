using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;
using static Program;

// Roadmap 6.5 receipt: a bulk pass over 32M rows (128 MiB value column, per-row bound columns -
// the statclamp bulk shape, five streams per row) - sequential Kernel.Run vs Bulk.Run's
// persistent pool, measured interleaved (seq, bulk, seq, bulk...) so both see the same
// cache/DRAM state. L9 is the hard gate (results must equal the sequential run, verified
// against a fresh copy); the speedup gate only trips on pool pathologies - on this hardware
// one core already near-saturates DRAM for stream-shaped kernels, so the honest ratio is
// modest (1.1-1.3x) and the gate is no-regression, not scaling.

internal static unsafe class BulkPass
{
    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "bulkpass")
        {
            return 0;
        }

        const int rows = 1 << 25;
        var value = (int*)NativeMemory.AllocZeroed((nuint)rows * sizeof(int));
        var twin = (int*)NativeMemory.AllocZeroed((nuint)rows * sizeof(int));
        var applied = (int*)NativeMemory.AllocZeroed((nuint)rows * sizeof(int));
        var minRows = (int*)NativeMemory.Alloc((nuint)rows * sizeof(int));
        var maxRows = (int*)NativeMemory.Alloc((nuint)rows * sizeof(int));
        try
        {
            for (var i = 0; i < rows; i++)
            {
                value[i] = i & 1023;
                twin[i] = i & 1023;
                minRows[i] = int.MinValue / 2;
                maxRows[i] = int.MaxValue / 2;
            }

            var column = new Column<int>(value, rows);
            var twinColumn = new Column<int>(twin, rows);
            var appliedColumn = new Column<int>(applied, rows);
            var bounds = Bounds.Rows(minRows, maxRows);
            var delta = new Delta(1);
            var window = Window.Range(0, rows);
            Kernel.Run(twinColumn, window, delta, bounds);
            Bulk.Run(column, window, delta, bounds);
            for (var i = 0; i < rows; i += 4096)
            {
                if (value[i] != twin[i])
                {
                    Console.WriteLine(Markdown ? "| bulkpass | FAIL: diverged from sequential | - | - |" : "bulkpass               FAIL diverged from sequential");
                    return 1;
                }
            }

            for (var i = 0; i < 8; i++)
            {
                Kernel.Run(column, window, delta, bounds, appliedColumn);
                Bulk.Run(column, window, delta, bounds, appliedColumn);
            }

            const int samples = 32;
            var seq = new double[samples];
            var bulk = new double[samples];
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < samples; i++)
            {
                var sw = Stopwatch.GetTimestamp();
                Kernel.Run(column, window, delta, bounds, appliedColumn);
                seq[i] = (Stopwatch.GetTimestamp() - sw) * 1e3 / Stopwatch.Frequency;
                sw = Stopwatch.GetTimestamp();
                Bulk.Run(column, window, delta, bounds, appliedColumn);
                bulk[i] = (Stopwatch.GetTimestamp() - sw) * 1e3 / Stopwatch.Frequency;
            }

            var allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            Array.Sort(seq);
            Array.Sort(bulk);
            var speedup = seq[samples / 2] / bulk[samples / 2];
            if (allocDelta != 0)
            {
                Console.WriteLine(Markdown ? $"| bulkpass | FAIL: allocated {allocDelta} B | - | - |" : $"bulkpass               FAIL allocated {allocDelta} B");
                return 1;
            }

            if (speedup < 1.0)
            {
                Console.WriteLine(Markdown ? $"| bulkpass | FAIL: speedup {speedup:0.00}x < 1.0x | - | - |" : $"bulkpass               FAIL speedup {speedup:0.00}x  (seq {seq[samples / 2]:0.00} ms  bulk {bulk[samples / 2]:0.00} ms)");
                return 1;
            }

            Console.WriteLine(Markdown
                ? $"| bulkpass | {seq[samples / 2]:0.0} -> {bulk[samples / 2]:0.0} ms - {speedup:0.0}x on {Environment.ProcessorCount} cores | {rows:N0} rows, 5 streams | 0 B |"
                : $"bulkpass               seq {seq[samples / 2]:0.00} ms  bulk {bulk[samples / 2]:0.00} ms  speedup {speedup:0.00}x  cores {Environment.ProcessorCount}");
            return 0;
        }
        finally
        {
            NativeMemory.Free(value);
            NativeMemory.Free(twin);
            NativeMemory.Free(applied);
            NativeMemory.Free(minRows);
            NativeMemory.Free(maxRows);
        }
    }
}
