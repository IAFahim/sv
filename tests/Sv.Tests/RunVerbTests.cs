using System;
using Sv;
using Xunit;

public unsafe class RunVerbTests
{
    [Fact]
    public void RangeWindows_MatchTheOldVerb_AndTheScalarOracle()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 100f).Build(), 256);
        const int n = 256;
        var value = stackalloc int[n];
        var mirror = stackalloc int[n];
        var boardMirror = stackalloc int[n];
        var deltaCol = stackalloc int[n];
        var appliedA = stackalloc int[n];
        var appliedB = stackalloc int[n];
        var minRows = stackalloc int[n];
        var maxRows = stackalloc int[n];
        var rng = new Random(7);
        for (var i = 0; i < n; i++)
        {
            value[i] = rng.Next(-50, 50);
            mirror[i] = value[i];
            boardMirror[i] = value[i];
            deltaCol[i] = rng.Next(-30, 30);
            minRows[i] = -10;
            maxRows[i] = 60;
        }

        var boundsList = new[] { Bounds.None, Bounds.Constant(-10, 60), Bounds.Rows(minRows, maxRows) };
        foreach (var bounds in boundsList)
        {
            for (var i = 0; i < n; i++)
            {
                value[i] = mirror[i];
                boardMirror[i] = mirror[i];
            }

            var v = new Column<int>(value, n);
            var b = new Column<int>(boardMirror, n);
            Kernel.Run(v, Window.Range(16, 96), new Delta(deltaCol), bounds, new Column<int>(appliedA, n));
            board.RunIntrinsicsChunk(boardMirror, deltaCol, bounds.Clamp, 16, 96, appliedB);
            for (var i = 0; i < n; i++)
            {
                var inWindow = i >= 16 && i < 112;
                var expected = inWindow ? bounds.Clamp.Apply(mirror[i] + deltaCol[i], i) : mirror[i];
                Assert.Equal(expected, value[i]);
                Assert.Equal(boardMirror[i], value[i]);
                if (inWindow)
                {
                    Assert.Equal(expected - mirror[i], appliedA[i]);
                    Assert.Equal(appliedB[i], appliedA[i]);
                }
            }

            for (var i = 0; i < n; i++)
            {
                appliedB[i] = bounds.Clamp.Apply(value[i] + 3, i);
            }

            v = new Column<int>(value, n);
            Kernel.Run(v, Window.Range(0, n), new Delta(3), bounds);
            board.RunIntrinsicsChunk(boardMirror, 3, bounds.Clamp, 0, n);
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(appliedB[i], value[i]);
                Assert.Equal(boardMirror[i], value[i]);
            }
        }
    }

    [Fact]
    public void MaskedWindows_MatchTheOldVerb_AtAlignedAndUnalignedOffsets()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 100f).Build(), 256);
        const int n = 256;
        var words = stackalloc ulong[4];
        words[0] = 0x5555_5555_5555_5555ul;
        words[1] = ulong.MaxValue;
        words[2] = 0x0F0F_0F0F_0F0F_0F0Ful;
        words[3] = 0ul;
        var value = stackalloc int[n];
        var twin = stackalloc int[n];
        var appliedA = stackalloc int[n];
        var appliedB = stackalloc int[n];
        foreach (var offset in new[] { 0, 3, 64 })
        {
            var rng = new Random(offset + 11);
            for (var i = 0; i < n; i++)
            {
                value[i] = rng.Next(0, 40);
                twin[i] = value[i];
                appliedA[i] = 0;
                appliedB[i] = 0;
            }

            var wordCount = (n - offset) >> 6;
            var bounds = Bounds.Constant(0, 50);
            Kernel.Run(new Column<int>(value, n), Window.Masked(offset, words, wordCount), new Delta(7), bounds, new Column<int>(appliedA, n));
            board.RunEnabledChunk(twin, 7, bounds.Clamp, words, wordCount, offset, appliedB);
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(twin[i], value[i]);
            }

            var reach = offset + wordCount * 64;
            for (var i = offset; i < reach; i++)
            {
                Assert.Equal(appliedB[i], appliedA[i]);
            }
        }
    }

    [Fact]
    public void Guards_RejectWindowsBeyondTheColumn()
    {
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        var words = stackalloc ulong[1];
        var column = new Column<int>(value, 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.Run(column, Window.Range(0, 65), new Delta(1), Bounds.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.Run(column, Window.Range(-1, 1), new Delta(1), Bounds.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.Run(column, Window.Masked(1, words, 1), new Delta(1), Bounds.None));
        Assert.Throws<ArgumentException>(() => Kernel.Run(column, Window.Range(0, 64), new Delta(1), Bounds.None, new Column<int>(applied, 63)));
        Assert.Throws<ArgumentException>(() => new Column<int>(null, 4));
        Assert.Throws<ArgumentNullException>(() => new Delta((int*)null));
        Assert.Throws<ArgumentNullException>(() => Window.Masked(0, null, 1));
    }

    [Fact]
    public void MaskedWindow_PartitionIsInvariant_UnderShuffledOrder()
    {
        const int n = 320;
        var rng = new Random(20261023);
        var words = stackalloc ulong[4];
        for (var w = 0; w < 4; w++)
        {
            words[w] = (ulong)rng.NextInt64(long.MinValue, long.MaxValue);
        }

        var deltaCol = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            deltaCol[i] = rng.Next(-25, 25);
        }

        var whole = stackalloc int[n];
        var parts = stackalloc int[n];
        var appliedWhole = stackalloc int[n];
        var appliedParts = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            whole[i] = rng.Next(-40, 40);
            parts[i] = whole[i];
        }

        var bounds = Bounds.Constant(-60, 60);
        Kernel.Run(new Column<int>(whole, n), Window.Masked(0, words, 4), new Delta(deltaCol), bounds, new Column<int>(appliedWhole, n));

        var order = new[] { 2, 0, 3, 1 };
        foreach (var w in order)
        {
            Kernel.Run(new Column<int>(parts, n), Window.Masked(w * 64, words + w, 1), new Delta(deltaCol), bounds, new Column<int>(appliedParts, n));
        }

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(whole[i], parts[i]);
            Assert.Equal(appliedWhole[i], appliedParts[i]);
        }
    }

    [Fact]
    public void Bounds_StatsCaptureStatColumns_AtConstruction()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("floor", -5f).Stat("cap", 45f).Build(), 64);
        var floor = board.Schema.Stat("floor");
        var cap = board.Schema.Stat("cap");
        board.Acquire();
        board.Recalculate();
        var bounds = Bounds.Stats(board, floor, cap);
        var value = stackalloc int[64];
        value[0] = 100;
        Kernel.Run(new Column<int>(value, 64), Window.Range(0, 1), new Delta(0), bounds);
        Assert.Equal(45, value[0]);
        value[0] = -100;
        Kernel.Run(new Column<int>(value, 64), Window.Range(0, 1), new Delta(0), bounds);
        Assert.Equal(-5, value[0]);
    }
}
