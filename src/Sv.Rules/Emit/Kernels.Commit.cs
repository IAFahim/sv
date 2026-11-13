using System.Collections.Generic;
using Sv.Rules.Passes;

namespace Sv.Rules.Emit
{
    public sealed partial class KernelEmitter
    {
        void WriteHostSurface()
        {
            body.Blank();
            body.Open("public static int DeltaTarget(int deltaLane)");
            body.Open("switch (deltaLane)");
            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                body.Line("case " + Text(s) + ": return " + Text(layers.DeltaIntrinsics[s]) + ";");
            }

            body.Line("default: return -1;");
            body.Close();
            body.Close();
            WriteBoundStat("FloorStat", true);
            WriteBoundStat("CeilingStat", false);
            body.Blank();
            body.Open("public static void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling)");
            body.Open("switch (deltaLane)");
            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                var fold = FoldOf(layers.DeltaIntrinsics[s]);
                var intrinsic = schema.Find(ValueClass.Intrinsic, layers.DeltaIntrinsics[s]);
                var scale = intrinsic == null ? 1 : intrinsic.Scale;
                body.Line("case " + Text(s) + ":");
                body.Line("    " + BoundFill(fold == null ? Rules.Bound.None : fold.Floor, "floor", "int.MinValue", scale));
                body.Line("    " + BoundFill(fold == null ? Rules.Bound.None : fold.Ceiling, "ceiling", "int.MaxValue", scale));
                body.Line("    return;");
            }

            body.Line("default:");
            body.Line("    LaneMath.Fill(floor, int.MinValue);");
            body.Line("    LaneMath.Fill(ceiling, int.MaxValue);");
            body.Line("    return;");
            body.Close();
            body.Close();
            body.Blank();
            body.Open("public static int EventId(int ordinal)");
            body.Open("switch (ordinal)");
            for (var i = 0; i < layers.Events.Count; i++)
            {
                body.Line("case " + Text(i) + ": return " + Text(layers.Events[i].Rule.Event) + ";");
            }

            body.Line("default: return -1;");
            body.Close();
            body.Close();
            body.Blank();
            body.Open("public static bool Forwards(int template)");
            body.Open("switch (template)");
            var templates = schema.Templates;
            for (var t = 0; t < templates.Length; t++)
            {
                if (templates[t].Forwards.Length > 0)
                {
                    body.Line("case " + Text(t) + ": return true;");
                }
            }

            body.Line("default: return false;");
            body.Close();
            body.Close();
            body.Blank();
            body.Open("public static int Forward(in WorldView world, int slot, int channel)");
            body.Line("var template = ChunkMeta.TemplateOf(Lanes.WordsOf(world, slot >> 6, world.Layout.MetaLane)[ChunkMeta.Template]);");
            body.Line("var via = -1;");
            body.Open("switch (template)");
            for (var t = 0; t < templates.Length; t++)
            {
                var forwards = templates[t].Forwards;
                if (forwards.Length == 0)
                {
                    continue;
                }

                body.Line("case " + Text(t) + ":");
                body.Open("switch (channel)");
                var seen = new HashSet<int>();
                for (var f = 0; f < forwards.Length; f++)
                {
                    if (seen.Add(forwards[f].Channel))
                    {
                        body.Line("case " + Text(forwards[f].Channel) + ": via = " + Text(forwards[f].Via) + "; break;");
                    }
                }

                body.Close();
                body.Blank();
                body.Line("break;");
            }

