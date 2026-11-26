using System.Runtime.CompilerServices;

namespace Sv.Columns.Tests;

public sealed class EdgeTests
{
    static ColumnPack Small()
    {
        var pack = new ColumnPack();
        pack.Lanes.Add(new LaneSpec("Health", 100));
        pack.Lanes.Add(new LaneSpec("Taken"));
        pack.Channels.Add("Damage");
        pack.Templates.Add(new TemplateSpec("unit"));
        pack.Rules.Add(new FlowRule("Health", System.Array.Empty<string>(), new[] { "Damage" }));
        pack.Rules.Add(new FlowRule("Taken", new[] { "Damage" }, System.Array.Empty<string>()));
        return pack;
    }

    [Fact]
    public void ATargetPastTheSlotBitsIsDroppedAndHitsNobody()
    {
        using var pack = Small().Compile();
        using var world = new ColumnWorld(pack, 128);
        var five = 0;
        for (var i = 0; i < 6; i++)
        {
            five = world.Spawn(0);
        }

        world.Writer(0).Send(Record.SlotLimit + five, 0, 7);
        world.Writer(0).Send(-1, 0, 7);
        world.Writer(0).Send(int.MinValue, 0, 7);
        world.Step();
        Assert.Equal(100, world.Get(five, pack.Lane("Health")));
        Assert.Equal(3, world.Dropped);
    }

    [Fact]
    public void ATargetPastTheCapacityIsDroppedEvenWhenItFitsTheSlotBits()
    {
        using var pack = Small().Compile();
        using var world = new ColumnWorld(pack, 64);
        var slot = world.Spawn(0);
        world.Writer(0).Send(64, 0, 7);
        world.Writer(0).SendFrom(slot + 64, slot, 0, 7);
        world.Step();
        Assert.Equal(93, world.Get(slot, pack.Lane("Health")));
        Assert.Equal(1, world.Dropped);
    }

    [Fact]
    public void AChannelOutsideTheRecordBitsIsDroppedAndDoesNotAliasAnother()
    {
        using var pack = Small().Compile();
        using var world = new ColumnWorld(pack, 64);
        var slot = world.Spawn(0);
        world.Writer(0).Send(slot, 16, 7);
        world.Writer(0).Send(slot, -16, 7);
        world.Writer(0).SendFrom(slot, slot, 32, 7);
        world.Step();
        Assert.Equal(100, world.Get(slot, pack.Lane("Health")));
        Assert.Equal(3, world.Dropped);
    }

    [Theory]
    [InlineData(1L << 62, 4)]
    [InlineData(long.MaxValue, 2)]
    [InlineData(long.MaxValue, int.MaxValue)]
    [InlineData(1L << 40, 1 << 30)]
    public void AmountTimesHitsSaturatesInsteadOfWrapping(long amount, int hits)
    {
        using var compiled = Small().Compile();
        using var twin = new Lockstep(compiled, 64);
        var slot = twin.Spawn("unit");
        twin.Send(slot, "Damage", amount, hits: hits);
        twin.SendFrom(slot, slot, "Damage", -amount, hits: hits);
        twin.Send(slot, "Damage", amount, hits: hits);
        twin.Step();
        Assert.Equal((int)Record.AmountMax, twin.Get(slot, "Taken"));
    }

    [Fact]
    public void AConvertOfAHugeSumMovesTheExactFraction()
    {
        var spec = Small();
        spec.Channels.Add("Heal");
        spec.Lanes.Add(new LaneSpec("Healed"));
        spec.Rules.Insert(0, new ScaleRule(new[] { "Damage" }, 16, null, null));
        spec.Rules.Insert(1, new ConvertRule("Damage", "Heal", 65535, 65536, null));
        spec.Rules.Add(new FlowRule("Healed", new[] { "Heal" }, System.Array.Empty<string>()));
        using var compiled = spec.Compile();
        using var twin = new Lockstep(compiled, 64);
        var slot = twin.Spawn("unit");
        for (var i = 0; i < 1 << 16; i++)
        {
            twin.Send(slot, "Damage", Record.AmountMax);
        }

        twin.Step();
        Assert.Equal((int)(16 * Record.AmountMax), twin.Get(slot, "Taken"));
        Assert.Equal(int.MaxValue, twin.Get(slot, "Healed"));
        Assert.Equal((int)(100 - 16 * Record.AmountMax), twin.Get(slot, "Health"));
    }

