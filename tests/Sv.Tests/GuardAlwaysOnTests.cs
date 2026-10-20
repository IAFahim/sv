using System;
using Frent;
using Sv;
using Sv.Frent;
using Xunit;

[Collection("Frent")]
public unsafe class GuardAlwaysOnTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Intrinsic("mana")
        .Build();

    [Fact]
    public void ChunkRange_ThrowsWithoutSvChecked()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        Assert.Throws<ArgumentOutOfRangeException>(() => board.RunIntrinsicsChunk(value, 1, Clamp.Constant(0, 10), 60, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.RunIntrinsicsChunk(value, 1, Clamp.Constant(0, 10), -1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.RunIntrinsicsChunk(value, 1, Clamp.Constant(0, 10), 0, -4));
    }

    [Fact]
    public void EnabledRange_ThrowsWithoutSvChecked()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        Assert.Throws<ArgumentOutOfRangeException>(() => board.RunEnabledChunk(value, 1, Clamp.Constant(0, 10), ulong.MaxValue, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.RunEnabledChunk(value, 1, Clamp.Constant(0, 10), ulong.MaxValue, -64));
    }

    [Fact]
    public void ChunkSeam_FullOneWord_BeyondValidRows_ThrowsWithoutSvChecked()
    {
        using var world = new World();
        using var population = new Population(world, TestSchema(), 128);
        var slots = new SvSlot[32];
        var rows = new int[32];
        var applied = new int[64];
        for (var i = 0; i < 32; i++)
        {
            slots[i] = new SvSlot(i);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSeam.RunEnabledChunk(population.Board, rows, slots, ulong.MaxValue, 0, 5, Clamp.Constant(0, 100), applied));
    }

    [Fact]
    public void ChunkSeam_ZeroPaddedPartialTail_StillRuns()
    {
        using var world = new World();
        using var population = new Population(world, TestSchema(), 128);
        var slots = new SvSlot[32];
        var rows = new int[32];
        var applied = new int[64];
        ulong word = (1ul << 32) - 1;
        for (var i = 0; i < 32; i++)
        {
            slots[i] = new SvSlot(i);
            rows[i] = 10;
        }

        ChunkSeam.RunEnabledChunk(population.Board, rows, slots, word, 0, 5, Clamp.Constant(0, 100), applied);

        Assert.Equal(15, rows[0]);
        Assert.Equal(5, applied[0]);
        Assert.Equal(0, applied[40]);
    }

    [Fact]
    public void ApplyEnabled_PartialTailWindow_FastPathStaysCorrect()
    {
        using var world = new World();
        using var population = new Population(world, TestSchema(), 192);
        var entities = new Entity[100];
        for (var i = 0; i < 100; i++)
        {
            entities[i] = world.Create(new Health { Value = 100 });
            population.Bind(entities[i]);
        }

        var applied = new int[128];
        var words = stackalloc ulong[1];
        words[0] = (1ul << 36) - 1;
        var windows = 0;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 1, 64, -3, Clamp.Constant(0, 200), applied);
            windows++;
        }

        Assert.Equal(1, windows);
        Assert.Equal(1, population.FastPathChunks);
        for (var i = 0; i < 100; i++)
        {
            var expected = i >= 64 ? 97 : 100;
            Assert.True(entities[i].Get<Health>().Value == expected, $"entity {i} health {entities[i].Get<Health>().Value} expected {expected}");
        }
    }
}
