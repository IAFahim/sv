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

    [Fact]
    public unsafe void RebaseRules_RangeAndMasked_MatchPerRow()
    {
        const int n = 130;
        using var bulk = DriftStore.Create(n);
        using var perRow = DriftStore.Create(n);
        var active = new bool[n];
        var rate = stackalloc float[n];
        var floor = stackalloc float[n];
        var ceiling = stackalloc float[n];
        var rng = new Random(20261030);
        for (var i = 0; i < n; i++)
        {
            rate[i] = i % 7 == 0 ? float.NaN : rng.Next(-6, 7);
            floor[i] = i % 9 == 0 ? float.NegativeInfinity : rng.Next(-30, 0);
            ceiling[i] = i % 11 == 0 ? float.PositiveInfinity : rng.Next(0, 40) + 0.5f;
            if (i == 5 || i == 70)
            {
                continue;
            }

            var value = rng.Next(-50, 51);
            var rule = ClampShift.Step(rng.Next(-4, 5), -30, 40);
            bulk.Capture(i, value, i % 3, rule);
            perRow.Capture(i, value, i % 3, rule);
            active[i] = true;
        }

        bulk.RebaseRules(Window.Range(0, 40), 20, rate, floor, ceiling);
        for (var i = 0; i < 40; i++)
        {
            if (active[i])
            {
                perRow.Rebase(i, 20, ClampShift.Step(Clamp.BoundOf(rate[i]), Clamp.BoundOf(floor[i]), Clamp.BoundOf(ceiling[i])));
            }
        }

        var words = stackalloc ulong[2];
        words[0] = 0xF0F0_F0F0_F0F0_F0F0ul;
        bulk.RebaseRules(Window.Masked(64, words, 1), 20, rate, floor, ceiling);
        for (var row = 64; row < 128; row++)
        {
            if (active[row] && (words[0] & (1ul << (row - 64))) != 0)
            {
                perRow.Rebase(row, 20, ClampShift.Step(Clamp.BoundOf(rate[row]), Clamp.BoundOf(floor[row]), Clamp.BoundOf(ceiling[row])));
            }
        }

        for (var i = 0; i < n; i++)
        {
            if (!active[i])
            {
                Assert.False(bulk.SegmentActive(i));
                continue;
            }

            for (var tick = 20; tick <= 34; tick++)
            {
                Assert.Equal(perRow.Read(i, tick), bulk.Read(i, tick));
            }
        }

        Assert.Throws<ArgumentException>(() => bulk.RebaseRules(Window.Range(0, 4), 21, null, floor, ceiling));
    }

    [Fact]
    public unsafe void Renumber_ReadCommutesWithPermutation()
    {
        const int n = 96;
        using var drift = DriftStore.Create(n);
        var rng = new Random(20261031);
        var active = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (rng.Next(4) == 0)
            {
                continue;
            }

            drift.Capture(i, rng.Next(-50, 51), rng.Next(3), Rule(rng));
            active[i] = true;
        }

        var before = new int[n];
        for (var i = 0; i < n; i++)
        {
            if (active[i])
            {
                before[i] = drift.Read(i, 123);
            }
        }

        var perm = new int[n];
        for (var i = 0; i < n; i++)
        {
            perm[i] = i;
        }

        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }

        var oldToNew = new int[n];
        var next = 0;
        for (var i = 0; i < n; i++)
        {
            oldToNew[i] = active[i] ? perm[next++] : -1;
        }

        drift.Renumber(oldToNew);
        var targeted = new bool[n];
        for (var old = 0; old < n; old++)
        {
            if (active[old])
            {
                targeted[oldToNew[old]] = true;
                Assert.Equal(before[old], drift.Read(oldToNew[old], 123));
                Assert.Equal(before[old], drift.Read(oldToNew[old], 456));
            }
        }

        for (var slot = 0; slot < n; slot++)
        {
            Assert.Equal(targeted[slot], drift.SegmentActive(slot));
        }

        oldToNew[0] = n;
        Assert.Throws<ArgumentOutOfRangeException>(() => drift.Renumber(oldToNew));
        oldToNew[0] = -1;
        Assert.Throws<ArgumentException>(() => drift.Renumber(oldToNew.AsSpan(0, n - 1)));
    }

    [Fact]
    public unsafe void Rollback_RestoresDriftSegments_WithBoardUndo()
    {
        using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 16);
        using var drift = DriftStore.Create(6);
        drift.AttachBoard(board);
        var rng = new Random(20261101);
        for (var slot = 0; slot < 6; slot++)
        {
            drift.Capture(slot, rng.Next(-50, 51), 0, Rule(rng));
        }

        board.BeginUndoWindow(8);
        var marks = new (int V, long T, ClampShift R)[8][];
        for (var frame = 0; frame < 8; frame++)
        {
            marks[frame] = new (int V, long T, ClampShift R)[6];
            for (var slot = 0; slot < 6; slot++)
            {
                drift.CopySegment(slot, out var rule, out var t0, out var v0);
                marks[frame][slot] = (v0, t0, rule);
            }

            board.BeginUndoFrame();
            for (var slot = 0; slot < 6; slot++)
            {
                if (rng.Next(2) == 0)
                {
                    drift.Write(slot, rng.Next(-80, 81), frame + 1);
                }

                if (rng.Next(3) == 0)
                {
                    drift.Rebase(slot, frame + 1, Rule(rng));
                }
            }
        }

        for (var depth = 1; depth <= 8; depth++)
        {
            board.Rollback(depth);
            var expected = marks[8 - depth];
            for (var slot = 0; slot < 6; slot++)
            {
                drift.CopySegment(slot, out var rule, out var t0, out var v0);
                Assert.True(expected[slot].V == v0 && expected[slot].T == t0 && expected[slot].R.Equals(rule), $"depth {depth} slot {slot}: expected ({expected[slot].V},{expected[slot].T}), got ({v0},{t0})");
                for (var tick = expected[slot].T; tick <= expected[slot].T + 32; tick++)
                {
                    Assert.Equal(expected[slot].R.Repeat(tick - expected[slot].T).Apply(expected[slot].V), drift.Read(slot, tick));
                }
            }

            for (var frame = 8 - depth; frame < 8; frame++)
            {
                board.BeginUndoFrame();
                for (var slot = 0; slot < 6; slot++)
                {
                    if (rng.Next(2) == 0)
                    {
                        drift.Write(slot, rng.Next(-80, 81), frame + 1);
                    }

                    if (rng.Next(3) == 0)
                    {
                        drift.Rebase(slot, frame + 1, Rule(rng));
                    }
                }
            }
        }
    }

    sealed class DriftModel
    {
        public bool[] Active = new bool[0];

        public int[] Value = new int[0];

        public ClampShift[] Rule = new ClampShift[0];

        public long Tick;

        public DriftModel Clone()
        {
            return new DriftModel { Active = (bool[])Active.Clone(), Value = (int[])Value.Clone(), Rule = (ClampShift[])Rule.Clone(), Tick = Tick };
        }
    }

    [Fact]
    public unsafe void RandomSchedule_HitsHealsBoundChangesSpawnsRenumbersRollbacks_MatchesEager()
    {
        const int n = 24;
        const int frames = 160;
        using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 64);
        using var drift = DriftStore.Create(n);
        drift.AttachBoard(board);
        var rng = new Random(20261102);
        var model = new DriftModel { Active = new bool[n], Value = new int[n], Rule = new ClampShift[n], Tick = 0 };
        var rate = stackalloc float[n];
        var floor = stackalloc float[n];
        var ceiling = stackalloc float[n];
        board.BeginUndoWindow(8);
        var marks = new List<DriftModel>();
        var tick = 0L;
        var framesRun = 0;
        while (framesRun < frames)
        {
            framesRun++;
            tick++;
            marks.Add(model.Clone());
            board.BeginUndoFrame();
            for (var slot = 0; slot < n; slot++)
            {
                if (model.Active[slot] && rng.Next(3) == 0)
                {
                    var raw = rng.Next(-90, 91);
                    model.Value[slot] = raw;
                    drift.Write(slot, raw, tick);
                }
            }

            var boundChange = rng.Next(4) == 0;
            if (boundChange)
            {
                for (var slot = 0; slot < n; slot++)
                {
                    rate[slot] = rng.Next(-6, 7);
                    floor[slot] = rng.Next(-40, 0);
                    ceiling[slot] = rng.Next(0, 45);
                }

                drift.RebaseRules(Window.Range(0, n), tick, rate, floor, ceiling);
            }

            for (var slot = 0; slot < n; slot++)
            {
                if (model.Active[slot])
                {
                    model.Value[slot] = model.Rule[slot].Apply(model.Value[slot]);
                }
            }

            if (boundChange)
            {
                for (var slot = 0; slot < n; slot++)
                {
                    if (model.Active[slot])
                    {
                        model.Rule[slot] = ClampShift.Step(Clamp.BoundOf(rate[slot]), Clamp.BoundOf(floor[slot]), Clamp.BoundOf(ceiling[slot]));
                    }
                }
            }

            for (var slot = 0; slot < n; slot++)
            {
                if (!model.Active[slot] && rng.Next(5) == 0)
                {
                    var value = rng.Next(-50, 51);
                    var rule = Rule(rng);
                    model.Active[slot] = true;
                    model.Value[slot] = value;
                    model.Rule[slot] = rule;
                    drift.Capture(slot, value, tick, rule);
                }
            }

            model.Tick = tick;
            for (var slot = 0; slot < n; slot++)
            {
                if (model.Active[slot])
                {
                    Assert.True(drift.Read(slot, tick) == model.Value[slot], $"frame {framesRun} tick {tick} slot {slot}: drift {drift.Read(slot, tick)} vs eager {model.Value[slot]}");
                }
            }

            var choice = rng.Next(12);
            if (choice == 0 && marks.Count >= 3 && framesRun < frames - 6)
            {
                var depth = 1 + rng.Next(Math.Min(3, marks.Count - 1));
                board.Rollback(depth);
                model = marks[^depth];
                marks.RemoveRange(marks.Count - depth, depth);
                tick = model.Tick;
                for (var slot = 0; slot < n; slot++)
                {
                    if (model.Active[slot])
                    {
                        Assert.True(drift.Read(slot, tick) == model.Value[slot], $"post-rollback tick {tick} slot {slot}");
                    }
                }
            }
            else if (choice == 1 && framesRun < frames - 10)
            {
                var perm = new int[n];
                for (var i = 0; i < n; i++)
                {
                    perm[i] = i;
                }

                for (var i = n - 1; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    (perm[i], perm[j]) = (perm[j], perm[i]);
                }

                var oldToNew = new int[board.Capacity];
                for (var i = 0; i < board.Capacity; i++)
                {
                    oldToNew[i] = -1;
                }

                var next = 0;
                for (var slot = 0; slot < n; slot++)
                {
                    if (model.Active[slot])
                    {
                        oldToNew[slot] = perm[next++];
                    }
                }

                var active = model.Active;
                var value = model.Value;
                var rule = model.Rule;
                model.Active = new bool[n];
                model.Value = new int[n];
                model.Rule = new ClampShift[n];
                for (var slot = 0; slot < n; slot++)
                {
                    if (active[slot])
                    {
                        model.Active[oldToNew[slot]] = true;
                        model.Value[oldToNew[slot]] = value[slot];
                        model.Rule[oldToNew[slot]] = rule[slot];
                    }
                }

                drift.Renumber(oldToNew.AsSpan(0, n));
                board.RenumberSlots(oldToNew, 0);
                board.BeginUndoWindow(8);
                marks.Clear();
                for (var slot = 0; slot < n; slot++)
                {
                    if (model.Active[slot])
                    {
                        Assert.True(drift.Read(slot, tick) == model.Value[slot], $"post-renumber tick {tick} slot {slot}");
                    }
                }
            }
        }
    }
}
