using Kernels.Testing;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sv;
using static Program;

internal static unsafe class Allocator
{
    public static T* New<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T), (nuint)sizeof(T));
    }

    public static T* Zeroed<T>(int count) where T : unmanaged
    {
        return (T*)NativeMemory.AllocZeroed((nuint)count * (nuint)sizeof(T), (nuint)sizeof(T));
    }
}

internal abstract unsafe class BenchBase : IDisposable
{
    public abstract long Frame(int iterations);

    public virtual int RowsPerFrame => Rows;

    public virtual string Verify() => "ok";

    public virtual void Dispose()
    {
    }
}

internal static class Program
{
    internal const int Rows = 100_000;

    static int Main(string[] args)
    {
        var scenario = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "all";
        var ms = 2000;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--ms" && i + 1 < args.Length)
            {
                ms = int.Parse(args[i + 1]);
            }

            if (args[i] == "--md")
            {
                Markdown = true;
            }
        }

        if (Markdown)
        {
            Console.WriteLine($"<!-- sv-bench rows={Rows} vector256={Simd.Has256} vector128={Simd.Has128} -->");
        }
        else
        {
            Console.WriteLine($"sv-bench rows={Rows} vector256={Simd.Has256} vector128={Simd.Has128}");
        }

        var failures = 0;
        failures += Run("intrinsics-uniform", scenario, ms, () => new IntrinsicsBench(uniform: true, statClamp: false));
        failures += Run("intrinsics-varying", scenario, ms, () => new IntrinsicsBench(uniform: false, statClamp: false));
        failures += Run("intrinsics-statclamp", scenario, ms, () => new IntrinsicsBench(uniform: false, statClamp: true));
        failures += Run("intrinsics-statmirror", scenario, ms, () => new IntrinsicsBench(uniform: false, statClamp: true, mirror: true));
        failures += Run("enabled-runwalk", scenario, ms, EnabledRunwalkBench.New);
        failures += Run("stat-recalc", scenario, ms, () => StatRecalcBench.New(false));
        failures += Run("stat-churn", scenario, ms, () => StatRecalcBench.New(true));
        failures += Run("events-emit", scenario, ms, () => EventsEmitBench.New(false));
        failures += Run("events-emit-struct", scenario, ms, () => EventsEmitBench.New(true));
        failures += Run("input-capture", scenario, ms, InputCaptureBench.New);
        failures += Run("fused-tick", scenario, ms, FusedTickBench.New);
        failures += Run("frame-run", scenario, ms, () => FrameRunBench.New(false, ""));
        var sink = OperatingSystem.IsLinux() ? "/dev/null" : System.IO.Path.GetTempFileName();
        failures += Run("tape-digest", scenario, ms, () => FrameRunBench.New(true, sink));
        failures += Run("tape-record", scenario, ms, () => TapeRecordBench.New(sink));
        failures += Ff1m.Run(scenario, ms);
        failures += Spawn1m.Run(scenario, ms);
        failures += Run("rollback-resim", scenario, ms, RollbackBench.New);
        failures += Run("keyframe", scenario, ms, KeyframeBench.New);
        failures += BulkPass.Run(scenario, ms);
        failures += Renumber1m.Run(scenario, ms);
        failures += WheelExpiry.Run(scenario, ms);
        failures += DayToDay.Run(scenario, ms);
        failures += Fold1m.Run(scenario, ms);
        failures += Statop10k.Run(scenario, ms);
        failures += Derive.Run(scenario, ms);
        failures += Frame1m.Run(scenario, ms);
        failures += SvV3Bench.V3.Run(scenario, ms);
        if (!Markdown)
        {
            Console.WriteLine(failures == 0 ? "all scenarios ok" : $"{failures} scenario(s) failed");
        }

