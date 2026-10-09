using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class DeadSlotQueueTests
{
    [Fact]
    public void AcquireReleaseBeforeRecalc_NeverMakesTheDeadSlotASetMember()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("speed", 6f).Stat("mana", 1f).Build(), 128);
        var speed = board.Schema.Stat("speed");
        var keep = board.Acquire();
        var transient = board.Acquire();
        board.AddModifier(transient, speed, new Modifier(ModifierKind.Added, 1f));
        board.Release(transient);
        board.Recalculate();
        Assert.Equal(board.LiveCount, board.MemberTotal(speed));
        Assert.Equal(board.LiveCount, board.MemberTotal(board.Schema.Stat("mana")));
        Assert.Equal(6f, board.Output(speed)[keep]);
    }

    [Fact]
    public void RenumberAfterTransientSlot_NeverBroadcastsOutsideTheColumn()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("speed", 6f).Build(), 64);
        var speed = board.Schema.Stat("speed");
        var guarded = (float*)NativeMemory.Alloc(66 * sizeof(float));
        try
        {
            guarded[0] = 12345f;
            guarded[65] = 12345f;
            board.BindOutput(speed, guarded + 1, 1, 64);
            var a = board.Acquire();
            var b = board.Acquire();
            board.Release(b);
            board.Recalculate();
            var map = new int[64];
            Array.Fill(map, -1);
            map[a] = 7;
            board.RenumberSlots(map, 1);
            board.BindOutput(speed, guarded + 1, 1, 64);
            board.AddModifier(7, speed, new Modifier(ModifierKind.Added, 1f));
            board.Recalculate();
            Assert.Equal(12345f, guarded[0]);
            Assert.Equal(12345f, guarded[65]);
            Assert.Equal(7f, guarded[1 + 7]);
            Assert.Equal(board.LiveCount, board.MemberTotal(speed));
        }
        finally
        {
            NativeMemory.Free(guarded);
        }
    }
}
