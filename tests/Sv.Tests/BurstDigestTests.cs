using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sv;
using Sv.Burst;
using Xunit;
using Xunit.Abstractions;

public unsafe class BurstDigestTests
{
    readonly ITestOutputHelper output;

    public BurstDigestTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "sv.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void Shims_TzcntMatchesBitOperationsEverywhere()
    {
        Assert.Equal(64, Bcl.Tzcnt(0));
        for (var i = 0; i < 4096; i++)
        {
            Assert.Equal(System.Numerics.BitOperations.TrailingZeroCount((ulong)i), Bcl.Tzcnt((ulong)i));
        }

        for (var bit = 0; bit < 64; bit++)
        {
            var power = 1ul << bit;
            Assert.Equal(bit, Bcl.Tzcnt(power));
            if (bit > 0)
            {
                Assert.Equal(System.Numerics.BitOperations.TrailingZeroCount(power - 1), Bcl.Tzcnt(power - 1));
            }
        }

        Assert.Equal(0, Bcl.Tzcnt(ulong.MaxValue));
        var state = Scenario.Seed ^ 0xDEADBEEFul;
        for (var i = 0; i < 100_000; i++)
        {
            var value = Scenario.Next(ref state);
            Assert.Equal(System.Numerics.BitOperations.TrailingZeroCount(value), Bcl.Tzcnt(value));
        }
    }

    [Fact]
    public void Shims_BoundOfMatchesClampBoundOfOnSpecialsAndRandomBits()
    {
        Span<uint> bits = stackalloc uint[]
        {
            0x7FC00000u, 0xFFC00000u, 0x7F800000u, 0xFF800000u, 0x4F000000u, 0xCF000000u,
            0x4EFFFFFFu, 0x4F000001u, 0xCEFFFFFFu, 0xCF000001u, 0x3F800000u, 0xBF800000u,
            0x7F7FFFFFu, 0xFF7FFFFFu, 0x00000000u, 0x80000000u, 0x36A12283u, 0xB6A12283u,
            0x4EFFFFFFu, 0x4F000000u, 0x4EFFFFFEu, 0x4F07FFFFu, 0x4F000000u, 0x4F7FFFFFu,
        };
        foreach (var bitsOf in bits)
        {
            var value = BitConverter.Int32BitsToSingle((int)bitsOf);
            Assert.Equal(Clamp.BoundOf(value), Bcl.BoundOf(value));
        }

        var state = Scenario.Seed;
        for (var i = 0; i < 100_000; i++)
        {
            var raw = (int)(uint)(Scenario.Next(ref state) & 0xFFFFFFFFul);
            var value = BitConverter.Int32BitsToSingle(raw);
            Assert.Equal(Clamp.BoundOf(value), Bcl.BoundOf(value));
        }
    }

    [Fact]
    public void SharedKernelSources_AreByteIdenticalInUnityProject()
    {
        var root = RepoRoot();
        foreach (var name in new[] { "Bcl.cs", "Kernels.cs", "Scenario.cs" })
        {
            var source = File.ReadAllBytes(Path.Combine(root, "src", "Sv.Burst", name));
            var unity = File.ReadAllBytes(Path.Combine(root, "tests", "Sv.Burst", "unity", "Assets", "Scripts", "SvBurst", name));
            Assert.Equal(source, unity);
        }
    }

    [Fact]
    public void CrossTargetDigest_BurstLegMatchesSvKernelRunOnDotNet()
    {
        var digestSv = RunThroughSv();
        var digestBurstLeg = RunThroughBurstLeg();
        output.WriteLine($"dotnet sv digest     {digestSv:X16}");
        output.WriteLine($"dotnet burst-leg     {digestBurstLeg:X16}");
        Assert.Equal(digestSv, digestBurstLeg);
    }

    [Fact]
    public void BurstReceipt_MatchesDotNetDigest_WhenPresent()
    {
        var receipt = Path.Combine(RepoRoot(), "tests", "Sv.Burst", "receipt", "burst-receipt.json");
        if (!File.Exists(receipt))
        {
            output.WriteLine($"burst receipt absent ({receipt}) — editor leg not run on this host; .NET side pinned by CrossTargetDigest_BurstLegMatchesSvKernelRunOnDotNet");
            return;
        }

        using var json = JsonDocument.Parse(File.ReadAllText(receipt));
        var status = json.RootElement.GetProperty("status").GetString();
        var digestMono = json.RootElement.GetProperty("digestMono").GetString();
        var digestBurst = json.RootElement.GetProperty("digestBurst").GetString();
        var burstEnabled = json.RootElement.GetProperty("burstCompilerEnabled").GetBoolean();
        output.WriteLine($"burst receipt {status} unity {json.RootElement.GetProperty("unityVersion").GetString()} burstEnabled {burstEnabled} mono {digestMono} burst {digestBurst}");
        Assert.Equal("ok", status);
        Assert.True(burstEnabled);
        Assert.Equal(digestMono, digestBurst);
        Assert.Equal(RunThroughBurstLeg().ToString("X16"), digestBurst);
        Assert.Equal(RunThroughSv().ToString("X16"), digestBurst);
    }