        return failures == 0 ? 0 : 1;
    }

    internal static bool Markdown { get; private set; }

    static int Run(string name, string scenario, int ms, Func<BenchBase> factory)
    {
        if (scenario != "all" && scenario != name)
        {
            return 0;
        }

        using var bench = factory();
        var verify = bench.Verify();
        bench.Frame(64);
        var sw = Stopwatch.GetTimestamp();
        bench.Frame(64);
        var perCallSeconds = Math.Max((Stopwatch.GetTimestamp() - sw) / 64.0 / Stopwatch.Frequency, 1e-9);
        var iterations = Math.Max(1, (int)(ms / 5000.0 / perCallSeconds));
        var batches = new double[5];
        for (var b = 0; b < batches.Length; b++)
        {
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            sw = Stopwatch.GetTimestamp();
            var checksum = bench.Frame(iterations);
            var ticks = Stopwatch.GetTimestamp() - sw;
            var allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            if (allocDelta != 0)
            {
                verify = $"ALLOCATED {allocDelta} managed bytes";
            }

            if (checksum == long.MinValue)
            {
                verify = "dead output";
            }

            batches[b] = ticks / (double)iterations / Stopwatch.Frequency * 1_000_000.0;
        }

        Array.Sort(batches);
        if (verify == null || !verify.StartsWith("ok"))
        {
            Console.WriteLine(Markdown
                ? $"| {name} | FAIL: {verify} | — | — |"
                : $"{name,-22} FAIL: {verify}");
            return 1;
        }

        var bestUs = batches[0];
        var rate = bench.RowsPerFrame == 1
            ? $"{bestUs * 1000.0:0} ns/op"
            : $"{bench.RowsPerFrame / bestUs:0.0} M rows/s";
        Console.WriteLine(Markdown
            ? $"| {name} | {bestUs:0.000} | {rate} | 0 B |"
            : $"{name,-22} {bestUs,9:0.000} us/frame {rate,16} alloc=0B");
        return 0;
    }
}

internal sealed unsafe class IntrinsicsBench : BenchBase
{
    readonly Board board;

    readonly int* value;

    readonly int* delta;

    readonly int* applied;

    readonly int[] seed;

    readonly int[] seedDelta;

    readonly Clamp clamp;

    readonly bool uniform;

