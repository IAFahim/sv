using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class MirrorRollbackTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("max_hp", 100f)
        .Intrinsic("hp")
        .Build();

    [Fact]
    public void LoadState_OlderSnapshot_KeepsMirrorPointerStable()
    {
        using var board = Board.Create(TestSchema(), 64);
        using var state = new SvState();
        var stat = board.Schema.Stat("max_hp");
        var slot = board.Acquire();
        board.Recalculate();
        board.SaveState(state);

        var mirror = board.MirrorBounds(stat);
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 40f));
        board.Recalculate();
        Assert.Equal(Clamp.BoundOf(140f), mirror[slot]);

        board.LoadState(state);

        Assert.Equal((nint)mirror, (nint)board.MirrorBounds(stat));
        Assert.Equal(Clamp.BoundOf(100f), mirror[slot]);
    }

    [Fact]
    public void LoadState_SnapshotMirror_RestoresContentBitExactly()
    {
        using var board = Board.Create(TestSchema(), 64);
        using var state = new SvState();
        var stat = board.Schema.Stat("max_hp");
        var slot = board.Acquire();
        board.Recalculate();
        var mirror = board.MirrorBounds(stat);
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 25f));
        board.Recalculate();
        var expected = mirror[slot];
        board.SaveState(state);

        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 30f));
        board.Recalculate();
        Assert.NotEqual(expected, mirror[slot]);

        board.LoadState(state);

        Assert.Equal(expected, mirror[slot]);
    }

    [Fact]
    public void LoadState_RebroadcastsBoundColumnsFromRestoredState()
    {
        using var board = Board.Create(TestSchema(), 64);
        using var state = new SvState();
        var column = (float*)NativeMemory.AllocZeroed((nuint)64 * sizeof(float));
        try
        {
            var stat = board.Schema.Stat("max_hp");
            var slot = board.Acquire();
            board.BindOutput(stat, column, 1, 64);
            board.Recalculate();
            board.SaveState(state);

            column[slot] = 999f;
            board.LoadState(state);
            Assert.Equal(999f, column[slot]);

            board.Recalculate();

            Assert.Equal(100f, column[slot]);
        }
        finally
        {
            NativeMemory.Free(column);
        }
    }

    [Fact]
    public void LoadState_DirtyMarks_DoNotChangeRestoredOutputs()
    {
        using var board = Board.Create(TestSchema(), 64);
        using var state = new SvState();
        var stat = board.Schema.Stat("max_hp");
        var slot = board.Acquire();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 12f));
        board.Recalculate();
        board.SaveState(state);
        var expected = board.Output(stat)[slot];

        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 50f));
        board.Recalculate();
        board.LoadState(state);
        board.Recalculate();

        Assert.Equal(expected, board.Output(stat)[slot]);
    }
}
