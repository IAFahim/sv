using System;
using Frent;
using Sv;
using Sv.Frent;
using Xunit;

public struct Health
{
    public int Value;
}

public struct Tag
{
}

[Collection("Frent")]
public sealed unsafe class PopulationTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

    [Fact]
    public void DeletedEveryThird_FusedWindowRemaps_ThenHealsAfterReconcile()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[256];
        var expected = new int[256];
        for (var i = 0; i < 256; i++)
        {
            entities[i] = world.Create(new Health { Value = 1000 - i });
            population.Bind(entities[i]);
            expected[i] = 1000 - i;
        }

        for (var i = 2; i < 256; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var clamp = Clamp.Constant(0, 2000);
        var applied = new int[512];
        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, -7, clamp, applied);
            Assert.True(window.RowCount > 0);
        }

        Assert.True(population.RemapChunks >= 1, "post-delete frame should remap");
        Assert.Equal(0, population.FastPathChunks);
        for (var i = 0; i < 256; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                continue;
            }

            Assert.Equal(Math.Clamp(expected[i] - 7, 0, 2000), entities[i].Get<Health>().Value);
            expected[i] = Math.Clamp(expected[i] - 7, 0, 2000);
        }

        population.Reconcile();
        var live = 0;
        var slotSeen = new bool[171];
        for (var i = 0; i < 256; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                continue;
            }

            Assert.True(population.TrySlot(entities[i], out var slot));
            Assert.True(slot >= 0 && slot < 171);
            Assert.False(slotSeen[slot], $"slot {slot} claimed twice");
            slotSeen[slot] = true;
            live++;
        }

        Assert.Equal(live, population.Board.LiveCount);
        for (var slot = 0; slot < 171; slot++)
        {
            Assert.True(slotSeen[slot], $"slot {slot} unclaimed");
        }

        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, -7, clamp, applied);
        }

        Assert.Equal(1, population.FastPathChunks);
        Assert.Equal(0, population.RemapChunks);
        for (var i = 0; i < 256; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                continue;
            }

            Assert.Equal(Math.Clamp(expected[i] - 7, 0, 2000), entities[i].Get<Health>().Value);
            expected[i] = Math.Clamp(expected[i] - 7, 0, 2000);
        }
    }

    [Fact]
    public void EnabledWindow_ParticipatesInHeal()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[128];
        for (var i = 0; i < 128; i++)
        {
            entities[i] = world.Create(new Health { Value = 500 });
            population.Bind(entities[i]);
        }

        for (var i = 1; i < 128; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        population.Reconcile();
        var clamp = Clamp.Constant(0, 600);
        var applied = new int[512];
        var words = stackalloc ulong[2];
        words[0] = 0x5555_5555_5555_5555ul;
        words[1] = 0x0015_5555;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 2, 0, 5, clamp, applied);
        }

        Assert.True(population.FastPathChunks >= 1);
        Assert.Equal(0, population.RemapChunks);
        for (var i = 0; i < 128; i++)
        {
            if (i % 3 == 1 && i >= 1)
            {
                continue;
            }

            population.TrySlot(entities[i], out var slot);
            var expect = slot % 2 == 0 ? 505 : 500;
            Assert.Equal(expect, entities[i].Get<Health>().Value);
        }
    }

    [Fact]
    public void Reconcile_RenumbersPerArchetype_WithAlignment()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var tagged = new Entity[40];
        for (var i = 0; i < 40; i++)
        {
            tagged[i] = world.Create(new Health { Value = i }, new Tag());
            population.Bind(tagged[i]);
        }

        var plain = new Entity[20];
        for (var i = 0; i < 20; i++)
        {
            plain[i] = world.Create(new Health { Value = 1000 + i });
            population.Bind(plain[i]);
        }

        var perm = population.Reconcile();
        foreach (var window in population.Chunks<Health>())
        {
            Assert.True(window.IsContiguous);
        }

        var taggedSlots = new int[40];
        for (var i = 0; i < 40; i++)
        {
            Assert.True(population.TrySlot(tagged[i], out taggedSlots[i]));
        }

        var plainSlots = new int[20];
        for (var i = 0; i < 20; i++)
        {
            Assert.True(population.TrySlot(plain[i], out plainSlots[i]));
        }

        Array.Sort(taggedSlots);
        Array.Sort(plainSlots);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(taggedSlots[0] + i, taggedSlots[i]);
        }

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(plainSlots[0] + i, plainSlots[i]);
        }

        var bases = new[] { taggedSlots[0], plainSlots[0] };
        Array.Sort(bases);
        Assert.Equal(0, bases[0]);
        Assert.Equal(64, bases[1]);
    }

    [Fact]
    public void PopulationWindows_AllocateZeroManagedBytesOnWarmPath()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 256);
        var entities = new Entity[192];
        for (var i = 0; i < 192; i++)
        {
            entities[i] = world.Create(new Health { Value = 250 - i });
            population.Bind(entities[i]);
        }

        var clamp = Clamp.Constant(0, 300);
        var applied = new int[256];
        var words = stackalloc ulong[3];
        words[0] = ulong.MaxValue;
        words[1] = 0x5555_5555_5555_5555ul;
        words[2] = 0ul;
        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, -1, clamp, applied);
            window.ApplyEnabled(population.Board, words, 3, 0, 1, clamp, applied);
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var it = 0; it < 2000; it++)
        {
            foreach (var window in population.Chunks<Health>())
            {
                window.Apply(population.Board, -1, clamp, applied);
                window.ApplyEnabled(population.Board, words, 3, 0, 1, clamp, applied);
            }

            sink += applied[it % 192];
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"population windows allocated {deltaBytes} managed bytes (sink {sink})");
    }
}