    [Fact]
    public void AConvertOfAHugeSumAgreesOnEveryKernel()
    {
        var spec = Small();
        spec.Channels.Add("Heal");
        spec.Lanes.Add(new LaneSpec("Healed"));
        spec.Rules.Insert(0, new ScaleRule(new[] { "Damage" }, 16, null, null));
        spec.Rules.Insert(1, new ConvertRule("Damage", "Heal", 40503, 65536, null));
        spec.Rules.Add(new FlowRule("Healed", new[] { "Heal" }, System.Array.Empty<string>()));
        using var compiled = spec.Compile();
        var digests = new HashSet<ulong>();
        var taken = new HashSet<int>();
        foreach (var kernels in new[] { KernelChoice.Scalar, KernelChoice.Vector, KernelChoice.Best })
        {
            using var world = new ColumnWorld(compiled, 128, 2, 2, kernels);
            var slots = new int[8];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = world.Spawn(0);
            }

            for (var i = 0; i < 1 << 20; i++)
            {
                world.Writer(i & 1).Send(slots[i % slots.Length], 0, Record.AmountMax - i);
            }

            world.Step();
            digests.Add(world.Digest());
            taken.Add(world.Get(slots[3], compiled.Lane("Taken")));
        }

        Assert.Single(digests);
        Assert.Single(taken);
    }

    [Fact]
    public void AWorldRefusesEveryCallAfterDispose()
    {
        using var pack = Small().Compile();
        var world = new ColumnWorld(pack, 64);
        var slot = world.Spawn(0);
        var writer = world.Writer(0);
        world.Dispose();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Step());
        Assert.Throws<ObjectDisposedException>(() => world.Spawn(0));
        Assert.Throws<ObjectDisposedException>(() => world.Get(slot, 0));
        Assert.Throws<ObjectDisposedException>(() => world.Set(slot, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => world.Despawn(slot));
        Assert.Throws<ObjectDisposedException>(() => world.IsAlive(slot));
        Assert.Throws<ObjectDisposedException>(() => world.Digest());
        Assert.Throws<ObjectDisposedException>(() => world.ChunkAlive(0));
        Assert.Throws<ObjectDisposedException>(() => world.Dropped);
        Assert.Throws<ObjectDisposedException>(() => writer.Send(slot, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => writer.SendRecord(0));
    }

    [Fact]
    public void AWorldRefusesToStepOnceItsPackIsDisposed()
    {
        var pack = Small().Compile();
        using var world = new ColumnWorld(pack, 64);
        var slot = world.Spawn(0);
        pack.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Step());
        Assert.Throws<ObjectDisposedException>(() => world.Spawn(0));
        Assert.Throws<ObjectDisposedException>(() => world.Set(slot, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => new ColumnWorld(pack, 64));
    }

    [Fact]
    public void NothingChangesTheWorldBetweenBeginFrameAndEndFrame()
    {
        using var pack = Small().Compile();
        using var world = new ColumnWorld(pack, 128);
        var slot = world.Spawn(0);
        world.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => world.Spawn(0));
        Assert.Throws<InvalidOperationException>(() => world.Despawn(slot));
        Assert.Throws<InvalidOperationException>(() => world.Set(slot, 0, 1));
        world.Reduce(0);
        world.Resolve(0);
        world.EndFrame();
        world.Set(slot, 0, 5);
        Assert.Equal(5, world.Get(slot, 0));
        Assert.True(world.IsAlive(world.Spawn(0)));
    }

    [Fact]
    public void AFullWorldReusesAChunkOnlyOnceItIsEmpty()
    {
        var spec = Small();
        spec.Templates.Add(new TemplateSpec("other"));
        using var compiled = spec.Compile();
        using var twin = new Lockstep(compiled, 128);
        var first = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            first.Add(twin.Spawn("unit"));
        }

        var second = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            second.Add(twin.Spawn("other"));
        }

        Assert.Throws<InvalidOperationException>(() => twin.World.Spawn(compiled.Template("other")));
        twin.Despawn(second[10]);
        Assert.Throws<InvalidOperationException>(() => twin.World.Spawn(compiled.Template("unit")));
        Assert.Equal(second[10], twin.Spawn("other"));
        foreach (var slot in first)
        {
            twin.Despawn(slot);
        }

        var reused = twin.Spawn("other");
        Assert.InRange(reused, 0, 63);
        twin.Send(reused, "Damage", 9);
        twin.Send(second[0], "Damage", 1);
        twin.Step();
        Assert.Equal(91, twin.Get(reused, "Health"));
        Assert.Equal(99, twin.Get(second[0], "Health"));
    }

    [Fact]
    public void LanesAtTheIntLimitsSaturateTheSameOnEveryKernel()
    {
        var spec = Small();
        spec.Lanes.Add(new LaneSpec("Rate"));
        spec.Templates[0].Ops.Add(new OpSpec("Damage", 32767, times: "Rate"));
        spec.Templates[0].Ops.Add(new OpSpec("Damage", -32767, times: "Rate", when: Cond.Lt("Health", 0)));
        using var compiled = spec.Compile();
        using var twin = new Lockstep(compiled, 64);
        var a = twin.Spawn("unit");
        var b = twin.Spawn("unit");
        var c = twin.Spawn("unit");
        twin.Set(a, "Rate", int.MaxValue);
        twin.Set(b, "Rate", int.MinValue);
        twin.Set(c, "Health", int.MinValue);
        twin.Set(c, "Rate", int.MinValue);
        for (var f = 0; f < 3; f++)
        {
            twin.Send(a, "Damage", Record.AmountMax, hits: 1000);
            twin.Send(b, "Damage", Record.AmountMin, hits: 1000);
            twin.Step();
        }

        Assert.Equal(int.MinValue, twin.Get(a, "Health"));
        Assert.Equal(int.MaxValue, twin.Get(b, "Health"));
        Assert.Equal(int.MaxValue, twin.Get(a, "Taken"));
    }
    [Fact]
    public void AReclaimedChunkNeverTakesASpawnOfItsOldTemplate()
    {
        var spec = Small();
        spec.Templates.Add(new TemplateSpec("other"));
        spec.Templates[1].Values["Health"] = 7;
        using var compiled = spec.Compile();
        using var world = new ColumnWorld(compiled, 128);
        var unit = compiled.Template("unit");
        var other = compiled.Template("other");
        var units = Enumerable.Range(0, 64).Select(_ => world.Spawn(unit)).ToList();
        var others = Enumerable.Range(0, 64).Select(_ => world.Spawn(other)).ToList();
        world.Despawn(units[0]);
        foreach (var slot in units.Skip(1))
        {
            world.Despawn(slot);
        }

        var reclaimed = world.Spawn(other);
        Assert.Equal(other, world.TemplateOf(reclaimed));
        Assert.Throws<InvalidOperationException>(() => world.Spawn(unit));
        for (var slot = 0; slot < world.Capacity; slot++)
        {
            if (world.IsAlive(slot))
            {
                Assert.Equal(other, world.TemplateOf(slot));
                Assert.Equal(7, world.Get(slot, compiled.Lane("Health")));
            }
        }
    }

    [Fact]
    public void SpawnAndDespawnChurnOnAFullChunkAllocatesNothing()
    {
        using var compiled = Small().Compile();
        using var world = new ColumnWorld(compiled, 256);
        var slots = Enumerable.Range(0, 64).Select(_ => world.Spawn(0)).ToArray();
        Churn(world, slots, 100_000);

        var least = long.MaxValue;
        for (var window = 0; window < 3; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            Churn(world, slots, 100_000);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, least);
        Assert.Equal(64, world.Count);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Churn(ColumnWorld world, int[] slots, int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            world.Despawn(slots[i % 64]);
            slots[i % 64] = world.Spawn(0);
        }
    }
}
