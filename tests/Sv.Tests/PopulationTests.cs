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
    public void EnabledWindow_PartialTailWord_StaysInsideTheChunk()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[70];
        for (var i = 0; i < 70; i++)
        {
            entities[i] = world.Create(new Health { Value = 400 });
            population.Bind(entities[i]);
        }

        population.Reconcile();
        var clamp = Clamp.Constant(0, 1000);
        var applied = new int[512];
        var words = stackalloc ulong[2];
        words[0] = ulong.MaxValue;
        words[1] = 0b11_1111;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 2, 0, 5, clamp, applied);
        }

        Assert.Equal(1, population.FastPathChunks);
        Assert.Equal(0, population.RemapChunks);
        for (var i = 0; i < 70; i++)
        {
            Assert.Equal(405, entities[i].Get<Health>().Value);
            Assert.Equal(5, applied[i]);
        }

        words[0] = 0;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 2, 0, 1, clamp, applied);
        }

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(405, entities[i].Get<Health>().Value);
            Assert.Equal(0, applied[i]);
        }

        for (var i = 64; i < 70; i++)
        {
            Assert.Equal(406, entities[i].Get<Health>().Value);
            Assert.Equal(1, applied[i]);
        }

        words[1] = 0b111_1111;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            foreach (var window in population.Chunks<Health>())
            {
                window.ApplyEnabled(population.Board, words, 2, 0, 1, clamp, applied);
            }
        });
    }

    [Fact]
    public void EnabledWindow_UnalignedOffset_FastPath()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[135];
        for (var i = 0; i < 135; i++)
        {
            entities[i] = world.Create(new Health { Value = 500 });
            population.Bind(entities[i]);
        }

        population.Reconcile();
        var clamp = Clamp.Constant(0, 1000);
        var applied = new int[512];
        var words = stackalloc ulong[3];
        words[0] = ulong.MaxValue;
        words[1] = 0x0F0F_0F0F_0F0F_0F0Ful;
        words[2] = 0b11;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 3, 5, 3, clamp, applied);
        }

        Assert.Equal(1, population.FastPathChunks);
        Assert.Equal(0, population.RemapChunks);
        for (var i = 0; i < 135; i++)
        {
            int expect;
            int expectApplied;
            if (i < 5)
            {
                expect = 500;
                expectApplied = 0;
            }
            else if (i < 69)
            {
                expect = 503;
                expectApplied = 3;
            }
            else if (i < 133)
            {
                var on = ((i - 69) & 7) < 4;
                expect = on ? 503 : 500;
                expectApplied = on ? 3 : 0;
            }
            else
            {
                expect = 503;
                expectApplied = 3;
            }

            Assert.Equal(expect, entities[i].Get<Health>().Value);
            Assert.Equal(expectApplied, applied[i]);
        }
    }

    [Fact]
    public void RemapChunk_OverflowHeal_SaturatesInsteadOfWrapping()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[96];
        for (var i = 0; i < 96; i++)
        {
            entities[i] = world.Create(new Health { Value = 2_000_000_000 });
            population.Bind(entities[i]);
        }

        for (var i = 2; i < 96; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var clamp = Clamp.Constant(0, 100);
        var applied = new int[512];
        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, 2_000_000_000, clamp, applied);
        }

        Assert.True(population.RemapChunks >= 1, "post-delete frame should remap");
        for (var i = 0; i < 96; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                continue;
            }

            Assert.Equal(100, entities[i].Get<Health>().Value);
        }
    }

    [Fact]
    public void RemapEnabledChunk_OverflowDamage_SaturatesInsteadOfWrapping()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[96];
        for (var i = 0; i < 96; i++)
        {
            entities[i] = world.Create(new Health { Value = -2_000_000_000 });
            population.Bind(entities[i]);
        }

        for (var i = 2; i < 96; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var clamp = Clamp.Constant(0, 100);
        var applied = new int[512];
        var words = stackalloc ulong[2];
        words[0] = ulong.MaxValue;
        words[1] = ulong.MaxValue;
        foreach (var window in population.Chunks<Health>())
        {
            window.ApplyEnabled(population.Board, words, 2, 0, -2_000_000_000, clamp, applied);
        }

        Assert.True(population.RemapChunks >= 1, "post-delete frame should remap");
        for (var i = 0; i < 96; i++)
        {
            if (i % 3 == 2 && i >= 2)
            {
                continue;
            }

            Assert.Equal(0, entities[i].Get<Health>().Value);
        }
    }

    [Fact]
    public void Reconcile_RenumbersPerArchetype_Packed()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var tagged = new Entity[40];
        for (var i = 0; i < 40; i++)
        {
            tagged[i] = world.Create(new Health { Value = i }, new Tag());
            population.Bind(tagged[i]);
        }

        var plain = new Entity[40];
        for (var i = 0; i < 40; i++)
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

        var plainSlots = new int[40];
        for (var i = 0; i < 40; i++)
        {
            Assert.True(population.TrySlot(plain[i], out plainSlots[i]));
        }

        Array.Sort(taggedSlots);
        Array.Sort(plainSlots);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(taggedSlots[0] + i, taggedSlots[i]);
            Assert.Equal(plainSlots[0] + i, plainSlots[i]);
        }

        var bases = new[] { taggedSlots[0], plainSlots[0] };
        Array.Sort(bases);
        Assert.Equal(0, bases[0]);
        Assert.Equal(40, bases[1]);
        for (var i = 0; i < 512; i++)
        {
            if (perm[i] >= 0)
            {
                Assert.True(perm[i] < 80, "packed renumber never leaves a hole below the top of the last block");
            }
        }
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

    [Fact]
    public void SameWindow_ClassificationServesEveryOp()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[128];
        for (var i = 0; i < 128; i++)
        {
            entities[i] = world.Create(new Health { Value = 500 + i });
            population.Bind(entities[i]);
        }

        for (var i = 1; i < 128; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var clamp = Clamp.Constant(0, 2000);
        var words = stackalloc ulong[2];
        words[0] = ulong.MaxValue;
        words[1] = 0;
        var applied = new int[256];
        var fragmented = 0;
        foreach (var window in population.Chunks<Health>())
        {
            if (window.IsContiguous)
            {
                continue;
            }

            fragmented++;
            window.Apply(population.Board, 0, clamp, applied);
            window.ApplyEnabled(population.Board, words, 2, 0, 0, clamp, applied);
            Assert.False(window.IsContiguous);
        }

        Assert.True(fragmented >= 1);
        Assert.Equal(fragmented * 2, population.RemapChunks);
        Assert.Equal(0, population.FastPathChunks);
    }

    [Fact]
    public void FragmentedApplyEnabled_OutOfWindowRows_KeepValueAndReportZero()
    {
        using var world = new World();
        using var population = new Population(world, Schema(), 512);
        var entities = new Entity[128];
        for (var i = 0; i < 128; i++)
        {
            entities[i] = world.Create(new Health { Value = 1000 - i });
            population.Bind(entities[i]);
        }

        for (var i = 1; i < 128; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        var clamp = Clamp.Constant(0, 2000);
        var words = stackalloc ulong[1];
        words[0] = ulong.MaxValue;
        var applied = new int[256];
        foreach (var window in population.Chunks<Health>())
        {
            Assert.False(window.IsContiguous);
            window.ApplyEnabled(population.Board, words, 1, 0, -7, clamp, applied);
        }

        Assert.True(population.RemapChunks >= 1);
        Assert.Equal(0, population.FastPathChunks);
        for (var i = 0; i < 128; i++)
        {
            if (i % 3 == 1 && i >= 1)
            {
                continue;
            }

            population.TrySlot(entities[i], out var slot);
            var expected = slot < 64 ? Math.Clamp(1000 - i - 7, 0, 2000) : 1000 - i;
            Assert.Equal(expected, entities[i].Get<Health>().Value);
        }

        foreach (var chunk in world.Query<Health, SvSlot>().EnumerateChunks<Health, SvSlot>())
        {
            chunk.Deconstruct(out _, out var slots);
            for (var row = 0; row < slots.Length; row++)
            {
                Assert.Equal(slots[row].Slot < 64 ? -7 : 0, applied[row]);
            }
        }
    }
}
