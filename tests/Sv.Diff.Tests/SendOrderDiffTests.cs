using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;
using SvScenario = Sv.Pack.Scenario;

namespace DiffTests
{
    public sealed class SendOrderSeeds
    {
        public static readonly int First = 4001 + Seeds.Offset;
        public const int Count = 100;

        public SendOrderSeeds()
        {
            var clock = Stopwatch.StartNew();
            var cases = new List<GeneratedCase>();
            for (var seed = First; seed < First + Count; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions
                {
                    Bricks = (seed & 1) == 0,
                    CardModifiers = (seed & 2) == 0,
                    StatOps = (seed & 4) == 0,
                    Absorb = (seed & 8) != 0,
                }));
            }

            All = KernelCompiler.Build(cases);
            BuildMilliseconds = clock.ElapsedMilliseconds;
        }

        public List<CompiledCase> All { get; }

        public long BuildMilliseconds { get; }
    }

    public sealed class SendOrderDiffTests : IClassFixture<SendOrderSeeds>
    {
        static readonly MethodInfo CompiledRun = typeof(Runs).GetMethod(nameof(Runs.Compiled))!;
        readonly SendOrderSeeds seeds;
        readonly ITestOutputHelper output;

        public SendOrderDiffTests(SendOrderSeeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " seeds checked, " + failures.Count + " failed, seeds " + SendOrderSeeds.First + "-" + (SendOrderSeeds.First + SendOrderSeeds.Count - 1) + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no seed reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        [Fact]
        public void Shuffling_send_order_within_each_frame_preserves_every_value_and_digest()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.All)
            {
                checkedCount++;
                if (!item.Ready)
                {
                    failures.Add(Seeds.Receipt("sendorder", item, "compile failed: " + item.Failure));
                    continue;
                }

                var original = Runs.Read(item.Plan!, item.Case.Scenario);
                var shuffled = ShuffleSendsPerFrame(original, item.Case.Seed);
                var options = Seeds.Options(1, 64, StoragePolicy.Shared);
                var runner = CompiledRun.MakeGenericMethod(item.Kernels!);
                var first = (RunTrace)runner.Invoke(null, new object[] { item.Plan!, original, options })!;
                var second = (RunTrace)runner.Invoke(null, new object[] { item.Plan!, shuffled, options })!;
                var difference = RunTrace.FirstDifference(first, second, true);
                if (difference.Length > 0)
                {
                    failures.Add(Seeds.Receipt("sendorder", item, difference));
                }
            }

            Verdict("sendorder", checkedCount, failures);
        }

        static SvScenario ShuffleSendsPerFrame(SvScenario source, int seed)
        {
            var copy = new SvScenario
            {
                Name = source.Name,
                Frames = source.Frames,
                Capacity = source.Capacity,
                Digest = source.Digest,
            };
            copy.Entities.AddRange(source.Entities);
            copy.Kills.AddRange(source.Kills);
            copy.Modifiers.AddRange(source.Modifiers);
            copy.Expects.AddRange(source.Expects);

            var random = new Random(seed);
            for (var frame = 0L; frame < source.Frames; frame++)
            {
                var frameSends = source.Sends.Where(send => send.Frame == frame).ToList();
                for (var i = frameSends.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    var tmp = frameSends[i];
                    frameSends[i] = frameSends[j];
                    frameSends[j] = tmp;
                }

                copy.Sends.AddRange(frameSends);
            }

            foreach (var send in source.Sends)
            {
                if (send.Frame < 0 || send.Frame >= source.Frames)
                {
                    copy.Sends.Add(send);
                }
            }

            return copy;
        }
    }
}
