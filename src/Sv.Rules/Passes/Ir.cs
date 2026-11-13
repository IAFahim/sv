using System.Collections.Generic;

namespace Sv.Rules.Passes
{
    public enum MaskOp : byte
    {
        Positive = 0,
        NonZero = 1,
        Zero = 2,
        EqualTo = 3,
        NotEqualTo = 4,
        Level = 5,
        NotLevel = 6,
    }

    public readonly struct RowMask
    {
        public MaskOp Op { get; }

        public int Lane { get; }

        public int Value { get; }

        public RowMask(MaskOp op, int lane, int value)
        {
            Op = op;
            Lane = lane;
            Value = value;
        }

        public bool Same(RowMask other) => Op == other.Op && Lane == other.Lane && Value == other.Value;
    }

    public sealed class Condition
    {
        public int Guard { get; }

        public RowMask[] Masks { get; }

        public Expression Rest { get; }

        public Condition(int guard, RowMask[] masks, Expression rest)
        {
            Guard = guard;
            Masks = masks;
            Rest = rest;
        }

        public bool Never => Rest.Op == Op.BoolConst && Rest.Integer == 0;

        public static Condition Of(int guard, Expression when) => new Condition(guard, new RowMask[0], when);
    }

    public sealed class GateIr
    {
        public GateRule Rule { get; }

        public int Order { get; }

        public bool ChannelState { get; }

        public Condition When { get; set; }

        public long[]? Channels { get; set; }

        public int GroupBit { get; set; } = -1;

        public GateIr(GateRule rule, int order, bool channelState, Condition when)
        {
            Rule = rule;
            Order = order;
            ChannelState = channelState;
            When = when;
        }

        public bool Decides => Rule.Admit != AdmitVerdict.None;

        public bool Shapes => Rule.Shapes.Length > 0;

        public bool Absorbs => Rule.Absorbs.Length > 0;
    }

    public sealed class EmitIr
    {
        public EmitRule Rule { get; }

        public int Index { get; }

        public bool HasOther { get; }

        public bool HasSelf { get; }

        public bool WantsUntaken { get; }

        public int Stratum { get; set; }

        public Condition When { get; set; }

        public EmitIr(EmitRule rule, int index, bool hasOther, bool hasSelf, bool wantsUntaken, Condition when)
        {
            Rule = rule;
            Index = index;
            HasOther = hasOther;
            HasSelf = hasSelf;
            WantsUntaken = wantsUntaken;
            When = when;
        }
    }

    public sealed class EdgeIr
    {
        public EdgeRule Rule { get; }

        public int EventRule { get; set; } = -1;

        public Condition When { get; set; }

        public EdgeIr(EdgeRule rule, Condition when)
        {
            Rule = rule;
            When = when;
        }
    }

    public sealed class FlowIr
    {
        public FlowRule Rule { get; }

        public Condition When { get; set; }

        public FlowIr(FlowRule rule, Condition when)
        {
            Rule = rule;
            When = when;
        }
    }

    public sealed class FoldIr
    {
        public int Intrinsic { get; }

        public FoldRule? Rule { get; }

        public int DeltaSlot { get; set; } = -1;

        public FoldIr(int intrinsic, FoldRule? rule)
        {
            Intrinsic = intrinsic;
            Rule = rule;
        }

        public bool Rebinds => Rule != null && (Rule.Floor.Kind == BoundKind.Stat || Rule.Ceiling.Kind == BoundKind.Stat);
    }

    public sealed class EventIr
    {
        public EventRule Rule { get; }

        public int Index { get; }

        public Expression Enter { get; set; }

        public Expression Stay { get; set; }

        public EventIr(EventRule rule, int index, Expression enter, Expression stay)
        {
            Rule = rule;
            Index = index;
            Enter = enter;
            Stay = stay;
        }
    }