            body.Line("default: break;");
            body.Close();
            body.Blank();
            body.Open("if (via < 0)");
            body.Line("return -1;");
            body.Close();
            body.Blank();
            body.Line("var to = Lanes.Read(world, slot, via);");
            body.Line("return to < 0 ? EffectKey.NoSource : to;");
            body.Close();
        }

        FoldRule? FoldOf(int intrinsic)
        {
            for (var f = 0; f < layers.Folds.Count; f++)
            {
                if (layers.Folds[f].Intrinsic == intrinsic)
                {
                    return layers.Folds[f].Rule;
                }
            }

            return null;
        }

        void WriteBoundStat(string name, bool floor)
        {
            body.Blank();
            body.Open("public static int " + name + "(int deltaLane)");
            body.Open("switch (deltaLane)");
            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                var fold = FoldOf(layers.DeltaIntrinsics[s]);
                if (fold == null)
                {
                    continue;
                }

                var bound = floor ? fold.Floor : fold.Ceiling;
                if (bound.Kind == BoundKind.Stat)
                {
                    body.Line("case " + Text(s) + ": return " + Text(bound.Stat) + ";");
                }
            }

            body.Line("default: return -1;");
            body.Close();
            body.Close();
        }

        string BoundFill(Rules.Bound bound, string output, string fallback, int intrinsicScale)
        {
            switch (bound.Kind)
            {
                case BoundKind.Constant: return "LaneMath.Fill(" + output + ", " + Names.Int(bound.Constant) + ");";
                case BoundKind.Stat:
                    return "StatLaw.Rescale(Lanes.Ints(world, chunk, " + Text(bound.Stat) + "), " + Text(ScaleOfStat(bound.Stat)) + ", " + Text(intrinsicScale) + ", " + output + ");";
                default: return "LaneMath.Fill(" + output + ", " + fallback + ");";
            }
        }

        void WriteEvents()
        {
            for (var f = 0; f < layers.Folds.Count; f++)
            {
                var rule = layers.Folds[f].Rule;
                if (rule != null)
                {
                    unit.EmittedRules.Add(rule.Id);
                }
            }

            body.Blank();
            body.Open("public static ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges)");
            body.Line("var any = 0ul;");
            if (layers.Events.Count > 0)
            {
                body.Line("var totals = (long*)(scratch + " + Text(layers.DeltaIntrinsics.Count * Rows) + ");");
                body.Line("var dirty = recheck;");
                body.Line("var v = View(world, chunk);");
                var scope = new ExprScope { Totals = "totals", FreshEvents = true, Capacity = "0" };
                for (var i = 0; i < layers.Events.Count; i++)
                {
                    WriteEvent(layers.Events[i], scope);
                }
            }

            body.Blank();
            body.Line("return any;");
            body.Close();
        }

        void WriteEvent(EventIr raised, ExprScope scope)
        {
            unit.EmittedRules.Add(raised.Rule.Id);
            var lane = Text(LaneNumbers.LaneOfEvent(raised.Rule.Event));
            var word = Text(LaneNumbers.WordOfEvent(raised.Rule.Event));
            body.Blank();
            body.Block();
            body.Line("var state = Lanes.WordsOf(world, chunk, " + lane + ")[" + word + "];");
            body.Line("var enter = 0ul;");
            body.Line("var stay = 0ul;");
            body.Line("var rows = dirty;");
            body.Open("while (rows != 0ul)");
            body.Line("var row = Bits.Tzcnt(rows);");
            body.Line("rows &= rows - 1ul;");
            body.Line("enter |= " + Bool(raised.Enter, scope) + " ? 1ul << row : 0ul;");
            body.Line("stay |= " + Bool(raised.Stay, scope) + " ? 1ul << row : 0ul;");
            body.Close();
            body.Blank();
            body.Line("var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);");
            body.Line("var edge = state ^ next;");
            body.Line("edges[" + Text(raised.Index) + "] = edge;");
            body.Line("any |= edge;");
            body.Open("if (edge != 0ul)");
            body.Line("((ulong*)Lanes.Writable(world, ref writer, chunk, " + lane + "))[" + word + "] = next;");
            body.Close();
            body.Close();
        }

        void WriteEdgeEmits()
        {
            body.Blank();
            body.Open("public static void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)");
            body.Line("var totals = (long*)(scratch + " + Text(layers.DeltaIntrinsics.Count * Rows) + ");");
            var any = false;
            for (var i = 0; i < layers.Edges.Count; i++)
            {
                any |= layers.Edges[i].EventRule >= 0;
            }

            if (any)
            {
                body.Line("var v = View(world, chunk);");
                body.Line("Masks(world, ref v);");
                var scope = new ExprScope { Totals = "totals", FreshEvents = true, Capacity = "capacity" };
                for (var i = 0; i < layers.Edges.Count; i++)
                {
                    WriteEdge(layers.Edges[i], scope);
                }
            }

            body.Close();
        }

        void WriteEdge(EdgeIr edge, ExprScope scope)
        {
            unit.EmittedRules.Add(edge.Rule.Id);
            if (edge.EventRule < 0)
            {
                return;
            }

            var raised = edge.Rule.Event;
            var level = "Lanes.WordsOf(world, chunk, " + Text(LaneNumbers.LaneOfEvent(raised)) + ")[" + Text(LaneNumbers.WordOfEvent(raised)) + "]";
            body.Blank();
            body.Block();
            body.Line("var rows = edges[" + Text(edge.EventRule) + "] & " + (edge.Rule.Rising ? level : "~" + level) + ";");
            body.Open("while (rows != 0ul)");
            body.Line("var row = Bits.Tzcnt(rows);");
            body.Line("rows &= rows - 1ul;");
            body.Line("var self = 1ul << row;");
            body.Open("if (" + Condition(edge.When, scope, true) + ")");
            body.Line("var slot = (chunk << 6) | row;");
            body.Line("var kind = " + (kindLane < 0 ? "RuleMath.KindOf(0)" : "RuleMath.KindOf(" + Lane(kindLane) + "[row])") + ";");
            for (var a = 0; a < edge.Rule.Actions.Length; a++)
            {
                var action = edge.Rule.Actions[a];
                if (action.SameChannel)
                {
                    continue;
                }

                body.Block();
                body.Line("var amount = " + Names.Int(action.Constant) + ";");
                if (action.ScaleStat >= 0)
                {
                    body.Line("amount = StatLaw.ScaleBy(amount, " + Lane(action.ScaleStat) + "[row], " + Text(ScaleOfStat(action.ScaleStat)) + ");");
                }

                body.Line("var to = " + Destination(action, "slot", "EffectKey.NoSource") + ";");
                body.Open("if (amount != 0)");
                body.Open("if ((ulong)to < (ulong)capacity)");
                body.Line("next.Send(EffectKey.Pack((int)to, slot, (byte)kind, (byte)" + Text(action.Channel) + ", (Origin)" + Text((int)action.Origin) + ", 0), amount, 1);");
                body.Close();
                body.Close();
                body.Close();
            }

            body.Close();
            body.Close();
            body.Close();
        }

        void WriteHelpers()
        {
            for (var i = 0; i < sumSets.Count; i++)
            {
                var channels = sumSets[i];
                body.Blank();
                body.Open("static long Sum" + Text(i) + "(Effect* effects, Outcome* outcomes, int from, int to, int depthLimit)");
                body.Line("var total = 0L;");
                body.Open("for (var r = from; r < to; r++)");
                var covered = channels.Length == 0 ? string.Empty : " && " + InSet(channels, "(long)EffectKey.Channel(effects[r].Key)");
                body.Open("if (outcomes[r].Survives != 0 && EffectKey.Depth(effects[r].Key) <= depthLimit" + covered + ")");
                body.Line("total = RuleMath.Add(total, RuleMath.Consumed(effects[r], outcomes[r], " + Text((int)sumMeasures[i]) + "));");
                body.Close();
                body.Close();
                body.Blank();
                body.Line("return total;");
                body.Close();
            }

            for (var i = 0; i < sets.Count; i++)
            {
                var members = sets[i];
                body.Blank();
                var word = true;
                var mask = 0ul;
                for (var m = 0; m < members.Length; m++)
                {
                    word &= members[m] >= 0 && members[m] < 64;
                    mask |= word ? 1ul << (int)members[m] : 0ul;
                }

                if (word)
                {
                    body.Line("static bool In" + Text(i) + "(long value) => (ulong)value < 64ul && ((" + Names.Hex(mask) + " >> (int)value) & 1ul) != 0ul;");
                    continue;
                }

                body.Open("static bool In" + Text(i) + "(long value)");
                body.Open("switch (value)");
                for (var m = 0; m < members.Length; m++)
                {
                    body.Line("case " + Names.Long(members[m]) + ":");
                }

                body.Line("    return true;");
                body.Line("default:");
                body.Line("    return false;");
                body.Close();
                body.Close();
            }

            body.Blank();
            body.Open("public static ChunkView View(in WorldView world, int chunk)");
            body.Line("var v = default(ChunkView);");
            body.Line("v.Chunk = chunk;");
            foreach (var lane in viewLanes)
            {
                body.Line("v.L" + Text(lane) + " = Lanes.Ints(world, chunk, " + Text(lane) + ");");
            }

            foreach (var lane in viewEvents)
            {
                body.Line("v.E" + Text(lane) + " = Lanes.WordsOf(world, chunk, " + Text(lane) + ");");
            }

            body.Line("return v;");
            body.Close();
            body.Blank();
            body.Open("public static void Masks(in WorldView world, ref ChunkView v)");
            for (var g = 0; g < kernel.Guards.Count; g++)
            {
                var card = Text(kernel.Guards[g]);
                body.Line("v.C" + card + " = Mask.Positive(Lanes.Ints(world, v.Chunk, " + card + "));");
            }

            for (var m = 0; m < kernel.Masks.Count; m++)
            {
                var mask = MaskText(kernel.Masks[m]);
                var guard = kernel.MaskGuards[m];
                body.Line("v.M" + Text(m) + " = " + (kernel.MaskAlways[m] || guard < 0 ? mask : "v.C" + Text(guard) + " != 0ul ? " + mask + " : 0ul") + ";");
            }

            body.Close();
        }

        static string MaskText(RowMask mask)
        {
            var lane = mask.Lane.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var value = Names.Int(mask.Value);
            switch (mask.Op)
            {
                case MaskOp.Positive: return "Mask.Positive(Lanes.Ints(world, v.Chunk, " + lane + "))";
                case MaskOp.NonZero: return "Mask.NonZero(Lanes.Ints(world, v.Chunk, " + lane + "))";
                case MaskOp.Zero: return "~Mask.NonZero(Lanes.Ints(world, v.Chunk, " + lane + "))";
                case MaskOp.EqualTo: return "Mask.EqualTo(Lanes.Ints(world, v.Chunk, " + lane + "), " + value + ")";
                case MaskOp.NotEqualTo: return "~Mask.EqualTo(Lanes.Ints(world, v.Chunk, " + lane + "), " + value + ")";
                case MaskOp.Level: return "Lanes.WordsOf(world, v.Chunk, " + lane + ")[" + value + "]";
                default: return "~Lanes.WordsOf(world, v.Chunk, " + lane + ")[" + value + "]";
            }
        }
    }
}
