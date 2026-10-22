using System;
using Frent;
using Sv;
using Sv.Frent;
using Xunit;

public struct Mana
{
    public int Value;
}

[Collection("Frent")]
public sealed class FrentAdapterTests
{
    static Schema ManaSchema()
    {
        return SchemaBuilder.Create()
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();
    }

    [Fact]
    public void BindUnbind_SlotLifecycle_StaysDense()
    {
        using var world = new World();
        using var adapter = new FrentBoard(ManaSchema(), 512);
        var entities = new Entity[300];
        for (var i = 0; i < 300; i++)
        {
            entities[i] = world.Create(new Mana { Value = i });
            var slot = adapter.Bind(entities[i]);
            Assert.Equal(i, slot);
        }

        Assert.Equal(300, adapter.Board.LiveCount);

        for (var i = 1; i < 300; i += 2)
        {
            adapter.Unbind(entities[i]);
        }

        Assert.Equal(150, adapter.Board.LiveCount);
        Assert.False(adapter.TrySlot(entities[1], out _));
        Assert.True(adapter.TrySlot(entities[0], out var slot0));
        Assert.Equal(0, slot0);

        var rebound = adapter.Bind(world.Create(new Mana { Value = -1 }));
        Assert.Equal(1, rebound);
    }

    [Fact]
    public void RunChunk_ContiguousFastPath_WritesThroughToFrentStorage()
    {
        using var world = new World();
        using var adapter = new FrentBoard(ManaSchema(), 256);
        var entities = new Entity[128];
        for (var i = 0; i < 128; i++)
        {
            entities[i] = world.Create(new Mana { Value = 90 + i });
            adapter.Bind(entities[i]);
        }

        var clamp = Clamp.Constant(0, 150);
        foreach (var chunk in world.Query<Mana, SvSlot>().EnumerateChunks<Mana, SvSlot>())
        {
            chunk.Deconstruct(out var rows, out var slots);
            var applied = new int[rows.Length];
            ChunkSeam.RunChunk(adapter.Board, rows, slots, 10, clamp, applied);
            for (var i = 0; i < rows.Length; i++)
            {
                var expected = Math.Clamp(90 + i + 10, 0, 150);
                Assert.Equal(expected, rows[i].Value);
                Assert.Equal(expected - (90 + i), applied[i]);
            }
        }

        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(Math.Clamp(90 + i + 10, 0, 150), entities[i].Get<Mana>().Value);
        }
    }

    [Fact]
    public void RunEnabledChunk_FastPath_ZeroesAppliedForDisabledRows()
    {
        using var world = new World();
        using var adapter = new FrentBoard(ManaSchema(), 256);
        for (var i = 0; i < 64; i++)
        {
            adapter.Bind(world.Create(new Mana { Value = 40 }));
        }

        var clamp = Clamp.Constant(0, int.MaxValue);
        foreach (var chunk in world.Query<Mana, SvSlot>().EnumerateChunks<Mana, SvSlot>())
        {
            chunk.Deconstruct(out var rows, out var slots);
            var applied = new int[64];
            ChunkSeam.RunEnabledChunk(adapter.Board, rows, slots, 0x5555555555555555ul, 0, 10, clamp, applied);
            for (var i = 0; i < 64; i++)
            {
                if (i % 2 == 0)
                {
                    Assert.Equal(50, rows[i].Value);
                    Assert.Equal(10, applied[i]);
                }
                else
                {
                    Assert.Equal(40, rows[i].Value);
                    Assert.Equal(0, applied[i]);
                }
            }
        }
    }

    [Fact]
    public void FragmentedChunk_InPlaceWalk_MatchesOracle()
    {
        using var world = new World();
        using var population = new Population(world, ManaSchema(), 512);
        var entities = new Entity[256];
        for (var i = 0; i < 256; i++)
        {
            entities[i] = world.Create(new Mana { Value = 500 - i });
            population.Bind(entities[i]);
        }

        for (var i = 2; i < 256; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var sawFragmented = false;
        var clamp = Clamp.Constant(0, 400);
        foreach (var window in population.Chunks<Mana>())
        {
            if (!window.IsContiguous)
            {
                sawFragmented = true;
            }

            window.Apply(population.Board, -3, clamp, default);
        }

        Assert.True(sawFragmented);
        Assert.True(population.RemapChunks >= 1);
        for (var i = 0; i < 256; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                Assert.False(entities[i].IsAlive);
            }
            else
            {
                Assert.Equal(Math.Clamp(500 - i - 3, 0, 400), entities[i].Get<Mana>().Value);
            }
        }
    }

    [Fact]
    public void RunChunk_RejectsNonContiguousAndWrongSize()
    {
        using var world = new World();
        using var adapter = new FrentBoard(ManaSchema(), 64);
        var a = world.Create(new Mana { Value = 1 });
        var b = world.Create(new Mana { Value = 2 });
        var c = world.Create(new Mana { Value = 3 });
        adapter.Bind(a);
        adapter.Bind(b);
        adapter.Bind(c);
        adapter.Unbind(b);
        b.Delete();

        foreach (var chunk in world.Query<Mana, SvSlot>().EnumerateChunks<Mana, SvSlot>())
        {
            chunk.Deconstruct(out var rows, out var slots);
            Assert.False(ChunkSeam.TrySlotRange(slots, out _, out _));
            var threw = false;
            try
            {
                ChunkSeam.RunChunk(adapter.Board, rows, slots, 1, Clamp.Constant(0, 10), default);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert.True(threw);
        }
    }
}
