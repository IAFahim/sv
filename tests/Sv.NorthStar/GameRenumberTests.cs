using System;
using NorthStar;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.NorthStar;

public struct NoTimeline : ITimelineStep
{
    public static bool Loop => false;

    public static void Apply(Game game, float[] effect)
    {
    }

    public static void Transform(Game game, float[] effect)
    {
    }

    public static void Advance(Game game)
    {
    }
}

public sealed unsafe class GameRenumberTests
{
    static GameHost<EventLink<ManaGained, Chain>, TimelineLink<NoTimeline, Chain>, Chain, EmptyWorld> BuildHost(InputState input)
    {
        var schema = SchemaBuilder.Create()
            .Stat("power", 10f)
            .Build();
        return Game.Create(schema, 64)
            .Event<ManaGained>()
            .Timeline<NoTimeline>()
            .Build(new EmptyWorld(), input);
    }

    [Fact]
    public void Renumber_PermutesEveryGameColumn()
    {
        using var input = InputState.Create(1, 1, 8);
        using var host = BuildHost(input);
        var game = host.Game;
        for (var slot = 0; slot < 4; slot++)
        {
            game.Board.Acquire();
            game.TimelineIds[slot] = (ushort)(100 + slot);
            game.TimelineClocks[slot] = (ushort)(200 + slot);
            game.Applied[slot] = 300 + slot;
            game.Effect(0)[slot] = 400 + slot;
        }

        var oldToNew = new int[game.Capacity];
        for (var i = 0; i < game.Capacity; i++)
        {
            oldToNew[i] = -1;
        }

        oldToNew[0] = 1;
        oldToNew[1] = 0;
        oldToNew[2] = 2;
        oldToNew[3] = 3;
        game.Renumber(oldToNew);

        Assert.Equal(101u, game.TimelineIds[0]);
        Assert.Equal(100u, game.TimelineIds[1]);
        Assert.Equal(102u, game.TimelineIds[2]);
        Assert.Equal(201u, game.TimelineClocks[0]);
        Assert.Equal(200u, game.TimelineClocks[1]);
        Assert.Equal(301, game.Applied[0]);
        Assert.Equal(300, game.Applied[1]);
        Assert.Equal(401f, game.Effect(0)[0]);
        Assert.Equal(400f, game.Effect(0)[1]);
        Assert.Equal(0u, game.TimelineIds[4]);
        Assert.Equal(0f, game.Effect(0)[4]);
    }

    [Fact]
    public void Renumber_WrongMapLength_Throws()
    {
        using var input = InputState.Create(1, 1, 8);
        using var host = BuildHost(input);
        var game = host.Game;
        var shortMap = new int[game.Capacity - 1];
        Assert.Throws<ArgumentException>(() => game.Renumber(shortMap));
    }
}
