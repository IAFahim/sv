using System;

namespace Sv.Columns
{
    public enum CondOp : byte
    {
        Gt = 0,
        Ge = 1,
        Lt = 2,
        Le = 3,
        Eq = 4,
        Ne = 5,
        Below = 6,
        Bit = 7,
        Event = 8,
        Entered = 9,
        All = 10,
    }

    public sealed class Cond
    {
        public CondOp Op { get; }

        public string Lane { get; }

        public string? Other { get; }

        public int Value { get; }

        public bool Negated { get; }

        public Cond[] Parts { get; }

        Cond(CondOp op, string lane, string? other, int value, bool negated, Cond[] parts)
        {
            Op = op;
            Lane = lane;
            Other = other;
            Value = value;
            Negated = negated;
            Parts = parts;
        }

        public static Cond Compare(CondOp op, string lane, int value) => op <= CondOp.Ne
            ? new Cond(op, lane, null, value, false, Array.Empty<Cond>())
            : throw new ArgumentOutOfRangeException(nameof(op), "Compare takes Gt, Ge, Lt, Le, Eq or Ne");

        public static Cond Compare(CondOp op, string lane, string otherLane) => op <= CondOp.Ne
            ? new Cond(op, lane, otherLane, 0, false, Array.Empty<Cond>())
            : throw new ArgumentOutOfRangeException(nameof(op), "Compare takes Gt, Ge, Lt, Le, Eq or Ne");

        public static Cond Gt(string lane, int value) => Compare(CondOp.Gt, lane, value);

        public static Cond Ge(string lane, int value) => Compare(CondOp.Ge, lane, value);

        public static Cond Lt(string lane, int value) => Compare(CondOp.Lt, lane, value);

        public static Cond Le(string lane, int value) => Compare(CondOp.Le, lane, value);

        public static Cond Eq(string lane, int value) => Compare(CondOp.Eq, lane, value);

        public static Cond Ne(string lane, int value) => Compare(CondOp.Ne, lane, value);

        public static Cond Below(string lane, string ofLane, int percent) => new Cond(CondOp.Below, lane, ofLane, percent, false, Array.Empty<Cond>());

        public static Cond Bit(string lane, int bit) => new Cond(CondOp.Bit, lane, null, bit, false, Array.Empty<Cond>());

        public static Cond Event(string name) => new Cond(CondOp.Event, name, null, 0, false, Array.Empty<Cond>());

        public static Cond Entered(string name) => new Cond(CondOp.Entered, name, null, 0, false, Array.Empty<Cond>());

        public static Cond All(params Cond[] parts) => new Cond(CondOp.All, string.Empty, null, 0, false, parts);

        public static Cond Not(Cond atom) => atom.Op != CondOp.All
            ? new Cond(atom.Op, atom.Lane, atom.Other, atom.Value, !atom.Negated, atom.Parts)
            : throw new ArgumentException("Not takes a single test, not All", nameof(atom));

        public override string ToString()
        {
            var text = Op switch
            {
                CondOp.All => "all(" + string.Join(", ", Array.ConvertAll(Parts, p => p.ToString())) + ")",
                CondOp.Below => "below(" + Lane + ", " + Other + ", " + Value + "%)",
                CondOp.Bit => "bit(" + Lane + ", " + Value + ")",
                CondOp.Event => "event(" + Lane + ")",
                CondOp.Entered => "entered(" + Lane + ")",
                _ => Op.ToString().ToLowerInvariant() + "(" + Lane + ", " + (Other ?? Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ")",
            };
            return Negated ? "not " + text : text;
        }
    }
}
