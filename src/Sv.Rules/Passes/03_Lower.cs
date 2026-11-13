using System.Collections.Generic;

namespace Sv.Rules.Passes
{
    public sealed class LowerPass : CompilerPass
    {
        public override string Id => "lower";

        public override void Run(CompileUnit unit)
        {
            var pack = unit.Pack!;
            var plan = unit.Plan!;
            var layers = unit.Layers;
            for (var g = 0; g < plan.GateOrder.Length; g++)
            {
                var gate = plan.GateOrder[g];
                layers.Gates.Add(new GateIr(gate, g, plan.GateIsChannelState[g], Condition.Of(gate.Card, ExprTools.Inline(gate.When, pack))));
            }

            var self = new HashSet<EmitRule>(plan.SelfOrder);
            for (var e = 0; e < pack.Emits.Length; e++)
            {
                var rule = pack.Emits[e];
                var hasOther = false;
                for (var a = 0; a < rule.Actions.Length; a++)
                {
                    hasOther |= !rule.IsSameFrameSelf(rule.Actions[a]);
                }

                layers.Emits.Add(new EmitIr(rule, e, hasOther, self.Contains(rule), plan.EmitWantsUntaken[e], Condition.Of(rule.Card, ExprTools.Inline(rule.When, pack))));
            }

            for (var e = 0; e < pack.Edges.Length; e++)
            {
                var edge = new EdgeIr(pack.Edges[e], Condition.Of(pack.Edges[e].Card, ExprTools.Inline(pack.Edges[e].When, pack)));
                for (var r = 0; r < pack.Events.Length; r++)
                {
                    if (pack.Events[r].Event == edge.Rule.Event)
                    {
                        edge.EventRule = r;
                        break;
                    }
                }

                layers.Edges.Add(edge);
            }

            for (var f = 0; f < pack.Flows.Length; f++)
            {
                layers.Flows.Add(new FlowIr(pack.Flows[f], Condition.Of(pack.Flows[f].Card, ExprTools.Inline(pack.Flows[f].When, pack))));
            }

            for (var r = 0; r < pack.Events.Length; r++)
            {
                var rule = pack.Events[r];
                layers.Events.Add(new EventIr(rule, r, ExprTools.Inline(rule.Enter, pack), ExprTools.Inline(rule.Stay, pack)));
            }

            var deltas = new SortedSet<int>();
            var budgets = new SortedSet<int>();
            for (var f = 0; f < pack.Flows.Length; f++)
            {
                for (var t = 0; t < pack.Flows[f].Terms.Length; t++)
                {
                    deltas.Add(pack.Flows[f].Terms[t].Intrinsic);
                }
            }

            var schema = pack.Schema;
            for (var c = 0; c < schema.Channels.Length; c++)
            {
                if (schema.Channels[c].Into >= 0)
                {
                    deltas.Add(schema.Channels[c].Into);
                }
            }

            for (var g = 0; g < pack.Gates.Length; g++)
            {
                for (var a = 0; a < pack.Gates[g].Absorbs.Length; a++)
                {
                    deltas.Add(pack.Gates[g].Absorbs[a].Budget);
                    budgets.Add(pack.Gates[g].Absorbs[a].Budget);
                }

                for (var s = 0; s < pack.Gates[g].Spends.Length; s++)
                {
                    deltas.Add(pack.Gates[g].Spends[s]);
                    budgets.Add(pack.Gates[g].Spends[s]);
                }
            }

            for (var f = 0; f < pack.Folds.Length; f++)
            {
                var fold = pack.Folds[f];
                if (fold.Floor.Kind == BoundKind.Stat || fold.Ceiling.Kind == BoundKind.Stat)
                {
                    deltas.Add(fold.Intrinsic);
                }
            }

            foreach (var id in deltas)
            {
                if (schema.IndexOf(ValueClass.Intrinsic, id) >= 0)
                {
                    layers.DeltaIntrinsics.Add(id);
                }
            }

            foreach (var id in budgets)
            {
                layers.AbsorbBudgets.Add(id);
            }

            foreach (var id in deltas)
            {
                var mode = schema.AssignModeOf(id, pack.Flows);
                if (mode != AssignMode.Add && schema.IndexOf(ValueClass.Intrinsic, id) >= 0)
                {
                    layers.AssignIntrinsics.Add(id);
                    layers.AssignModes.Add(mode);
                }
            }

            for (var i = 0; i < schema.Intrinsics.Length; i++)
            {
                var id = schema.Intrinsics[i].Id;
                var fold = new FoldIr(id, plan.FoldOf(id));
                fold.DeltaSlot = layers.DeltaIntrinsics.IndexOf(id);
                if (fold.DeltaSlot >= 0 || fold.Rebinds)
                {
                    layers.Folds.Add(fold);
                }
            }
        }
    }
}
