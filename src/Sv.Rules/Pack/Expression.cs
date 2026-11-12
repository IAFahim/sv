using System;

namespace Sv.Rules
{
    public enum ExprType : byte
    {
        Bool = 0,
        Int = 1,
        Float = 2,
    }

    public enum Op : byte
    {
        BoolConst = 0,
        IntConst = 1,
        FloatConst = 2,
        KindConst = 3,
        ChannelConst = 4,
        RecordField = 5,
        Cell = 6,
        ChannelTotal = 7,
        Aggregate = 8,
        Chain = 9,
        AnyChain = 10,
        And = 11,
        Or = 12,
        Not = 13,
        Eq = 14,
        Ne = 15,
        Lt = 16,
        Le = 17,
        Gt = 18,
        Ge = 19,
        InSet = 20,
        Bit = 21,
        Below = 22,
        Add = 23,
        Sub = 24,
        Mul = 25,
        Min = 26,
        Max = 27,
        Neg = 28,
        ToFloat = 29,
        BoundOf = 30,
        Select = 31,
    }

    public enum RecordField : byte
    {
        Kind = 0,
        Channel = 1,
        Source = 2,
        Origin = 3,
        Depth = 4,
        Amount = 5,
        Hits = 6,
        Taken = 7,
        NotTaken = 8,
    }

    public enum Subject : byte
    {
        Self = 0,
        Source = 1,
        Owner = 2,
        Reference = 3,
    }

    public enum Measure : byte
    {
        Offered = 0,
        Taken = 1,
        NotTaken = 2,
    }

    public enum AggregateGroup : byte
    {
        Current = 0,
        Frame = 1,
    }

    public sealed class Expression
    {
        static readonly Expression[] NoArgs = new Expression[0];
        static readonly long[] NoSet = new long[0];

        public Op Op { get; }

        public ExprType Type { get; }

        public Expression[] Args { get; }

        public long Integer { get; }

        public float Real { get; }

        public int Id { get; }

        public int Via { get; }

        public Subject Subject { get; }

        public ValueClass Class { get; }

        public RecordField Field { get; }

        public Measure Measure { get; }

        public AggregateGroup Group { get; }

        public long[] Set { get; }

        Expression(Op op, ExprType type, Expression[] args, long integer, float real, int id, int via, Subject subject, ValueClass valueClass, RecordField field, Measure measure, AggregateGroup group, long[] set)
        {
            Op = op;
            Type = type;
            Args = args;
            Integer = integer;
            Real = real;
            Id = id;
            Via = via;
            Subject = subject;
            Class = valueClass;
            Field = field;
            Measure = measure;
            Group = group;
            Set = set;
        }

        static Expression Leaf(Op op, ExprType type, long integer = 0, float real = 0f, int id = -1, int via = -1, Subject subject = Subject.Self, ValueClass valueClass = ValueClass.Intrinsic, RecordField field = RecordField.Kind, Measure measure = Measure.Offered, AggregateGroup group = AggregateGroup.Current, long[]? set = null) =>
            new Expression(op, type, NoArgs, integer, real, id, via, subject, valueClass, field, measure, group, set ?? NoSet);

        static Expression Node(Op op, ExprType type, Expression[] args, long[]? set = null) =>
            new Expression(op, type, args, 0, 0f, -1, -1, Subject.Self, ValueClass.Intrinsic, RecordField.Kind, Measure.Offered, AggregateGroup.Current, set ?? NoSet);

        public static readonly Expression True = Leaf(Op.BoolConst, ExprType.Bool, 1);

        public static readonly Expression False = Leaf(Op.BoolConst, ExprType.Bool, 0);

        public static Expression Bool(bool value) => value ? True : False;

        public static Expression Int(long value) => Leaf(Op.IntConst, ExprType.Int, value);

        public static Expression Float(float value) => Leaf(Op.FloatConst, ExprType.Float, 0, value);

        public static Expression KindConst(int kind) => Leaf(Op.KindConst, ExprType.Int, kind, id: kind);

        public static Expression ChannelConst(int channel) => Leaf(Op.ChannelConst, ExprType.Int, channel, id: channel);

        public static Expression Record(RecordField field) => Leaf(Op.RecordField, ExprType.Int, field: field);

        public static Expression Cell(Subject subject, int via, ValueClass valueClass, int id)
        {
            var type = valueClass == ValueClass.Event ? ExprType.Bool : ExprType.Int;
            return Leaf(Op.Cell, type, id: id, via: via, subject: subject, valueClass: valueClass);
        }

        public static Expression Total(int channel) => Leaf(Op.ChannelTotal, ExprType.Int, id: channel);

        public static Expression Aggregate(Measure measure, AggregateGroup group, long[] channels) =>
            Leaf(Op.Aggregate, ExprType.Int, measure: measure, group: group, set: SortedSet(channels));

        public static Expression Chain(int chainRule) => Leaf(Op.Chain, ExprType.Bool, id: chainRule);

        public static Expression AnyChain(int tag) => Leaf(Op.AnyChain, ExprType.Bool, id: tag);

        public static Expression And(Expression[] args) => Node(Op.And, ExprType.Bool, args);

        public static Expression Or(Expression[] args) => Node(Op.Or, ExprType.Bool, args);

        public static Expression Not(Expression arg) => Node(Op.Not, ExprType.Bool, new[] { arg });

        public static Expression Compare(Op op, Expression left, Expression right) => Node(op, ExprType.Bool, new[] { left, right });

        public static Expression InSet(Expression value, long[] members) => Node(Op.InSet, ExprType.Bool, new[] { value }, SortedSet(members));

        public static Expression Bit(Expression value, Expression index) => Node(Op.Bit, ExprType.Bool, new[] { value, index });

        public static Expression Below(Expression value, Expression whole, Expression numerator, Expression denominator) =>
            Node(Op.Below, ExprType.Bool, new[] { value, whole, numerator, denominator });

        public static Expression Arithmetic(Op op, Expression left, Expression right) => Node(op, left.Type, new[] { left, right });

        public static Expression Neg(Expression arg) => Node(Op.Neg, arg.Type, new[] { arg });

        public static Expression ToFloat(Expression arg) => Node(Op.ToFloat, ExprType.Float, new[] { arg });

        public static Expression BoundOf(Expression arg) => Node(Op.BoundOf, ExprType.Int, new[] { arg });

        public static Expression Select(Expression condition, Expression whenTrue, Expression whenFalse) =>
            Node(Op.Select, whenTrue.Type, new[] { condition, whenTrue, whenFalse });

        public static long[] SortedSet(long[] members)
        {
            var copy = new long[members.Length];
            Array.Copy(members, copy, members.Length);
            Array.Sort(copy);
            var written = 0;
            for (var i = 0; i < copy.Length; i++)
            {
                if (written == 0 || copy[written - 1] != copy[i])
                {
                    copy[written] = copy[i];
                    written++;
                }
            }

            if (written == copy.Length)
            {
                return copy;
            }

            var unique = new long[written];
            Array.Copy(copy, unique, written);
            return unique;
        }

        public bool Contains(long member) => Array.BinarySearch(Set, member) >= 0;

        public bool IsConstantTrue => Op == Op.BoolConst && Integer != 0;
    }
}
