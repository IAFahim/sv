using System;
using System.Collections.Generic;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;
using Ref = Sv.Rules.Reference;
using SvScenario = Sv.Pack.Scenario;

namespace ScenarioTests
{
    public sealed class Twin<TKernels> : IDisposable
        where TKernels : struct, IPackKernels
    {
        readonly PackPlan plan;
        readonly HostPack pack;
        readonly Ref.Interpreter interpreter;
        readonly SortedDictionary<int, int> templateBySlot = new SortedDictionary<int, int>();
        Ref.RefState state;
        Ref.FrameInputs pending = new Ref.FrameInputs();

        public Twin(PackPlan plan, HostOptions options, int retainFrames = 64)
        {
            this.plan = plan;
            pack = HostPack.From(plan);
            interpreter = new Ref.Interpreter(plan, new Ref.InterpreterOptions { RetainFrames = retainFrames });
            state = interpreter.NewWorld(options.EntityCapacity);
            Host = new FrameLoop<TKernels>(pack, options);
        }

        public FrameLoop<TKernels> Host { get; }

        public Ref.RefState Reference => state;

        public long Frame => Host.Frame;

        public int Spawn(string template)
        {
            var index = TemplateIndex(template);
            var slot = Host.Spawn(index);
            pending.Spawn(slot, plan.Schema.Templates[index].Id);
            templateBySlot[slot] = index;
            return slot;
        }

        public void Kill(int slot)
        {
            Host.Kill(slot);
            pending.Kill(slot);
        }

        public void Send(int target, int source, string channel, int amount, int hits = 1)
        {
            var channelId = plan.Schema.FindByName(ValueClass.Channel, channel)!.Id;
            var kindLane = plan.Schema.KindIntrinsic;
            var raw = source == EffectKey.NoSource || kindLane < 0 ? 0 : Host.Read(source, kindLane);
            var kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
            Host.Send(target, source, kind, channelId, amount, hits);
            pending.Send(target, source, kind, channelId, amount, hits);
        }

        public void Step()
        {
            Host.Step();
            var result = interpreter.Step(state, pending);
            state = result.State;
            pending = result.NextInputs();
        }

        public int Intrinsic(string name) => plan.Schema.FindByName(ValueClass.Intrinsic, name)!.Id;

        public int HostValue(int slot, string intrinsic) => Host.Read(slot, Intrinsic(intrinsic));

        public int ReferenceValue(int slot, string intrinsic) => state.Intrinsic(slot, Intrinsic(intrinsic));

        public ulong HostDigest => Host.Digest();

        public ulong ReferenceDigest
        {
            get
            {
                var scenario = new SvScenario();
                foreach (var pair in templateBySlot)
                {
                    scenario.Entities.Add(new ScenarioEntity { Slot = pair.Key, TemplateIndex = pair.Value });
                }

                return Sv.Pack.ReferenceDigest.Of(state, scenario, pack);
            }
        }

        int TemplateIndex(string name)
        {
            for (var t = 0; t < plan.Schema.Templates.Length; t++)
            {
                if (string.CompareOrdinal(plan.Schema.Templates[t].Name, name) == 0)
                {
                    return t;
                }
            }

            throw new ArgumentException("no template " + name);
        }

        public void Dispose() => Host.Dispose();
    }
}
