using System;
using System.Globalization;

namespace Sv.Rules
{
    internal sealed class ExprFacts
    {
        public uint RecordFields;
        public bool ReadsAggregate;
        public bool ReadsOfferedAggregate;
        public bool ReadsSourceRow;
        public bool ReadsOtherRow;
        public readonly Rows<int> Totals = new Rows<int>();

        public bool ReadsRecord => RecordFields != 0;

        public bool Reads(RecordField field) => (RecordFields & (1u << (int)field)) != 0;

        public bool ReadsTotals => Totals.Count > 0;

        public bool ReadsUntaken => Reads(RecordField.Amount) || Reads(RecordField.NotTaken) || ReadsOfferedAggregate;

        public bool ReadsOnlyChannelOfRecord => (RecordFields & ~(1u << (int)RecordField.Channel)) == 0 && !ReadsSourceRow && !ReadsAggregate;

        public bool TotalsMeet(long[] written, bool writesAll)
        {
            for (var i = 0; i < Totals.Count; i++)
            {
                if (writesAll || Array.BinarySearch(written, (long)Totals[i]) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PackPlan
    {
        public ContentPack Pack { get; }

        public ulong Hash { get; }

        public GateRule[] GateOrder { get; }

        public bool[] GateIsChannelState { get; }

        public bool[] EmitWantsUntaken { get; }

        public int[] EmitStratum { get; }

        public EmitRule[] SelfOrder { get; }

        public Diagnostic[] Warnings { get; }

        public PackPlan(ContentPack pack, ulong hash, GateRule[] gateOrder, bool[] gateIsChannelState, bool[] emitWantsUntaken, int[] emitStratum, EmitRule[] selfOrder, Diagnostic[] warnings)
        {
            Pack = pack;
            Hash = hash;
            GateOrder = gateOrder;
            GateIsChannelState = gateIsChannelState;
            EmitWantsUntaken = emitWantsUntaken;
            EmitStratum = emitStratum;
            SelfOrder = selfOrder;
            Warnings = warnings;
        }

        public PackSchema Schema => Pack.Schema;

        public FoldRule? FoldOf(int intrinsic)
        {
            var folds = Pack.Folds;
            for (var i = 0; i < folds.Length; i++)
            {
                if (folds[i].Intrinsic == intrinsic)
                {
                    return folds[i];
                }
            }

            return null;
        }

        public EventRule? EventRuleOf(int raised)
        {
            var events = Pack.Events;
            for (var i = 0; i < events.Length; i++)
            {
                if (events[i].Event == raised)
                {
                    return events[i];
                }
            }

            return null;
        }

        public int EmitIndex(EmitRule rule)
        {
            var emits = Pack.Emits;
            for (var i = 0; i < emits.Length; i++)
            {
                if (ReferenceEquals(emits[i], rule))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public static class PackCompiler
    {
        public static Checked<PackPlan> Plan(ContentPack pack)
        {
            var check = new PackCheck(pack);
            var plan = check.Run();
            var diagnostics = check.Diagnostics;
            return new Checked<PackPlan>(diagnostics.Errors == 0 ? plan : null, diagnostics.ToArray());
        }

        public static Checked<PackPlan> Compile(in PackSource source)
        {
            var read = PackReader.Read(source);
            if (!read.Ok || read.Value == null)
            {
                return new Checked<PackPlan>(null, read.Diagnostics);
            }

            var planned = Plan(read.Value);
            var merged = new Diagnostic[read.Diagnostics.Length + planned.Diagnostics.Length];
            Array.Copy(read.Diagnostics, merged, read.Diagnostics.Length);
            Array.Copy(planned.Diagnostics, 0, merged, read.Diagnostics.Length, planned.Diagnostics.Length);
            return new Checked<PackPlan>(planned.Value, merged);
        }

        public static Diagnostic[] Evolution(PackSchema previous, PackSchema next)
        {
            var diagnostics = new DiagnosticList();
            const string file = PackSource.ValuesFile;
            if (next.NextLane < previous.NextLane)
            {
                diagnostics.Error(DiagnosticCode.LaneCounter, file, 0, -1, -1, "nextLane went down from " + previous.NextLane.ToString(CultureInfo.InvariantCulture) + " to " + next.NextLane.ToString(CultureInfo.InvariantCulture));
            }

            for (var c = ValueClass.Stat; c <= ValueClass.Kind; c++)
            {
                var before = previous.Of(c);
                var after = next.Of(c);
                for (var i = 0; i < after.Length; i++)
                {
                    var row = after[i];
                    var sameName = previous.FindByName(c, row.Name);
                    if (sameName != null && sameName.Id != row.Id)
                    {
                        diagnostics.Error(DiagnosticCode.LaneMoved, file, row.Offset, -1, -1, row.Name + " moved from " + sameName.Id.ToString(CultureInfo.InvariantCulture) + " to " + row.Id.ToString(CultureInfo.InvariantCulture) + "; a number never changes once given");
                        continue;
                    }

                    if (sameName != null)
                    {
                        continue;
                    }

                    var sameId = previous.Find(c, row.Id);
                    var retiredLane = (c == ValueClass.Stat || c == ValueClass.Intrinsic || c == ValueClass.Event) && row.Lane < previous.NextLane && !LaneUsed(previous, row.Lane, c == ValueClass.Event);
                    if (sameId != null || retiredLane)
                    {
                        diagnostics.Error(DiagnosticCode.LaneReused, file, row.Offset, -1, -1, row.Name + " takes number " + row.Id.ToString(CultureInfo.InvariantCulture) + " which was " + (sameId == null ? "retired" : sameId.Name) + "; new values take numbers from nextLane");
                    }
                }

                for (var i = 0; i < before.Length; i++)
                {
                    if (next.FindByName(c, before[i].Name) == null && next.Find(c, before[i].Id) != null)
                    {
                        diagnostics.Error(DiagnosticCode.LaneReused, file, 0, -1, -1, "the number of removed value " + before[i].Name + " was given to another value");
                    }
                }
            }

            return diagnostics.ToArray();
        }

        static bool LaneUsed(PackSchema schema, int lane, bool forEvent)
        {
            if (forEvent)
            {
                for (var i = 0; i < schema.Events.Length; i++)
                {
                    if (schema.Events[i].Lane == lane)
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }
    }

    internal sealed class PackCheck
    {
        public readonly DiagnosticList Diagnostics = new DiagnosticList();

        readonly ContentPack pack;
        readonly PackSchema schema;

        public PackCheck(ContentPack pack)
        {
            this.pack = pack;
            schema = pack.Schema;
        }

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        static string Named(Rule rule) => rule.Name + " (" + Text(rule.Id) + ")";

        void Error(DiagnosticCode code, Rule rule, Rule? other, string message) =>
            Diagnostics.Error(code, PackSource.RulesFile, rule.Offset, rule.Id, other == null ? -1 : other.Id, message);

        void Warning(DiagnosticCode code, Rule rule, Rule? other, string message) =>
            Diagnostics.Warning(code, PackSource.RulesFile, rule.Offset, rule.Id, other == null ? -1 : other.Id, message);

        public PackPlan? Run()
        {
            CheckLimits();
            CheckChainCycles();
            for (var i = 0; i < pack.Chains.Length; i++)
            {
                CheckExpression(pack.Chains[i], pack.Chains[i].When, true);
            }

            var gateOrder = OrderGates();
            var channelState = new bool[gateOrder.Length];
            for (var i = 0; i < gateOrder.Length; i++)
            {
                channelState[i] = CheckGate(gateOrder[i]);
            }

            CheckAdmitTies(gateOrder);
            var wants = new bool[pack.Emits.Length];
            for (var i = 0; i < pack.Emits.Length; i++)
            {
                wants[i] = CheckEmit(pack.Emits[i]);
            }

            for (var i = 0; i < pack.Edges.Length; i++)
            {
                CheckEdge(pack.Edges[i]);
            }

            for (var i = 0; i < pack.Flows.Length; i++)
            {
                CheckFlow(pack.Flows[i]);
            }

            CheckFolds();
            CheckEvents();
            var stratum = Stratify(out var selfOrder);
            var warnings = new Rows<Diagnostic>();
            for (var i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity == Severity.Warning)
                {
                    warnings.Add(Diagnostics[i]);
                }
            }

            if (Diagnostics.Errors > 0)
            {
                return null;
            }

            return new PackPlan(pack, PackHash.Of(pack), gateOrder, channelState, wants, stratum, selfOrder, warnings.ToArray());
        }

        void CheckLimits()
        {
            if (schema.Channels.Length > LaneNumbers.MaxChannels)
            {
                Diagnostics.Error(DiagnosticCode.TooManyChannels, PackSource.ValuesFile, 0, -1, -1, "more than 256 channels");
            }

            if (schema.Kinds.Length > LaneNumbers.MaxKinds)
            {
                Diagnostics.Error(DiagnosticCode.TooManyKinds, PackSource.ValuesFile, 0, -1, -1, "more than 256 kinds");
            }
        }

        bool IntrinsicExists(int id) => schema.IndexOf(ValueClass.Intrinsic, id) >= 0;

        bool Holder(int id)
        {
            var found = schema.Find(ValueClass.Intrinsic, id);
            return found != null && (found.Role == IntrinsicRole.Owner || found.Role == IntrinsicRole.Reference);
        }

        void CheckCard(Rule rule)
        {
            if (rule.Card >= 0 && !IntrinsicExists(rule.Card))
            {
                Error(DiagnosticCode.UnknownName, rule, null, Named(rule) + " is guarded by a card that is no intrinsic");
            }
        }

        public ExprFacts Facts(Expression e)
        {
            var facts = new ExprFacts();
            Collect(e, facts, 0);
            return facts;
        }

        void Collect(Expression e, ExprFacts facts, int depth)
        {
            if (depth > 256)
            {
                return;
            }

            switch (e.Op)
            {
                case Op.RecordField:
                    facts.RecordFields |= 1u << (int)e.Field;
                    break;
                case Op.Cell:
                    facts.ReadsSourceRow |= e.Subject == Subject.Source;
                    facts.ReadsOtherRow |= e.Subject != Subject.Self;
                    break;
                case Op.ChannelTotal:
                    facts.Totals.Add(e.Id);
                    break;
                case Op.Aggregate:
                    facts.ReadsAggregate = true;
                    facts.ReadsOfferedAggregate |= e.Measure != Measure.Taken;
                    break;
                case Op.Chain:
                {
                    var chain = pack.Chain(e.Id);
                    if (chain != null)
                    {
                        Collect(chain.When, facts, depth + 1);
                    }

                    break;
                }

                case Op.AnyChain:
                    for (var i = 0; i < pack.Chains.Length; i++)
                    {
                        if (Array.IndexOf(pack.Chains[i].Tags, e.Id) >= 0)
                        {
                            Collect(pack.Chains[i].When, facts, depth + 1);
                        }
                    }

                    break;
            }

            for (var i = 0; i < e.Args.Length; i++)
            {
                Collect(e.Args[i], facts, depth + 1);
            }
        }

        void CheckExpression(Rule rule, Expression e, bool wantBool)
        {
            if (wantBool && e.Type != ExprType.Bool)
            {
                Error(DiagnosticCode.TypeMismatch, rule, null, Named(rule) + ": a condition must be true or false");
            }

            CheckNode(rule, e, 0);
        }

        void CheckNode(Rule rule, Expression e, int depth)
        {
            if (depth > 256)
            {
                Error(DiagnosticCode.TypeMismatch, rule, null, Named(rule) + ": expression nested deeper than 256");
                return;
            }

            var ok = true;
            switch (e.Op)
            {
                case Op.BoolConst:
                    ok = e.Type == ExprType.Bool;
                    break;
                case Op.IntConst:
                case Op.RecordField:
                case Op.Aggregate:
                    ok = e.Type == ExprType.Int;
                    if (e.Op == Op.Aggregate)
                    {
                        for (var i = 0; i < e.Set.Length; i++)
                        {
                            ok &= schema.IndexOf(ValueClass.Channel, (int)e.Set[i]) >= 0;
                        }
                    }

                    break;
                case Op.FloatConst:
                    ok = e.Type == ExprType.Float && !float.IsNaN(e.Real) && !float.IsInfinity(e.Real);
                    break;
                case Op.KindConst:
                    ok = schema.IndexOf(ValueClass.Kind, e.Id) >= 0;
                    break;
                case Op.ChannelConst:
                case Op.ChannelTotal:
                    ok = schema.IndexOf(ValueClass.Channel, e.Id) >= 0;
                    break;
                case Op.Cell:
                    ok = (e.Class == ValueClass.Stat || e.Class == ValueClass.Intrinsic || e.Class == ValueClass.Event) && schema.IndexOf(e.Class, e.Id) >= 0
                        && (e.Subject == Subject.Self || e.Subject == Subject.Source || Holder(e.Via));
                    break;
                case Op.Chain:
                    ok = pack.Chain(e.Id) != null;
                    break;
                case Op.AnyChain:
                    ok = e.Id >= 0 && e.Id < schema.Tags.Length;
                    break;
                case Op.And:
                case Op.Or:
                    for (var i = 0; i < e.Args.Length; i++)
                    {
                        ok &= e.Args[i].Type == ExprType.Bool;
                    }

                    break;
                case Op.Not:
                    ok = e.Args.Length == 1 && e.Args[0].Type == ExprType.Bool;
                    break;
                case Op.Eq:
                case Op.Ne:
                    ok = e.Args.Length == 2 && e.Args[0].Type == e.Args[1].Type;
                    break;
                case Op.Lt:
                case Op.Le:
                case Op.Gt:
                case Op.Ge:
                    ok = e.Args.Length == 2 && e.Args[0].Type == e.Args[1].Type && e.Args[0].Type != ExprType.Bool;
                    break;
                case Op.InSet:
                    ok = e.Args.Length == 1 && e.Args[0].Type == ExprType.Int;
                    break;
                case Op.Bit:
                    ok = e.Args.Length == 2 && e.Args[0].Type == ExprType.Int && e.Args[1].Type == ExprType.Int;
                    break;
                case Op.Below:
                    ok = e.Args.Length == 4;
                    for (var i = 0; ok && i < 4; i++)
                    {
                        ok &= e.Args[i].Type == ExprType.Int;
                    }

                    break;
                case Op.Add:
                case Op.Sub:
                case Op.Mul:
                case Op.Min:
                case Op.Max:
                    ok = e.Args.Length == 2 && e.Args[0].Type == e.Args[1].Type && e.Args[0].Type == e.Type && e.Type != ExprType.Bool;
                    break;
                case Op.Neg:
                    ok = e.Args.Length == 1 && e.Args[0].Type == e.Type && e.Type != ExprType.Bool;
                    break;
                case Op.ToFloat:
                    ok = e.Args.Length == 1 && e.Args[0].Type == ExprType.Int;
                    break;
                case Op.BoundOf:
                    ok = e.Args.Length == 1 && e.Args[0].Type == ExprType.Float;
                    break;
                case Op.Select:
                    ok = e.Args.Length == 3 && e.Args[0].Type == ExprType.Bool && e.Args[1].Type == e.Args[2].Type && e.Args[1].Type == e.Type;
                    break;
            }

            if (!ok)
            {
                Error(DiagnosticCode.TypeMismatch, rule, null, Named(rule) + ": operator " + e.Op.ToString() + " has operands of the wrong type or names nothing");
            }

            for (var i = 0; i < e.Args.Length; i++)
            {
                CheckNode(rule, e.Args[i], depth + 1);
            }
        }

        void CheckChainCycles()
        {
            var state = new byte[pack.Chains.Length];
            for (var i = 0; i < pack.Chains.Length; i++)
            {
                Visit(i, state);
            }
        }

        void Visit(int index, byte[] state)
        {
            if (state[index] == 2)
            {
                return;
            }

            state[index] = 1;
            var used = new Rows<int>();
            ChainsUsed(pack.Chains[index].When, used);
            for (var u = 0; u < used.Count; u++)
            {
                var next = used[u];
                if (state[next] == 1)
                {
                    Error(DiagnosticCode.CyclicChain, pack.Chains[index], pack.Chains[next], "chains " + Named(pack.Chains[index]) + " and " + Named(pack.Chains[next]) + " use each other");
                    continue;
                }

                Visit(next, state);
            }

            state[index] = 2;
        }

        void ChainsUsed(Expression e, Rows<int> used)
        {
            if (e.Op == Op.Chain)
            {
                for (var i = 0; i < pack.Chains.Length; i++)
                {
                    if (pack.Chains[i].Id == e.Id)
                    {
                        used.Add(i);
                    }
                }
            }
            else if (e.Op == Op.AnyChain)
            {
                for (var i = 0; i < pack.Chains.Length; i++)
                {
                    if (Array.IndexOf(pack.Chains[i].Tags, e.Id) >= 0)
                    {
                        used.Add(i);
                    }
                }
            }

            for (var i = 0; i < e.Args.Length; i++)
            {
                ChainsUsed(e.Args[i], used);
            }
        }

        GateRule[] OrderGates()
        {
            var order = new GateRule[pack.Gates.Length];
            Array.Copy(pack.Gates, order, order.Length);
            var keys = new long[order.Length];
            for (var i = 0; i < order.Length; i++)
            {
                keys[i] = ((long)(-(long)order[i].Priority) << 32) | (uint)order[i].Id;
            }

            Array.Sort(keys, order);
            return order;
        }

        bool CheckGate(GateRule gate)
        {
            CheckCard(gate);
            CheckExpression(gate, gate.When, true);
            var facts = Facts(gate.When);
            if (facts.Reads(RecordField.Taken) || facts.Reads(RecordField.NotTaken) || facts.ReadsAggregate || facts.ReadsTotals)
            {
                Error(DiagnosticCode.ScopeViolation, gate, null, Named(gate) + ": a gate decides what is taken, so it cannot read taken, notTaken, aggregates or channel totals");
            }

            for (var i = 0; i < gate.Absorbs.Length; i++)
            {
                if (!IntrinsicExists(gate.Absorbs[i].Budget))
                {
                    Error(DiagnosticCode.UnknownName, gate, null, Named(gate) + ": absorb budget is no intrinsic");
                }
            }

            for (var i = 0; i < gate.Shapes.Length; i++)
            {
                if (float.IsNaN(gate.Shapes[i].Value) || float.IsInfinity(gate.Shapes[i].Value))
                {
                    Error(DiagnosticCode.BadValue, gate, null, Named(gate) + ": a shape value is finite");
                }
            }

            return facts.ReadsOnlyChannelOfRecord;
        }

        void CheckAdmitTies(GateRule[] order)
        {
            for (var i = 0; i < order.Length; i++)
            {
                var a = order[i];
                for (var j = i + 1; j < order.Length && order[j].Priority == a.Priority; j++)
                {
                    var b = order[j];
                    if (a.Admit != AdmitVerdict.None && b.Admit != AdmitVerdict.None && a.Admit != b.Admit && !Disjoint(a.When, b.When))
                    {
                        Error(DiagnosticCode.AdmitTie, a, b, Named(a) + " and " + Named(b) + " have priority " + Text(a.Priority) + " and opposite verdicts on records both can match; give one a higher priority");
                    }

                    if (a.Group >= 0 && a.Group == b.Group && a.Shapes.Length > 0 && b.Shapes.Length > 0 && !Disjoint(a.When, b.When))
                    {
                        Error(DiagnosticCode.ShapeGroupTie, a, b, Named(a) + " and " + Named(b) + " share exclusive group " + Text(a.Group) + " and priority " + Text(a.Priority) + "; one must be higher");
                    }
                }
            }

            for (var i = 0; i < order.Length; i++)
            {
                var top = order[i];
                if (top.Admit == AdmitVerdict.None || top.Card >= 0 || !top.When.IsConstantTrue)
                {
                    continue;
                }

                for (var j = i + 1; j < order.Length; j++)
                {
                    if (order[j].Admit != AdmitVerdict.None && order[j].Priority < top.Priority)
                    {
                        Warning(DiagnosticCode.Shadowed, order[j], top, "the verdict of " + Named(order[j]) + " never decides: " + Named(top) + " has a higher priority and always applies");
                    }
                }
            }
        }

        static bool Disjoint(Expression a, Expression b) =>
            Disjoint(a, b, RecordField.Channel) || Disjoint(a, b, RecordField.Kind) || Disjoint(a, b, RecordField.Origin);

        static bool Disjoint(Expression a, Expression b, RecordField field)
        {
            var left = Allowed(a, field);
            var right = Allowed(b, field);
            if (left == null || right == null)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (Array.BinarySearch(right, left[i]) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        static long[]? Allowed(Expression e, RecordField field)
        {
            if (e.Op == Op.And)
            {
                long[]? narrowest = null;
                for (var i = 0; i < e.Args.Length; i++)
                {
                    var allowed = Allowed(e.Args[i], field);
                    if (allowed != null)
                    {
                        narrowest = narrowest == null ? allowed : Intersect(narrowest, allowed);
                    }
                }

                return narrowest;
            }

            if (e.Op == Op.Eq && e.Args.Length == 2)
            {
                var record = e.Args[0].Op == Op.RecordField ? e.Args[0] : e.Args[1].Op == Op.RecordField ? e.Args[1] : null;
                var constant = ReferenceEquals(record, e.Args[0]) ? e.Args[1] : e.Args[0];
                if (record != null && record.Field == field && IsIntConstant(constant))
                {
                    return new[] { constant.Integer };
                }
            }

            if (e.Op == Op.InSet && e.Args[0].Op == Op.RecordField && e.Args[0].Field == field)
            {
                return e.Set;
            }

            return null;
        }

        static bool IsIntConstant(Expression e) => e.Op == Op.IntConst || e.Op == Op.KindConst || e.Op == Op.ChannelConst;

        static long[] Intersect(long[] a, long[] b)
        {
            var rows = new Rows<long>();
            for (var i = 0; i < a.Length; i++)
            {
                if (Array.BinarySearch(b, a[i]) >= 0)
                {
                    rows.Add(a[i]);
                }
            }

            return rows.ToArray();
        }

        bool CheckEmit(EmitRule emit)
        {
            CheckCard(emit);
            CheckExpression(emit, emit.When, true);
            var facts = Facts(emit.When);
            if (emit.Actions.Length == 0)
            {
                Error(DiagnosticCode.BadEmit, emit, null, Named(emit) + " emits nothing");
            }

            if (emit.DepthLimit < 0 || emit.DepthLimit > RuleLimits.WorldDepthLimit)
            {
                Error(DiagnosticCode.DepthLimit, emit, null, Named(emit) + ": depthLimit is 0.." + Text(RuleLimits.WorldDepthLimit));
            }

            var grouped = emit.Per == EmitPer.Source || emit.Per == EmitPer.Frame;
            if (grouped && facts.ReadsRecord)
            {
                Error(DiagnosticCode.ScopeViolation, emit, null, Named(emit) + ": per " + emit.Per.ToString() + " has no single record to read");
            }

            if (emit.Per == EmitPer.Frame && facts.ReadsSourceRow)
            {
                Error(DiagnosticCode.ScopeViolation, emit, null, Named(emit) + ": per Frame has no single source to read");
            }

            var wantsUntaken = facts.ReadsUntaken;
            var anyOther = false;
            for (var i = 0; i < emit.Actions.Length; i++)
            {
                var action = emit.Actions[i];
                var other = !emit.IsSameFrameSelf(action);
                anyOther |= other;
                wantsUntaken |= action.Amount == AmountKind.Offered || action.Amount == AmountKind.NotTaken;
                CheckAction(emit, action, false);
                if (grouped && action.SameChannel)
                {
                    Error(DiagnosticCode.BadEmit, emit, null, Named(emit) + ": channel \"same\" needs per Record or Hit");
                }

                if (emit.Per == EmitPer.Frame && action.To == EmitTo.Source)
                {
                    Error(DiagnosticCode.BadEmit, emit, null, Named(emit) + ": per Frame has no single source to send to");
                }

                if (other && emit.DepthLimit + 1 > RuleLimits.WorldDepthLimit)
                {
                    Error(DiagnosticCode.DepthLimit, emit, null, Named(emit) + ": a record it emits could reach depth " + Text(emit.DepthLimit + 1) + ", above the world limit " + Text(RuleLimits.WorldDepthLimit));
                }
            }

            if (anyOther && facts.ReadsTotals)
            {
                Error(DiagnosticCode.ScopeViolation, emit, null, Named(emit) + ": channel totals exist only for same-frame self emits; this rule also emits records");
            }

            return wantsUntaken;
        }

        void CheckAction(Rule rule, EmitAction action, bool edge)
        {
            if ((action.To == EmitTo.Owner || action.To == EmitTo.Reference) && !Holder(action.Via))
            {
                Error(DiagnosticCode.UnknownName, rule, null, Named(rule) + ": emit target is no owner or reference intrinsic");
            }

            if (!action.SameChannel && schema.IndexOf(ValueClass.Channel, action.Channel) < 0)
            {
                Error(DiagnosticCode.UnknownName, rule, null, Named(rule) + ": emit channel does not exist");
            }

            if (action.ScaleStat >= 0 && schema.IndexOf(ValueClass.Stat, action.ScaleStat) < 0)
            {
                Error(DiagnosticCode.UnknownName, rule, null, Named(rule) + ": emit scale stat does not exist");
            }

            if (edge && (action.Amount != AmountKind.Constant || action.To == EmitTo.Source || action.SameChannel))
            {
                Error(DiagnosticCode.BadEmit, rule, null, Named(rule) + ": an edge emit has a constant amount, a named channel and no source");
            }
        }

        void CheckEdge(EdgeRule edge)
        {
            CheckCard(edge);
            CheckExpression(edge, edge.When, true);
            if (schema.IndexOf(ValueClass.Event, edge.Event) < 0)
            {
                Error(DiagnosticCode.UnknownName, edge, null, Named(edge) + " names no event");
            }

            var facts = Facts(edge.When);
            if (facts.ReadsRecord || facts.ReadsAggregate || facts.ReadsSourceRow)
            {
                Error(DiagnosticCode.ScopeViolation, edge, null, Named(edge) + ": an edge has no record and no source");
            }

            for (var i = 0; i < edge.Actions.Length; i++)
            {
                CheckAction(edge, edge.Actions[i], true);
            }
        }

        void CheckFlow(FlowRule flow)
        {
            CheckCard(flow);
            CheckExpression(flow, flow.When, true);
            var facts = Facts(flow.When);
            if (facts.ReadsRecord || facts.ReadsAggregate || facts.ReadsSourceRow)
            {
                Error(DiagnosticCode.ScopeViolation, flow, null, Named(flow) + ": a flow runs once per entity and has no record");
            }

            for (var i = 0; i < flow.Terms.Length; i++)
            {
                if (!IntrinsicExists(flow.Terms[i].Intrinsic) || schema.IndexOf(ValueClass.Channel, flow.Terms[i].Channel) < 0)
                {
                    Error(DiagnosticCode.UnknownName, flow, null, Named(flow) + ": a term names a missing intrinsic or channel");
                }
            }
        }

        void CheckFolds()
        {
            for (var i = 0; i < pack.Folds.Length; i++)
            {
                var fold = pack.Folds[i];
                if (!IntrinsicExists(fold.Intrinsic))
                {
                    Error(DiagnosticCode.UnknownName, fold, null, Named(fold) + " bounds no intrinsic");
                }

                if ((fold.Floor.Kind == BoundKind.Stat && schema.IndexOf(ValueClass.Stat, fold.Floor.Stat) < 0)
                    || (fold.Ceiling.Kind == BoundKind.Stat && schema.IndexOf(ValueClass.Stat, fold.Ceiling.Stat) < 0))
                {
                    Error(DiagnosticCode.UnknownName, fold, null, Named(fold) + ": a bound names no stat");
                }

                for (var j = 0; j < i; j++)
                {
                    if (pack.Folds[j].Intrinsic == fold.Intrinsic)
                    {
                        Error(DiagnosticCode.DuplicateFold, fold, pack.Folds[j], Named(fold) + " and " + Named(pack.Folds[j]) + " bound the same intrinsic");
                    }
                }
            }
        }

        void CheckEvents()
        {
            for (var i = 0; i < pack.Events.Length; i++)
            {
                var raised = pack.Events[i];
                if (schema.IndexOf(ValueClass.Event, raised.Event) < 0)
                {
                    Error(DiagnosticCode.UnknownName, raised, null, Named(raised) + " names no event");
                }

                CheckExpression(raised, raised.Enter, true);
                CheckExpression(raised, raised.Stay, true);
                var enter = Facts(raised.Enter);
                var stay = Facts(raised.Stay);
                if (enter.ReadsOtherRow || stay.ReadsOtherRow || enter.ReadsRecord || stay.ReadsRecord || enter.ReadsAggregate || stay.ReadsAggregate)
                {
                    Error(DiagnosticCode.EventReadsOtherRow, raised, null, Named(raised) + ": an event condition reads only its own row");
                }

                for (var j = 0; j < i; j++)
                {
                    if (pack.Events[j].Event == raised.Event)
                    {
                        Error(DiagnosticCode.DuplicateEventRule, raised, pack.Events[j], Named(raised) + " and " + Named(pack.Events[j]) + " both define one event");
                    }
                }
            }
        }

        static long[] SelfWrites(EmitRule rule, out bool writesAll)
        {
            writesAll = false;
            var rows = new Rows<long>();
            for (var i = 0; i < rule.Actions.Length; i++)
            {
                var action = rule.Actions[i];
                if (!rule.IsSameFrameSelf(action))
                {
                    continue;
                }

                if (action.SameChannel)
                {
                    writesAll = true;
                }
                else
                {
                    rows.Add(action.Channel);
                }
            }

            return Expression.SortedSet(rows.ToArray());
        }

        int[] Stratify(out EmitRule[] selfOrder)
        {
            var emits = pack.Emits;
            var count = emits.Length;
            var isSelf = new bool[count];
            var writes = new long[count][];
            var writesAll = new bool[count];
            var facts = new ExprFacts[count];
            for (var i = 0; i < count; i++)
            {
                writes[i] = SelfWrites(emits[i], out writesAll[i]);
                isSelf[i] = writes[i].Length > 0 || writesAll[i];
                facts[i] = Facts(emits[i].When);
            }

            var stratum = new int[count];
            var state = new byte[count];
            for (var i = 0; i < count; i++)
            {
                if (isSelf[i])
                {
                    Depth(i, isSelf, writes, writesAll, facts, stratum, state);
                }
            }

            var order = new Rows<EmitRule>();
            var keys = new Rows<long>();
            for (var i = 0; i < count; i++)
            {
                if (isSelf[i])
                {
                    order.Add(emits[i]);
                    keys.Add(((long)stratum[i] << 32) | (uint)emits[i].Id);
                }
            }

            selfOrder = order.ToArray();
            Array.Sort(keys.ToArray(), selfOrder);
            return stratum;
        }

        int Depth(int index, bool[] isSelf, long[][] writes, bool[] writesAll, ExprFacts[] facts, int[] stratum, byte[] state)
        {
            if (state[index] == 2)
            {
                return stratum[index];
            }

            if (state[index] == 1)
            {
                return 0;
            }

            state[index] = 1;
            var level = 0;
            for (var w = 0; w < writes.Length; w++)
            {
                if (!isSelf[w] || !facts[index].TotalsMeet(writes[w], writesAll[w]))
                {
                    continue;
                }

                if (state[w] == 1)
                {
                    Error(DiagnosticCode.SelfRuleCycle, pack.Emits[index], pack.Emits[w], Named(pack.Emits[w]) + " writes a channel total that " + Named(pack.Emits[index]) + " reads, in a cycle; mark one nextFrame");
                    continue;
                }

                var below = Depth(w, isSelf, writes, writesAll, facts, stratum, state);
                level = below + 1 > level ? below + 1 : level;
            }

            stratum[index] = level;
            state[index] = 2;
            return level;
        }
    }
}
