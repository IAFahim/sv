using System.Collections.Generic;
using Sv.Rules.Passes;

namespace Sv.Rules.Emit
{
    public sealed partial class KernelEmitter
    {
        public const string FileName = "Kernels.g.cs";
        public const int Rows = 64;

        readonly CompileUnit unit;
        readonly PackSchema schema;
        readonly LayerIr layers;
        readonly KernelIr kernel;
        readonly CodeWriter body = new CodeWriter();
        readonly int kindLane;

        KernelEmitter(CompileUnit unit)
        {
            this.unit = unit;
            schema = unit.Schema;
            layers = unit.Layers;
            kernel = unit.Kernel;
            kindLane = schema.KindIntrinsic;
        }

        public static string Emit(CompileUnit unit)
        {
            var emitter = new KernelEmitter(unit);
            return emitter.Write();
        }

        int ShapeCapacity => kernel.ShapeCapacity < 1 ? 1 : kernel.ShapeCapacity;

        int ChannelIndex(int channel) => schema.IndexOf(ValueClass.Channel, channel);

        string Held(int guard) => guard < 0 ? "true" : "(v.C" + Text(guard) + " & self) != 0ul";

        string Write()
        {
            WriteCounts();
            WriteDecide();
            WriteTarget();
            WriteGate();
            WriteDispatch(kernel.Admit, kernel.AdmitAny, "Admit");
            WriteSelfAdmit();
            WriteDispatch(kernel.SelfAdmit, kernel.SelfAdmitAny, "SelfAdmit");
            for (var i = 0; i < layers.Emits.Count; i++)
            {
                if (layers.Emits[i].HasOther)
                {
                    WriteEmit(layers.Emits[i], false);
                }

                if (layers.Emits[i].HasSelf)
                {
                    WriteEmit(layers.Emits[i], true);
                }
            }

            WriteHostSurface();
            WriteEvents();
            WriteEdgeEmits();
            WriteHelpers();
            var file = new CodeWriter();
            file.Line("using Sv;");
            file.Line("using System.Runtime.InteropServices;");
            file.Blank();
            file.Open("namespace " + unit.Options.Namespace);
            WriteSupport(file);
            file.Blank();
            file.Open("public static unsafe class Kernels");
            var text = file.ToString() + Indent(body.ToString(), 2) + "    }\n}\n";
            return text;
        }

        static string Indent(string text, int depth)
        {
            var pad = new string(' ', depth * 4);
            var lines = text.Split('\n');
            var result = new System.Text.StringBuilder(text.Length + lines.Length * pad.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == lines.Length - 1 && lines[i].Length == 0)
                {
                    break;
                }

                result.Append(lines[i].Length == 0 ? string.Empty : pad).Append(lines[i]).Append('\n');
            }

            return result.ToString();
        }

        void WriteCounts()
        {
            var eventLanes = new SortedSet<int>();
            for (var i = 0; i < layers.Events.Count; i++)
            {
                eventLanes.Add(LaneNumbers.LaneOfEvent(layers.Events[i].Rule.Event));
            }

            body.Line("public static readonly int Channels = " + Text(schema.Channels.Length) + ";");
            body.Blank();
            body.Line("public static readonly int DeltaLanes = " + Text(layers.DeltaIntrinsics.Count) + ";");
            body.Blank();
            body.Line("public static readonly int ScratchLanes = " + Text(layers.DeltaIntrinsics.Count + 2 * schema.Channels.Length) + ";");
            body.Blank();
            body.Line("public static readonly int RecordBytes = sizeof(Outcome) + sizeof(int);");
            body.Blank();
            body.Line("public static readonly int EventCount = " + Text(layers.Events.Count) + ";");
            body.Blank();
            body.Line("public static readonly int LanesWrittenPerChunk = " + Text(layers.Folds.Count + eventLanes.Count) + ";");
            body.Blank();
            body.Open("public static int ChannelIndex(int channel)");
            body.Open("switch (channel)");
            for (var c = 0; c < schema.Channels.Length; c++)
            {
                body.Line("case " + Text(schema.Channels[c].Id) + ": return " + Text(c) + ";");
            }

            body.Line("default: return -1;");
            body.Close();
            body.Close();
            body.Blank();
            body.Open("public static int DeltaSlotOf(int intrinsic)");
            body.Open("switch (intrinsic)");
            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                body.Line("case " + Text(layers.DeltaIntrinsics[s]) + ": return " + Text(s) + ";");
            }

