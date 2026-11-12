using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;
using static Program;

internal static unsafe class Fold1m
{
    const int Count = 1 << 20;

    const int Intrinsics = 4;

    const int Reps = 41;

    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "fold1m")
        {
            return 0;
        }

        var schema = SchemaBuilder.Create().Stat("max_hp", 1000f).Stat("max_mana", 500f).Stat("max_stamina", 300f).Stat("max_shield", 200f).Build();
        using var board = Board.Create(schema, Count);
        var stats = new[] { schema.Stat("max_hp"), schema.Stat("max_mana"), schema.Stat("max_stamina"), schema.Stat("max_shield") };
        var value = new int*[Intrinsics];
        var spare = new int*[Intrinsics];
        var delta = new int*[Intrinsics];
        var source = new int*[Intrinsics];
        var changed = new ulong*[Intrinsics];
        var random = new Random(42);
        for (var k = 0; k < Intrinsics; k++)
        {
            value[k] = Allocator.Zeroed<int>(Count);
            spare[k] = Allocator.Zeroed<int>(Count);
            delta[k] = Allocator.Zeroed<int>(Count);
            source[k] = Allocator.Zeroed<int>(Count);
            changed[k] = Allocator.Zeroed<ulong>(Count / 64);
            for (var i = 0; i < Count; i++)
            {
                value[k][i] = random.Next(0, 200);
                source[k][i] = random.Next(4) == 0 ? 0 : random.Next(-30, 30);
            }

            Buffer.MemoryCopy(value[k], spare[k], Count * 4L, Count * 4L);
        }

        for (var i = 0; i < Count; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        var statBounds = new Bounds[Intrinsics];
        var constantBounds = new Bounds[Intrinsics];
        for (var k = 0; k < Intrinsics; k++)
        {
            board.OutputPtr(stats[k]);
            statBounds[k] = Bounds.Stats(board, null, stats[k]);
            constantBounds[k] = Bounds.Constant(0, 150 + k * 50);
        }

        var verify = "ok";
        for (var k = 0; k < Intrinsics; k++)
        {
            Buffer.MemoryCopy(source[k], delta[k], Count * 4L, Count * 4L);
            Kernel.Fold(new Column<int>(value[k], Count), Window.Range(0, Count), new Column<int>(delta[k], Count), statBounds[k], new Column<ulong>(changed[k], Count / 64));
            Buffer.MemoryCopy(source[k], delta[k], Count * 4L, Count * 4L);
            Kernel.Run(new Column<int>(spare[k], Count), Window.Range(0, Count), new Delta(delta[k]), statBounds[k]);
            for (var i = 0; i < Count; i++)
            {
                if (value[k][i] != spare[k][i])
                {
                    verify = $"FAIL fold and run diverge at intrinsic {k} row {i}";
                    break;
                }
            }
        }

        var results = new double[6];
        var samples = new double[Reps];
        var allocDelta = 0L;
        for (var arm = 0; arm < 6; arm++)
        {
            var bounds = arm < 3 ? constantBounds : statBounds;
            var fold = arm % 3 == 0;
            var withApplied = arm % 3 == 2;
            for (var rep = 0; rep < Reps; rep++)
            {
                for (var k = 0; k < Intrinsics; k++)
                {
                    Buffer.MemoryCopy(source[k], delta[k], Count * 4L, Count * 4L);
                }

                var allocBefore = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var k = 0; k < Intrinsics; k++)
                {
                    if (fold)
                    {
                        Kernel.Fold(new Column<int>(value[k], Count), Window.Range(0, Count), new Column<int>(delta[k], Count), bounds[k], new Column<ulong>(changed[k], Count / 64));
                    }
                    else
                    {
                        Kernel.Run(new Column<int>(value[k], Count), Window.Range(0, Count), new Delta(delta[k]), bounds[k], withApplied ? new Column<int>(spare[k], Count) : default);
                        new Span<int>(delta[k], Count).Clear();
                    }
                }

                samples[rep] = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency * 1000.0;
                allocDelta += GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            }

            Array.Sort(samples, 1, Reps - 1);
            results[arm] = samples[1 + (Reps - 1) / 2];
        }

        if (allocDelta != 0)
        {
            verify = $"FAIL allocated {allocDelta} managed bytes";
        }

        for (var k = 0; k < Intrinsics; k++)
        {
            NativeMemory.Free(value[k]);
            NativeMemory.Free(spare[k]);
            NativeMemory.Free(delta[k]);
            NativeMemory.Free(source[k]);
            NativeMemory.Free(changed[k]);
        }

        var killConstant = results[0] > results[1] * 0.9;
        var killStat = results[3] > results[4] * 0.9;
        var line = $"fold1m  constant: fold {results[0]:0.000} ms vs run+clear {results[1]:0.000} ms ({results[1] / results[0]:0.00}x) vs run+applied+clear {results[2]:0.000} ms ({results[2] / results[0]:0.00}x)  stat-ceiling: fold {results[3]:0.000} ms vs run+clear {results[4]:0.000} ms ({results[4] / results[3]:0.00}x) vs run+applied+clear {results[5]:0.000} ms ({results[5] / results[3]:0.00}x)  [{Count:N0} rows x {Intrinsics} intrinsics, 1 thread, vector256={Simd.Has256}, median of {Reps - 1}]  target<=1.5ms:{(results[3] <= 1.5 ? "pass" : "miss")}  kill(>=10% faster):{(killConstant || killStat ? "TRIPPED" : "clear")}";
        Console.WriteLine(verify.StartsWith("ok", StringComparison.Ordinal) ? line + "  alloc=0B" : $"fold1m  FAIL: {verify}");
        return verify.StartsWith("ok", StringComparison.Ordinal) ? 0 : 1;
    }
}
