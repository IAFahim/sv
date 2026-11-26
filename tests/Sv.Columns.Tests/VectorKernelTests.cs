namespace Sv.Columns.Tests;

public sealed class VectorKernelTests
{
    static int Any(Dice dice) => (dice.Next(65536) << 16) | dice.Next(65536);

    static void Drive(CompiledPack pack, ulong seed, int frames, int writers, int parts)
    {
        var dice = new Dice(seed ^ 0x5EEDul);
        using var scalar = new ColumnWorld(pack, 1024, writers, parts, KernelChoice.Scalar);
        using var vector = new ColumnWorld(pack, 1024, writers, parts, KernelChoice.Vector);
        var live = new List<int>();
        var lanes = pack.LaneNames.Where(n => n[0] != '@').Select(pack.Lane).ToList();
        void Spawn()
        {
            var template = pack.Template(dice.Pick(pack.TemplateNames));
            var slot = scalar.Spawn(template);
            Assert.Equal(slot, vector.Spawn(template));
            live.Add(slot);
        }

        for (var i = dice.Range(1, 300); i > 0; i--)
        {
            Spawn();
        }

        for (var f = 0; f < frames; f++)
        {
            for (var i = dice.Range(0, 400); i > 0 && live.Count > 0; i--)
            {
                var target = dice.Pick(live);
                var source = dice.Pick(live);
                var channel = dice.Next(pack.ChannelNames.Count);
                var amount = dice.Chance(5) ? (long)dice.Range(-1, 1) << 40 : dice.Range(-20, 40);
                var writer = dice.Next(writers);
                var hits = dice.Range(1, 3);
                scalar.Writer(writer).SendFrom(source, target, channel, amount, hits);
                vector.Writer(writer).SendFrom(source, target, channel, amount, hits);
            }

            for (var i = dice.Range(0, 10); i > 0 && live.Count > 0 && lanes.Count > 0; i--)
            {
                var slot = dice.Pick(live);
                var lane = dice.Pick(lanes);
                var value = dice.Chance(20) ? Any(dice) : dice.Range(-10, 90);
                scalar.Set(slot, lane, value);
                vector.Set(slot, lane, value);
            }

            if (dice.Chance(30) && live.Count > 1)
            {
                var gone = dice.Pick(live);
                live.Remove(gone);
                scalar.Despawn(gone);
                vector.Despawn(gone);
            }

            if (dice.Chance(30) && live.Count < 1000)
            {
                Spawn();
            }

            scalar.Step();
            vector.Step();
            Assert.Equal(scalar.Digest(), vector.Digest());
        }
    }

    [Fact]
    public void TheVectorChoiceUsesThePortableKernelWhenItIsAccelerated()
    {
        using var pack = ArenaPack.Build().Compile();
        using var vector = new ColumnWorld(pack, 64, 1, 1, KernelChoice.Vector);
        using var scalar = new ColumnWorld(pack, 64, 1, 1, KernelChoice.Scalar);
        using var best = new ColumnWorld(pack, 64);
        Assert.Equal(Simd.Supported ? KernelPath.Vector : KernelPath.Scalar, vector.KernelPath);
        Assert.False(vector.Vectorized);
        Assert.Equal(KernelPath.Scalar, scalar.KernelPath);
        Assert.Equal(Kernels.Supported ? KernelPath.Vector256 : Simd.Supported ? KernelPath.Vector : KernelPath.Scalar, best.KernelPath);
    }

    [Fact]
    public void ArenaPackMatchesTheScalarLawUnderRandomTraffic()
    {
        using var pack = ArenaPack.Build().Compile();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            Drive(pack, seed, 24, 1 + (int)(seed % 3), 1 + (int)(seed % 4));
        }
    }

    [Fact]
    public void RandomPacksMatchTheScalarLaw()
    {
        var compiled = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            CompiledPack pack;
            try
            {
                pack = RandomPacks.Build(seed).Compile();
            }
            catch (PackException)
            {
                continue;
            }

            using (pack)
            {
                compiled++;
                try
                {
                    Drive(pack, seed, 12, 1 + (int)(seed % 3), 1 + (int)(seed % 3));
                }
                catch (Exception e) when (e is not PackException)
                {
                    throw new Exception("seed " + seed + Environment.NewLine + PackText.Describe(pack.Source), e);
                }
            }
        }

        Assert.True(compiled > 150, "only " + compiled + " of 200 random packs compiled");
    }

    [Fact]
    public void LanesTooWideForInt32SumsFallBackExactly()
    {
        var pack = new ColumnPack();
        foreach (var name in new[] { "Health", "Power", "Rage", "Armor" })
        {
            pack.Lanes.Add(new LaneSpec(name, 0));
        }

        pack.Channels.AddRange(new[] { "Damage", "Healing" });
        var brute = new TemplateSpec("brute");
        brute.Ops.Add(new OpSpec("Damage", 32767, times: "Power"));
        brute.Ops.Add(new OpSpec("Damage", -32767, times: "Rage", when: Cond.Gt("Armor", 0)));
        brute.Ops.Add(new OpSpec("Healing", 32767, when: Cond.Gt("Rage", 5)));
        brute.Ops.Add(new OpSpec("Healing", 7));
        pack.Templates.Add(brute);
        pack.Rules.Add(new FlowRule("Health", new[] { "Healing" }, new[] { "Damage" }));
        using var compiled = pack.Compile();
        using var scalar = new ColumnWorld(compiled, 256, 1, 1, KernelChoice.Scalar);
        using var vector = new ColumnWorld(compiled, 256, 1, 1, KernelChoice.Vector);
        var dice = new Dice(41);
        var template = compiled.Template("brute");
        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(scalar.Spawn(template), vector.Spawn(template));
        }

        var power = compiled.Lane("Power");
        var rage = compiled.Lane("Rage");
        var armor = compiled.Lane("Armor");
        for (var frame = 0; frame < 16; frame++)
        {
            for (var slot = 0; slot < 256; slot++)
            {
                var wide = slot >= 128 && (slot & 1) == 0;
                foreach (var (lane, value) in new[]
                {
                    (power, wide ? Any(dice) : dice.Range(-16000, 16000)),
                    (rage, wide ? Any(dice) : dice.Range(-16000, 16000)),
                    (armor, dice.Range(-1, 1)),
                })
                {
                    scalar.Set(slot, lane, value);
                    vector.Set(slot, lane, value);
                }
            }

            scalar.Step();
            vector.Step();
            Assert.Equal(scalar.Digest(), vector.Digest());
        }
    }

    [Fact]
    public void AWarmFrameAllocatesNothing()
    {
        using var pack = ArenaPack.Build().Compile();
        using var world = new ColumnWorld(pack, 4096, 2, 2, KernelChoice.Vector);
        var dice = new Dice(5);
        for (var i = 0; i < 3000; i++)
        {
            world.Spawn(pack.Template(dice.Pick(pack.TemplateNames)));
        }

        void Frame()
        {
            for (var i = 0; i < 20000; i++)
            {
                world.Writer(i & 1).SendFrom((i * 7) % 3000, i % 3000, i % pack.ChannelNames.Count, (i % 50) - 10);
            }

            world.Step();
        }

        Frame();
        Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 10; f++)
        {
            Frame();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
