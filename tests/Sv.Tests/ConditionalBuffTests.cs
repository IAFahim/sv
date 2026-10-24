using System;
using Sv;
using Xunit;

public unsafe class ConditionalBuffTests
{
    [Fact]
    public void ConditionalBuff_AddRemoveRideCrossings_NoPerFramePredicate()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("damage", 10f).Intrinsic("hp").Build(), 4);
        using var drift = DriftStore.Create(4);
        drift.AttachBoard(board);
        var damage = board.Schema.Stat("damage");
        var slot = board.Acquire();
        drift.Capture(slot, 100, 0, ClampShift.Step(-2, 0, 100));

        var buffTick = ClampShift.FirstTickAtLeast(ClampShift.Step(2, 0, 70), 0, 70, 1000);
        Assert.Equal(35, buffTick);
        Assert.Equal(32, drift.Read(slot, 34));
        Assert.Equal(30, drift.Read(slot, 35));

        board.ScheduleCrossing(buffTick, 0x100L);
        Span<long> due = stackalloc long[4];
        for (var tick = 1; tick < 35; tick++)
        {
            board.ExpireModifiers(tick);
            Assert.Equal(0, board.TakeDueCrossings(due));
        }

        board.ExpireModifiers(35);
        Assert.Equal(1, board.TakeDueCrossings(due));
        Assert.Equal(0x100L, due[0]);
        var handle = board.AddModifier(slot, damage, new Modifier(ModifierKind.Multi, 0.25f));
        board.Recalculate();
        Assert.Equal(12.5f, board.Stat(slot, damage));

        drift.Capture(slot, 30, 35, ClampShift.Step(2, 0, 100));
        var clearTick = 35 + ClampShift.FirstTickAtLeast(ClampShift.Step(2, 0, 30), 0, 30, 1000);
        Assert.Equal(50, clearTick);
        Assert.Equal(60, drift.Read(slot, clearTick));
        board.ScheduleCrossing(clearTick, 0x101L);
        for (var tick = 36; tick < 50; tick++)
        {
            board.ExpireModifiers(tick);
            Assert.Equal(0, board.TakeDueCrossings(due));
            Assert.Equal(12.5f, board.Stat(slot, damage));
        }

        board.ExpireModifiers(50);
        Assert.Equal(1, board.TakeDueCrossings(due));
        Assert.Equal(0x101L, due[0]);
        board.RemoveModifier(slot, damage, handle);
        board.Recalculate();
        Assert.Equal(10f, board.Stat(slot, damage));
    }

    [Fact]
    public void ConditionalBuff_PopulationFiresInOnePass_Converges()
    {
        const int rows = 200;
        using var board = Board.Create(SchemaBuilder.Create().Stat("damage", 10f).Intrinsic("hp").Build(), rows);
        using var drift = DriftStore.Create(rows);
        drift.AttachBoard(board);
        var damage = board.Schema.Stat("damage");
        var rising = ClampShift.Step(2, 0, 270);
        var latest = 0L;
        for (var slot = 0; slot < rows; slot++)
        {
            board.Acquire();
            drift.Capture(slot, 300 - slot, 0, ClampShift.Step(-2, 0, 300));
            var tick = ClampShift.FirstTickAtLeast(rising, 0, 270 - slot, 1000);
            Assert.True(drift.Read(slot, tick) <= 30);
            Assert.True(drift.Read(slot, tick - 1) > 30);
            board.ScheduleCrossing(tick, slot);
            latest = Math.Max(latest, tick);
        }

        Assert.Equal(135, latest);
        Assert.Equal(0, board.ExpireModifiers(latest - 1));
        var expected = new (long Tick, long Id)[rows];
        for (var slot = 0; slot < rows; slot++)
        {
            expected[slot] = ((270 - slot + 1) / 2, slot);
        }

        Array.Sort(expected, static (a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Id.CompareTo(b.Id));
        Span<long> early = stackalloc long[rows];
        Assert.Equal(198, board.TakeDueCrossings(early));
        for (var i = 0; i < 198; i++)
        {
            Assert.Equal(expected[i].Id, early[i]);
        }

        Assert.Equal(0, board.ExpireModifiers(latest));
        Span<long> due = stackalloc long[2];
        Assert.Equal(2, board.TakeDueCrossings(due));
        Assert.Equal(expected[198].Id, due[0]);
        Assert.Equal(expected[199].Id, due[1]);
        for (var i = 0; i < 198; i++)
        {
            board.AddModifier((int)early[i], damage, new Modifier(ModifierKind.Multi, 0.25f));
        }

        for (var i = 0; i < 2; i++)
        {
            board.AddModifier((int)due[i], damage, new Modifier(ModifierKind.Multi, 0.25f));
        }

        board.Recalculate();
        Assert.Equal(1, board.SetCount);
        for (var slot = 0; slot < rows; slot++)
        {
            Assert.Equal(12.5f, board.Stat(slot, damage));
        }
    }
}