    internal IntrinsicsBench(bool uniform, bool statClamp, bool mirror = false)
    {
        this.uniform = uniform;
        board = Board.Create(statClamp ? StatSchema() : SimpleSchema(), Rows);
        value = Allocator.New<int>(Rows);
        delta = Allocator.New<int>(Rows);
        applied = Allocator.New<int>(Rows);
        seed = Seed(11);
        seedDelta = Seed(33);
        for (var i = 0; i < Rows; i++)
        {
            value[i] = seed[i];
            delta[i] = seedDelta[i];
        }

        if (statClamp)
        {
            var minCol = Allocator.New<float>(Rows);
            var maxCol = Allocator.New<float>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                minCol[i] = i % 7 == 0 ? -1e30f : 1000f;
                maxCol[i] = i % 11 == 0 ? 1e30f : 900_000f;
            }

            var minStat = board.Schema.Stat("floor");
            var maxStat = board.Schema.Stat("ceiling");
            board.BindOutput(minStat, minCol);
            board.BindOutput(maxStat, maxCol);
            if (mirror)
            {
                for (var i = 0; i < Rows; i++)
                {
                    board.Acquire();
                }

                board.MirrorBounds(minStat);
                board.MirrorBounds(maxStat);
                clamp = Clamp.StatMirror(board, minStat, maxStat);
            }
            else
            {
                clamp = Clamp.Stat(board, minStat, maxStat);
            }
        }
        else
        {
            clamp = Clamp.Constant(0, 900_000);
        }
    }

    static int[] Seed(int seed)
    {
        var random = new Random(seed);
        var values = new int[Rows];
        for (var i = 0; i < Rows; i++)
        {
            values[i] = random.Next(-500_000, 500_000);
        }

        return values;
    }

    static Schema SimpleSchema() => SchemaBuilder.Create().Intrinsic("mana").Build();

    static Schema StatSchema() => SchemaBuilder.Create()
        .Stat("floor", 0f)
        .Stat("ceiling", 900_000f)
        .Intrinsic("mana")
        .Build();

    public override string Verify()
    {
        var reference = Allocator.New<int>(Rows);
        var referenceApplied = Allocator.New<int>(Rows);
        var candidate = Allocator.New<int>(Rows);
        var candidateApplied = Allocator.New<int>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            reference[i] = seed[i];
            candidate[i] = seed[i];
        }

        Simd.ChunkScalar(reference, uniform ? null : delta, 7, clamp, 0, Rows, referenceApplied);
        if (uniform)
        {
            board.RunIntrinsicsChunk(candidate, 7, clamp, 0, Rows, candidateApplied);
        }
        else
        {
            board.RunIntrinsicsChunk(candidate, delta, clamp, 0, Rows, candidateApplied);
        }

        for (var i = 0; i < Rows; i++)
        {
            if (reference[i] != candidate[i] || referenceApplied[i] != candidateApplied[i])
            {
                NativeMemory.Free(reference);
                NativeMemory.Free(referenceApplied);
                NativeMemory.Free(candidate);
                NativeMemory.Free(candidateApplied);
                return $"MISMATCH row {i}: {reference[i]}/{referenceApplied[i]} vs {candidate[i]}/{candidateApplied[i]}";
            }
        }

        NativeMemory.Free(reference);
        NativeMemory.Free(referenceApplied);
        NativeMemory.Free(candidate);
        NativeMemory.Free(candidateApplied);
        return "ok";
    }

    public override long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            if (uniform)
            {
                board.RunIntrinsicsChunk(value, 7, clamp, 0, Rows, applied);
            }
            else
            {
                board.RunIntrinsicsChunk(value, delta, clamp, 0, Rows, applied);
            }

            checksum += value[it % Rows] + applied[(it * 31) % Rows];
        }

        return checksum;
    }

    public override void Dispose()
    {
        board.Dispose();
        NativeMemory.Free(value);
        NativeMemory.Free(delta);
        NativeMemory.Free(applied);
    }
}

internal sealed unsafe class EnabledRunwalkBench : BenchBase
{
    readonly Board board;

    readonly int* value;

    readonly int* applied;

    readonly int[] seed;

    readonly Clamp clamp;

    EnabledRunwalkBench(Board board, int* value, int* applied, int[] seed, Clamp clamp)
    {
        this.board = board;
        this.value = value;
        this.applied = applied;
        this.seed = seed;
        this.clamp = clamp;
    }

    internal static EnabledRunwalkBench New()
    {
        var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), Rows);
        var value = Allocator.New<int>(Rows);
        var applied = Allocator.New<int>(Rows);
        var seed = new int[Rows];
        var random = new Random(66);
        for (var i = 0; i < Rows; i++)
        {
            seed[i] = random.Next(-500_000, 500_000);
            value[i] = seed[i];
        }

        return new EnabledRunwalkBench(board, value, applied, seed, Clamp.Constant(0, 900_000));
    }

    public override string Verify()
    {
        var reference = Allocator.New<int>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            reference[i] = seed[i];
        }

        var clamp = Clamp.Constant(0, 900_000);
        for (var offset = 0; offset + 64 <= Rows; offset += 64)
        {
            ulong mask = 0x5555_5555_5555_5555;
            while (mask != 0)
            {
                var bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                reference[offset + bit] = clamp.Apply(reference[offset + bit] + 7, offset + bit);
            }
        }

        ulong full = 0x5555_5555_5555_5555;
        for (var offset = 0; offset + 64 <= Rows; offset += 64)
        {
            board.RunEnabledChunk(value, 7, clamp, full, offset, applied);
        }

        for (var i = 0; i < Rows; i++)
        {
            if (reference[i] != value[i])
            {
                NativeMemory.Free(reference);
                return $"MISMATCH row {i}: {reference[i]} vs {value[i]}";
            }
        }

        NativeMemory.Free(reference);
        return "ok";
    }

    public override long Frame(int iterations)
    {
        ulong mask = 0x5555_5555_5555_5555;
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            for (var offset = 0; offset + 64 <= Rows; offset += 64)
            {
                board.RunEnabledChunk(value, 7, clamp, mask, offset, applied);
            }

            checksum += value[it % Rows] + applied[(it * 31) % Rows];
        }

        return checksum;
    }

    public override void Dispose()
    {
        board.Dispose();
        NativeMemory.Free(value);
        NativeMemory.Free(applied);
    }
}

