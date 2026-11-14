using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;
using SvScenario = Sv.Pack.Scenario;

namespace DiffTests
{
    public sealed class RewindReplaySeeds
    {
        public static readonly int First = 6001 + Seeds.Offset;
        public const int Count = 100;

        public RewindReplaySeeds()
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

    public sealed class RewindReplayDiffTests : IClassFixture<RewindReplaySeeds>
    {
        static readonly MethodInfo CheckMethod = typeof(RewindReplayDiffTests).GetMethod(nameof(CheckSeed), BindingFlags.Static | BindingFlags.NonPublic)!;
        readonly RewindReplaySeeds seeds;
        readonly ITestOutputHelper output;

        public RewindReplayDiffTests(RewindReplaySeeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " seeds checked, " + failures.Count + " failed, seeds " + RewindReplaySeeds.First + "-" + (RewindReplaySeeds.First + RewindReplaySeeds.Count - 1) + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no seed reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        [Fact]
        public void Rewinding_to_an_earlier_epoch_and_replaying_gives_identical_digests()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.All)
            {
                checkedCount++;
                if (!item.Ready)
                {
                    failures.Add(Seeds.Receipt("rewind", item, "compile failed: " + item.Failure));
                    continue;
                }

                var scenario = Runs.Read(item.Plan!, item.Case.Scenario);
                var checker = CheckMethod.MakeGenericMethod(item.Kernels!);
                foreach (var (workers, policy) in new[] { (1, StoragePolicy.Shared), (4, StoragePolicy.Dense) })
                {
                    var finding = (string)checker.Invoke(null, new object[] { item.Plan!, scenario, item.Case.Seed, workers, policy })!;
                    if (finding.Length > 0)
                    {
                        failures.Add(Seeds.Receipt("rewind", item, workers + " workers " + policy + ": " + finding));
                        break;
                    }
                }
            }

            Verdict("rewind", checkedCount, failures);
        }

        static string CheckSeed<TKernels>(PackPlan plan, SvScenario scenario, int seed, int workers, StoragePolicy policy)
            where TKernels : struct, IPackKernels
        {
            if (scenario.Frames <= 0)
            {
                return string.Empty;
            }

            var pack = HostPack.From(plan);
            var options = new HostOptions
            {
                Workers = workers,
                Parts = 64,
                Policy = policy,
                EntityCapacity = Math.Max(1024, scenario.Capacity),
                FramesPerEpoch = 2,
            };

            var straight = new ulong[scenario.Frames];
            using (var host = new FrameLoop<TKernels>(pack, options))
            {
                for (var frame = 0L; frame < scenario.Frames; frame++)
                {
                    var fault = FeedFrame(host, pack, plan, scenario, frame);
                    if (fault.Length > 0)
                    {
                        return fault;
                    }

                    host.Step();
                    straight[frame] = host.LastDigest;
                }
            }

            var stopFrame = scenario.Frames > 2 ? scenario.Frames - (seed & 1) : scenario.Frames;
            using (var host = new FrameLoop<TKernels>(pack, options))
            {
                for (var frame = 0L; frame < stopFrame; frame++)
                {
                    var fault = FeedFrame(host, pack, plan, scenario, frame);
                    if (fault.Length > 0)
                    {
                        return fault;
                    }

                    host.Step();
                }

                var epoch = host.World.EpochAtOrBefore(stopFrame / 2);
                if (epoch == host.World.Epoch && epoch > host.World.OldestEpoch)
                {
                    epoch--;
                }

                if (!host.Rewind(epoch))
                {
                    return "Rewind(" + epoch + ") refused at frame " + stopFrame;
                }

                for (var frame = host.Frame; frame < scenario.Frames; frame++)
                {
                    var fault = FeedFrame(host, pack, plan, scenario, frame);
                    if (fault.Length > 0)
                    {
                        return fault;
                    }

                    host.Step();
                    if (host.LastDigest != straight[frame])
                    {
                        return "frame " + frame + " after rewind to epoch " + epoch + ": expected " + RunTrace.Hex(straight[frame]) + ", got " + RunTrace.Hex(host.LastDigest);
                    }
                }
            }

            return string.Empty;
        }

        static string FeedFrame<TKernels>(FrameLoop<TKernels> host, HostPack pack, PackPlan plan, SvScenario scenario, long frame)
            where TKernels : struct, IPackKernels
        {
            for (var e = 0; e < scenario.Entities.Count; e++)
            {
                var entity = scenario.Entities[e];
                if (entity.Frame == frame)
                {
                    var slot = host.Spawn(entity.TemplateIndex);
                    if (slot != entity.Slot)
                    {
                        return "frame " + frame + " entity " + entity.Name + " got slot " + slot + ", planned " + entity.Slot;
                    }
                }
            }

            for (var k = 0; k < scenario.Kills.Count; k++)
            {
                var kill = scenario.Kills[k];
                if (kill.Frame == frame)
                {
                    host.Kill(scenario.SlotOf(kill.Entity));
                }
            }

            for (var m = 0; m < scenario.Modifiers.Count; m++)
            {
                var modifier = scenario.Modifiers[m];
                if (modifier.Frame == frame)
                {
                    var slot = scenario.SlotOf(modifier.Entity);
                    host.Modify(modifier.Remove ? StatOp.RemoveSource(slot, modifier.Source) : StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source, long.MaxValue, modifier.Layer));
                }
            }

            var kindLane = plan.Schema.KindIntrinsic;
            for (var s = 0; s < scenario.Sends.Count; s++)
            {
                var send = scenario.Sends[s];
                if (send.Frame != frame)
                {
                    continue;
                }

                var source = send.Source == SvScenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                var kind = send.Kind;
                if (kind == SvScenario.Unset)
                {
                    var raw = send.Source == SvScenario.Unset || kindLane < 0
                        ? 0
                        : host.World.StateOf(source) == SlotState.Live ? host.Read(source, kindLane) : pack.TemplateValues[scenario.Entities[send.Source].TemplateIndex][kindLane];
                    kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
                }

                host.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, send.Origin);
            }

            return string.Empty;
        }
    }
}
