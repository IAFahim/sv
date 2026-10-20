using System;
using System.Runtime.InteropServices;
using NorthStar;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.NorthStar;

public unsafe struct EmptyWorld : IFrameWorld
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

public sealed unsafe class DigestCoverageTests
{
    static GameHost<EventLink<ManaGained, Chain>, Chain, Chain, EmptyWorld> BuildHost(InputState input)
    {
        var schema = SchemaBuilder.Create()
            .Stat("power", 10f)
            .Build();
        return Game.Create(schema, 64)
            .Event<ManaGained>()
            .Build(new EmptyWorld(), input);
    }

    [Fact]
    public void WriteBody_DigestsBoundColumns_NotStaleOwnedColumns()
    {
        using var input = InputState.Create(1, 1, 8);
        using var host = BuildHost(input);
        var game = host.Game;
        var board = game.Board;
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();
        var bound = (float*)NativeMemory.AllocZeroed((nuint)64 * sizeof(float));
        try
        {
            board.BindOutput(stat, bound, 1, 64);
            board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 90f));
            board.Recalculate();
            Assert.Equal(100f, bound[slot]);
            Assert.Equal(0f, board.Output(stat)[slot]);

            var body = stackalloc byte[Tape.BodyLength(game)];
            var digest = Tape.WriteBody(game, 0.016666668f, body);
            var at = body + sizeof(uint) + game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong);
            Assert.Equal(100f, *(float*)at);

            bound[slot] = 42f;
            var digestAfter = Tape.WriteBody(game, 0.016666668f, body);
            Assert.NotEqual(digest, digestAfter);
            at = body + sizeof(uint) + game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong);
            Assert.Equal(42f, *(float*)at);
        }
        finally
        {
            NativeMemory.Free(bound);
        }
    }

    [Fact]
    public void WriteBody_BoundColumnWithStride_DigestsStridedValues()
    {
        using var input = InputState.Create(1, 1, 8);
        using var host = BuildHost(input);
        var game = host.Game;
        var board = game.Board;
        var stat = board.Schema.Stat("power");
        var slotA = board.Acquire();
        var slotB = board.Acquire();
        var bound = (float*)NativeMemory.AllocZeroed((nuint)128 * sizeof(float));
        try
        {
            board.BindOutput(stat, bound, 2, 128);
            board.AddModifier(slotA, stat, new Modifier(ModifierKind.Added, 1f));
            board.AddModifier(slotB, stat, new Modifier(ModifierKind.Added, 2f));
            board.Recalculate();
            Assert.Equal(11f, bound[slotA * 2]);
            Assert.Equal(12f, bound[slotB * 2]);

            var body = stackalloc byte[Tape.BodyLength(game)];
            Tape.WriteBody(game, 0.016666668f, body);
            var at = body + sizeof(uint) + game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong);
            Assert.Equal(11f, *(float*)at);
            Assert.Equal(12f, *((float*)at + 1));
        }
        finally
        {
            NativeMemory.Free(bound);
        }
    }
}