internal sealed unsafe class StatRecalcBench : BenchBase
{
    readonly Board board;

    readonly float* column;

    readonly StatId power;

    readonly ModifierHandle[] handles;

    readonly int churn;

    StatRecalcBench(Board board, float* column, StatId power, ModifierHandle[] handles, bool churn)
    {
        this.board = board;
        this.column = column;
        this.power = power;
        this.handles = handles;
        this.churn = churn ? 1 : 0;
    }

    public override int RowsPerFrame => 1;

    internal static StatRecalcBench New(bool churn)
    {
        var board = Board.Create(
            SchemaBuilder.Create().Stat("power", 10f).Intrinsic("mana").Build(), Rows);
        var power = board.Schema.Stat("power");
        var column = Allocator.New<float>(Rows);
        board.BindOutput(power, column);
        for (var i = 0; i < Rows; i++)
        {
            board.Acquire();
        }

        var handles = new ModifierHandle[1000];
        for (var g = 0; g < 1000; g++)
        {
            handles[g] = board.AddModifier((g * 97) % Rows, power, new Modifier(ModifierKind.Multi, 1f + (g % 8) * 0.25f));
        }

        board.Recalculate();
        return new StatRecalcBench(board, column, power, handles, churn);
    }

    public override string Verify()
    {
        var expected = churn == 0 ? StrictFloat.Multiply(10f, StrictFloat.Add(1f, 1f)) : 10f;
        var actual = column[churn];
        if (BitConverter.SingleToUInt32Bits(expected) != BitConverter.SingleToUInt32Bits(actual))
        {
            return $"MISMATCH broadcast: expected {expected} got {actual}";
        }

        return "ok";
    }

    public override long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            var g = it % 1000;
            var slot = (g * 97 + churn) % Rows;
            var fresh = board.AddModifier(slot, power, new Modifier(ModifierKind.Multi, 1.5f));
            board.RemoveModifier(slot, power, fresh);
            board.Recalculate();
            checksum += (long)BitConverter.SingleToUInt32Bits(column[slot]);
        }

        return checksum;
    }

    public override void Dispose()
    {
        board.Dispose();
        NativeMemory.Free(column);
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct ManaEvent
{
    public int Slot;

    public int Amount;
}

internal struct ManaMaker : IEventMaker<ManaEvent>
{
    public ManaEvent Make(int slot, int value) => new() { Slot = slot, Amount = value };
}

internal static class Makers
{
    [UnmanagedCallersOnly]
    public static ManaEvent MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };
}

internal sealed unsafe class EventsEmitBench : BenchBase
{
    readonly EventStreams streams;

    readonly Channel<ManaEvent> channel;

    readonly byte* cond;

    readonly int* applied;

    readonly int expectedCount;

    readonly bool structMaker;

    EventsEmitBench(EventStreams streams, Channel<ManaEvent> channel, byte* cond, int* applied, int expectedCount, bool structMaker)
    {
        this.structMaker = structMaker;
        this.streams = streams;
        this.channel = channel;
        this.cond = cond;
        this.applied = applied;
        this.expectedCount = expectedCount;
    }

