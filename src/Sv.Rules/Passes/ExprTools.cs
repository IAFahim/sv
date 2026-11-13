using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sv.Rules.Reference;

namespace Sv.Rules.Passes
{
    public sealed class ExprReads
    {
        public bool Record;
        public bool Source;
        public bool OtherRow;
        public bool Aggregate;
        public bool Totals;
        public bool Channel;
        public readonly SortedSet<int> SelfLanes = new SortedSet<int>();
        public readonly SortedSet<int> SelfEventLanes = new SortedSet<int>();
        public readonly SortedSet<int> ViaLanes = new SortedSet<int>();

        public bool RowOnly => !Record && !Source && !Aggregate && !Totals && !Channel;
    }

    public static class ExprTools
    {
        static readonly Expression[] None = new Expression[0];

        public static Expression With(Expression e, Expression[] args)
        {
            switch (e.Op)
            {
                case Op.And: return Expression.And(args);
                case Op.Or: return Expression.Or(args);
                case Op.Not: return Expression.Not(args[0]);
                case Op.Eq:
                case Op.Ne:
                case Op.Lt:
                case Op.Le:
                case Op.Gt:
                case Op.Ge:
                    return Expression.Compare(e.Op, args[0], args[1]);
                case Op.InSet: return Expression.InSet(args[0], e.Set);
                case Op.Bit: return Expression.Bit(args[0], args[1]);
                case Op.Below: return Expression.Below(args[0], args[1], args[2], args[3]);
                case Op.Add:
                case Op.Sub:
                case Op.Mul:
                case Op.Min:
                case Op.Max:
                    return Expression.Arithmetic(e.Op, args[0], args[1]);
                case Op.Neg: return Expression.Neg(args[0]);
                case Op.ToFloat: return Expression.ToFloat(args[0]);
                case Op.BoundOf: return Expression.BoundOf(args[0]);
                case Op.Select: return Expression.Select(args[0], args[1], args[2]);
                default: return e;
            }
        }

        public static Expression Inline(Expression e, ContentPack pack, int depth = 0)
        {
            if (depth > 256)
            {
                return Expression.False;
            }

            if (e.Op == Op.Chain)
            {
                var chain = pack.Chain(e.Id);
                return chain == null ? Expression.False : Inline(chain.When, pack, depth + 1);
            }

            if (e.Op == Op.AnyChain)
            {
                var members = new List<Expression>();
                for (var i = 0; i < pack.Chains.Length; i++)
                {
                    if (Array.IndexOf(pack.Chains[i].Tags, e.Id) >= 0)
                    {
                        members.Add(Inline(pack.Chains[i].When, pack, depth + 1));
                    }
                }

                return members.Count == 0 ? Expression.False : members.Count == 1 ? members[0] : Expression.Or(members.ToArray());
            }

            if (e.Args.Length == 0)
            {
                return e;
            }

            var args = new Expression[e.Args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Inline(e.Args[i], pack, depth + 1);
            }

            return With(e, args);
        }

        public static Expression WithChannel(Expression e, int channel)
        {
            if (e.Op == Op.RecordField && e.Field == RecordField.Channel)
            {
                return Expression.Int(channel);
            }

            if (e.Args.Length == 0)
            {
                return e;
            }

            var args = new Expression[e.Args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = WithChannel(e.Args[i], channel);
            }

            return Fold(With(e, args));
        }

        static bool IsInt(Expression e) => e.Op == Op.IntConst || e.Op == Op.KindConst || e.Op == Op.ChannelConst;

        static bool IsBool(Expression e) => e.Op == Op.BoolConst;

        public static Expression Fold(Expression e)
        {
            if (e.Args.Length == 0)
            {
                return e;
            }

            var args = new Expression[e.Args.Length];
            var changed = false;
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = Fold(e.Args[i]);
                changed |= !ReferenceEquals(args[i], e.Args[i]);
            }

