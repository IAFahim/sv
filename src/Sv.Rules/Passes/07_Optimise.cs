using System.Collections.Generic;

namespace Sv.Rules.Passes
{
    public sealed class OptimisePass : CompilerPass
    {
        public override string Id => "optimise";

        public override void Run(CompileUnit unit)
        {
            var schema = unit.Schema;
            var layers = unit.Layers;
            var kernel = unit.Kernel;
            kernel.MethodCap = unit.Options.MethodCap < 1 ? 1 : unit.Options.MethodCap;
            for (var i = 0; i < layers.Gates.Count; i++)
            {
                var gate = layers.Gates[i];
                gate.When = Folded(gate.When);
                gate.Channels = ExprTools.ChannelsOf(gate.When.Rest);
                kernel.ShapeCapacity += gate.Rule.Shapes.Length;
            }

            for (var i = 0; i < layers.Emits.Count; i++)
            {
                layers.Emits[i].When = Folded(layers.Emits[i].When);
            }

            for (var i = 0; i < layers.Edges.Count; i++)
            {
                layers.Edges[i].When = Folded(layers.Edges[i].When);
            }

            for (var i = 0; i < layers.Flows.Count; i++)
            {
                layers.Flows[i].When = Folded(layers.Flows[i].When);
            }

            for (var i = 0; i < layers.Events.Count; i++)
            {
                layers.Events[i].Enter = ExprTools.Fold(layers.Events[i].Enter);
                layers.Events[i].Stay = ExprTools.Fold(layers.Events[i].Stay);
            }

            for (var c = 0; c < schema.Channels.Length; c++)
            {
                var channel = schema.Channels[c].Id;
                kernel.Admit.Add(Dispatch(layers.Gates, channel, false));
                kernel.SelfAdmit.Add(Dispatch(layers.Gates, channel, true));
            }

            kernel.AdmitAny = Dispatch(layers.Gates, -1, false);
            kernel.SelfAdmitAny = Dispatch(layers.Gates, -1, true);

            var guards = new SortedSet<int>();
            for (var i = 0; i < layers.Gates.Count; i++)
            {
                Register(kernel, layers.Gates[i].When, guards);
            }

            for (var i = 0; i < layers.Emits.Count; i++)
            {
                Register(kernel, layers.Emits[i].When, guards);
            }

            for (var i = 0; i < layers.Edges.Count; i++)
            {
                Register(kernel, layers.Edges[i].When, guards);
            }

            for (var i = 0; i < layers.Flows.Count; i++)
            {
                Register(kernel, layers.Flows[i].When, guards);
            }

            kernel.Guards.AddRange(guards);
        }

        static Condition Folded(Condition condition) => new Condition(condition.Guard, condition.Masks, ExprTools.Fold(condition.Rest));

        static GateChannel Dispatch(List<GateIr> gates, int channel, bool channelState)
        {
            var dispatch = new GateChannel(channel);
            for (var g = 0; g < gates.Count; g++)
            {
                var gate = gates[g];
                if ((channelState && !gate.ChannelState) || (!gate.Decides && !gate.Shapes))
                {
                    continue;
                }

                if (channel < 0)
                {
                    if (gate.Channels == null)
                    {
                        dispatch.Cases.Add(new GateCase(gate, gate.When));
                    }

                    continue;
                }

                if (gate.Channels != null && System.Array.BinarySearch(gate.Channels, (long)channel) < 0)
                {
                    continue;
                }

                var rest = ExprTools.WithChannel(gate.When.Rest, channel);
                if (rest.Op == Op.BoolConst && rest.Integer == 0)
                {
                    continue;
                }

                dispatch.Cases.Add(new GateCase(gate, new Condition(gate.When.Guard, gate.When.Masks, rest)));
            }

            return dispatch;
        }

        static void Register(KernelIr kernel, Condition condition, SortedSet<int> guards)
        {
            if (condition.Guard >= 0)
            {
                guards.Add(condition.Guard);
            }

            for (var m = 0; m < condition.Masks.Length; m++)
            {
                var mask = condition.Masks[m];
                var at = MaskIndex(kernel, mask);
                if (at < 0)
                {
                    kernel.Masks.Add(mask);
                    kernel.MaskGuards.Add(condition.Guard);
                    kernel.MaskAlways.Add(condition.Guard < 0);
                    continue;
                }

                if (kernel.MaskGuards[at] != condition.Guard || condition.Guard < 0)
                {
                    kernel.MaskAlways[at] = true;
                }
            }
        }

        public static int MaskIndex(KernelIr kernel, RowMask mask)
        {
            for (var k = 0; k < kernel.Masks.Count; k++)
            {
                if (kernel.Masks[k].Same(mask))
                {
                    return k;
                }
            }

            return -1;
        }
    }
}