    internal static EventsEmitBench New(bool structMaker)
    {
        var streams = EventStreams.Create(1, 1 << 20);
        var channel = streams.CreateChannel<ManaEvent>();
        var cond = Allocator.New<byte>(Rows);
        var applied = Allocator.New<int>(Rows);
        var random = new Random(88);
        var expected = 0;
        for (var i = 0; i < Rows; i++)
        {
            cond[i] = (byte)(random.Next(10) == 0 ? 1 : 0);
            applied[i] = random.Next(20) == 0 ? random.Next(1, 50) : 0;
            expected += (cond[i] != 0 ? 1 : 0) + (applied[i] != 0 ? 1 : 0);
        }

        return new EventsEmitBench(streams, channel, cond, applied, expected, structMaker);
    }

    public override string Verify()
    {
        streams.EmitFromBools(channel, cond, 0, Rows, &Makers.MakeMana);
        streams.EmitFromNonZero(channel, applied, 0, Rows, &Makers.MakeMana);
        streams.Swap();
        var count = 0;
        while (streams.TryReadNext(out _, out _, out _))
        {
            count++;
        }

        return count == expectedCount ? $"ok events={count}" : $"MISMATCH drained {count} != expected {expectedCount}";
    }

    public override long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            if (structMaker)
            {
                var maker = new ManaMaker();
                streams.EmitFromBools(channel, cond, 0, Rows, ref maker);
                streams.EmitFromNonZero(channel, applied, 0, Rows, ref maker);
            }
            else
            {
                streams.EmitFromBools(channel, cond, 0, Rows, &Makers.MakeMana);
                streams.EmitFromNonZero(channel, applied, 0, Rows, &Makers.MakeMana);
            }

            streams.Swap();
            while (streams.TryReadNext(out _, out var payload, out _))
            {
                checksum += ((ManaEvent*)payload)->Amount;
            }
        }

        return checksum;
    }

    public override void Dispose()
    {
        streams.Dispose();
        NativeMemory.Free(cond);
        NativeMemory.Free(applied);
    }
}

internal sealed unsafe class InputCaptureBench : BenchBase
{
    readonly InputState input;

    readonly uint* axes;

    readonly ulong* buttons;

    public override int RowsPerFrame => 1;

    InputCaptureBench(InputState input, uint* axes, ulong* buttons)
    {
        this.input = input;
        this.axes = axes;
        this.buttons = buttons;
    }

    internal static InputCaptureBench New()
    {
        var input = InputState.Create(4, 2, 64);
        var axes = Allocator.New<uint>(4);
        var buttons = Allocator.New<ulong>(2);
        return new InputCaptureBench(input, axes, buttons);
    }

    public override string Verify()
    {
        axes[0] = 0b1010u;
        buttons[0] = 0b0110ul;
        buttons[1] = 0;
        input.Capture(axes, buttons);
        buttons[0] = 0b0011ul;
        input.Capture(axes, buttons);
        if (!input.Pressed(0) || input.Pressed(1) || !input.Released(2) || input.Released(1))
        {
            return "MISMATCH edge reference";
        }

        return "ok";
    }

    public override long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            axes[0] = (uint)it;
            buttons[0] = (ulong)it;
            buttons[1] = ~(ulong)it;
            input.Capture(axes, buttons);
            checksum += (input.Pressed(0) ? 1 : 0) + (input.Released(64) ? 1 : 0) + input.AxisBits(3);
        }

        return checksum;
    }

    public override void Dispose()
    {
        input.Dispose();
        NativeMemory.Free(axes);
        NativeMemory.Free(buttons);
    }
}

internal sealed unsafe class FusedTickBench : BenchBase
{
    const int Frames = 3;

    readonly Board board;

    readonly EventStreams streams;

    readonly InputState input;

    readonly Channel<ManaEvent> manaChannel;

    readonly StatId power;

    readonly StatId ceiling;

    readonly int* mana;

    readonly int* applied;

    readonly byte* landed;

    readonly float* powerColumn;

    readonly float* ceilingColumn;