            switch (e.Op)
            {
                case Op.And:
                case Op.Or:
                {
                    var stop = e.Op == Op.Or;
                    var kept = new List<Expression>();
                    for (var i = 0; i < args.Length; i++)
                    {
                        if (IsBool(args[i]))
                        {
                            if ((args[i].Integer != 0) == stop)
                            {
                                return Expression.Bool(stop);
                            }

                            continue;
                        }

                        if (args[i].Op == e.Op)
                        {
                            kept.AddRange(args[i].Args);
                        }
                        else
                        {
                            kept.Add(args[i]);
                        }
                    }

                    if (kept.Count == 0)
                    {
                        return Expression.Bool(!stop);
                    }

                    if (kept.Count == 1)
                    {
                        return kept[0];
                    }

                    return e.Op == Op.And ? Expression.And(kept.ToArray()) : Expression.Or(kept.ToArray());
                }

                case Op.Not:
                    if (IsBool(args[0]))
                    {
                        return Expression.Bool(args[0].Integer == 0);
                    }

                    if (args[0].Op == Op.Not)
                    {
                        return args[0].Args[0];
                    }

                    break;
                case Op.Eq:
                case Op.Ne:
                case Op.Lt:
                case Op.Le:
                case Op.Gt:
                case Op.Ge:
                    if (IsInt(args[0]) && IsInt(args[1]))
                    {
                        var a = args[0].Integer;
                        var b = args[1].Integer;
                        return Expression.Bool(e.Op == Op.Eq ? a == b : e.Op == Op.Ne ? a != b : e.Op == Op.Lt ? a < b : e.Op == Op.Le ? a <= b : e.Op == Op.Gt ? a > b : a >= b);
                    }

                    if (IsBool(args[0]) && IsBool(args[1]) && (e.Op == Op.Eq || e.Op == Op.Ne))
                    {
                        return Expression.Bool((args[0].Integer == args[1].Integer) == (e.Op == Op.Eq));
                    }

                    break;
                case Op.InSet:
                    if (IsInt(args[0]))
                    {
                        return Expression.Bool(e.Contains(args[0].Integer));
                    }

                    if (e.Set.Length == 0)
                    {
                        return Expression.False;
                    }

                    break;
                case Op.Bit:
                    if (IsInt(args[0]) && IsInt(args[1]))
                    {
                        var index = args[1].Integer;
                        return Expression.Bool(index >= 0 && index < 64 && ((args[0].Integer >> (int)index) & 1L) != 0);
                    }

                    break;
                case Op.Below:
                    if (IsInt(args[0]) && IsInt(args[1]) && IsInt(args[2]) && IsInt(args[3]))
                    {
                        return Expression.Bool(ReferenceLaw.Below(args[0].Integer, args[1].Integer, args[2].Integer, args[3].Integer));
                    }

                    break;
                case Op.Add:
                case Op.Sub:
                case Op.Mul:
                case Op.Min:
                case Op.Max:
                    if (e.Type == ExprType.Int && IsInt(args[0]) && IsInt(args[1]))
                    {
                        var x = args[0].Integer;
                        var y = args[1].Integer;
                        switch (e.Op)
                        {
                            case Op.Add: return Expression.Int(ReferenceLaw.Add(x, y));
                            case Op.Sub: return Expression.Int(ReferenceLaw.Add(x, ReferenceLaw.Negate(y)));
                            case Op.Mul: return Expression.Int(ReferenceLaw.Multiply(x, y));
                            case Op.Min: return Expression.Int(x < y ? x : y);
                            default: return Expression.Int(x > y ? x : y);
                        }
                    }

                    break;
                case Op.Neg:
                    if (e.Type == ExprType.Int && IsInt(args[0]))
                    {
                        return Expression.Int(ReferenceLaw.Negate(args[0].Integer));
                    }

                    break;
                case Op.Select:
                    if (IsBool(args[0]))
                    {
                        return args[0].Integer != 0 ? args[1] : args[2];
                    }

                    break;
            }

