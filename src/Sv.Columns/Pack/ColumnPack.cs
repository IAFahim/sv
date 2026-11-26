using System;
using System.Collections.Generic;

namespace Sv.Columns
{
    public sealed class LaneSpec
    {
        public LaneSpec(string name, int defaultValue = 0)
        {
            Name = name;
            Default = defaultValue;
        }

        public string Name { get; }

        public int Default { get; }
    }

    public sealed class OpSpec
    {
        public OpSpec(string channel, int amount, string? times = null, Cond? when = null, string? kind = null)
        {
            Channel = channel;
            Amount = amount;
            Times = times;
            When = when;
            Kind = kind;
        }

        public string Channel { get; }

        public int Amount { get; }

        public string? Times { get; }

        public Cond? When { get; }

        public string? Kind { get; }
    }

    public sealed class CardSpec
    {
        public CardSpec(string name, string lane, params OpSpec[] ops)
        {
            Name = name;
            Lane = lane;
            Ops = new List<OpSpec>(ops);
        }

        public string Name { get; }

        public string Lane { get; }

        public List<OpSpec> Ops { get; }
    }

    public sealed class TemplateSpec
    {
        public TemplateSpec(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Dictionary<string, int> Values { get; } = new Dictionary<string, int>();

        public Dictionary<string, int> Cards { get; } = new Dictionary<string, int>();

        public List<OpSpec> Ops { get; } = new List<OpSpec>();
    }

    public abstract class RuleSpec
    {
    }

    public sealed class DenyRule : RuleSpec
    {
        public DenyRule(string[]? channels, string[]? kinds, Cond? when)
        {
            Channels = channels;
            Kinds = kinds;
            When = when;
        }

        public string[]? Channels { get; }

        public string[]? Kinds { get; }

        public Cond? When { get; }
    }

    public sealed class DenyByMaskRule : RuleSpec
    {
        public DenyByMaskRule(string lane, bool byKind)
        {
            Lane = lane;
            ByKind = byKind;
        }

        public string Lane { get; }

        public bool ByKind { get; }
    }

    public sealed class ScaleRule : RuleSpec
    {
        public ScaleRule(string[] channels, int times, Cond? when, Cond? sourceWhen)
        {
            Channels = channels;
            Times = times;
            When = when;
            SourceWhen = sourceWhen;
        }

        public string[] Channels { get; }

        public int Times { get; }

        public Cond? When { get; }

        public Cond? SourceWhen { get; }
    }

    public sealed class ConvertRule : RuleSpec
    {
        public ConvertRule(string from, string to, int numerator, int denominator, Cond? when)
        {
            From = from;
            To = to;
            Numerator = numerator;
            Denominator = denominator;
            When = when;
        }

        public string From { get; }

        public string To { get; }

        public int Numerator { get; }

        public int Denominator { get; }

        public Cond? When { get; }
    }

    public sealed class AbsorbRule : RuleSpec
    {
        public AbsorbRule(string budget, params string[] channels)
        {
            Budget = budget;
            Channels = channels;
        }

        public string Budget { get; }

        public string[] Channels { get; }
    }

    public sealed class FlowRule : RuleSpec
    {
        public FlowRule(string lane, string[] add, string[] subtract)
        {
            Lane = lane;
            Add = add;
            Subtract = subtract;
        }

        public string Lane { get; }

        public string[] Add { get; }

        public string[] Subtract { get; }
    }

    public readonly struct Bound
    {
        Bound(string? lane, int value, bool present)
        {
            Lane = lane;
            Value = value;
            Present = present;
        }

        public string? Lane { get; }

        public int Value { get; }

        public bool Present { get; }

        public static Bound None => default;

        public static Bound Of(int value) => new Bound(null, value, true);

        public static Bound Of(string lane) => new Bound(lane ?? throw new ArgumentNullException(nameof(lane)), 0, true);
    }

    public sealed class BoundsRule : RuleSpec
    {
        public BoundsRule(string lane, Bound floor, Bound ceiling)
        {
            Lane = lane;
            Floor = floor;
            Ceiling = ceiling;
        }

        public string Lane { get; }

        public Bound Floor { get; }

        public Bound Ceiling { get; }
    }

    public sealed class EventRule : RuleSpec
    {
        public EventRule(string name, Cond enter, Cond? stay = null)
        {
            Name = name;
            Enter = enter;
            Stay = stay;
        }

        public string Name { get; }

        public Cond Enter { get; }

        public Cond? Stay { get; }
    }

    public sealed class TimelineOutbox
    {
        public TimelineOutbox(string channel, string? target = null)
        {
            Channel = channel;
            Target = target;
        }

        public string Channel { get; }

        public string? Target { get; }
    }

    public sealed class TimelineStart
    {
        public TimelineStart(string eventName, string timeline)
        {
            Event = eventName;
            Timeline = timeline;
        }

        public string Event { get; }

        public string Timeline { get; }
    }

    public sealed class TimelineGlue
    {
        public List<string> Cards { get; } = new List<string>();

        public List<TimelineOutbox> Outbox { get; } = new List<TimelineOutbox>();

        public List<TimelineStart> Starts { get; } = new List<TimelineStart>();
    }

    public sealed class ColumnPack
    {
        public List<LaneSpec> Lanes { get; } = new List<LaneSpec>();

        public List<string> Channels { get; } = new List<string>();

        public List<string> Kinds { get; } = new List<string>();

        public string? KindLane { get; set; }

        public List<CardSpec> Cards { get; } = new List<CardSpec>();

        public List<TemplateSpec> Templates { get; } = new List<TemplateSpec>();

        public List<RuleSpec> Rules { get; } = new List<RuleSpec>();

        public TimelineGlue Timelines { get; } = new TimelineGlue();

        public CompiledPack Compile() => PackCompiler.Compile(this);
    }
}