    static ulong RunThroughBurstLeg()
    {
        const int rows = Scenario.Rows;
        var buffers = AllocBuffers(rows);
        try
        {
            var ops = (KOp*)NativeMemory.Alloc((nuint)Scenario.Ops, (nuint)sizeof(KOp));
            var count = Scenario.Build(ops, buffers.Value, buffers.Delta, buffers.Applied, buffers.MinRows, buffers.MaxRows, buffers.MinStats, buffers.MaxStats, buffers.Words);
            var state = new KState { Value = buffers.Value, Applied = buffers.Applied };
            Sv.Burst.Kernels.RunProgram(&state, ops, count);
            NativeMemory.Free(ops);
            return Scenario.Digest(buffers.Value, buffers.Applied, rows);
        }
        finally
        {
            FreeBuffers(buffers);
        }
    }

    static ulong RunThroughSv()
    {
        const int rows = Scenario.Rows;
        var buffers = AllocBuffers(rows);
        var ops = (KOp*)NativeMemory.Alloc((nuint)Scenario.Ops, (nuint)sizeof(KOp));
        try
        {
            var count = Scenario.Build(ops, buffers.Value, buffers.Delta, buffers.Applied, buffers.MinRows, buffers.MaxRows, buffers.MinStats, buffers.MaxStats, buffers.Words);
            using var board = Board.Create(SchemaBuilder.Create()
                .Stat("floor", 6f)
                .Stat("ceiling", 900_000f)
                .Build(), rows);
            var floor = board.Schema.Stat("floor");
            var ceiling = board.Schema.Stat("ceiling");
            board.BindOutput(floor, buffers.MinStats);
            board.BindOutput(ceiling, buffers.MaxStats);
            var value = new Column<int>(buffers.Value, rows);
            var applied = new Column<int>(buffers.Applied, rows);
            for (var i = 0; i < count; i++)
            {
                var op = ops[i];
                var window = op.Kind == KOpKind.ChunkUniform || op.Kind == KOpKind.ChunkPerRow
                    ? Window.Range(op.Start, op.Count)
                    : Window.Masked(op.Start, op.Words, op.WordCount);
                var delta = op.Kind == KOpKind.ChunkUniform || op.Kind == KOpKind.MaskedUniform
                    ? new Delta(op.Uniform)
                    : new Delta(op.PerRow);
                Kernel.Run(value, window, delta, MapBounds(board, floor, ceiling, op.Bound), applied);
            }

            return Scenario.Digest(buffers.Value, buffers.Applied, rows);
        }
        finally
        {
            NativeMemory.Free(ops);
            FreeBuffers(buffers);
        }
    }

    static Bounds MapBounds(Board board, StatId floor, StatId ceiling, KBound bound)
    {
        if (bound.Kind == KBoundKind.Constant)
        {
            return Bounds.Constant(bound.Min, bound.Max);
        }

        if (bound.Kind == KBoundKind.Rows)
        {
            return Bounds.Rows(bound.MinRows, bound.MaxRows);
        }

        if (bound.Kind == KBoundKind.Stats)
        {
            return Bounds.Stats(board, bound.MinStats != null ? floor : (StatId?)null, bound.MaxStats != null ? ceiling : (StatId?)null);
        }

        return Bounds.None;
    }

    static Buffers AllocBuffers(int rows)
    {
        return new Buffers
        {
            Value = (int*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(int)),
            Delta = (int*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(int)),
            Applied = (int*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(int)),
            MinRows = (int*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(int)),
            MaxRows = (int*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(int)),
            MinStats = (float*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(float)),
            MaxStats = (float*)NativeMemory.AllocZeroed((nuint)rows, (nuint)sizeof(float)),
            Words = (ulong*)NativeMemory.AllocZeroed((nuint)(rows / 64), (nuint)sizeof(ulong)),
        };
    }

    static void FreeBuffers(Buffers buffers)
    {
        NativeMemory.Free(buffers.Value);
        NativeMemory.Free(buffers.Delta);
        NativeMemory.Free(buffers.Applied);
        NativeMemory.Free(buffers.MinRows);
        NativeMemory.Free(buffers.MaxRows);
        NativeMemory.Free(buffers.MinStats);
        NativeMemory.Free(buffers.MaxStats);
        NativeMemory.Free(buffers.Words);
    }

    struct Buffers
    {
        public int* Value;
        public int* Delta;
        public int* Applied;
        public int* MinRows;
        public int* MaxRows;
        public float* MinStats;
        public float* MaxStats;
        public ulong* Words;
    }
}