            return changed ? With(e, args) : e;
        }

        public static Expression[] Conjuncts(Expression e)
        {
            if (e.Op == Op.And)
            {
                return e.Args;
            }

            if (e.IsConstantTrue)
            {
                return None;
            }

            return new[] { e };
        }

        public static Expression All(List<Expression> parts) =>
            parts.Count == 0 ? Expression.True : parts.Count == 1 ? parts[0] : Expression.And(parts.ToArray());

        public static ExprReads Reads(Expression e)
        {
            var reads = new ExprReads();
            Collect(e, reads, 0);
            return reads;
        }

        public static void Collect(Expression e, ExprReads reads, int depth)
        {
            if (depth > 512)
            {
                return;
            }

            switch (e.Op)
            {
                case Op.Roll:
                    reads.Record = true;
                    break;
                case Op.RecordField:
                    if (e.Field == RecordField.Channel)
                    {
                        reads.Channel = true;
                    }
                    else
                    {
                        reads.Record = true;
                    }

                    break;
                case Op.Cell:
                    if (e.Subject == Subject.Self)
                    {
                        if (e.Class == ValueClass.Event)
                        {
                            reads.SelfEventLanes.Add(LaneNumbers.LaneOfEvent(e.Id));
                        }
                        else
                        {
                            reads.SelfLanes.Add(e.Id);
                        }
                    }
                    else
                    {
                        reads.OtherRow = true;
                        reads.Source |= e.Subject == Subject.Source;
                        if (e.Subject != Subject.Source)
                        {
                            reads.ViaLanes.Add(e.Via);
                        }
                    }

                    break;
                case Op.ChannelTotal:
                    reads.Totals = true;
                    break;
                case Op.Aggregate:
                    reads.Aggregate = true;
                    break;
            }

            for (var i = 0; i < e.Args.Length; i++)
            {
                Collect(e.Args[i], reads, depth + 1);
            }
        }

        public static long[]? ChannelsOf(Expression e)
        {
            if (e.Op == Op.And)
            {
                long[]? narrowest = null;
                for (var i = 0; i < e.Args.Length; i++)
                {
                    var allowed = ChannelsOf(e.Args[i]);
                    if (allowed != null)
                    {
                        narrowest = narrowest == null ? allowed : Intersect(narrowest, allowed);
                    }
                }

                return narrowest;
            }

            if (e.Op == Op.Or)
            {
                var union = new List<long>();
                for (var i = 0; i < e.Args.Length; i++)
                {
                    var allowed = ChannelsOf(e.Args[i]);
                    if (allowed == null)
                    {
                        return null;
                    }

                    union.AddRange(allowed);
                }

                return Expression.SortedSet(union.ToArray());
            }

            if (e.Op == Op.Eq && e.Args.Length == 2)
            {
                var left = e.Args[0];
                var right = e.Args[1];
                if (left.Op == Op.RecordField && left.Field == RecordField.Channel && IsInt(right))
                {
                    return new[] { right.Integer };
                }

                if (right.Op == Op.RecordField && right.Field == RecordField.Channel && IsInt(left))
                {
                    return new[] { left.Integer };
                }
            }

            if (e.Op == Op.InSet && e.Args[0].Op == Op.RecordField && e.Args[0].Field == RecordField.Channel)
            {
                return e.Set;
            }

            if (e.Op == Op.BoolConst && e.Integer == 0)
            {
                return new long[0];
            }

            return null;
        }

        static long[] Intersect(long[] a, long[] b)
        {
            var kept = new List<long>();
            for (var i = 0; i < a.Length; i++)
            {
                if (Array.BinarySearch(b, a[i]) >= 0)
                {
                    kept.Add(a[i]);
                }
            }

            return kept.ToArray();
        }

        public static string Key(Expression e)
        {
            var text = new StringBuilder();
            Key(e, text, 0);
            return text.ToString();
        }

