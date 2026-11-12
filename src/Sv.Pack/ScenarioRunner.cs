using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Sv.Host;
using Sv.Rules;
using Ref = Sv.Rules.Reference;

namespace Sv.Pack
{
    public sealed class ScenarioOutcome
    {
        public ulong Digest;
        public readonly List<string> Failures = new List<string>();
        public readonly List<string> Lines = new List<string>();

        public bool Passed => Failures.Count == 0;
    }

    public static class ReferenceDigest
    {
        public static ulong Of(Ref.RefState state, Scenario scenario, HostPack pack)
        {
            var schema = state.Schema;
            var sum = 0ul;
            unchecked
            {
                for (var e = 0; e < scenario.Entities.Count; e++)
                {
                    var planned = scenario.Entities[e];
                    var slot = planned.Slot;
                    if (!state.IsSlot(slot) || state.Slots[slot].Status != Ref.SlotStatus.Live)
                    {
                        continue;
                    }

                    var entity = state.Slots[slot];
                    var defaults = pack.TemplateValues[planned.TemplateIndex];
                    sum += CellDigest.Key(WorldDigest.LivenessColumn, slot, (uint)planned.TemplateIndex);
                    for (var i = 0; i < schema.Intrinsics.Length; i++)
                    {
                        var lane = schema.Intrinsics[i].Lane;
                        sum += entity.Intrinsics[i] != defaults[lane] ? CellDigest.Key(lane, slot, (uint)entity.Intrinsics[i]) : 0ul;
                    }

                    for (var i = 0; i < schema.Stats.Length; i++)
                    {
                        var lane = schema.Stats[i].Lane;
                        sum += entity.Stats[i] != defaults[lane] ? CellDigest.Key(lane, slot, (uint)entity.Stats[i]) : 0ul;
                    }

                    var lanes = new SortedDictionary<int, uint>();
                    for (var i = 0; i < schema.Events.Length; i++)
                    {
                        var lane = EventId.Lane(schema.Events[i].Id);
                        lanes.TryGetValue(lane, out var levels);
                        lanes[lane] = levels | (entity.Events[i] ? 1u << EventId.Word(schema.Events[i].Id) : 0u);
                    }

                    foreach (var pair in lanes)
                    {
                        sum += pair.Value != (uint)defaults[pair.Key] ? CellDigest.Key(pair.Key, slot, pair.Value) : 0ul;
                    }
                }
            }

            return sum;
        }
    }

    public static class ScenarioRunner
    {
        public static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

        public static ScenarioOutcome Reference(PackPlan plan, Scenario scenario, Func<long, Ref.Trace?>? traceOf = null)
        {
            SlotPlanner.Plan(scenario);
            var pack = HostPack.From(plan);
            var interpreter = new Ref.Interpreter(plan);
            var state = interpreter.NewWorld(scenario.Capacity);
            var outcome = new ScenarioOutcome();
            var carried = new List<Ref.RefRecord>();
            for (var frame = 0L; frame < scenario.Frames; frame++)
            {
                var inputs = new Ref.FrameInputs();
                inputs.Records.AddRange(carried);
                for (var e = 0; e < scenario.Entities.Count; e++)
                {
                    var entity = scenario.Entities[e];
                    if (entity.Frame == frame)
                    {
                        inputs.Spawn(entity.Slot, entity.Template);
                    }
                }

                for (var k = 0; k < scenario.Kills.Count; k++)
                {
                    if (scenario.Kills[k].Frame == frame)
                    {
                        inputs.Kill(scenario.SlotOf(scenario.Kills[k].Entity));
                    }
                }

                for (var m = 0; m < scenario.Modifiers.Count; m++)
                {
                    var modifier = scenario.Modifiers[m];
                    if (modifier.Frame == frame)
                    {
                        inputs.StatOps.Add(new Ref.RefStatOp(
                            modifier.Remove ? Ref.StatOpKind.Remove : Ref.StatOpKind.Add,
                            scenario.SlotOf(modifier.Entity),
                            modifier.Stat,
                            modifier.Source,
                            modifier.Shape,
                            modifier.Value));
                    }
                }

                for (var s = 0; s < scenario.Sends.Count; s++)
                {
                    var send = scenario.Sends[s];
                    if (send.Frame != frame)
                    {
                        continue;
                    }

                    var source = send.Source == Scenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                    var kind = send.Kind != Scenario.Unset ? send.Kind : ReferenceKind(state, plan.Schema, pack, scenario, send.Source);
                    inputs.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, (RecordOrigin)(byte)send.Origin);
                }

                var result = interpreter.Step(state, inputs, traceOf?.Invoke(frame));
                state = result.State;
                carried = result.Next;
                Check(scenario, frame, outcome, (slot, expect) => ReadReference(state, expect, slot));
            }

            outcome.Digest = ReferenceDigest.Of(state, scenario, pack);
            Pin(scenario, outcome);
            return outcome;
        }

