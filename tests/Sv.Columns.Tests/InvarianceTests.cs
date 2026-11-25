namespace Sv.Columns.Tests;

public sealed class InvarianceTests
{
    readonly struct Hit
    {
        public Hit(int target, int source, int channel, long amount, int hits)
        {
            Target = target;
            Source = source;
            Channel = channel;
            Amount = amount;
            Hits = hits;
        }

        public int Target { get; }

        public int Source { get; }

        public int Channel { get; }

        public long Amount { get; }

        public int Hits { get; }
    }

    static List<List<Hit>> Traffic(CompiledPack pack, IReadOnlyList<int> slots, int frames, ulong seed)
    {
        var dice = new Dice(seed);
        var all = new List<List<Hit>>();
        for (var f = 0; f < frames; f++)
        {
            var frame = new List<Hit>();
            for (var i = dice.Range(0, 3000); i > 0; i--)
            {
                frame.Add(new Hit(dice.Pick(slots), dice.Chance(50) ? dice.Pick(slots) : -1, dice.Next(pack.ChannelNames.Count), dice.Range(-30, 60), dice.Range(1, 3)));
            }

            all.Add(frame);
        }

        return all;
    }

    static List<ulong> Run(CompiledPack pack, int capacity, string[] spawns, List<List<Hit>> traffic, int writers, int parts, KernelChoice kernels, ulong shuffleSeed)
    {
        using var world = new ColumnWorld(pack, capacity, writers, parts, kernels);
        foreach (var template in spawns)
        {
            world.Spawn(pack.Template(template));
        }

        var dice = new Dice(shuffleSeed);
        var digests = new List<ulong>();
        foreach (var frame in traffic)
        {
            var order = frame.ToArray();
            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = dice.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            foreach (var hit in order)
            {
                var writer = world.Writer(dice.Next(writers));
                if (hit.Source >= 0)
                {
                    writer.Send(hit.Target, hit.Source, hit.Channel, hit.Amount, hit.Hits);
                }
                else
                {
                    writer.Send(hit.Target, hit.Channel, hit.Amount, hit.Hits);
                }
            }

            world.Step();
            digests.Add(world.Digest());
        }

        return digests;
    }

    [Fact]
    public void BestKernelsRunWhereTheHardwareHasThem()
    {
        using var pack = ArenaPack.Build().Compile();
        using var world = new ColumnWorld(pack, 64);
        Assert.Equal(Kernels.Supported, world.Vectorized);
    }

    [Fact]
    public void RecordOrderWriterCountPartCountAndKernelsDoNotChangeTheResult()
    {
        using var pack = ArenaPack.Build().Compile();
        var dice = new Dice(77);
        var spawns = Enumerable.Range(0, 700).Select(_ => dice.Pick(pack.TemplateNames)).ToArray();
        var slots = new List<int>();
        using (var probe = new ColumnWorld(pack, 4096))
        {
            slots.AddRange(spawns.Select(t => probe.Spawn(pack.Template(t))));
        }

        var traffic = Traffic(pack, slots, 40, 99);
        var expected = Run(pack, 4096, spawns, traffic, 1, 1, KernelChoice.Scalar, 1);
        foreach (var (writers, parts, kernels, seed) in new[]
        {
            (1, 1, KernelChoice.Best, 2ul),
            (4, 1, KernelChoice.Scalar, 3ul),
            (3, 5, KernelChoice.Best, 4ul),
            (8, 16, KernelChoice.Best, 5ul),
            (2, 3, KernelChoice.Scalar, 6ul),
        })
        {
            Assert.Equal(expected, Run(pack, 4096, spawns, traffic, writers, parts, kernels, seed));
        }
    }

    [Fact]
    public void AWarmFrameAllocatesNothing()
    {
        using var pack = ArenaPack.Build().Compile();
        foreach (var kernels in new[] { KernelChoice.Scalar, KernelChoice.Best })
        {
            using var world = new ColumnWorld(pack, 4096, 2, 2, kernels);
            var dice = new Dice(5);
            for (var i = 0; i < 3000; i++)
            {
                world.Spawn(pack.Template(dice.Pick(pack.TemplateNames)));
            }

            void Frame()
            {
                for (var i = 0; i < 20000; i++)
                {
                    world.Writer(i & 1).Send(i % 3000, (i * 7) % 3000, i % pack.ChannelNames.Count, (i % 50) - 10);
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
}
