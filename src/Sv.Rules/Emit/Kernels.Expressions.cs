using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sv.Rules.Passes;

namespace Sv.Rules.Emit
{
    public sealed class ExprScope
    {
        public string? Effect;
        public string? Outcome;
        public string? Source;
        public string? Channel;
        public string? GroupFrom;
        public string? GroupTo;
        public string? GroupLimit;
        public string? FrameFrom;
        public string? FrameTo;
        public string? Totals;
        public bool FreshEvents;
        public int RuleId = -1;
        public string Capacity = "a.Capacity";
        public string Effects = "a.Effects";
        public string Outcomes = "a.Outcomes";
    }

    public sealed partial class KernelEmitter
    {
        readonly SortedSet<int> viewLanes = new SortedSet<int>();
        readonly SortedSet<int> viewEvents = new SortedSet<int>();
        readonly Dictionary<string, int> setMethods = new Dictionary<string, int>();
        readonly List<long[]> sets = new List<long[]>();
        readonly Dictionary<string, int> sumMethods = new Dictionary<string, int>();
        readonly List<long[]> sumSets = new List<long[]>();
        readonly List<Measure> sumMeasures = new List<Measure>();

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        string Lane(int lane)
        {
            viewLanes.Add(lane);
            return "v.L" + Text(lane);
        }

