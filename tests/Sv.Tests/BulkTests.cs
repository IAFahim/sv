using System;
using Sv;
using Xunit;

public unsafe class BulkTests
{
    [Fact]
    public void ParallelRun_EqualsSequential_RangeAndMasked()
    {
        const int n = 4096;
        var rng = new Random(4242);
        var words = stackalloc ulong[n / 64];
        for (var w = 0; w < n / 64; w++)
        {
            words[w] = (ulong)rng.NextInt64(long.MinValue, long.MaxValue);
        }

        var deltaCol = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            deltaCol[i] = rng.Next(-30, 30);
        }

        var sequential = stackalloc int[n];
        var parallel = stackalloc int[n];
        var appliedSequential = stackalloc int[n];
        var appliedParallel = stackalloc int[n];
        var minRows = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            sequential[i] = rng.Next(-60, 60);
            parallel[i] = sequential[i];
            minRows[i] = -70;
        }

        var bounds = Bounds.Rows(minRows, null);
        var windows = new[]
        {
            Window.Range(0, n),
            Window.Range(37, 2000),
            Window.Masked(0, words, n / 64),
            Window.Masked(19, words, 32),
        };

        for (var repeat = 0; repeat < 3; repeat++)
        {
            foreach (var window in windows)
            {
                for (var i = 0; i < n; i++)
                {
                    appliedSequential[i] = int.MinValue;
                    appliedParallel[i] = int.MinValue;
                }

                Kernel.Run(new Column<int>(sequential, n), window, new Delta(deltaCol), bounds, new Column<int>(appliedSequential, n));
                Bulk.Run(new Column<int>(parallel, n), window, new Delta(deltaCol), bounds, new Column<int>(appliedParallel, n));
                for (var i = 0; i < n; i++)
                {
                    Assert.Equal(sequential[i], parallel[i]);
                    Assert.Equal(appliedSequential[i], appliedParallel[i]);
                }
            }
        }
    }

    [Fact]
    public void InvalidWindow_Throws_AndThePoolStaysUsable()
    {
        var value = stackalloc int[64];
        var column = new Column<int>(value, 64);
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => Bulk.Run(column, Window.Range(0, 65), new Delta(1), Bounds.None));

        var ok = stackalloc int[64];
        for (var i = 0; i < 64; i++)
        {
            ok[i] = 0;
        }

        Bulk.Run(new Column<int>(ok, 64), Window.Range(0, 64), new Delta(5), Bounds.Constant(0, 10));
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(5, ok[i]);
        }
    }

    [Fact]
    public void WarmPathAllocatesNothing()
    {
        var value = stackalloc int[8192];
        var column = new Column<int>(value, 8192);
        Bulk.Run(column, Window.Range(0, 8192), new Delta(1), Bounds.None);
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            Bulk.Run(column, Window.Range(0, 8192), new Delta(1), Bounds.None);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before == 0, "warm Bulk.Run allocated managed bytes");
    }

    [Fact]
    public void FaultInASlice_ThrowsOnTheCaller_AndLeavesThePoolUsable()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
        using var board = Board.Create(schema, 8192);
        var power = schema.Stat("power");
        var bound = stackalloc float[8192];
        board.BindOutput(power, bound, 1, 8192);
        var bounds = Bounds.Stats(board, null, power);
        board.InvalidateBindings();
        const int n = 8192;
        var rows = stackalloc int[n];
        var seq = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            rows[i] = i;
            seq[i] = i;
        }

#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => Bulk.Run(new Column<int>(rows, n), Window.Range(0, n), default, bounds));
#endif
        board.BindOutput(power, bound, 1, 8192);
        var fresh = Bounds.Stats(board, null, power);
        Bulk.Run(new Column<int>(rows, n), Window.Range(0, n), default, fresh);
        Kernel.Run(new Column<int>(seq, n), Window.Range(0, n), default, Bounds.Stats(board, null, power));
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(seq[i], rows[i]);
        }
    }
}