    FusedTickBench(Board board, EventStreams streams, InputState input, Channel<ManaEvent> manaChannel, StatId power, StatId ceiling, int* mana, int* applied, byte* landed, float* powerColumn, float* ceilingColumn)
    {
        this.board = board;
        this.streams = streams;
        this.input = input;
        this.manaChannel = manaChannel;
        this.power = power;
        this.ceiling = ceiling;
        this.mana = mana;
        this.applied = applied;
        this.landed = landed;
        this.powerColumn = powerColumn;
        this.ceilingColumn = ceilingColumn;
    }

    internal static FusedTickBench New()
    {
        var board = Board.Create(
            SchemaBuilder.Create().Stat("power", 10f).Stat("ceiling", 900_000f).Intrinsic("mana").Build(), Rows);
        var power = board.Schema.Stat("power");
        var ceiling = board.Schema.Stat("ceiling");
        var powerColumn = Allocator.New<float>(Rows);
        board.BindOutput(power, powerColumn);
        var ceilingColumn = Allocator.New<float>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            ceilingColumn[i] = 900_000f;
        }

        board.BindOutput(ceiling, ceilingColumn);
        for (var i = 0; i < Rows; i++)
        {
            board.Acquire();
        }

        var mana = Allocator.New<int>(Rows);
        var applied = Allocator.New<int>(Rows);
        var landed = Allocator.Zeroed<byte>(Rows);
        var streams = EventStreams.Create(1, 1 << 21);
        var manaChannel = streams.CreateChannel<ManaEvent>();
        var input = InputState.Create(1, 1, 16);
        board.Recalculate();
        return new FusedTickBench(board, streams, input, manaChannel, power, ceiling, mana, applied, landed, powerColumn, ceilingColumn);
    }

    public override string Verify()
    {
        for (var i = 0; i < Rows; i++)
        {
            mana[i] = 40;
            applied[i] = 0;
        }

        var clamp = Clamp.StatMax(board, ceiling);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];
        buttons[0] = 1;
        input.Capture(axes, buttons);
        board.RunIntrinsicsChunk(mana, 25, clamp, 0, Rows, applied);
        board.Recalculate();
        streams.EmitFromBools(manaChannel, landed, 0, Rows, &Makers.MakeMana);
        streams.EmitFromNonZero(manaChannel, applied, 0, Rows, &Makers.MakeMana);
        streams.Swap();
        var count = 0;
        var manaSum = 0;
        while (streams.TryReadNext(out _, out _, out _))
        {
            count++;
        }

        for (var i = 0; i < Rows; i += Rows / 10)
        {
            manaSum += mana[i];
        }

        if (count != Rows || mana[0] != 65)
        {
            return $"MISMATCH fused tick: events {count} != {Rows}, mana {mana[0]} != 65";
        }

        return $"ok events={count} mana0={manaSum / 10}";
    }

    public override long Frame(int iterations)
    {
        var clamp = Clamp.StatMax(board, ceiling);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            buttons[0] = it % 2 == 0 ? 1ul : 0ul;
            input.Capture(axes, buttons);
            if (input.Pressed(0))
            {
                board.RunIntrinsicsChunk(mana, 25, clamp, 0, Rows, applied);
            }

            board.Recalculate();
            streams.EmitFromBools(manaChannel, landed, 0, Rows, &Makers.MakeMana);
            streams.EmitFromNonZero(manaChannel, applied, 0, Rows, &Makers.MakeMana);
            for (var i = 0; i < Rows; i++)
            {
                applied[i] = 0;
            }

            streams.Swap();
            while (streams.TryReadNext(out _, out var payload, out _))
            {
                checksum += ((ManaEvent*)payload)->Amount;
            }
        }

        return checksum;
    }

    public override void Dispose()
    {
        board.Dispose();
        streams.Dispose();
        input.Dispose();
        NativeMemory.Free(mana);
        NativeMemory.Free(applied);
        NativeMemory.Free(landed);
        NativeMemory.Free(powerColumn);
        NativeMemory.Free(ceilingColumn);
    }
}
