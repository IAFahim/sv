using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;
using Ref = Sv.Rules.Reference;
using SvScenario = Sv.Pack.Scenario;

namespace DiffTests
{
    public sealed class StatLayerDiffTests
    {
        readonly ITestOutputHelper output;

        public StatLayerDiffTests(ITestOutputHelper output) => this.output = output;

        static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "packs", "lego")))
                {
                    dir = dir.Parent;
                }

                return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
            }
        }

        static RunTrace RunReference(PackPlan plan, SvScenario scenario)
        {
            var trace = new RunTrace();
            var pack = HostPack.From(plan);
            var interpreter = new Ref.Interpreter(plan);
            var state = interpreter.NewWorld(scenario.Capacity);
            var carried = new List<Ref.RefRecord>();
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                var inputs = new Ref.FrameInputs();
                inputs.Records.AddRange(carried);
                foreach (var entity in scenario.Entities)
                {
                    if (entity.Frame == frame)
                    {
                        inputs.Spawn(entity.Slot, entity.Template);
                    }
                }

                foreach (var modifier in scenario.Modifiers)
                {
                    if (modifier.Frame == frame)
                    {
                        inputs.StatOps.Add(new Ref.RefStatOp(
                            modifier.Remove ? Ref.StatOpKind.Remove : Ref.StatOpKind.Add,
                            scenario.SlotOf(modifier.Entity),
                            modifier.Stat,
                            modifier.Source,
                            modifier.Shape,
                            modifier.Value,
                            modifier.Layer));
                    }
                }

                foreach (var send in scenario.Sends)
                {
                    if (send.Frame == frame)
                    {
                        var source = send.Source == SvScenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                        var kind = send.Kind == SvScenario.Unset ? 0 : send.Kind;
                        inputs.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, (RecordOrigin)(byte)send.Origin);
                    }
                }

                var result = interpreter.Step(state, inputs);
                state = result.State;
                carried = result.Next;
                var snapshot = new FrameSnapshot();
                RecordSnapshot(snapshot, plan.Schema, scenario, slot => Status(state.Slots[slot].Status), (slot, valueClass, id) =>
                    valueClass == ValueClass.Stat ? state.Stat(slot, id) : valueClass == ValueClass.Event ? (state.Event(slot, id) ? 1 : 0) : state.Intrinsic(slot, id));
                snapshot.Digest = ReferenceDigest.Of(state, scenario, pack);
                trace.Frames.Add(snapshot);
            }

            return trace;
        }

        static RunTrace RunCompiled<TKernels>(PackPlan plan, SvScenario scenario, HostOptions options)
            where TKernels : struct, IPackKernels
        {
            var trace = new RunTrace();
            var pack = HostPack.From(plan);
            options.EntityCapacity = Math.Max(options.EntityCapacity, scenario.Capacity);
            using var host = new FrameLoop<TKernels>(pack, options);
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                foreach (var entity in scenario.Entities)
                {
                    if (entity.Frame == frame)
                    {
                        host.Spawn(entity.TemplateIndex);
                    }
                }

                foreach (var modifier in scenario.Modifiers)
                {
                    if (modifier.Frame == frame)
                    {
                        var slot = scenario.SlotOf(modifier.Entity);
                        host.Modify(modifier.Remove
                            ? StatOp.RemoveSource(slot, modifier.Source)
                            : StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source, long.MaxValue, modifier.Layer));
                    }
                }

                foreach (var send in scenario.Sends)
                {
                    if (send.Frame == frame)
                    {
                        var source = send.Source == SvScenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                        var kind = send.Kind == SvScenario.Unset ? 0 : send.Kind;
                        host.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, send.Origin);
                    }
                }

                host.Step();
                var snapshot = new FrameSnapshot();
                RecordSnapshot(snapshot, plan.Schema, scenario, slot => Status(host.World.StateOf(slot)), (slot, valueClass, id) =>
                    valueClass == ValueClass.Event ? (host.Level(slot, id) ? 1 : 0) : host.Read(slot, id));
                snapshot.Digest = host.Digest();
                trace.Frames.Add(snapshot);
            }

            return trace;
        }

        static int Status(Ref.SlotStatus status) => status == Ref.SlotStatus.Live ? 1 : status == Ref.SlotStatus.Dying ? 2 : 0;

        static int Status(SlotState state) => state == SlotState.Live ? 1 : state == SlotState.Dying ? 2 : 0;

        static void RecordSnapshot(FrameSnapshot snapshot, PackSchema schema, SvScenario scenario, Func<int, int> status, Func<int, ValueClass, int, int> read)
        {
            for (var e = 0; e < scenario.Entities.Count; e++)
            {
                var entity = scenario.Entities[e];
                var state = status(entity.Slot);
                snapshot.Add(entity.Name + ".status", state);
                if (state == 0)
                {
                    continue;
                }

                for (var i = 0; i < schema.Intrinsics.Length; i++)
                {
                    snapshot.Add(entity.Name + "." + schema.Intrinsics[i].Name, read(entity.Slot, ValueClass.Intrinsic, schema.Intrinsics[i].Id));
                }

                for (var i = 0; i < schema.Stats.Length; i++)
                {
                    snapshot.Add(entity.Name + "." + schema.Stats[i].Name, read(entity.Slot, ValueClass.Stat, schema.Stats[i].Id));
                }

                for (var i = 0; i < schema.Events.Length; i++)
                {
                    snapshot.Add(entity.Name + "." + schema.Events[i].Name, read(entity.Slot, ValueClass.Event, schema.Events[i].Id));
                }
            }
        }

        [Fact]
        public void Compiled_and_reference_match_across_layered_cards_ops_and_derived_stat_chains()
        {
            const string values = "{\"schema\":1,\"nextLane\":7,\"rows\":["
                + "{\"id\":0,\"name\":\"Strength\",\"class\":\"stat\",\"default\":100},"
                + "{\"id\":1,\"name\":\"Power\",\"class\":\"stat\",\"default\":0,\"scale\":10,\"derive\":{\"from\":\"Strength\",\"coefficient\":0.5}},"
                + "{\"id\":2,\"name\":\"Reach\",\"class\":\"stat\",\"default\":0,\"derive\":{\"from\":\"Strength\",\"curve\":[1,2,4,8],\"min\":100,\"step\":50}},"
                + "{\"id\":3,\"name\":\"Hybrid\",\"class\":\"stat\",\"default\":0,\"scale\":100,\"derive\":[{\"from\":\"Power\",\"coefficient\":1.0},{\"from\":\"Reach\",\"coefficient\":2.0}]},"
                + "{\"id\":4,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":500},"
                + "{\"id\":5,\"name\":\"CoreTraining\",\"class\":\"intrinsic\",\"default\":0},"
                + "{\"id\":6,\"name\":\"ExtBuff\",\"class\":\"intrinsic\",\"default\":0},"
                + "{\"id\":0,\"name\":\"Physical\",\"class\":\"channel\"},"
                + "{\"id\":1,\"name\":\"GiveExt\",\"class\":\"channel\",\"into\":\"ExtBuff\"}"
                + "]}";
            const string rules = "{\"schema\":1,\"rows\":["
                + "{\"id\":1,\"name\":\"hit_flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\",\"Physical\"]}]},"
                + "{\"id\":2,\"name\":\"power_gate\",\"layer\":\"gate\",\"priority\":0,\"when\":{\"eq\":[\"record.channel\",\"Physical\"]},\"then\":[{\"additive\":{\"cell\":\"source.Hybrid\"}}]}"
                + "]}";
            const string cards = "{\"schema\":1,\"rows\":["
                + "{\"id\":1,\"name\":\"core_training\",\"value\":\"CoreTraining\",\"modifiers\":[{\"stat\":\"Strength\",\"added\":20},{\"stat\":\"Strength\",\"additive\":0.5}]},"
                + "{\"id\":2,\"name\":\"ext_buff\",\"value\":\"ExtBuff\",\"modifiers\":[{\"stat\":\"Strength\",\"added\":5,\"perCount\":true,\"layer\":\"external\"},{\"stat\":\"Power\",\"additive\":0.2,\"layer\":\"external\"}]}"
                + "]}";
            const string templates = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"hero\",\"values\":{\"CoreTraining\":1,\"ExtBuff\":0}}]}";
            const string scenarioJson = "{\"schema\":1,\"name\":\"layers_and_derived\",\"frames\":5,"
                + "\"entities\":[{\"name\":\"A\",\"template\":\"hero\",\"frame\":0},{\"name\":\"B\",\"template\":\"hero\",\"frame\":1}],"
                + "\"modifiers\":["
                + "{\"frame\":0,\"entity\":\"A\",\"stat\":\"Strength\",\"shape\":\"Multi\",\"value\":1000,\"source\":1,\"layer\":\"external\"},"
                + "{\"frame\":1,\"entity\":\"A\",\"stat\":\"Strength\",\"shape\":\"Added\",\"value\":10,\"source\":2,\"layer\":\"external\"},"
                + "{\"frame\":2,\"entity\":\"A\",\"stat\":\"Strength\",\"source\":2,\"op\":\"remove\"},"
                + "{\"frame\":2,\"entity\":\"A\",\"stat\":\"Strength\",\"shape\":\"Added\",\"value\":10,\"source\":3,\"layer\":\"core\"},"
                + "{\"frame\":3,\"entity\":\"A\",\"stat\":\"Power\",\"shape\":\"Added\",\"value\":5,\"source\":4,\"layer\":\"external\"}"
                + "],"
                + "\"sends\":["
                + "{\"frame\":1,\"target\":\"A\",\"channel\":\"GiveExt\",\"amount\":2},"
                + "{\"frame\":2,\"source\":\"A\",\"target\":\"B\",\"channel\":\"Physical\",\"amount\":100},"
                + "{\"frame\":4,\"source\":\"A\",\"target\":\"B\",\"channel\":\"Physical\",\"amount\":100}"
                + "]}";

            var item = KernelCompiler.Build(new[] { new GeneratedCase { Seed = -99, Namespace = "Diff.StatLayers", Values = values, Rules = rules, Cards = cards, Templates = templates, Scenario = scenarioJson } }).Single();
            Assert.True(item.Ready, item.Failure);
            Assert.Equal(string.Empty, item.Warnings);

            var scenario = Runs.Read(item.Plan!, scenarioJson);
            var reference = RunReference(item.Plan!, scenario);
            var runCompiled = typeof(StatLayerDiffTests).GetMethod(nameof(RunCompiled), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var (workers, policy) in new[] { (1, StoragePolicy.Shared), (4, StoragePolicy.Dense) })
            {
                var compiled = (RunTrace)runCompiled.MakeGenericMethod(item.Kernels!).Invoke(null, new object[] { item.Plan!, scenario, Seeds.Options(workers, 64, policy) })!;
                var diff = RunTrace.FirstDifference(reference, compiled, true);
                output.WriteLine("workers " + workers + " " + policy + " diff: " + (diff.Length == 0 ? "none" : diff));
                Assert.Equal(string.Empty, diff);
            }

            Assert.Equal(198, reference.Frames[0].Values[Array.IndexOf(reference.Frames[0].Names.ToArray(), "A.Strength")]);
            Assert.Equal(209, reference.Frames[1].Values[Array.IndexOf(reference.Frames[1].Names.ToArray(), "A.Strength")]);
            Assert.Equal(225, reference.Frames[2].Values[Array.IndexOf(reference.Frames[2].Names.ToArray(), "A.Strength")]);
        }

        [Fact]
        public void Existing_packs_keep_exact_pack_hashes_through_reader_and_writer()
        {
            var root = RepoRoot;
            var referencePack = PackReader.Read(PackFolder.Read(Path.Combine(root, "packs", "reference")));
            var legoPack = PackReader.Read(PackFolder.Read(Path.Combine(root, "packs", "lego")));
            Assert.NotNull(referencePack.Value);
            Assert.NotNull(legoPack.Value);
            Assert.Equal(0xBD70627B1C671888ul, PackHash.Of(referencePack.Value!));
            Assert.Equal(0x9A043301EA06000Eul, PackHash.Of(legoPack.Value!));
            Assert.Equal(0xBD70627B1C671888ul, PackHash.Of(PackWriter.Write(referencePack.Value!)));
            Assert.Equal(0x9A043301EA06000Eul, PackHash.Of(PackWriter.Write(legoPack.Value!)));
        }
    }
}
