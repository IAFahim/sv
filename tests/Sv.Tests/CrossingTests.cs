using System;
using Sv;
using Xunit;

public class CrossingTests
{
    [Fact]
    public unsafe void ScheduledTick_IsTheFirstEagerTickThatReachesTheThreshold()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("max_mana", 100f).Intrinsic("mana").Build(), 8);
        var rng = new Random(20261103);
        var due = new long[8];
        var baseTick = 1000L;
        for (var trial = 0; trial < 200; trial++)
        {
            var step = rng.Next(1, 9);
            var floor = rng.Next(-50, 50);
            var ceiling = rng.Next(floor + 1, floor + 200);
            var rising = ClampShift.Step(step, floor, ceiling);
            var value = rng.Next(floor, ceiling + 1);
            var threshold = rng.Next(floor, ceiling + 2);
            const long horizon = 4000;
            var first = ClampShift.FirstTickAtLeast(rising, value, threshold, horizon);

            var eager = 0L;
            var v = value;
            while (eager < horizon)
            {
                eager++;
                v = rising.Apply(v);
                if (v >= threshold)
                {
                    break;
                }
            }

            if (v < threshold)
            {
                eager = horizon + 1;
            }

            Assert.True(first == eager, $"trial {trial}: scheduled {first} vs eager {eager} (step {step}, [{floor},{ceiling}], value {value}, threshold {threshold})");
            if (eager > horizon)
            {
                continue;
            }

            var id = 42L + trial;
            board.ScheduleCrossing(baseTick + first, id);
            Assert.Equal(1, board.PendingCrossings);
            for (var tick = 0; tick < first; tick++)
            {
                board.ExpireModifiers(baseTick + tick);
                Assert.Equal(0, board.TakeDueCrossings(due));
            }

            board.ExpireModifiers(baseTick + first);
            Assert.Equal(1, board.TakeDueCrossings(due));
            Assert.Equal(id, due[0]);
            Assert.Equal(0, board.PendingCrossings);
            baseTick += first + 64;
        }
    }

    [Fact]
    public unsafe void Crossings_DrainInTickIdOrder_AlongsideExpiries()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", 10f).Build(), 8);
        var slot = board.Acquire();
        var stat = board.Schema.Stat("power");
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 5f), 0, 30);
        board.ScheduleCrossing(20, 7);
        board.ScheduleCrossing(20, 3);
        board.ScheduleCrossing(10, 50);
        Assert.Equal(3, board.PendingCrossings);

        var due = new long[8];
        Assert.Equal(0, board.ExpireModifiers(10));
        Assert.Equal(1, board.TakeDueCrossings(due));
        Assert.Equal(50L, due[0]);
        Assert.Equal(2, board.PendingCrossings);

        Assert.Equal(0, board.ExpireModifiers(19));
        Assert.Equal(0, board.TakeDueCrossings(due));
        Assert.Equal(0, board.ExpireModifiers(20));
        Assert.Equal(2, board.TakeDueCrossings(due));
        Assert.Equal(3L, due[0]);
        Assert.Equal(7L, due[1]);
        Assert.Equal(1, board.ExpireModifiers(30));
        Assert.Equal(0, board.PendingCrossings);
    }

    [Fact]
    public unsafe void Rollback_RestoresCrossings_AndTheRingIsAnOutput()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", 10f).Build(), 8);
        var slot = board.Acquire();
        var stat = board.Schema.Stat("power");
        board.BeginUndoWindow(8);
        board.BeginUndoFrame();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 5f), 0, 30);
        board.ScheduleCrossing(10, 50);
        board.ScheduleCrossing(20, 60);
        board.BeginUndoFrame();
        Assert.Equal(0, board.ExpireModifiers(10));
        var due = new long[8];
        Assert.Equal(1, board.TakeDueCrossings(due));
        Assert.Equal(50L, due[0]);

        board.Rollback(1);
        Assert.Equal(2, board.PendingCrossings);
        Assert.Equal(0, board.TakeDueCrossings(due));
        board.ExpireModifiers(10);
        Assert.Equal(1, board.TakeDueCrossings(due));
        Assert.Equal(50L, due[0]);
    }

    [Fact]
    public void TakeDueCrossings_TooSmallSpan_Throws()
    {
        using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 8);
        board.ScheduleCrossing(5, 1);
        board.ScheduleCrossing(5, 2);
        board.ExpireModifiers(5);
        Assert.Throws<ArgumentException>(() => board.TakeDueCrossings(new long[1]));
        var due = new long[2];
        Assert.Equal(2, board.TakeDueCrossings(due));
        Assert.Equal(1L, due[0]);
        Assert.Equal(2L, due[1]);
    }
}