        string InSet(long[] members, string value)
        {
            var key = new StringBuilder();
            for (var i = 0; i < members.Length; i++)
            {
                key.Append(members[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            if (!setMethods.TryGetValue(key.ToString(), out var index))
            {
                index = sets.Count;
                sets.Add(members);
                setMethods.Add(key.ToString(), index);
            }

            return "In" + Text(index) + "(" + value + ")";
        }

        string Sum(Measure measure, long[] channels, string from, string to, string limit, ExprScope scope)
        {
            var key = new StringBuilder();
            key.Append((int)measure).Append(':');
            for (var i = 0; i < channels.Length; i++)
            {
                key.Append(channels[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            }

            if (!sumMethods.TryGetValue(key.ToString(), out var index))
            {
                index = sumSets.Count;
                sumSets.Add(channels);
                sumMeasures.Add(measure);
                sumMethods.Add(key.ToString(), index);
            }

            return "Sum" + Text(index) + "(" + scope.Effects + ", " + scope.Outcomes + ", " + from + ", " + to + ", " + limit + ")";
        }

        int ScaleOfStat(int stat)
        {
            var row = schema.Find(ValueClass.Stat, stat);
            return row == null ? 1 : row.Scale;
        }

        int DefaultOf(ValueClass valueClass, int id)
        {
            var row = schema.Find(valueClass, id);
            return row == null ? 0 : row.IntDefault;
        }

        public string Condition(Condition condition, ExprScope scope, bool withGuard)
        {
            var parts = new List<string>();
            if (withGuard && condition.Guard >= 0)
            {
                parts.Add("(v.C" + Text(condition.Guard) + " & self) != 0ul");
            }

            for (var m = 0; m < condition.Masks.Length; m++)
            {
                parts.Add("(v.M" + Text(OptimisePass.MaskIndex(kernel, condition.Masks[m])) + " & self) != 0ul");
            }

            if (!condition.Rest.IsConstantTrue)
            {
                parts.Add(Bool(condition.Rest, scope));
            }

            return parts.Count == 0 ? "true" : string.Join(" && ", parts);
        }

        public string Bool(Expression e, ExprScope scope) => Compile(e, scope, 0);

        string Compile(Expression e, ExprScope scope, int depth)
        {
            if (depth > 512)
            {
                return "false";
            }

            switch (e.Op)
            {
                case Op.BoolConst: return e.Integer != 0 ? "true" : "false";
                case Op.IntConst:
                case Op.KindConst:
                case Op.ChannelConst: return Names.Long(e.Integer);
                case Op.FloatConst: return Names.Float(e.Real);
                case Op.RecordField: return Field(e.Field, scope);
                case Op.Roll:
                    return scope.Effect == null
                        ? "false"
                        : "Roll.Passes(" + Names.Hex(schema.Seed) + ", world.Frame, " + scope.Effect + ".Key, " + scope.Effect + ".Amount, " + Names.Int(scope.RuleId) + ", 0, " + Names.Int((int)e.Integer) + ")";
                case Op.Cell: return Cell(e, scope);
                case Op.ChannelTotal:
                {
                    var index = schema.IndexOf(ValueClass.Channel, e.Id);
                    return scope.Totals == null || index < 0 ? "0L" : scope.Totals + "[" + Text(index * 64) + " + row]";
                }

                case Op.Aggregate:
                {
                    var group = e.Group == AggregateGroup.Frame;
                    var from = group ? scope.FrameFrom : scope.GroupFrom;
                    var to = group ? scope.FrameTo : scope.GroupTo;
                    if (from == null || to == null)
                    {
                        return "0L";
                    }

                    return Sum(e.Measure, e.Set, from, to, group ? Text(RuleLimits.WorldDepthLimit) : scope.GroupLimit ?? Text(RuleLimits.WorldDepthLimit), scope);
                }

                case Op.Chain:
                case Op.AnyChain:
                    return "false";
                case Op.And:
                case Op.Or:
                {
                    var parts = new string[e.Args.Length];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        parts[i] = Compile(e.Args[i], scope, depth + 1);
                    }

                    return parts.Length == 0 ? (e.Op == Op.And ? "true" : "false") : "(" + string.Join(e.Op == Op.And ? " && " : " || ", parts) + ")";
                }

                case Op.Not: return "(!" + Compile(e.Args[0], scope, depth + 1) + ")";
                case Op.Eq:
                case Op.Ne:
                case Op.Lt:
                case Op.Le:
                case Op.Gt:
                case Op.Ge:
                {
                    var left = Compile(e.Args[0], scope, depth + 1);
                    var right = Compile(e.Args[1], scope, depth + 1);
                    if (e.Args[0].Type == ExprType.Float && e.Op == Op.Ne)
                    {
                        return "RuleMath.Differ(" + left + ", " + right + ")";
                    }

                    return "(" + left + " " + Symbol(e.Op) + " " + right + ")";
                }

                case Op.InSet: return InSet(e.Set, Compile(e.Args[0], scope, depth + 1));
                case Op.Bit: return "RuleMath.Bit(" + Compile(e.Args[0], scope, depth + 1) + ", " + Compile(e.Args[1], scope, depth + 1) + ")";
                case Op.Below:
                    return "RuleMath.Below(" + Compile(e.Args[0], scope, depth + 1) + ", " + Compile(e.Args[1], scope, depth + 1) + ", "
                        + Compile(e.Args[2], scope, depth + 1) + ", " + Compile(e.Args[3], scope, depth + 1) + ")";
                case Op.Add:
                case Op.Sub:
                case Op.Mul:
                case Op.Min:
                case Op.Max:
                {
                    var left = Compile(e.Args[0], scope, depth + 1);
                    var right = Compile(e.Args[1], scope, depth + 1);
                    var floating = e.Type == ExprType.Float;
                    var name = e.Op == Op.Add ? "Add" : e.Op == Op.Sub ? "Subtract" : e.Op == Op.Mul ? "Multiply" : e.Op == Op.Min ? "Min" : "Max";
                    return "RuleMath." + name + (floating ? "Float" : string.Empty) + "(" + left + ", " + right + ")";
                }

                case Op.Neg:
                    return e.Type == ExprType.Float ? "(-" + Compile(e.Args[0], scope, depth + 1) + ")" : "RuleMath.Negate(" + Compile(e.Args[0], scope, depth + 1) + ")";
                case Op.ToFloat: return "((float)" + Compile(e.Args[0], scope, depth + 1) + ")";
                case Op.BoundOf: return "((long)LaneMath.BoundOf(" + Compile(e.Args[0], scope, depth + 1) + "))";
                case Op.Select:
                    return "(" + Compile(e.Args[0], scope, depth + 1) + " ? " + Compile(e.Args[1], scope, depth + 1) + " : " + Compile(e.Args[2], scope, depth + 1) + ")";
                default:
                    return "false";
            }
        }

        static string Symbol(Op op)
        {
            switch (op)
            {
                case Op.Eq: return "==";
                case Op.Ne: return "!=";
                case Op.Lt: return "<";
                case Op.Le: return "<=";
                case Op.Gt: return ">";
                default: return ">=";
            }
        }

        static string Field(RecordField field, ExprScope scope)
        {
            if (scope.Effect == null)
            {
                return field == RecordField.Channel && scope.Channel != null ? "((long)" + scope.Channel + ")" : "0L";
            }

            var key = scope.Effect + ".Key";
            switch (field)
            {
                case RecordField.Kind: return "((long)EffectKey.Kind(" + key + "))";
                case RecordField.Channel: return scope.Channel != null ? "((long)" + scope.Channel + ")" : "((long)EffectKey.Channel(" + key + "))";
                case RecordField.Source: return "((long)EffectKey.Source(" + key + "))";
                case RecordField.Origin: return "((long)EffectKey.OriginOf(" + key + "))";
                case RecordField.Depth: return "((long)EffectKey.Depth(" + key + "))";
                case RecordField.Amount: return "((long)" + scope.Effect + ".Amount)";
                case RecordField.Hits: return "((long)" + scope.Effect + ".Hits)";
                case RecordField.Taken: return scope.Outcome == null ? "0L" : "RuleMath.Taken(" + scope.Outcome + ")";
                default: return scope.Outcome == null ? "0L" : "RuleMath.NotTaken(" + scope.Effect + ", " + scope.Outcome + ")";
            }
        }

        string RawCell(Expression e, ExprScope scope)
        {
            if (e.Subject == Subject.Self)
            {
                return Lane(e.Id) + "[row]";
            }

            var slot = e.Subject == Subject.Source ? (scope.Source == null ? "-1L" : "(long)" + scope.Source) : "(long)" + Lane(e.Via) + "[row]";
            return "RuleMath.Read(world, " + scope.Capacity + ", " + slot + ", " + Text(e.Id) + ", " + Names.Int(DefaultOf(e.Class, e.Id)) + ")";
        }

        int ScaleOfCell(Expression e)
        {
            var row = schema.Find(e.Class, e.Id);
            return row == null ? 1 : row.Scale;
        }

        string ScaledCell(Expression e, ExprScope scope, int unitScale) =>
            "StatLaw.Rescale(" + RawCell(e, scope) + ", " + Text(ScaleOfCell(e)) + ", " + Text(unitScale) + ")";

        string AmountCell(Expression e, ExprScope scope) =>
            e.Class == ValueClass.Stat ? ScaledCell(e, scope, 1) : RawCell(e, scope);

        string Cell(Expression e, ExprScope scope)
        {
            if (e.Subject == Subject.Self)
            {
                switch (e.Class)
                {
                    case ValueClass.Event:
                    {
                        var lane = LaneNumbers.LaneOfEvent(e.Id);
                        var word = Text(LaneNumbers.WordOfEvent(e.Id));
                        if (scope.FreshEvents)
                        {
                            return "(((Lanes.WordsOf(world, v.Chunk, " + Text(lane) + ")[" + word + "] >> row) & 1ul) != 0ul)";
                        }

                        viewEvents.Add(lane);
                        return "(((v.E" + Text(lane) + "[" + word + "] >> row) & 1ul) != 0ul)";
                    }

                    case ValueClass.Stat:
                        return "((long)StatLaw.Rescale(" + Lane(e.Id) + "[row], " + Text(ScaleOfStat(e.Id)) + ", 1))";
                    default:
                        return "((long)" + Lane(e.Id) + "[row])";
                }
            }

            var slot = e.Subject == Subject.Source ? (scope.Source == null ? "-1L" : "(long)" + scope.Source) : "(long)" + Lane(e.Via) + "[row]";
            switch (e.Class)
            {
                case ValueClass.Event:
                {
                    var fallback = schema.Find(ValueClass.Event, e.Id);
                    return "RuleMath.Level(world, " + scope.Capacity + ", " + slot + ", " + Text(LaneNumbers.LaneOfEvent(e.Id)) + ", " + Text(LaneNumbers.WordOfEvent(e.Id)) + ", "
                        + (fallback != null && fallback.EventDefault ? "true" : "false") + ")";
                }

                case ValueClass.Stat:
                    return "((long)StatLaw.Rescale(RuleMath.Read(world, " + scope.Capacity + ", " + slot + ", " + Text(e.Id) + ", " + Names.Int(DefaultOf(ValueClass.Stat, e.Id)) + "), " + Text(ScaleOfStat(e.Id)) + ", 1))";
                default:
                    return "((long)RuleMath.Read(world, " + scope.Capacity + ", " + slot + ", " + Text(e.Id) + ", " + Names.Int(DefaultOf(ValueClass.Intrinsic, e.Id)) + "))";
            }
        }
    }
}
