using System;
using Sv;
using Xunit;

public unsafe class LazyColumnTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Derived("doubled", 0f, ("power", 2f))
        .Build();

    [Fact]
    public void UnreadStats_CostNoColumnBytes()
    {
        using var board = Board.Create(Schema(), 1000);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.Recalculate();
        var before = board.MemoryBytes;

        board.Output(power)[slot] += 0f;
        Assert.Equal(before + (long)board.Capacity * sizeof(float), board.MemoryBytes);
    }

    [Fact]
    public void ChangedWords_TrackNeverMaterializedStats()
    {
        using var board = Board.Create(Schema(), 64);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.Recalculate();

        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        var words = board.ChangedWords(power);
        Assert.NotEqual(0ul, words[slot >> 6] & (1ul << (slot & 63)));
        Assert.Equal(15f, board.Output(power)[slot]);
    }

    [Fact]
    public void SaveLoad_MixedMaterialization_RoundTripsByteStably()
    {
        using var board = Board.Create(Schema(), 64);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(12f, board.Output(power)[slot]);
        using var saved = new SvState();
        board.SaveState(saved);
        var bytes = saved.Bytes.ToArray();

        using var fresh = Board.Create(Schema(), 64);
        fresh.LoadState(saved);
        using var resaved = new SvState();
        fresh.SaveState(resaved);
        Assert.True(bytes.AsSpan().SequenceEqual(resaved.Bytes), "save flags must round-trip through load");
        Assert.Equal(12f, fresh.Output(power)[slot]);
        Assert.Equal(24f, fresh.Output(fresh.Schema.Stat("doubled"))[slot]);
    }

    [Fact]
    public void Renumber_MaterializedPermuted_LazyStillCorrect()
    {
        using var board = Board.Create(Schema(), 128);
        var power = board.Schema.Stat("power");
        var doubled = board.Schema.Stat("doubled");
        var slot = board.Acquire();
        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        Assert.Equal(15f, board.Output(power)[slot]);

        var map = new int[128];
        Array.Fill(map, -1);
        map[slot] = 7;
        board.RenumberSlots(map, 1);
        board.Recalculate();
        Assert.Equal(15f, board.Output(power)[7]);
        Assert.Equal(30f, board.Output(doubled)[7]);
    }
}
