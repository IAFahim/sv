using Sv.Host;

namespace Sv.Columns.Tests;

public sealed class RunnerTests
{
    [Fact]
    public void TheWorkerRunnerGivesTheSingleThreadResult()
    {
        using var pack = ArenaPack.Build().Compile();
        foreach (var threads in new[] { 1, 2, 4, 7 })
        {
            using var workers = new Workers(threads);
            using var single = new ColumnWorld(pack, 4096, threads, 1, KernelChoice.Scalar);
            using var parallel = new ColumnWorld(pack, 4096, threads, threads * 3);
            using var runner = new ColumnRunner(parallel, workers);
            var dice = new Dice((ulong)threads);
            var slots = new List<int>();
            for (var i = 0; i < 2500; i++)
            {
                var template = pack.Template(dice.Pick(pack.TemplateNames));
                var slot = single.Spawn(template);
                Assert.Equal(slot, parallel.Spawn(template));
                slots.Add(slot);
            }

            for (var frame = 0; frame < 15; frame++)
            {
                for (var i = 0; i < 6000; i++)
                {
                    var target = dice.Pick(slots);
                    var source = dice.Pick(slots);
                    var channel = dice.Next(pack.ChannelNames.Count);
                    var amount = dice.Range(-15, 40);
                    single.Writer(dice.Next(threads)).Send(target, source, channel, amount);
                    parallel.Writer(dice.Next(threads)).Send(target, source, channel, amount);
                }

                single.Step();
                runner.Step();
                Assert.Equal(single.Digest(), parallel.Digest());
            }
        }
    }
}
