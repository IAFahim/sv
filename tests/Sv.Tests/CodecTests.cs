using System;
using Sv;
using Sv.Compose;
using Xunit;

public unsafe class CodecTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("resolve", 5f)
        .Build();

    static void Populate(Board board)
    {
        var power = board.Schema.Stat("power");
        var resolve = board.Schema.Stat("resolve");
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
        }

        for (var round = 0L; round < 6; round++)
        {
            for (var i = 0; i < 8; i++)
            {
                board.AddModifier(slots[i], power, new Modifier(ModifierKind.Added, i % 3), 0, round + 1);
            }

            board.Recalculate();
            board.ExpireModifiers(round + 1);
            board.Recalculate();
            board.AddModifier(slots[(int)round % 8], resolve, new Modifier(ModifierKind.Multi, 1f + round / 4));
        }

        board.Recalculate();
    }

    [Fact]
    public void Board_SaveLoadSave_IsByteIdentical()
    {
        using var board = Board.Create(Schema(), 16);
        Populate(board);
        using var first = new SvState();
        board.SaveState(first);
        board.LoadState(first);
        using var second = new SvState();
        board.SaveState(second);
        Assert.True(first.Bytes.SequenceEqual(second.Bytes), "save(load(save)) must equal save");
    }

    [Fact]
    public void Board_SizeCodec_MatchesSavedLength()
    {
        using var board = Board.Create(Schema(), 16);
        Populate(board);
        var size = new SizeCodec();
        board.VisitState(ref size);
        using var saved = new SvState();
        board.SaveState(saved);
        Assert.Equal(size.Total, (long)saved.Length);
    }

    [Fact]
    public void Board_HashCodec_TracksStateNotIdentity()
    {
        using var a = Board.Create(Schema(), 16);
        using var b = Board.Create(Schema(), 16);
        Populate(a);
        Populate(b);
        var hashA = new HashCodec(14695981039346656037ul);
        a.VisitState(ref hashA);
        var hashB = new HashCodec(14695981039346656037ul);
        b.VisitState(ref hashB);
        Assert.Equal(hashA.Digest, hashB.Digest);

        a.AddModifier(0, a.Schema.Stat("power"), new Modifier(ModifierKind.Added, 99f));
        a.Recalculate();
        var hashC = new HashCodec(14695981039346656037ul);
        a.VisitState(ref hashC);
        Assert.NotEqual(hashA.Digest, hashC.Digest);
    }

    struct CodecWorld : IFrameWorld
    {
        public void Reconcile(Game game)
        {
        }

        public void DispatchEvent(Game game, ushort typeId, void* payload)
        {
        }

        public void RunIntrinsics(Game game)
        {
        }

        public void RunConditions(Game game)
        {
        }
    }

    [Fact]
    public void Game_SizeCodec_MatchesSavedLength()
    {
        var schema = SchemaBuilder.Create().Stat("power", 1f).Intrinsic("hp").Build();
        using var input = InputState.Create(1, 1, 8);
        using var host = Game.Create(schema, 64).Build(new CodecWorld(), input);
        var slot = host.Game.Board.Acquire();
        host.Game.Board.AddModifier(slot, schema.Stat("power"), new Modifier(ModifierKind.Added, 2f));
        host.Game.Board.Recalculate();

        var size = new SizeCodec();
        host.Game.VisitState(ref size);
        using var saved = new SvState();
        host.Game.SaveState(saved);
        Assert.Equal(size.Total, (long)saved.Length);

        host.Game.LoadState(saved);
        using var resaved = new SvState();
        host.Game.SaveState(resaved);
        Assert.True(saved.Bytes.SequenceEqual(resaved.Bytes), "game save(load(save)) must equal save");
    }
}
