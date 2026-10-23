using System;
using Sv;
using Xunit;

public unsafe class UndoTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("resolve", 5f)
        .Build();

    static uint Next(ref uint seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return seed;
    }

    static int LiveSlotAt(Board board, ref uint seed)
    {
        var pick = (int)(Next(ref seed) % (uint)board.Capacity);
        for (var offset = 0; offset < board.Capacity; offset++)
        {
            var slot = (pick + offset) % board.Capacity;
            if (board.IsLive(slot))
            {
                return slot;
            }
        }

        return -1;
    }

    static void RunStep(Board board, ref uint seed, long tick)
    {
        var power = board.Schema.Stat("power");
        var resolve = board.Schema.Stat("resolve");
        var choice = Next(ref seed) % 6;
        if (choice == 0)
        {
            board.Acquire();
        }
        else if (choice == 1)
        {
            var slot = LiveSlotAt(board, ref seed);
            if (slot >= 1)
            {
                board.Release(slot);
            }
        }
        else if (choice is 2 or 3)
        {
            var slot = LiveSlotAt(board, ref seed);
            if (slot >= 0)
            {
                var stat = choice == 2 ? power : resolve;
                var value = Next(ref seed) % 5;
                var kind = value % 2 == 0 ? ModifierKind.Added : ModifierKind.Multi;
                var expiry = value % 3 == 0 ? tick + 2 : long.MaxValue;
                board.AddModifier(slot, stat, new Modifier(kind, value), 0, expiry);
            }
        }
        else if (choice == 4)
        {
            board.ExpireModifiers(tick);
        }
        else
        {
            var slot = LiveSlotAt(board, ref seed);
            if (slot >= 0)
            {
                var spawned = stackalloc int[2];
                board.Spawn(slot, 2, spawned);
            }
        }

        board.Recalculate();
    }

    static void RunFrame(Board board, long frame, int steps)
    {
        var seed = 0x9E3779B9u * (uint)(frame + 1);
        for (var step = 0; step < steps; step++)
        {
            RunStep(board, ref seed, frame);
        }
    }

    [Fact]
    public void Rollback_MatchesSnapshotTakenAtTheMark()
    {
        using var board = Board.Create(Schema(), 64);
        board.BeginUndoWindow(8);
        var marks = new SvState[8];
        var digests = new ulong[8];
        for (var frame = 0; frame < 8; frame++)
        {
            marks[frame] = new SvState();
            board.SaveState(marks[frame]);
            digests[frame] = board.Digest;
            board.BeginUndoFrame();
            RunFrame(board, frame, 5);
        }

        for (var depth = 1; depth <= 8; depth++)
        {
            board.Rollback(depth);
            using var after = new SvState();
            board.SaveState(after);
            var expected = marks[8 - depth];
            Assert.True(expected.Bytes.SequenceEqual(after.Bytes), $"rollback {depth} must equal the snapshot at the mark");
            Assert.Equal(digests[8 - depth], board.Digest);
            for (var frame = 8 - depth; frame < 8; frame++)
            {
                board.BeginUndoFrame();
                RunFrame(board, frame, 5);
            }
        }
    }

    [Fact]
    public void Rollback_ResimReplaysFramesBitIdentically()
    {
        using var board = Board.Create(Schema(), 64);
        board.BeginUndoWindow(8);
        for (var frame = 0; frame < 8; frame++)
        {
            board.BeginUndoFrame();
            RunFrame(board, frame, 5);
        }

        using var first = new SvState();
        board.SaveState(first);
        var digest = board.Digest;

        board.Rollback(8);
        Assert.Equal(0, board.UndoFrames);
        for (var frame = 0; frame < 8; frame++)
        {
            board.BeginUndoFrame();
            RunFrame(board, frame, 5);
        }

        using var second = new SvState();
        board.SaveState(second);
        Assert.True(first.Bytes.SequenceEqual(second.Bytes), "resim after rollback must reproduce the frames bit-identically");
        Assert.Equal(digest, board.Digest);
    }

    [Fact]
    public void UndoWindow_EvictsOldestMarkAndLimitsRollback()
    {
        using var board = Board.Create(Schema(), 64);
        board.BeginUndoWindow(3);
        var marks = new SvState[5];
        for (var frame = 0; frame < 5; frame++)
        {
            marks[frame] = new SvState();
            board.SaveState(marks[frame]);
            board.BeginUndoFrame();
            RunFrame(board, frame, 3);
        }

        Assert.Equal(3, board.UndoFrames);
        Assert.Throws<ArgumentOutOfRangeException>(() => board.Rollback(4));
        board.Rollback(3);
        using var after = new SvState();
        board.SaveState(after);
        Assert.True(marks[2].Bytes.SequenceEqual(after.Bytes), "the deepest rollback reaches the oldest retained mark");
        board.EndUndoWindow();
        Assert.Equal(0, board.UndoFrames);
        Assert.Throws<InvalidOperationException>(() => board.Rollback(1));
    }

    [Fact]
    public void Rollback_AfterBulkSpawnRestoresDigest()
    {
        using var board = Board.Create(Schema(), 65536);
        var power = board.Schema.Stat("power");
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        board.BeginUndoWindow(2);
        var markDigest = board.Digest;
        using var mark = new SvState();
        board.SaveState(mark);
        board.BeginUndoFrame();

        var slots = stackalloc int[40000];
        board.Spawn(template, 40000, slots);
        Assert.True(board.UndoFrames > 0);
        board.Rollback(1);
        using var after = new SvState();
        board.SaveState(after);
        Assert.True(mark.Bytes.SequenceEqual(after.Bytes), "rollback over a deferred bulk spawn restores the mark");
        Assert.Equal(markDigest, board.Digest);
    }

    [Fact]
    public void RebindAndLoad_CloseTheUndoWindow()
    {
        using var board = Board.Create(Schema(), 16);
        var column = stackalloc float[16];
        board.BindOutput(board.Schema.Stat("power"), column);
        using var saved = new SvState();
        board.SaveState(saved);

        board.BeginUndoWindow(4);
        Assert.Equal(1, board.UndoFrames);
        board.BindOutput(board.Schema.Stat("power"), column);
        Assert.Equal(0, board.UndoFrames);
        board.BeginUndoWindow(4);
        board.LoadState(saved);
        Assert.Equal(0, board.UndoFrames);
    }

    [Fact]
    public void Rollback_DropsFreshlyMaterializedColumns()
    {
        using var board = Board.Create(Schema(), 64);
        var power = board.Schema.Stat("power");
        for (var i = 0; i < 5; i++)
        {
            board.Acquire();
        }

        board.AddModifier(2, power, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        var expected = board.Stat(2, power);
        board.BeginUndoWindow(8);
        board.BeginUndoFrame();
        var before = board.MemoryBytes;
        var column = board.OutputPtr(power);
        Assert.Equal(before + 64 * sizeof(float), board.MemoryBytes);
        Assert.Equal(expected, column[2]);
        board.Rollback(1);
        Assert.Equal(before, board.MemoryBytes);
        Assert.Equal(expected, board.OutputPtr(power)[2]);
        board.EndUndoWindow();
    }
}
