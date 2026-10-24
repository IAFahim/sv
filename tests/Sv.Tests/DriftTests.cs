using System;
using Sv;
using Xunit;

public class DriftTests
{
    static ClampShift Rule(Random rng)
    {
        var delta = rng.Next(-7, 8);
        var floor = rng.Next(-30, 5);
        var ceiling = rng.Next(floor, 40);
        return ClampShift.Step(delta, floor, ceiling);
    }

    [Fact]
    public unsafe void KernelEager_MatchesDrift_AtEveryTick()
    {
        const int n = 64;
        const int ticks = 200;
        using var drift = DriftStore.Create(n);
        var values = stackalloc int[n];
        var deltas = stackalloc int[n];
        var rng = new Random(20261025);
        for (var i = 0; i < n; i++)
        {
            values[i] = rng.Next(-40, 98);
            deltas[i] = -5 + (i % 11);
            drift.Capture(i, values[i], 0, ClampShift.Step(deltas[i], -40, 97));
        }

        var column = new Column<int>(values, n);
        var perRow = new Delta(deltas);
        var bounds = Bounds.Constant(-40, 97);
        for (var tick = 1; tick <= ticks; tick++)
        {
            Kernel.Run(column, Window.Range(0, n), perRow, bounds);
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(values[i], drift.Read(i, tick));
            }
        }
    }

    [Fact]
    public void WritesAndRebases_MatchEagerPerFrame()
    {
        const int slots = 8;
        const int frames = 400;
        using var drift = DriftStore.Create(slots);
        var eager = new int[slots];
        var rules = new ClampShift[slots];
        var rng = new Random(20261026);
        for (var i = 0; i < slots; i++)
        {
            rules[i] = Rule(rng);
            eager[i] = rng.Next(-60, 61);
            drift.Capture(i, eager[i], 0, rules[i]);
        }

        rules[3] = ClampShift.Step(2, 0, 10);
        drift.Capture(3, 0, 0, rules[3]);
        eager[3] = 0;

        for (var frame = 1; frame <= frames; frame++)
        {
            for (var i = 0; i < slots; i++)
            {
                if (i != 3 && rng.Next(3) == 0)
                {
                    var raw = rng.Next(-80, 81);
                    eager[i] = raw;
                    drift.Write(i, raw, frame);
                }

                eager[i] = rules[i].Apply(eager[i]);
                if (i != 3 && rng.Next(8) == 0)
                {
                    rules[i] = Rule(rng);
                    drift.Rebase(i, frame, rules[i]);
                }

                Assert.Equal(eager[i], drift.Read(i, frame));
            }
        }

        Assert.Equal(10, drift.Read(3, frames));
    }

    [Fact]
    public void Write_ComposesBeforeTheFrameDriftStep()
    {
        using var drift = DriftStore.Create(1);
        drift.Capture(0, 10, 5, ClampShift.Step(3, 0, 100));
        Assert.Equal(10, drift.Read(0, 5));
        Assert.Equal(13, drift.Read(0, 6));
        drift.Write(0, 40, 6);
        Assert.Equal(43, drift.Read(0, 6));
        Assert.Equal(52, drift.Read(0, 9));
        drift.Write(0, 7, 9);
        Assert.Equal(10, drift.Read(0, 9));
        Assert.Equal(19, drift.Read(0, 12));
        Assert.Throws<InvalidOperationException>(() => drift.Write(0, 5, 6));
    }

    [Fact]
    public void Rebase_FoldsSettledHistory_AndSwapsRule()
    {
        using var drift = DriftStore.Create(1);
        drift.Capture(0, 0, 0, ClampShift.Step(2, 0, 10));
        Assert.Equal(10, drift.Read(0, 100));
        drift.Rebase(0, 100, ClampShift.Step(-1, 0, 10));
        Assert.Equal(10, drift.Read(0, 100));
        Assert.Equal(9, drift.Read(0, 101));
        Assert.Equal(0, drift.Read(0, 130));
        drift.Rebase(0, 101, ClampShift.Step(5, 0, 10));
        Assert.Equal(9, drift.Read(0, 101));
        Assert.Equal(10, drift.Read(0, 102));
        Assert.Throws<InvalidOperationException>(() => drift.Rebase(0, 100, ClampShift.Step(1, 0, 10)));
    }

    [Fact]
    public unsafe void Materialize_RangeAndMasked_MatchRead()
    {
        const int n = 320;
        using var drift = DriftStore.Create(n);
        var rng = new Random(20261027);
        for (var i = 0; i < n; i++)
        {
            drift.Capture(i, rng.Next(-50, 51), i % 13, ClampShift.Step(-5 + (i % 9), -30, 40));
        }

        const int sentinel = int.MinValue;
        var range = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            range[i] = sentinel;
        }

        drift.Materialize(Window.Range(0, n), range, 25);
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(drift.Read(i, 25), range[i]);
        }

        var words = stackalloc ulong[3];
        for (var w = 0; w < 3; w++)
        {
            ulong word = 0;
            for (var bit = 0; bit < 64; bit++)
            {
                if (((w * 64 + bit) * 7) % 3 != 0)
                {
                    word |= 1ul << bit;
                }
            }

            words[w] = word;
        }

        var masked = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            masked[i] = sentinel;
        }

        drift.Materialize(Window.Masked(64, words, 3), masked, 25);
        for (var w = 0; w < 3; w++)
        {
            for (var bit = 0; bit < 64; bit++)
            {
                var row = 64 + w * 64 + bit;
                var expected = (words[w] & (1ul << bit)) != 0 ? drift.Read(row, 25) : sentinel;
                Assert.Equal(expected, masked[row]);
            }
        }

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(sentinel, masked[i]);
        }
    }

    [Fact]
    public void MissingSegment_Throws()
    {
        using var drift = DriftStore.Create(4);
        Assert.Throws<InvalidOperationException>(() => drift.Read(1, 10));
        Assert.Throws<InvalidOperationException>(() => drift.Write(1, 5, 10));
        Assert.Throws<InvalidOperationException>(() => drift.Rebase(1, 10, ClampShift.Step(1, 0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => drift.Read(4, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => drift.Read(-1, 10));
        Assert.Throws<ArgumentException>(() => DriftStore.Create(0));
    }

    [Fact]
    public void Dispose_IsIdempotent_AndGuardsUse()
    {
        var drift = DriftStore.Create(4);
        drift.Capture(0, 1, 0, ClampShift.Step(1, 0, 10));
        drift.Dispose();
        drift.Dispose();
        Assert.Throws<ObjectDisposedException>(() => drift.Read(0, 1));
    }
}
