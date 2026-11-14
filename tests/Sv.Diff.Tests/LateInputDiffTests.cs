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
    public sealed class LateInputSeeds
    {
        public static readonly int First = 8001 + Seeds.Offset;
        public const int Count = 100;

        public LateInputSeeds()
        {
            var clock = Stopwatch.StartNew();
            var cases = new List<GeneratedCase>();
            for (var seed = First; seed < First + Count; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions
                {
                    StatOps = (seed & 1) == 0,
                    CardModifiers = (seed % 3) == 0,
                }));
            }

            All = KernelCompiler.Build(cases);
            BuildMilliseconds = clock.ElapsedMilliseconds;
        }

        public List<CompiledCase> All { get; }

        public long BuildMilliseconds { get; }
    }

    public sealed class LateInputDiffTests : IClassFixture<LateInputSeeds>
    {
        static readonly (int Workers, StoragePolicy Policy)[] Configurations =
        {
            (1, StoragePolicy.Shared),
            (4, StoragePolicy.Dense),
        };

        static readonly MethodInfo verifyGeneric = typeof(LateInputDiffTests).GetMethod(nameof(VerifySeed), BindingFlags.Static | BindingFlags.NonPublic)!;

        readonly LateInputSeeds seeds;
        readonly ITestOutputHelper output;

        public LateInputDiffTests(LateInputSeeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        [Fact]
        public void Late_input_with_and_without_prior_rollback_matches_straight_run_with_zero_replay_faults()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.All)
            {
                checkedCount++;
                if (!item.Ready || item.Warnings.Length > 0)
                {
                    failures.Add(Seeds.Receipt("late-input-compile", item, item.Failure + item.Warnings));
                    continue;
                }

                var issue = (string)verifyGeneric.MakeGenericMethod(item.Kernels!).Invoke(null, new object[] { item })!;
                if (issue.Length > 0)
                {
                    failures.Add(Seeds.Receipt("late-input", item, issue));
                }
            }

            output.WriteLine(
                "late-input: " + checkedCount + " seeds checked, " + failures.Count + " failed, seeds "
                + LateInputSeeds.First + "-" + (LateInputSeeds.First + LateInputSeeds.Count - 1)
                + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount == LateInputSeeds.Count, "late-input: expected " + LateInputSeeds.Count + " seeds, checked " + checkedCount);
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        static unsafe string VerifySeed<TKernels>(CompiledCase item)
            where TKernels : struct, IPackKernels
        {
            var plan = item.Plan!;
            var scenario = Runs.Read(plan, item.Case.Scenario);
            var pack = HostPack.From(plan);
            var (lateFrame, targetSlot, channelId, amount) = PickLateInput(plan.Schema, scenario, item.Case.Seed);
            var key = EffectKey.Pack(targetSlot, EffectKey.NoSource, 0, (byte)channelId, Origin.Direct, 0);
            var record = new LoggedRecord(key, amount, 1);

            foreach (var (workers, policy) in Configurations)
            {
                var options = new HostOptions
                {
                    Workers = workers,
                    Parts = 64,
                    Policy = policy,
                    EntityCapacity = Math.Max(1024, scenario.Capacity),
                    FramesPerEpoch = 2,
                };

                var error = RunStraight<TKernels>(pack, plan.Schema.KindIntrinsic, scenario, options, lateFrame, targetSlot, channelId, amount, out var straightDigest);
                if (error.Length > 0)
                {
                    return policy + "/" + workers + "w straight: " + error;
                }

                error = RunLate<TKernels>(pack, plan.Schema.KindIntrinsic, scenario, options, lateFrame, &record, straightDigest);
                if (error.Length > 0)
                {
                    return policy + "/" + workers + "w late: " + error;
                }

                error = RunRollbackThenLate<TKernels>(pack, plan.Schema.KindIntrinsic, scenario, options, lateFrame, &record, item.Case.Seed, straightDigest);
                if (error.Length > 0)
                {
                    return policy + "/" + workers + "w rollback-then-late: " + error;
                }
            }

            return string.Empty;
        }

        static string RunStraight<TKernels>(
            HostPack pack,
            int kindLane,
            SvScenario scenario,
            HostOptions options,
            long lateFrame,
            int targetSlot,
            int channelId,
            int amount,
            out ulong digest)
            where TKernels : struct, IPackKernels
        {
            digest = 0ul;
            using var host = new FrameLoop<TKernels>(pack, options);
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                var error = FeedFrame(host, pack, scenario, frame, kindLane);
                if (error.Length > 0)
                {
                    return error;
                }

                if (frame == lateFrame)
                {
                    host.Send(targetSlot, EffectKey.NoSource, 0, channelId, amount, 1, Origin.Direct);
                }

                host.Step();
            }

            if (host.ReplayFaults != 0L)
            {
                return "ReplayFaults=" + host.ReplayFaults;
            }

            if (host.Frame != scenario.Frames)
            {
                return "frame " + host.Frame + " != " + scenario.Frames;
            }

            digest = host.Digest();
            return string.Empty;
        }

        static unsafe string RunLate<TKernels>(
            HostPack pack,
            int kindLane,
            SvScenario scenario,
            HostOptions options,
            long lateFrame,
            LoggedRecord* record,
            ulong straightDigest)
            where TKernels : struct, IPackKernels
        {
            using var host = new FrameLoop<TKernels>(pack, options);
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                var error = FeedFrame(host, pack, scenario, frame, kindLane);
                if (error.Length > 0)
                {
                    return error;
                }

                host.Step();
                if (host.Resolve())
                {
                    return "Resolve() returned true before any late input on frame " + frame;
                }
            }

            return ApplyLateAndCompare(host, scenario.Frames, lateFrame, record, straightDigest);
        }

        static unsafe string RunRollbackThenLate<TKernels>(
            HostPack pack,
            int kindLane,
            SvScenario scenario,
            HostOptions options,
            long lateFrame,
            LoggedRecord* record,
            int seed,
            ulong straightDigest)
            where TKernels : struct, IPackKernels
        {
            using var host = new FrameLoop<TKernels>(pack, options);
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                var error = FeedFrame(host, pack, scenario, frame, kindLane);
                if (error.Length > 0)
                {
                    return error;
                }

                host.Step();
            }

            var epochAtLate = host.World.EpochAtOrBefore(lateFrame);
            var oldest = host.World.OldestEpoch;
            var rewindEpoch = (seed & 2) != 0 && epochAtLate > oldest ? epochAtLate - 1u : epochAtLate;
            if (!host.Rewind(rewindEpoch))
            {
                return "Rewind(" + rewindEpoch + ") returned false (oldest=" + oldest + ", current=" + host.World.Epoch + ")";
            }

            for (var frame = host.Frame; frame < scenario.Frames; frame++)
            {
                var error = FeedFrame(host, pack, scenario, frame, kindLane);
                if (error.Length > 0)
                {
                    return "replay " + error;
                }

                host.Step();
                if (host.Resolve())
                {
                    return "Resolve() returned true during pre-late replay on frame " + frame;
                }
            }

            return ApplyLateAndCompare(host, scenario.Frames, lateFrame, record, straightDigest);
        }

        static unsafe string ApplyLateAndCompare<TKernels>(
            FrameLoop<TKernels> host,
            long totalFrames,
            long lateFrame,
            LoggedRecord* record,
            ulong straightDigest)
            where TKernels : struct, IPackKernels
        {
            var oldestFrame = host.World.Journal.BeginOf(host.World.OldestEpoch);
            if (lateFrame < oldestFrame || lateFrame >= host.Frame)
            {
                return "chosen lateFrame " + lateFrame + " outside ring window [" + oldestFrame + ", " + host.Frame + ")";
            }

            var accepted = host.Late(lateFrame, record, 1);
            if (accepted != lateFrame)
            {
                return "Late(" + lateFrame + ") returned " + accepted;
            }

            if (!host.Resolve())
            {
                return "Resolve() returned false after Late(" + lateFrame + ")";
            }

            if (host.Frame != totalFrames)
            {
                return "frame " + host.Frame + " != " + totalFrames;
            }

            if (host.ReplayFaults != 0L)
            {
                return "ReplayFaults=" + host.ReplayFaults;
            }

            var digest = host.Digest();
            if (digest != straightDigest)
            {
                return "digest " + RunTrace.Hex(digest) + " != straight " + RunTrace.Hex(straightDigest) + " (lateFrame=" + lateFrame + ")";
            }

            return string.Empty;
        }

        static string FeedFrame<TKernels>(FrameLoop<TKernels> host, HostPack pack, SvScenario scenario, long frame, int kindLane)
            where TKernels : struct, IPackKernels
        {
            for (var e = 0; e < scenario.Entities.Count; e++)
            {
                var entity = scenario.Entities[e];
                if (entity.Frame != frame)
                {
                    continue;
                }

                var slot = host.Spawn(entity.TemplateIndex);
                if (slot != entity.Slot)
                {
                    return "frame " + frame + " entity " + entity.Name + " got slot " + slot + ", planned " + entity.Slot;
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
                if (modifier.Frame != frame)
                {
                    continue;
                }

                var slot = scenario.SlotOf(modifier.Entity);
                host.Modify(modifier.Remove
                    ? StatOp.RemoveSource(slot, modifier.Source)
                    : StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source, long.MaxValue, modifier.Layer));
            }

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
                        : host.World.StateOf(source) == SlotState.Live
                            ? host.Read(source, kindLane)
                            : pack.TemplateValues[scenario.Entities[send.Source].TemplateIndex][kindLane];
                    kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
                }

                host.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, send.Origin);
            }

            return string.Empty;
        }

        static (long Frame, int Slot, int ChannelId, int Amount) PickLateInput(PackSchema schema, SvScenario scenario, int seed)
        {
            var u = unchecked((uint)seed);
            var startFrame = (int)(u % (uint)scenario.Frames);
            for (var frame = startFrame; frame >= 0; frame--)
            {
                var live = new List<int>();
                for (var e = 0; e < scenario.Entities.Count; e++)
                {
                    if (scenario.Entities[e].Frame > frame)
                    {
                        continue;
                    }

                    var killed = false;
                    for (var k = 0; k < scenario.Kills.Count; k++)
                    {
                        if (scenario.Kills[k].Entity == e && scenario.Kills[k].Frame <= frame)
                        {
                            killed = true;
                            break;
                        }
                    }

                    if (!killed)
                    {
                        live.Add(scenario.Entities[e].Slot);
                    }
                }

                if (live.Count > 0)
                {
                    var targetSlot = live[(int)(u % (uint)live.Count)];
                    var nonOwner = schema.Channels.Where(c => c.Name != "SetOwner").ToArray();
                    var pool = nonOwner.Length > 0 ? nonOwner : schema.Channels;
                    var channel = pool[(int)((u / 7u) % (uint)pool.Length)];
                    var amount = 1 + (int)(u & 7u);
                    return (frame, targetSlot, channel.Id, amount);
                }
            }

            throw new InvalidOperationException("late-input: no live entity found in scenario");
        }
    }
}
