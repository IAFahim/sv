using System;
using System.Collections.Generic;
using System.Globalization;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;
using Ref = Sv.Rules.Reference;
using SvScenario = Sv.Pack.Scenario;

namespace DiffTests
{
    public sealed class FrameSnapshot
    {
        public ulong Digest;
        public readonly List<string> Names = new List<string>();
        public readonly List<int> Values = new List<int>();

        public void Add(string name, int value)
        {
            Names.Add(name);
            Values.Add(value);
        }
    }

    public sealed class RunTrace
    {
        public readonly List<FrameSnapshot> Frames = new List<FrameSnapshot>();
        public readonly List<string> Faults = new List<string>();

        public static string FirstDifference(RunTrace expected, RunTrace actual, bool valuesToo)
        {
            if (expected.Faults.Count > 0 || actual.Faults.Count > 0)
            {
                return "faults: " + string.Join("; ", expected.Faults) + " | " + string.Join("; ", actual.Faults);
            }

            for (var frame = 0; frame < expected.Frames.Count; frame++)
            {
                var want = expected.Frames[frame];
                var got = actual.Frames[frame];
                if (valuesToo)
                {
                    for (var i = 0; i < want.Values.Count; i++)
                    {
                        if (want.Values[i] != got.Values[i])
                        {
                            return "frame " + frame + " " + want.Names[i] + ": expected " + want.Values[i] + ", got " + got.Values[i];
                        }
                    }
                }

                if (want.Digest != got.Digest)
                {
                    return "frame " + frame + " digest: expected " + Hex(want.Digest) + ", got " + Hex(got.Digest);
                }
            }

            return string.Empty;
        }

        public static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);
    }

    public static class Runs
    {
        public static SvScenario Read(PackPlan plan, string text)
        {
            var scenario = ScenarioReader.Read("generated.scenario.json", text, plan.Schema);
            SlotPlanner.Plan(scenario);
            return scenario;
        }

        static int Status(Ref.SlotStatus status) => status == Ref.SlotStatus.Live ? 1 : status == Ref.SlotStatus.Dying ? 2 : 0;

        static int Status(SlotState state) => state == SlotState.Live ? 1 : state == SlotState.Dying ? 2 : 0;

        static void Snapshot(FrameSnapshot snapshot, PackSchema schema, SvScenario scenario, Func<int, int> status, Func<int, ValueClass, int, int> read)
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

        public static RunTrace Reference(PackPlan plan, SvScenario scenario)
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

                foreach (var kill in scenario.Kills)
                {
                    if (kill.Frame == frame)
                    {
                        inputs.Kill(scenario.SlotOf(kill.Entity));
                    }
                }

                foreach (var modifier in scenario.Modifiers)
                {
                    if (modifier.Frame == frame)
                    {
                        inputs.StatOps.Add(new Ref.RefStatOp(modifier.Remove ? Ref.StatOpKind.Remove : Ref.StatOpKind.Add, scenario.SlotOf(modifier.Entity), modifier.Stat, modifier.Source, modifier.Shape, modifier.Value));
                    }
                }

                foreach (var send in scenario.Sends)
                {
                    if (send.Frame != frame)
                    {
                        continue;
                    }

                    var source = send.Source == SvScenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                    var kind = send.Kind;
                    if (kind == SvScenario.Unset)
                    {
                        var raw = send.Source == SvScenario.Unset || plan.Schema.KindIntrinsic < 0
                            ? 0
                            : state.IsLive(source) ? state.Intrinsic(source, plan.Schema.KindIntrinsic) : pack.TemplateValues[scenario.Entities[send.Source].TemplateIndex][plan.Schema.KindIntrinsic];
                        kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
                    }

                    inputs.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, (RecordOrigin)(byte)send.Origin);
                }

                var result = interpreter.Step(state, inputs);
                state = result.State;
                carried = result.Next;
                var snapshot = new FrameSnapshot();
                var current = state;
                Snapshot(snapshot, plan.Schema, scenario, slot => Status(current.Slots[slot].Status), (slot, valueClass, id) =>
                    valueClass == ValueClass.Stat ? current.Stat(slot, id) : valueClass == ValueClass.Event ? (current.Event(slot, id) ? 1 : 0) : current.Intrinsic(slot, id));
                snapshot.Digest = ReferenceDigest.Of(state, scenario, pack);
                trace.Frames.Add(snapshot);
            }

            return trace;
        }

        public static RunTrace Compiled<TKernels>(PackPlan plan, SvScenario scenario, HostOptions options)
            where TKernels : struct, IPackKernels
        {
            var trace = new RunTrace();
            var pack = HostPack.From(plan);
            options.EntityCapacity = Math.Max(options.EntityCapacity, scenario.Capacity);
            using var host = new FrameLoop<TKernels>(pack, options);
            var kindLane = plan.Schema.KindIntrinsic;
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                foreach (var entity in scenario.Entities)
                {
                    if (entity.Frame == frame)
                    {
                        var slot = host.Spawn(entity.TemplateIndex);
                        if (slot != entity.Slot)
                        {
                            trace.Faults.Add("entity " + entity.Name + " got slot " + slot + ", planned " + entity.Slot);
                        }
                    }
                }

                foreach (var kill in scenario.Kills)
                {
                    if (kill.Frame == frame)
                    {
                        host.Kill(scenario.SlotOf(kill.Entity));
                    }
                }

                foreach (var modifier in scenario.Modifiers)
                {
                    if (modifier.Frame == frame)
                    {
                        var slot = scenario.SlotOf(modifier.Entity);
                        host.Modify(modifier.Remove ? StatOp.RemoveSource(slot, modifier.Source) : StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source));
                    }
                }

                foreach (var send in scenario.Sends)
                {
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

                host.Step();
                var snapshot = new FrameSnapshot();
                Snapshot(snapshot, plan.Schema, scenario, slot => Status(host.World.StateOf(slot)), (slot, valueClass, id) =>
                    valueClass == ValueClass.Event ? (host.Level(slot, id) ? 1 : 0) : host.Read(slot, id));
                snapshot.Digest = host.Digest();
                trace.Frames.Add(snapshot);
            }

            return trace;
        }
    }
}