        static void Key(Expression e, StringBuilder text, int depth)
        {
            if (depth > 512)
            {
                return;
            }

            text.Append('(').Append((int)e.Op).Append(':').Append((int)e.Type);
            switch (e.Op)
            {
                case Op.BoolConst:
                case Op.IntConst:
                case Op.KindConst:
                case Op.ChannelConst:
                case Op.Roll:
                    text.Append(':').Append(e.Integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case Op.FloatConst:
                    text.Append(':').Append(BitConverter.SingleToInt32Bits(e.Real).ToString(CultureInfo.InvariantCulture));
                    break;
                case Op.RecordField:
                    text.Append(':').Append((int)e.Field);
                    break;
                case Op.Cell:
                    text.Append(':').Append((int)e.Subject).Append(':').Append(e.Via.ToString(CultureInfo.InvariantCulture)).Append(':').Append((int)e.Class).Append(':').Append(e.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case Op.ChannelTotal:
                case Op.Chain:
                case Op.AnyChain:
                    text.Append(':').Append(e.Id.ToString(CultureInfo.InvariantCulture));
                    break;
                case Op.Aggregate:
                    text.Append(':').Append((int)e.Measure).Append(':').Append((int)e.Group);
                    break;
            }

            for (var i = 0; i < e.Set.Length; i++)
            {
                text.Append(i == 0 ? '[' : ',').Append(e.Set[i].ToString(CultureInfo.InvariantCulture));
            }

            for (var i = 0; i < e.Args.Length; i++)
            {
                Key(e.Args[i], text, depth + 1);
            }

            text.Append(')');
        }

        public static bool TryMask(Expression e, PackSchema schema, out RowMask mask)
        {
            mask = default;
            if (e.Op == Op.Cell && e.Subject == Subject.Self && e.Class == ValueClass.Event)
            {
                mask = new RowMask(MaskOp.Level, LaneNumbers.LaneOfEvent(e.Id), LaneNumbers.WordOfEvent(e.Id));
                return true;
            }

            if (e.Op == Op.Not && e.Args[0].Op == Op.Cell && e.Args[0].Subject == Subject.Self && e.Args[0].Class == ValueClass.Event)
            {
                mask = new RowMask(MaskOp.NotLevel, LaneNumbers.LaneOfEvent(e.Args[0].Id), LaneNumbers.WordOfEvent(e.Args[0].Id));
                return true;
            }

            if (e.Args.Length != 2 || (e.Op != Op.Eq && e.Op != Op.Ne && e.Op != Op.Gt && e.Op != Op.Lt))
            {
                return false;
            }

            var op = e.Op;
            var cell = e.Args[0];
            var constant = e.Args[1];
            if (!IsSelfIntrinsic(cell, schema))
            {
                cell = e.Args[1];
                constant = e.Args[0];
                op = op == Op.Gt ? Op.Lt : op == Op.Lt ? Op.Gt : op;
            }

            if (!IsSelfIntrinsic(cell, schema) || !IsInt(constant) || constant.Integer < int.MinValue || constant.Integer > int.MaxValue)
            {
                return false;
            }

            var value = (int)constant.Integer;
            switch (op)
            {
                case Op.Eq:
                    mask = value == 0 ? new RowMask(MaskOp.Zero, cell.Id, 0) : new RowMask(MaskOp.EqualTo, cell.Id, value);
                    return true;
                case Op.Ne:
                    mask = value == 0 ? new RowMask(MaskOp.NonZero, cell.Id, 0) : new RowMask(MaskOp.NotEqualTo, cell.Id, value);
                    return true;
                case Op.Gt:
                    if (value == 0)
                    {
                        mask = new RowMask(MaskOp.Positive, cell.Id, 0);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        static bool IsSelfIntrinsic(Expression e, PackSchema schema) =>
            e.Op == Op.Cell && e.Subject == Subject.Self && e.Class == ValueClass.Intrinsic && schema.IndexOf(ValueClass.Intrinsic, e.Id) >= 0;
    }
}
