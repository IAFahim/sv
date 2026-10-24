using System;
using Sv;
using Xunit;

public class TotalClampTests
{
    static int SatSubtract(int after, int before)
    {
        var diff = after - before;
        return ((after ^ before) & (after ^ diff)) < 0 ? (after < 0 ? int.MinValue : int.MaxValue) : diff;
    }

    static int SatAdd(int a, int b)
    {
        var sum = a + b;
        return ((a ^ sum) & (b ^ sum)) < 0 ? (b < 0 ? int.MinValue : int.MaxValue) : sum;
    }

    static int ModelClamp(int v, int lo, int hi)
    {
        if (lo > hi)
        {
            lo = hi;
        }

        return v < lo ? lo : v > hi ? hi : v;
    }

    [Fact]
    public unsafe void CrossedBounds_CeilingWins_Columns()
    {
        const int n = 21;
        var values = stackalloc int[n];
        var lo = stackalloc int[n];
        var hi = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            values[i] = -100 + i * 10;
            lo[i] = 0;
            hi[i] = -5;
        }

        var clamp = Bounds.Rows(lo, hi);
        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), clamp);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(-5, values[i]);
        }

        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), clamp);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(-5, values[i]);
        }

        lo[3] = 7;
        hi[3] = 3;
        values[3] = int.MinValue;
        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), clamp);
        Assert.Equal(3, values[3]);
    }

    [Fact]
    public unsafe void CrossedBounds_CeilingWins_StatBounds()
    {
        const int n = 16;
        var schema = SchemaBuilder.Create().Stat("lo", 0f).Stat("hi", 0f).Build();
        using var board = Board.Create(schema, n);
        var lo = stackalloc float[n];
        var hi = stackalloc float[n];
        for (var i = 0; i < n; i++)
        {
            lo[i] = 5f;
            hi[i] = -5f;
        }

        board.BindOutput(schema.Stat("lo"), lo, 1, n);
        board.BindOutput(schema.Stat("hi"), hi, 1, n);
        var bounds = Bounds.Stats(board, schema.Stat("lo"), schema.Stat("hi"));

        var values = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            values[i] = 50 - i * 7;
        }

        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), bounds);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(-5, values[i]);
        }
    }

    [Fact]
    public unsafe void CrossedBounds_CeilingWins_MaskedWindow()
    {
        const int n = 128;
        var values = stackalloc int[n];
        var lo = stackalloc int[n];
        var hi = stackalloc int[n];
        var words = stackalloc ulong[2];
        words[0] = 0x00FF00FF00FF00FFul;
        words[1] = 0xF0F0F0F0F0F0F0F0ul;
        for (var i = 0; i < n; i++)
        {
            values[i] = i % 2 == 0 ? 40 : -40;
            lo[i] = 10;
            hi[i] = -10;
        }

        var applied = stackalloc int[n];
        var clamp = Bounds.Rows(lo, hi);
        Kernel.Run(new Column<int>(values, n), Window.Masked(0, words, 2), new Delta(3), clamp, new Column<int>(applied, n));
        for (var w = 0; w < 2; w++)
        {
            for (var bit = 0; bit < 64; bit++)
            {
                var i = w * 64 + bit;
                if ((words[w] & (1ul << bit)) != 0)
                {
                    Assert.Equal(-10, values[i]);
                }
                else
                {
                    Assert.Equal(i % 2 == 0 ? 40 : -40, values[i]);
                    Assert.Equal(0, applied[i]);
                }
            }
        }
    }

    [Fact]
    public unsafe void Applied_Saturates_WhenClampPullsBack()
    {
        const int n = 16;
        var values = stackalloc int[n];
        var applied = stackalloc int[n];
        values[0] = int.MinValue;
        values[1] = int.MaxValue;
        values[2] = int.MinValue;
        values[3] = int.MaxValue;
        for (var i = 4; i < n; i++)
        {
            values[i] = -50 + i;
        }

        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), Bounds.Constant(0, 100), new Column<int>(applied, n));
        Assert.Equal(0, values[0]);
        Assert.Equal(int.MaxValue, applied[0]);
        Assert.Equal(100, values[1]);
        Assert.Equal(100 - int.MaxValue, applied[1]);
        Assert.Equal(int.MaxValue, applied[2]);
        Assert.Equal(100 - int.MaxValue, applied[3]);

        values[0] = int.MinValue;
        values[1] = int.MaxValue;
        Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(0), Bounds.Constant(int.MinValue, int.MinValue), new Column<int>(applied, n));
        Assert.Equal(int.MinValue, values[0]);
        Assert.Equal(0, applied[0]);
        Assert.Equal(int.MinValue, values[1]);
        Assert.Equal(int.MinValue, applied[1]);
    }

    [Fact]
    public unsafe void TotalClamp_MatchesEagerModel_Random()
    {
        const int n = 512;
        var rng = new Random(20261028);
        var values = stackalloc int[n];
        var deltas = stackalloc int[n];
        var lo = stackalloc int[n];
        var hi = stackalloc int[n];
        var applied = stackalloc int[n];
        var model = new int[n];
        var modelApplied = new int[n];
        var extremes = new[] { int.MinValue, int.MaxValue, -1, 0, 1, 1 << 30, -(1 << 30) };
        for (var i = 0; i < n; i++)
        {
            values[i] = i % 7 == 0 ? extremes[rng.Next(extremes.Length)] : rng.Next(-1000, 1001);
            deltas[i] = i % 5 == 0 ? extremes[rng.Next(extremes.Length)] : rng.Next(-100, 101);
            lo[i] = rng.Next(-50, 51);
            hi[i] = rng.Next(-50, 51);
            model[i] = values[i];
        }

        for (var pass = 0; pass < 3; pass++)
        {
            for (var i = 0; i < n; i++)
            {
                var after = ModelClamp(SatAdd(model[i], deltas[i]), lo[i], hi[i]);
                modelApplied[i] = SatSubtract(after, model[i]);
                model[i] = after;
            }

            Kernel.Run(new Column<int>(values, n), Window.Range(0, n), new Delta(deltas), Bounds.Rows(lo, hi), new Column<int>(applied, n));
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(model[i], values[i]);
                Assert.Equal(modelApplied[i], applied[i]);
            }
        }
    }
}
