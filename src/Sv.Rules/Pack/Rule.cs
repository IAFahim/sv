namespace Sv.Rules
{
    public enum RuleLayer : byte
    {
        Chain = 0,
        Gate = 1,
        Emit = 2,
        Edge = 3,
        Flow = 4,
        Fold = 5,
        Event = 6,
    }

    public enum AdmitVerdict : byte
    {
        None = 0,
        Allow = 1,
        Deny = 2,
    }

    public enum AbsorbPer : byte
    {
        Hit = 0,
        Amount = 1,
    }

    public enum AbsorbOrder : byte
    {
        BySource = 0,
        ByAmountDescending = 1,
    }

    public enum EmitTo : byte
    {
        Self = 0,
        Source = 1,
        Owner = 2,
        Reference = 3,
    }

    public enum AmountKind : byte
    {
        Constant = 0,
        Offered = 1,
        Taken = 2,
        NotTaken = 3,
    }

    public enum EmitPer : byte
    {
        Record = 0,
        Hit = 1,
        Source = 2,
        Frame = 3,
    }

    public enum RecordOrigin : byte
    {
        Direct = 0,
        Timeline = 1,
        Reflected = 2,
        Triggered = 3,
    }

    public enum BoundKind : byte
    {
        None = 0,
        Constant = 1,
        Stat = 2,
    }

    public static class RuleLimits
    {
        public const int WorldDepthLimit = 3;
        public const int ReflectionDepthDefault = 0;
    }

    public abstract class Rule
    {
        public int Id { get; }

        public string Name { get; }

        public int Card { get; }

        public Expression When { get; }

        public int Offset { get; }

        protected Rule(int id, string name, int card, Expression when, int offset)
        {
            Id = id;
            Name = name;
            Card = card;
            When = when;
            Offset = offset;
        }

        public abstract RuleLayer Layer { get; }
    }

    public readonly struct ShapeEffect
    {
        public ShapeKind Kind { get; }

        public float Value { get; }

        public ShapeEffect(ShapeKind kind, float value)
        {
            Kind = kind;
            Value = value;
        }
    }

    public readonly struct AbsorbEffect
    {
        public int Budget { get; }

        public AbsorbPer Per { get; }

        public AbsorbOrder Order { get; }

        public AbsorbEffect(int budget, AbsorbPer per, AbsorbOrder order)
        {
            Budget = budget;
            Per = per;
            Order = order;
        }
    }

    public sealed class GateRule : Rule
    {
        public int Priority { get; }

        public AdmitVerdict Admit { get; }

        public ShapeEffect[] Shapes { get; }

        public int Group { get; }

        public AbsorbEffect[] Absorbs { get; }

        public GateRule(int id, string name, int card, Expression when, int offset, int priority, AdmitVerdict admit, ShapeEffect[] shapes, int group, AbsorbEffect[] absorbs)
            : base(id, name, card, when, offset)
        {
            Priority = priority;
            Admit = admit;
            Shapes = shapes;
            Group = group;
            Absorbs = absorbs;
        }

        public override RuleLayer Layer => RuleLayer.Gate;
    }

    public sealed class EmitAction
    {
        public EmitTo To { get; }

        public int Via { get; }

        public int Channel { get; }

        public AmountKind Amount { get; }

        public int Constant { get; }

        public int ScaleStat { get; }

        public long[] Over { get; }

        public RecordOrigin Origin { get; }

        public EmitAction(EmitTo to, int via, int channel, AmountKind amount, int constant, int scaleStat, long[] over, RecordOrigin origin)
        {
            To = to;
            Via = via;
            Channel = channel;
            Amount = amount;
            Constant = constant;
            ScaleStat = scaleStat;
            Over = over;
            Origin = origin;
        }

        public bool SameChannel => Channel < 0;

        public bool CoversChannel(int channel) => Over.Length == 0 || System.Array.BinarySearch(Over, (long)channel) >= 0;
    }

    public sealed class EmitRule : Rule
    {
        public int DepthLimit { get; }

        public bool NextFrame { get; }

        public EmitPer Per { get; }

        public EmitAction[] Actions { get; }

        public EmitRule(int id, string name, int card, Expression when, int offset, int depthLimit, bool nextFrame, EmitPer per, EmitAction[] actions)
            : base(id, name, card, when, offset)
        {
            DepthLimit = depthLimit;
            NextFrame = nextFrame;
            Per = per;
            Actions = actions;
        }

        public override RuleLayer Layer => RuleLayer.Emit;

        public bool IsSameFrameSelf(EmitAction action) => action.To == EmitTo.Self && !NextFrame;
    }

    public sealed class EdgeRule : Rule
    {
        public int Event { get; }

        public bool Rising { get; }

        public EmitAction[] Actions { get; }

        public EdgeRule(int id, string name, int card, Expression when, int offset, int raised, bool rising, EmitAction[] actions)
            : base(id, name, card, when, offset)
        {
            Event = raised;
            Rising = rising;
            Actions = actions;
        }

        public override RuleLayer Layer => RuleLayer.Edge;
    }

    public readonly struct FlowTerm
    {
        public int Intrinsic { get; }

        public int Channel { get; }

        public int Sign { get; }

        public FlowTerm(int intrinsic, int channel, int sign)
        {
            Intrinsic = intrinsic;
            Channel = channel;
            Sign = sign < 0 ? -1 : 1;
        }
    }

    public sealed class FlowRule : Rule
    {
        public FlowTerm[] Terms { get; }

        public FlowRule(int id, string name, int card, Expression when, int offset, FlowTerm[] terms)
            : base(id, name, card, when, offset)
        {
            Terms = terms;
        }

        public override RuleLayer Layer => RuleLayer.Flow;
    }

    public readonly struct Bound
    {
        public BoundKind Kind { get; }

        public int Constant { get; }

        public int Stat { get; }

        public Bound(BoundKind kind, int constant, int stat)
        {
            Kind = kind;
            Constant = constant;
            Stat = stat;
        }

        public static Bound None => new Bound(BoundKind.None, 0, -1);
    }

    public sealed class FoldRule : Rule
    {
        public int Intrinsic { get; }

        public Bound Floor { get; }

        public Bound Ceiling { get; }

        public FoldRule(int id, string name, int offset, int intrinsic, Bound floor, Bound ceiling)
            : base(id, name, -1, Expression.True, offset)
        {
            Intrinsic = intrinsic;
            Floor = floor;
            Ceiling = ceiling;
        }

        public override RuleLayer Layer => RuleLayer.Fold;
    }

    public sealed class EventRule : Rule
    {
        public int Event { get; }

        public Expression Enter { get; }

        public Expression Stay { get; }

        public EventRule(int id, string name, int offset, int raised, Expression enter, Expression stay)
            : base(id, name, -1, Expression.True, offset)
        {
            Event = raised;
            Enter = enter;
            Stay = stay;
        }

        public override RuleLayer Layer => RuleLayer.Event;
    }

    public sealed class ChainRule : Rule
    {
        public int[] Tags { get; }

        public ChainRule(int id, string name, int offset, int[] tags, Expression when)
            : base(id, name, -1, when, offset)
        {
            Tags = tags;
        }

        public override RuleLayer Layer => RuleLayer.Chain;
    }

    public sealed class ContentPack
    {
        public PackSchema Schema { get; }

        public ChainRule[] Chains { get; }

        public GateRule[] Gates { get; }

        public EmitRule[] Emits { get; }

        public EdgeRule[] Edges { get; }

        public FlowRule[] Flows { get; }

        public FoldRule[] Folds { get; }

        public EventRule[] Events { get; }

        public ContentPack(PackSchema schema, ChainRule[] chains, GateRule[] gates, EmitRule[] emits, EdgeRule[] edges, FlowRule[] flows, FoldRule[] folds, EventRule[] events)
        {
            Schema = schema;
            Chains = chains;
            Gates = gates;
            Emits = emits;
            Edges = edges;
            Flows = flows;
            Folds = folds;
            Events = events;
        }

        public ChainRule? Chain(int id)
        {
            for (var i = 0; i < Chains.Length; i++)
            {
                if (Chains[i].Id == id)
                {
                    return Chains[i];
                }
            }

            return null;
        }
    }
}
