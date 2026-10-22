using System;
using System.IO;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.Tests;

// Atom 1.7 (roadmap issues/0058, issue 0065): the state shape covers schema CONTENT
// (names, bases, derived sources — hashed), not just XORed counts (F18), and the
// composed game validates its header before any component loads — a mismatched
// LoadState throws with board and game untouched (F17).

public unsafe class StateIntegrityTests
{
    [Fact]
    public void BoardState_DistinguishesCountSwaps()
    {
        var oneStat = SchemaBuilder.Create().Stat("power", 1f).Build();
        var builder = SchemaBuilder.Create();
        for (var i = 0; i < 256; i++)
        {
            builder.Intrinsic($"slot{i}");
        }

        using var a = Board.Create(oneStat, 64);
        using var b = Board.Create(builder.Build(), 64);
        a.Acquire();
        using var state = new SvState();
        a.SaveState(state);
        Assert.Throws<InvalidDataException>(() => b.LoadState(state));
    }

    [Fact]
    public void BoardState_DistinguishesNamesBasesAndSources()
    {
        var original = SchemaBuilder.Create().Stat("power", 10f).Derived("atk", 1f, ("power", 2f)).Build();
        var renamed = SchemaBuilder.Create().Stat("vigor", 10f).Derived("atk", 1f, ("vigor", 2f)).Build();
        var rebased = SchemaBuilder.Create().Stat("power", 11f).Derived("atk", 1f, ("power", 2f)).Build();
        var resourced = SchemaBuilder.Create().Stat("power", 10f).Derived("atk", 1f, ("power", 3f)).Build();
        using var a = Board.Create(original, 64);
        a.Acquire();
        using var state = new SvState();
        a.SaveState(state);
        foreach (var schema in new[] { renamed, rebased, resourced })
        {
            using var other = Board.Create(schema, 64);
            Assert.Throws<InvalidDataException>(() => other.LoadState(state));
        }
    }

    struct Idle : ITimelineStep
    {
        public static bool Loop => true;

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

    struct IdleWorld : IFrameWorld
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
    public void GameLoad_MismatchedTimelineCount_LeavesGameUnchanged()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
        using var withTimeline = Game.Create(schema, 64).Timeline<Idle>().Build(new IdleWorld(), InputState.Create(1, 1, 8));
        using var bare = Game.Create(schema, 64).Build(new IdleWorld(), InputState.Create(1, 1, 8));
        var stat = schema.Stat("power");
        var slot = bare.Game.Board.Acquire();
        bare.Game.Board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 5f));
        bare.Game.Board.Recalculate();
        var before = bare.Game.Board.Output(stat)[slot];

        using var state = new SvState();
        withTimeline.Game.Board.Acquire();
        withTimeline.Game.SaveState(state);
        Assert.Throws<InvalidDataException>(() => bare.Game.LoadState(state));

        Assert.Equal(slot + 1, bare.Game.Board.LiveCount);
        Assert.Equal(before, bare.Game.Board.Output(stat)[slot]);
    }

    [Fact]
    public void GameLoad_MismatchedSchema_LeavesGameUnchanged()
    {
        var schemaA = SchemaBuilder.Create().Stat("power", 10f).Build();
        var schemaB = SchemaBuilder.Create().Stat("power", 12f).Build();
        using var hostA = Game.Create(schemaA, 64).Build(new IdleWorld(), InputState.Create(1, 1, 8));
        using var hostB = Game.Create(schemaB, 64).Build(new IdleWorld(), InputState.Create(1, 1, 8));
        var stat = schemaB.Stat("power");
        var slot = hostB.Game.Board.Acquire();
        hostB.Game.Board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 5f));
        hostB.Game.Board.Recalculate();
        var before = hostB.Game.Board.Output(stat)[slot];

        using var state = new SvState();
        hostA.Game.Board.Acquire();
        hostA.Game.SaveState(state);
        Assert.Throws<InvalidDataException>(() => hostB.Game.LoadState(state));

        Assert.Equal(slot + 1, hostB.Game.Board.LiveCount);
        Assert.Equal(before, hostB.Game.Board.Output(stat)[slot]);
    }
}
