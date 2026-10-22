using System;
using Sv;
using Xunit;

public unsafe class BindingGenerationTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

    [Fact]
    public void SvOwnedColumns_ReflectBroadcast_WithoutBind()
    {
        using var board = Board.Create(Schema(), 70);
        var speed = board.Schema.Stat("move_speed");
        Assert.Equal(128, board.Capacity);
        for (var i = 0; i < 5; i++)
        {
            board.Acquire();
            board.AddModifier(i, speed, new Modifier(ModifierKind.Added, i));
        }

        board.Recalculate();
        var column = board.OutputPtr(speed);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(board.Stat(i, speed), column[i]);
            Assert.Equal(6f + i, column[i]);
        }

        for (var i = 5; i < board.Capacity; i++)
        {
            Assert.Equal(0f, column[i]);
        }

        var span = board.Output(speed);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(column[i], span[i]);
        }
    }

    [Fact]
    public void ExternalBind_Invalidate_Recalculate_CheckedThrows_ReleaseSilent()
    {
        using var board = Board.Create(Schema(), 4);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        board.Acquire();
        board.BindExternal(speed, column);
        board.AddModifier(0, speed, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        Assert.Equal(7f, column[0]);

        board.AddModifier(0, speed, new Modifier(ModifierKind.Added, 2f));
        board.InvalidateBindings();
#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => board.Recalculate());
#else
        board.Recalculate();
        Assert.Equal(9f, column[0]);
#endif
    }

    [Fact]
    public void ExternalBind_Invalidate_Rebind_IsFreshAgain()
    {
        using var board = Board.Create(Schema(), 4);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        board.Acquire();
        board.BindExternal(speed, column);
        board.InvalidateBindings();
        board.BindExternal(speed, column);
        board.AddModifier(0, speed, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        Assert.Equal(7f, column[0]);
    }

    [Fact]
    public void ClampStat_CapturedBeforeInvalidation_ThrowsOnUseUnderChecked()
    {
        using var board = Board.Create(Schema(), 4);
        var maxMana = board.Schema.Stat("max_mana");
        var bound = stackalloc float[64];
        board.Acquire();
        board.BindOutput(maxMana, bound);
        board.Recalculate();
        Assert.Equal(100f, bound[0]);
        var clamp = Clamp.StatMax(board, maxMana);
        board.InvalidateBindings();
        var value = stackalloc int[64];
        value[0] = 500;
#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => board.RunIntrinsicsChunk(value, 0, clamp, 0, 1));
#else
        board.RunIntrinsicsChunk(value, 0, clamp, 0, 1);
        Assert.Equal(100, value[0]);
#endif
    }

    [Fact]
    public void BindExternal_HandleTracksGeneration()
    {
        using var board = Board.Create(Schema(), 4);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        board.Acquire();
        var handle = board.BindExternal(speed, column);
        Assert.Equal(board.BindingGeneration, handle.Generation);
        Assert.False(handle.IsStale(board));

        board.InvalidateBindings();
        Assert.True(handle.IsStale(board));

        var rebound = board.BindExternal(speed, column);
        Assert.False(rebound.IsStale(board));
        Assert.True(handle.IsStale(board));

        var other = board.Schema.Stat("max_mana");
        var otherColumn = stackalloc float[64];
        board.BindExternal(other, otherColumn);
        Assert.False(rebound.IsStale(board));
    }

    [Fact]
    public void ClampStat_OverSvOwned_NeverGoesStale()
    {
        using var board = Board.Create(Schema(), 4);
        var maxMana = board.Schema.Stat("max_mana");
        board.Acquire();
        board.Recalculate();
        var clamp = Clamp.StatMax(board, maxMana);
        board.InvalidateBindings();
        var value = stackalloc int[64];
        value[0] = 500;
        board.RunIntrinsicsChunk(value, 0, clamp, 0, 1);
        Assert.Equal(100, value[0]);
    }

    [Fact]
    public void SvOwnedRecalculate_AllocatesZeroManagedBytes()
    {
        using var board = Board.Create(Schema(), 128);
        var speed = board.Schema.Stat("move_speed");
        for (var i = 0; i < 127; i++)
        {
            board.Acquire();
        }

        var handle = board.AddModifier(0, speed, new Modifier(ModifierKind.Multi, 1.5f));
        board.Recalculate();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var it = 0; it < 2000; it++)
        {
            board.AddModifier(it % 127, speed, new Modifier(ModifierKind.Added, it % 5));
            board.Recalculate();
            var span = board.Output(speed);
            sink += (int)span[it % 127];
        }

        board.RemoveModifier(0, speed, handle);
        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"sv-owned recalculate allocated {deltaBytes} managed bytes (sink {sink})");
    }
}