    public sealed class LayerIr
    {
        public readonly List<GateIr> Gates = new List<GateIr>();
        public readonly List<EmitIr> Emits = new List<EmitIr>();
        public readonly List<EmitIr> SelfOrder = new List<EmitIr>();
        public readonly List<EdgeIr> Edges = new List<EdgeIr>();
        public readonly List<FlowIr> Flows = new List<FlowIr>();
        public readonly List<FoldIr> Folds = new List<FoldIr>();
        public readonly List<EventIr> Events = new List<EventIr>();
        public readonly List<int> DeltaIntrinsics = new List<int>();
        public readonly List<int> AbsorbBudgets = new List<int>();
        public readonly List<int> AssignIntrinsics = new List<int>();
        public readonly List<AssignMode> AssignModes = new List<AssignMode>();
    }

    public sealed class GateCase
    {
        public GateIr Gate { get; }

        public Condition When { get; }

        public GateCase(GateIr gate, Condition when)
        {
            Gate = gate;
            When = when;
        }
    }

    public sealed class GateBlock
    {
        public int Guard { get; }

        public string Method { get; set; } = string.Empty;

        public readonly List<GateCase> Cases = new List<GateCase>();

        public GateBlock(int guard)
        {
            Guard = guard;
        }
    }

    public sealed class GateChannel
    {
        public int Channel { get; }

        public string Method { get; set; } = string.Empty;

        public readonly List<GateCase> Cases = new List<GateCase>();

        public readonly List<GateBlock> Blocks = new List<GateBlock>();

        public GateChannel(int channel)
        {
            Channel = channel;
        }
    }

    public sealed class KernelIr
    {
        public readonly List<GateChannel> Admit = new List<GateChannel>();
        public readonly List<GateChannel> SelfAdmit = new List<GateChannel>();
        public GateChannel AdmitAny = new GateChannel(-1);
        public GateChannel SelfAdmitAny = new GateChannel(-1);
        public readonly List<RowMask> Masks = new List<RowMask>();
        public readonly List<int> MaskGuards = new List<int>();
        public readonly List<bool> MaskAlways = new List<bool>();
        public readonly List<int> Guards = new List<int>();
        public int MethodCap = 64;
        public int ShapeCapacity;
    }

    public readonly struct EmittedFile
    {
        public string Name { get; }

        public string Text { get; }

        public EmittedFile(string name, string text)
        {
            Name = name;
            Text = text;
        }
    }

    public sealed class CompileOptions
    {
        public string Namespace = "Game.Sv";
        public string ToolVersion = "sv-pack 3.0";
        public PackSchema? Previous;
        public int MethodCap = 64;
    }

    public sealed class CompileUnit
    {
        public CompileOptions Options { get; }

        public PackSource? Source { get; set; }

        public ContentPack? Pack { get; set; }

        public PackPlan? Plan { get; set; }

        public LayerIr Layers { get; } = new LayerIr();

        public KernelIr Kernel { get; } = new KernelIr();

        public DiagnosticList Diagnostics { get; } = new DiagnosticList();

        public List<EmittedFile> Files { get; } = new List<EmittedFile>();

        public HashSet<int> EmittedRules { get; } = new HashSet<int>();

        public List<string> RuleMap { get; } = new List<string>();

        public ulong PackHash { get; set; }

        public CompileUnit(CompileOptions options)
        {
            Options = options;
        }

        public void Absorb(Diagnostic[] diagnostics)
        {
            for (var i = 0; i < diagnostics.Length; i++)
            {
                var d = diagnostics[i];
                if (d.Severity == Severity.Error)
                {
                    Diagnostics.Error(d.Code, d.File, d.Offset, d.RuleId, d.OtherRuleId, d.Message);
                }
                else
                {
                    Diagnostics.Warning(d.Code, d.File, d.Offset, d.RuleId, d.OtherRuleId, d.Message);
                }
            }
        }

        public PackSchema Schema => Pack!.Schema;
    }
}