        static int ReferenceKind(Ref.RefState state, PackSchema schema, HostPack pack, Scenario scenario, int sourceEntity)
        {
            if (sourceEntity == Scenario.Unset || schema.KindIntrinsic < 0)
            {
                return 0;
            }

            var slot = scenario.SlotOf(sourceEntity);
            var raw = state.IsLive(slot)
                ? state.Intrinsic(slot, schema.KindIntrinsic)
                : pack.TemplateValues[scenario.Entities[sourceEntity].TemplateIndex][schema.KindIntrinsic];
            return raw < 0 ? 0 : raw > 255 ? 255 : raw;
        }

        static int ReadReference(Ref.RefState state, ScenarioExpect expect, int slot)
        {
            switch (expect.Class)
            {
                case ValueClass.Stat: return state.Stat(slot, expect.Value);
                case ValueClass.Event: return state.Event(slot, expect.Value) ? 1 : 0;
                default: return state.Intrinsic(slot, expect.Value);
            }
        }

        static void Check(Scenario scenario, long frame, ScenarioOutcome outcome, Func<int, ScenarioExpect, int> read)
        {
            for (var x = 0; x < scenario.Expects.Count; x++)
            {
                var expect = scenario.Expects[x];
                if (expect.Frame != frame)
                {
                    continue;
                }

                var entity = scenario.Entities[expect.Entity];
                var actual = read(entity.Slot, expect);
                var line = "frame " + frame.ToString(CultureInfo.InvariantCulture) + " " + entity.Name + "." + expect.ValueName + " = "
                    + actual.ToString(CultureInfo.InvariantCulture);
                if (actual == expect.EqualsValue)
                {
                    outcome.Lines.Add(line + " ok");
                }
                else
                {
                    outcome.Failures.Add(line + ", expected " + expect.EqualsValue.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        static void Pin(Scenario scenario, ScenarioOutcome outcome)
        {
            outcome.Lines.Add("digest " + Hex(outcome.Digest));
            if (scenario.Digest.HasValue && scenario.Digest.Value != outcome.Digest)
            {
                outcome.Failures.Add("digest " + Hex(outcome.Digest) + ", pinned " + Hex(scenario.Digest.Value));
            }
        }

        public static ScenarioOutcome Compiled(string kernelAssembly, PackPlan plan, Scenario scenario, HostOptions options)
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(kernelAssembly));
            Type? kernels = null;
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsValueType && typeof(IPackKernels).IsAssignableFrom(type))
                {
                    kernels = type;
                    break;
                }
            }

            if (kernels == null)
            {
                throw new InvalidOperationException("sv: " + kernelAssembly + " holds no struct implementing Sv.Host.IPackKernels");
            }

            var run = typeof(ScenarioRunner).GetMethod(nameof(CompiledWith), BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(kernels);
            try
            {
                return (ScenarioOutcome)run.Invoke(null, new object[] { plan, scenario, options })!;
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }

        public static unsafe ScenarioOutcome CompiledWith<TKernels>(PackPlan plan, Scenario scenario, HostOptions options)
            where TKernels : struct, IPackKernels
        {
            SlotPlanner.Plan(scenario);
            var pack = HostPack.From(plan);
            options.EntityCapacity = Math.Max(options.EntityCapacity, scenario.Capacity);
            var outcome = new ScenarioOutcome();
            using var host = new FrameLoop<TKernels>(pack, options);
            var kindLane = plan.Schema.KindIntrinsic;
            for (var frame = 0L; frame < scenario.Frames; frame++)
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
                        outcome.Failures.Add("entity " + entity.Name + " got slot " + slot.ToString(CultureInfo.InvariantCulture) + ", planned "
                            + entity.Slot.ToString(CultureInfo.InvariantCulture));
                    }
                }

                for (var k = 0; k < scenario.Kills.Count; k++)
                {
                    if (scenario.Kills[k].Frame == frame)
                    {
                        host.Kill(scenario.SlotOf(scenario.Kills[k].Entity));
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
                        : StatOp.Add(slot, modifier.Stat, modifier.Shape, modifier.Value, modifier.Source));
                }

                for (var s = 0; s < scenario.Sends.Count; s++)
                {
                    var send = scenario.Sends[s];
                    if (send.Frame != frame)
                    {
                        continue;
                    }

                    var source = send.Source == Scenario.Unset ? EffectKey.NoSource : scenario.SlotOf(send.Source);
                    var kind = send.Kind;
                    if (kind == Scenario.Unset)
                    {
                        var raw = send.Source == Scenario.Unset || kindLane < 0 ? 0 : host.Read(source, kindLane);
                        kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
                    }

                    host.Send(scenario.SlotOf(send.Target), source, kind, send.Channel, send.Amount, send.Hits, send.Origin);
                }

                host.Step();
                Check(scenario, frame, outcome, (slot, expect) => expect.Class == ValueClass.Event ? (host.Level(slot, expect.Value) ? 1 : 0) : host.Read(slot, expect.Value));
            }

            outcome.Digest = host.Digest();
            Pin(scenario, outcome);
            return outcome;
        }
    }
}