            body.Line("default: return -1;");
            body.Close();
            body.Close();
        }

        void WriteDecide()
        {
            body.Blank();
            body.Open("public static ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, byte* records, ref EffectWriter next)");
            body.Line("var v = View(world, chunk);");
            body.Line("Masks(world, ref v);");
            body.Line("var a = new DecideArgs { Effects = effects, Outcomes = (Outcome*)records, Order = (int*)(records + (long)count * sizeof(Outcome)), Totals = (long*)(scratch + " + Text(layers.DeltaIntrinsics.Count * Rows) + "), Delta = scratch, Capacity = capacity };");
            body.Line("var terms = stackalloc ShapeTerm[" + Text(ShapeCapacity) + "];");
            body.Line("var totalled = 0ul;");
            body.Line("var at = 0;");
            body.Open("while (at < count)");
            body.Line("var first = at;");
            body.Line("var target = EffectKey.Target(effects[at].Key);");
            body.Line("at++;");
            body.Open("while (at < count && EffectKey.Target(effects[at].Key) == target)");
            body.Line("at++;");
            body.Close();
            body.Blank();
            body.Line("var row = target & 63;");
            body.Line("var self = 1ul << row;");
            body.Line("Target(world, ref v, a, ref next, target, row, self, first, at, terms);");
            body.Open("for (var index = 0; index < " + Text(schema.Channels.Length) + "; index++)");
            body.Line("totalled |= a.Totals[(index << 6) + row] != 0L ? self : 0ul;");
            body.Close();
            body.Close();
            body.Blank();
            body.Line("return totalled;");
            body.Close();
        }

        ExprScope RecordScope(string? channel) => new ExprScope { Effect = "effect", Source = "EffectKey.Source(effect.Key)", Channel = channel };

        void WriteTarget()
        {
            body.Blank();
            body.Open("static void Target(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)");
            body.Open("for (var r = first; r < end; r++)");
            body.Line("Gate(world, ref v, a, row, self, r, terms);");
            body.Close();
            for (var b = 0; b < layers.AbsorbBudgets.Count; b++)
            {
                body.Blank();
                body.Line("var used" + Text(layers.AbsorbBudgets[b]) + " = 0L;");
            }

            for (var g = 0; g < layers.Gates.Count; g++)
            {
                var gate = layers.Gates[g];
                for (var e = 0; e < gate.Rule.Absorbs.Length; e++)
                {
                    WriteAbsorb(gate, gate.Rule.Absorbs[e]);
                }
            }

            body.Blank();
            body.Line("var untaken = -1;");
            for (var i = 0; i < layers.Emits.Count; i++)
            {
                var emit = layers.Emits[i];
                if (!emit.WantsUntaken)
                {
                    continue;
                }

                var limit = Text(emit.Rule.DepthLimit);
                body.Line("untaken = " + Held(emit.When.Guard) + " && untaken < " + limit + " ? " + limit + " : untaken;");
            }

            body.Open("for (var r = first; r < end; r++)");
            body.Line("var survives = RuleMath.Taken(a.Outcomes[r]) != 0L || EffectKey.Depth(a.Effects[r].Key) <= untaken;");
            body.Line("a.Outcomes[r].Survives = survives ? 1 : 0;");
            body.Close();
            for (var i = 0; i < layers.Emits.Count; i++)
            {
                var emit = layers.Emits[i];
                if (!emit.HasOther)
                {
                    continue;
                }

                body.Blank();
                body.Open("if (" + Held(emit.When.Guard) + ")");
                body.Line(EmitMethod(emit, false) + "(world, ref v, a, ref next, target, row, self, first, end, terms);");
                body.Close();
            }

            body.Blank();
            body.Open("for (var r = first; r < end; r++)");
            body.Open("if (a.Outcomes[r].Survives != 0)");
            body.Line("var index = ChannelIndex(EffectKey.Channel(a.Effects[r].Key));");
            body.Open("if (index >= 0)");
            body.Line("var at = (index << 6) + row;");
            body.Line("a.Totals[at] = RuleMath.Add(a.Totals[at], RuleMath.Taken(a.Outcomes[r]));");
            body.Close();
            body.Close();
            body.Close();
            for (var i = 0; i < layers.SelfOrder.Count; i++)
            {
                var emit = layers.SelfOrder[i];
                body.Blank();
                body.Open("if (" + Held(emit.When.Guard) + ")");
                body.Line(EmitMethod(emit, true) + "(world, ref v, a, ref next, target, row, self, first, end, terms);");
                body.Close();
            }

            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                body.Blank();
                body.Line("var delta" + Text(s) + " = 0L;");
            }

            var flowScope = new ExprScope { Totals = "a.Totals" };
            for (var f = 0; f < layers.Flows.Count; f++)
            {
                var flow = layers.Flows[f];
                unit.EmittedRules.Add(flow.Rule.Id);
                body.Blank();
                body.Open("if (" + Condition(flow.When, flowScope, true) + ")");
                for (var t = 0; t < flow.Rule.Terms.Length; t++)
                {
                    var term = flow.Rule.Terms[t];
                    var slot = Text(layers.DeltaIntrinsics.IndexOf(term.Intrinsic));
                    var from = ChannelIndex(term.Channel);
                    body.Line("delta" + slot + " = RuleMath.Add(delta" + slot + ", " + (term.Sign > 0 ? "" : "RuleMath.Negate(") + "a.Totals[" + Text(from * Rows) + " + row]" + (term.Sign > 0 ? "" : ")") + ");");
                }

                body.Close();
            }

            for (var c = 0; c < schema.Channels.Length; c++)
            {
                var into = schema.Channels[c].Into;
                var slot = into >= 0 ? layers.DeltaIntrinsics.IndexOf(into) : -1;
                if (slot < 0)
                {
                    continue;
                }

                body.Line("delta" + Text(slot) + " = RuleMath.Add(delta" + Text(slot) + ", a.Totals[" + Text(c * Rows) + " + row]);");
            }

            for (var b = 0; b < layers.AbsorbBudgets.Count; b++)
            {
                var budget = layers.AbsorbBudgets[b];
                var slot = Text(layers.DeltaIntrinsics.IndexOf(budget));
                body.Line("delta" + slot + " = RuleMath.Add(delta" + slot + ", -used" + Text(budget) + ");");
            }

            for (var s = 0; s < layers.DeltaIntrinsics.Count; s++)
            {
                body.Line("a.Delta[" + Text(s * Rows) + " + row] = EffectLaw.Saturate32(delta" + Text(s) + ");");
            }

            body.Close();
        }

        void WriteAbsorb(GateIr gate, AbsorbEffect effect)
        {
            var budget = Text(effect.Budget);
            unit.EmittedRules.Add(gate.Rule.Id);
            body.Blank();
            body.Open("if (" + Held(gate.When.Guard) + ")");
            body.Line("var candidates = 0;");
            body.Open("for (var r = first; r < end; r++)");
            body.Line("var effect = a.Effects[r];");
            body.Open("if (a.Outcomes[r].Admitted != 0 && " + Condition(gate.When, RecordScope(null), false) + ")");
            body.Line("a.Order[candidates] = r;");
            body.Line("candidates++;");
            body.Close();
            body.Close();
            if (effect.Order == AbsorbOrder.ByAmountDescending)
            {
                body.Blank();
                body.Open("for (var k = 1; k < candidates; k++)");
                body.Line("var at = k;");
                body.Open("while (at > 0 && RuleMath.Gross(a.Outcomes[a.Order[at - 1]]) < RuleMath.Gross(a.Outcomes[a.Order[at]]))");
                body.Line("var swapped = a.Order[at - 1];");
                body.Line("a.Order[at - 1] = a.Order[at];");
                body.Line("a.Order[at] = swapped;");
                body.Line("at--;");
                body.Close();
                body.Close();
            }

            body.Blank();
            body.Line("var budget = (long)" + Lane(effect.Budget) + "[row];");
            body.Open("for (var k = 0; k < candidates; k++)");
            body.Line("var remaining = (budget < 0L ? 0L : budget) - used" + budget + ";");
            body.Open("if (remaining <= 0L)");
            body.Line("break;");
            body.Close();
            body.Blank();
            body.Line("var outcome = a.Outcomes + a.Order[k];");
            if (effect.Per == AbsorbPer.Hit)
            {
                body.Line("var take = remaining < outcome->TakenHits ? remaining : outcome->TakenHits;");
                body.Line("outcome->TakenHits -= (int)take;");
                body.Line("used" + budget + " += take;");
            }
            else
            {
                body.Line("var available = RuleMath.Gross(*outcome) - outcome->Absorbed;");
                body.Open("if (available <= 0L)");
                body.Line("continue;");
                body.Close();
                body.Blank();
                body.Line("var take = remaining < available ? remaining : available;");
                body.Line("outcome->Absorbed += take;");
                body.Line("used" + budget + " += take;");
            }

            body.Close();
            body.Close();
        }

        void WriteGate()
        {
            body.Blank();
            body.Open("static void Gate(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, int r, ShapeTerm* terms)");
            body.Line("var effect = a.Effects[r];");
            body.Line("var state = default(GateState);");
            body.Line("var channel = (int)EffectKey.Channel(effect.Key);");
            WriteSwitch(kernel.Admit, kernel.AdmitAny, "effect");
            body.Line("var admitted = state.Decided == 0 || state.Admit != 0;");
            body.Line("var taken = 0;");
            body.Open("if (admitted)");
            body.Line("taken = effect.Amount;");
            body.Open("if (state.Terms > 0)");
            body.Line("StatLaw.Canonicalize(terms, state.Terms);");
            body.Line("taken = StatLaw.Fold(effect.Amount, terms, state.Terms);");
            body.Close();
            body.Close();
            body.Blank();
            body.Line("var outcome = a.Outcomes + r;");
            body.Line("outcome->Admitted = admitted ? 1 : 0;");
            body.Line("outcome->TakenPerHit = taken;");
            body.Line("outcome->TakenHits = effect.Hits;");
            body.Line("outcome->Absorbed = 0L;");
            body.Line("outcome->Survives = 0;");
            body.Close();
        }

        void WriteSwitch(List<GateChannel> channels, GateChannel any, string effect)
        {
            body.Open("switch (channel)");
            for (var c = 0; c < channels.Count; c++)
            {
                var dispatch = channels[c];
                if (dispatch.Blocks.Count == 0)
                {
                    body.Line("case " + Text(dispatch.Channel) + ": break;");
                    continue;
                }

                body.Line("case " + Text(dispatch.Channel) + ": " + dispatch.Method + "(world, ref v, a, row, self, " + effect + ", channel, ref state, terms); break;");
            }

            body.Line(any.Blocks.Count == 0 ? "default: break;" : "default: " + any.Method + "(world, ref v, a, row, self, " + effect + ", channel, ref state, terms); break;");
            body.Close();
        }

        static bool HasBlocks(List<GateChannel> channels, GateChannel any)
        {
            for (var c = 0; c < channels.Count; c++)
            {
                if (channels[c].Blocks.Count != 0)
                {
                    return true;
                }
            }

            return any.Blocks.Count != 0;
        }

        void WriteSelfAdmit()
        {
            body.Blank();
            body.Open("static int SelfAdmit(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, int channel, int amount, ShapeTerm* terms)");
            body.Line("var state = default(GateState);");
            if (HasBlocks(kernel.SelfAdmit, kernel.SelfAdmitAny))
            {
                body.Line("var effect = default(Effect);");
            }

            WriteSwitch(kernel.SelfAdmit, kernel.SelfAdmitAny, "effect");
            body.Open("if (state.Decided != 0 && state.Admit == 0)");
            body.Line("return 0;");
            body.Close();
            body.Blank();
            body.Open("if (state.Terms == 0)");
            body.Line("return amount;");
            body.Close();
            body.Blank();
            body.Line("StatLaw.Canonicalize(terms, state.Terms);");
            body.Line("return StatLaw.Fold(amount, terms, state.Terms);");
            body.Close();
        }

        void WriteDispatch(List<GateChannel> channels, GateChannel any, string prefix)
        {
            var self = prefix == "SelfAdmit";
            for (var c = 0; c < channels.Count; c++)
            {
                WriteChannel(channels[c], self);
            }

            WriteChannel(any, self);
        }

        void WriteChannel(GateChannel dispatch, bool selfEmit)
        {
            if (dispatch.Blocks.Count == 0)
            {
                return;
            }

            const string parameters = "(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)";
            body.Blank();
            body.Open("static void " + dispatch.Method + parameters);
            for (var b = 0; b < dispatch.Blocks.Count; b++)
            {
                var block = dispatch.Blocks[b];
                var call = block.Method + "(world, ref v, a, row, self, effect, channel, ref state, terms);";
                if (block.Guard < 0)
                {
                    body.Line(call);
                }
                else
                {
                    body.Open("if (" + Held(block.Guard) + ")");
                    body.Line(call);
                    body.Close();
                }
            }

            body.Close();
            var scope = selfEmit
                ? new ExprScope { Channel = "channel" }
                : RecordScope(dispatch.Channel < 0 ? null : "channel");
            for (var b = 0; b < dispatch.Blocks.Count; b++)
            {
                var block = dispatch.Blocks[b];
                body.Blank();
                body.Open("static void " + block.Method + parameters);
                for (var i = 0; i < block.Cases.Count; i++)
                {
                    WriteCase(block.Cases[i], scope, i > 0);
                }

                body.Close();
            }
        }

        void WriteCase(GateCase gateCase, ExprScope scope, bool spaced)
        {
            var gate = gateCase.Gate;
            unit.EmittedRules.Add(gate.Rule.Id);
            var condition = Condition(gateCase.When, scope, false);
            if (spaced)
            {
                body.Blank();
            }

            if (!gate.Shapes)
            {
                body.Open("if (state.Decided == 0 && " + condition + ")");
                body.Line("state.Decided = 1;");
                body.Line("state.Admit = " + (gate.Rule.Admit == AdmitVerdict.Allow ? "1" : "0") + ";");
                body.Close();
                return;
            }

            body.Open("if (" + condition + ")");
            if (gate.Decides)
            {
                body.Open("if (state.Decided == 0)");
                body.Line("state.Decided = 1;");
                body.Line("state.Admit = " + (gate.Rule.Admit == AdmitVerdict.Allow ? "1" : "0") + ";");
                body.Close();
            }

            var grouped = gate.GroupBit >= 0;
            if (grouped)
            {
                var bit = "(1ul << " + Text(gate.GroupBit) + ")";
                body.Open("if ((state.Groups & " + bit + ") == 0ul)");
                body.Line("state.Groups |= " + bit + ";");
            }

            for (var s = 0; s < gate.Rule.Shapes.Length; s++)
            {
                var shape = gate.Rule.Shapes[s];
                body.Line("terms[state.Terms] = new ShapeTerm(ShapeKind." + shape.Kind.ToString() + ", " + Names.Int(shape.Value) + ");");
                body.Line("state.Terms++;");
            }

            if (grouped)
            {
                body.Close();
            }

            body.Close();
        }

        static string EmitMethod(EmitIr emit, bool self) => (self ? "SelfRule" : "EmitRule") + Text(emit.Rule.Id);

        void WriteEmit(EmitIr emit, bool selfPart)
        {
            unit.EmittedRules.Add(emit.Rule.Id);
            var rule = emit.Rule;
            var limit = Text(rule.DepthLimit);
            body.Blank();
            body.Open("static void " + EmitMethod(emit, selfPart) + "(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)");
            if (!selfPart)
            {
                body.Line("var kind = " + (kindLane < 0 ? "RuleMath.KindOf(0)" : "RuleMath.KindOf(" + Lane(kindLane) + "[row])") + ";");
            }

            if (rule.Per == EmitPer.Frame)
            {
                body.Line("var depth = -1;");
                body.Open("for (var r = first; r < end; r++)");
                body.Line("var at = EffectKey.Depth(a.Effects[r].Key);");
                body.Line("depth = a.Outcomes[r].Survives != 0 && at <= " + limit + " && at > depth ? at : depth;");
                body.Close();
                body.Blank();
                body.Open("if (depth < 0)");
                body.Line("return;");
                body.Close();
                body.Blank();
                var scope = new ExprScope { Source = "EffectKey.NoSource", GroupFrom = "first", GroupTo = "end", GroupLimit = limit, FrameFrom = "first", FrameTo = "end", Totals = selfPart ? "a.Totals" : null };
                body.Open("if (" + Condition(emit.When, scope, false) + ")");
                WriteActions(emit, selfPart, scope, false, "first", "end");
                body.Close();
                body.Close();
                return;
            }

            body.Line("var groupFrom = first;");
            body.Open("while (groupFrom < end)");
            body.Line("var source = EffectKey.Source(a.Effects[groupFrom].Key);");
            body.Line("var groupTo = groupFrom + 1;");
            body.Open("while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)");
            body.Line("groupTo++;");
            body.Close();
            body.Blank();
            if (rule.Per == EmitPer.Source)
            {
                body.Line("var depth = -1;");
                body.Open("for (var r = groupFrom; r < groupTo; r++)");
                body.Line("var at = EffectKey.Depth(a.Effects[r].Key);");
                body.Line("depth = a.Outcomes[r].Survives != 0 && at <= " + limit + " && at > depth ? at : depth;");
                body.Close();
                body.Blank();
                var scope = new ExprScope { Source = "source", GroupFrom = "groupFrom", GroupTo = "groupTo", GroupLimit = limit, FrameFrom = "first", FrameTo = "end", Totals = selfPart ? "a.Totals" : null };
                body.Open("if (depth >= 0 && " + Condition(emit.When, scope, false) + ")");
                WriteActions(emit, selfPart, scope, false, "groupFrom", "groupTo");
                body.Close();
            }
            else
            {
                var scope = new ExprScope
                {
                    Effect = "effect",
                    Outcome = "outcome",
                    Source = "source",
                    GroupFrom = "groupFrom",
                    GroupTo = "groupTo",
                    GroupLimit = limit,
                    FrameFrom = "first",
                    FrameTo = "end",
                    Totals = selfPart ? "a.Totals" : null,
                };
                body.Open("for (var r = groupFrom; r < groupTo; r++)");
                body.Line("var effect = a.Effects[r];");
                body.Line("var outcome = a.Outcomes[r];");
                body.Line("var depth = EffectKey.Depth(effect.Key);");
                body.Open("if (outcome.Survives != 0 && depth <= " + limit + " && " + Condition(emit.When, scope, false) + ")");
                WriteActions(emit, selfPart, scope, true, "groupFrom", "groupTo");
                body.Close();
                body.Close();
            }

            body.Blank();
            body.Line("groupFrom = groupTo;");
            body.Close();
            body.Close();
        }

        void WriteActions(EmitIr emit, bool selfPart, ExprScope scope, bool perRecord, string from, string to)
        {
            var rule = emit.Rule;
            for (var i = 0; i < rule.Actions.Length; i++)
            {
                var action = rule.Actions[i];
                if (rule.IsSameFrameSelf(action) != selfPart)
                {
                    continue;
                }

                var covered = perRecord && action.Over.Length > 0 ? InSet(action.Over, "(long)EffectKey.Channel(effect.Key)") : "true";
                body.Open("if (" + covered + ")");
                WriteAction(rule, action, selfPart, scope, perRecord, from, to);
                body.Close();
            }
        }

        void WriteAction(EmitRule rule, EmitAction action, bool selfPart, ExprScope scope, bool perRecord, string from, string to)
        {
            body.Line("var channel = " + (action.SameChannel ? "(int)EffectKey.Channel(effect.Key)" : Text(action.Channel)) + ";");
            body.Line("var amount = 0;");
            body.Line("var hits = 1;");
            if (action.Amount == AmountKind.Constant)
            {
                body.Line("amount = " + Names.Int(action.Constant) + ";");
                if (rule.Per == EmitPer.Hit && perRecord)
                {
                    body.Line("hits = effect.Hits;");
                }
            }
            else
            {
                var measure = action.Amount == AmountKind.Offered ? Measure.Offered : action.Amount == AmountKind.Taken ? Measure.Taken : Measure.NotTaken;
                if (perRecord)
                {
                    body.Line("RuleMath.PerHit(effect, outcome, " + Text((int)measure) + ", out amount, out hits);");
                }
                else
                {
                    body.Line("amount = EffectLaw.Saturate32(" + Sum(measure, action.Over, from, to, Text(rule.DepthLimit), scope) + ");");
                }
            }

            if (action.ScaleStat >= 0)
            {
                body.Line("amount = StatLaw.ScaleBy(amount, " + Lane(action.ScaleStat) + "[row], " + Text(ScaleOfStat(action.ScaleStat)) + ");");
            }

            body.Open("if (amount != 0 && hits != 0)");
            if (selfPart)
            {
                body.Line("var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);");
                body.Line("var index = ChannelIndex(channel);");
                body.Open("if (index >= 0)");
                body.Line("var at = (index << 6) + row;");
                body.Line("a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));");
                body.Close();
            }
            else
            {
                body.Line("var to = " + Destination(action, "target", "source") + ";");
                body.Open("if ((ulong)to < (ulong)a.Capacity)");
                body.Line("next.Send(EffectKey.Pack((int)to, target, (byte)kind, (byte)channel, (Origin)" + Text((int)action.Origin) + ", depth + 1), amount, hits);");
                body.Close();
            }

            body.Close();
        }

        string Destination(EmitAction action, string self, string source)
        {
            switch (action.To)
            {
                case EmitTo.Self: return "(long)" + self;
                case EmitTo.Source: return "(" + source + " == EffectKey.NoSource ? -1L : (long)" + source + ")";
                default: return "(long)" + Lane(action.Via) + "[row]";
            }
        }
    }
}
