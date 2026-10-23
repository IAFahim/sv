using System;
using Sv;
using Xunit;

public unsafe class KeyframeTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("resolve", 5f)
        .Build();

    static void AssertColumnsEqual(Board a, Board b, StatId stat)
    {
        var x = a.Output(stat);
        var y = b.Output(stat);
        Assert.Equal(x.Length, y.Length);
        for (var i = 0; i < x.Length; i++)
        {
            Assert.Equal(BitConverter.SingleToUInt32Bits(x[i]), BitConverter.SingleToUInt32Bits(y[i]));
        }
    }

    [Fact]
    public void Keyframe_AppliedToBase_MatchesFullState()
    {
        using var board = Board.Create(Schema(), 1024);
        var power = board.Schema.Stat("power");
        var resolve = board.Schema.Stat("resolve");
        for (var i = 0; i < 64; i++)
        {
            board.Acquire();
        }

        board.AddModifier(3, power, new Modifier(ModifierKind.Multi, 1.5f));
        board.AddModifier(5, resolve, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();

        using var baseline = new SvState();
        board.SaveState(baseline);
        using var receiver = Board.Create(board.Schema, 1024);
        receiver.LoadState(baseline);

        using var keyframe = new SvState();
        var spawned = stackalloc int[3];
        for (var frame = 1L; frame <= 6; frame++)
        {
            board.AddModifier((int)(frame * 7 % 64), power, new Modifier(ModifierKind.Added, frame), 9, frame + 2);
            board.AddModifier((int)(frame * 11 % 64), resolve, new Modifier(ModifierKind.Multi, 1f + frame / 4));
            board.RemoveSource((int)(frame * 5 % 64), 9);
            if (frame == 3)
            {
                board.Release(4);
            }

            if (frame == 4)
            {
                board.Acquire();
            }

            if (frame == 5)
            {
                board.Spawn(1, 3, spawned);
            }

            board.ExpireModifiers(frame);
            board.Recalculate();
            Assert.True(board.KeyframeDirtyPages > 0);
            board.SaveKeyframe(keyframe);
            Assert.Equal(0, board.KeyframeDirtyPages);
            receiver.LoadKeyframe(keyframe);
            Assert.Equal(board.Digest, receiver.Digest);
        }

        Assert.Equal(board.LiveCount, receiver.LiveCount);
        Assert.Equal(board.Digest, receiver.Digest);
        AssertColumnsEqual(board, receiver, power);
        AssertColumnsEqual(board, receiver, resolve);
        for (var slot = 0; slot < 64; slot++)
        {
            Assert.Equal(board.Stat(slot, power), receiver.Stat(slot, power));
            Assert.Equal(board.Stat(slot, resolve), receiver.Stat(slot, resolve));
        }

        for (var frame = 0L; frame < 3; frame++)
        {
            var id = 10 + frame;
            board.AddModifier((int)(3 + frame), power, new Modifier(ModifierKind.Added, frame + 1), 0, long.MaxValue);
            receiver.AddModifier((int)(3 + frame), power, new Modifier(ModifierKind.Added, frame + 1), 0, long.MaxValue);
            board.RemoveSource((int)(3 + frame), (int)(id + 100));
            receiver.RemoveSource((int)(3 + frame), (int)(id + 100));
            board.ExpireModifiers(100 + frame);
            receiver.ExpireModifiers(100 + frame);
            board.Recalculate();
            receiver.Recalculate();
            Assert.Equal(board.Digest, receiver.Digest);
        }

        AssertColumnsEqual(board, receiver, power);
        AssertColumnsEqual(board, receiver, resolve);
    }

    [Fact]
    public void Keyframe_DirtyTracking_CountsPagesNotSlots()
    {
        using var board = Board.Create(Schema(), 2048);
        var power = board.Schema.Stat("power");
        for (var i = 0; i < 2048; i++)
        {
            board.Acquire();
        }

        using var keyframe = new SvState();
        board.SaveKeyframe(keyframe);
        Assert.Equal(0, board.KeyframeDirtyPages);

        board.AddModifier(5, power, new Modifier(ModifierKind.Added, 1f));
        Assert.Equal(1, board.KeyframeDirtyPages);
        board.AddModifier(250, power, new Modifier(ModifierKind.Added, 1f));
        Assert.Equal(1, board.KeyframeDirtyPages);
        board.AddModifier(300, power, new Modifier(ModifierKind.Added, 1f));
        Assert.Equal(2, board.KeyframeDirtyPages);

        using var saved = new SvState();
        board.SaveState(saved);
        Assert.Equal(2, board.KeyframeDirtyPages);
        board.Stat(5, power);
        board.OutputPtr(power);
        Assert.Equal(2, board.KeyframeDirtyPages);
        board.Recalculate();
        Assert.Equal(2, board.KeyframeDirtyPages);

        board.SaveKeyframe(keyframe);
        Assert.Equal(0, board.KeyframeDirtyPages);
        board.AddModifier(700, power, new Modifier(ModifierKind.Added, 1f));
        Assert.Equal(1, board.KeyframeDirtyPages);

        board.LoadState(saved);
        Assert.Equal(0, board.KeyframeDirtyPages);
    }

    [Fact]
    public void Keyframe_IncrementalChain_EvictsCleanPages()
    {
        using var board = Board.Create(Schema(), 4096);
        var power = board.Schema.Stat("power");
        for (var i = 0; i < 4096; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        using var warmup = new SvState();
        board.SaveKeyframe(warmup);
        using var baseline = new SvState();
        board.SaveState(baseline);
        using var receiver = Board.Create(board.Schema, 4096);
        receiver.LoadState(baseline);

        using var first = new SvState();
        board.AddModifier(0, power, new Modifier(ModifierKind.Added, 1f));
        board.AddModifier(300, power, new Modifier(ModifierKind.Added, 1f));
        board.AddModifier(700, power, new Modifier(ModifierKind.Added, 1f));
        board.AddModifier(1100, power, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        Assert.Equal(4, board.KeyframeDirtyPages);
        board.SaveKeyframe(first);
        receiver.LoadKeyframe(first);
        var firstLength = first.Length;

        using var second = new SvState();
        board.AddModifier(1500, power, new Modifier(ModifierKind.Added, 2f));
        board.AddModifier(1700, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(2, board.KeyframeDirtyPages);
        board.SaveKeyframe(second);
        receiver.LoadKeyframe(second);
        Assert.True(second.Length < firstLength);

        using var third = new SvState();
        board.AddModifier(2200, power, new Modifier(ModifierKind.Added, 3f));
        board.AddModifier(2500, power, new Modifier(ModifierKind.Added, 3f));
        board.AddModifier(2800, power, new Modifier(ModifierKind.Added, 3f));
        board.Recalculate();
        Assert.Equal(3, board.KeyframeDirtyPages);
        board.SaveKeyframe(third);
        receiver.LoadKeyframe(third);

        Assert.Equal(board.Digest, receiver.Digest);
        AssertColumnsEqual(board, receiver, power);
        Assert.Equal(0, board.KeyframeDirtyPages);
    }
}
