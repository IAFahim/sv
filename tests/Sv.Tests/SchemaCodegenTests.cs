using System;
using Sv;
using Xunit;

[SvSchema]
[SvStat("power", 10f)]
[SvStat("vigor", 3.5f)]
[SvDerived("damage", 2f, new[] { "power", "vigor" }, new[] { 1f, 0.5f })]
[SvIntrinsic("hp")]
internal static partial class GameSchema
{
}

public class SchemaCodegenTests
{
    static Schema HandBuilt() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("vigor", 3.5f)
        .Derived("damage", 2f, ("power", 1f), ("vigor", 0.5f))
        .Intrinsic("hp")
        .Build();

    [Fact]
    public void GeneratedSchema_MatchesHandBuiltBitForBit()
    {
        var hand = HandBuilt();
        var generated = GameSchema.Schema;
        Assert.Equal(hand.ContentHash, generated.ContentHash);
        Assert.Equal(hand.Stats.Count, generated.Stats.Count);
        for (var i = 0; i < hand.Stats.Count; i++)
        {
            Assert.Equal(hand.Stats[i].Name, generated.Stats[i].Name);
            Assert.Equal(hand.Stats[i].Base, generated.Stats[i].Base);
            Assert.Equal(hand.Stats[i].Sources.Count, generated.Stats[i].Sources.Count);
            for (var j = 0; j < hand.Stats[i].Sources.Count; j++)
            {
                Assert.Equal(hand.Stats[i].Sources[j], generated.Stats[i].Sources[j]);
            }
        }

        Assert.Equal(hand.Intrinsics.Count, generated.Intrinsics.Count);
        Assert.Equal(hand.Intrinsics[0].Name, generated.Intrinsics[0].Name);
    }

    [Fact]
    public void GeneratedMembers_ResolveThroughSchema()
    {
        Assert.Equal(GameSchema.Schema.Stat("power"), GameSchema.power);
        Assert.Equal(GameSchema.Schema.Stat("vigor"), GameSchema.vigor);
        Assert.Equal(GameSchema.Schema.Stat("damage"), GameSchema.damage);
        Assert.Equal(2, GameSchema.damage.Index);
        Assert.Equal(GameSchema.Schema.Intrinsic("hp"), GameSchema.hp);
    }

    [Fact]
    public void TypedAccessors_ReadLiveBoard()
    {
        using var board = Board.Create(GameSchema.Schema, 64);
        var slot = board.Acquire();
        board.Recalculate();
        Assert.Equal(10f, GameSchema.Getpower(board, slot));
        Assert.Equal(3.5f, GameSchema.Getvigor(board, slot));
        Assert.Equal(13.75f, GameSchema.Getdamage(board, slot));
        board.AddModifier(slot, GameSchema.power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(12f, GameSchema.Getpower(board, slot));
        Assert.Equal(15.75f, GameSchema.Getdamage(board, slot));
    }
}
