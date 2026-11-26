using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sv.Columns
{
    public sealed class PackException : Exception
    {
        public PackException(IReadOnlyList<string> problems)
            : base("pack does not compile:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", problems))
        {
            Problems = problems;
        }

        public IReadOnlyList<string> Problems { get; }
    }

    public static class PackLimits
    {
        public const int Lanes = 62;
        public const int Channels = 16;
        public const int Kinds = 4;
        public const int Events = 31;
        public const int ScaleRules = 8;
        public const int ScaleProduct = 16;
        public const int OpAmount = short.MaxValue;
        public const int TemplateAmountBudget = 1 << 20;
        public const int ConvertDenominator = 1 << 16;
        public const string EventsLane = "@events";
        public const string EnteredLane = "@entered";
    }

    internal static unsafe class PackCompiler
    {
        public static CompiledPack Compile(ColumnPack pack)
        {
            var scope = new Scope(pack);
            var named = scope.Check();
            try
            {
                return scope.Build();
            }
            catch (Exception failed) when (!named && !(failed is PackException))
            {
                throw scope.Failure();
            }
        }

        sealed class Scope
        {
            readonly ColumnPack pack;
            readonly List<string> problems = new List<string>();
            readonly HashSet<string> problemSet = new HashSet<string>(StringComparer.Ordinal);
            readonly Dictionary<string, int> lanes = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> channels = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> events = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> cards = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> templates = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<Atom> atoms = new List<Atom>();
            readonly List<CondRange> conds = new List<CondRange>();
            readonly Dictionary<string, int> condByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            int eventsLane;
            int enteredLane;

            public Scope(ColumnPack pack)
            {
                this.pack = pack;
            }

            void Problem(string text)
            {
                if (problemSet.Add(text))
                {
                    problems.Add(text);
                }
            }

            public PackException Failure() => new PackException(problems);

            void NoNulls<T>(List<T> items, string what)
                where T : class
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] == null)
                    {
                        Problem(what + "[" + i + "] is null");
                    }
                }
            }

            static bool IsName(string name)
            {
                if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
                {
                    return false;
                }

                foreach (var c in name)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_'))
                    {
                        return false;
                    }
                }

                return true;
            }

            void Declare(Dictionary<string, int> into, string what, string name)
            {
                if (!IsName(name))
                {
                    Problem(what + " name '" + name + "' must be a letter or underscore followed by letters, digits or underscores");
                    return;
                }

                if (into.ContainsKey(name))
                {
                    Problem(what + " '" + name + "' is declared twice");
                    return;
                }

                into[name] = into.Count;
            }

            public bool Check()
            {
                NoNulls(pack.Lanes, "lanes");
                NoNulls(pack.Channels, "channels");
                NoNulls(pack.Kinds, "kinds");
                NoNulls(pack.Cards, "cards");
                NoNulls(pack.Templates, "templates");
                NoNulls(pack.Rules, "rules");
                foreach (var card in pack.Cards)
                {
                    if (card != null)
                    {
                        NoNulls(card.Ops, "card '" + card.Name + "' ops");
                    }
                }

                foreach (var template in pack.Templates)
                {
                    if (template != null)
                    {
                        NoNulls(template.Ops, "template '" + template.Name + "' ops");
                    }
                }

                if (problems.Count > 0)
                {
                    throw Failure();
                }

                foreach (var lane in pack.Lanes)
                {
                    Declare(lanes, "lane", lane.Name);
                }

                if (pack.Lanes.Count > PackLimits.Lanes)
                {
                    Problem("a pack has at most " + PackLimits.Lanes + " lanes, this one has " + pack.Lanes.Count);
                }

                eventsLane = lanes.Count;
                lanes[PackLimits.EventsLane] = eventsLane;
                enteredLane = lanes.Count;
                lanes[PackLimits.EnteredLane] = enteredLane;
                foreach (var channel in pack.Channels)
                {
                    Declare(channels, "channel", channel);
                }

                if (channels.Count == 0 || channels.Count > PackLimits.Channels)
                {
                    Problem("a pack has 1 to " + PackLimits.Channels + " channels, this one has " + channels.Count);
                }

                foreach (var kind in pack.Kinds)
                {
                    Declare(kinds, "kind", kind);
                }

                if (kinds.Count == 0)
                {
                    kinds["none"] = 0;
                }

                if (kinds.Count > PackLimits.Kinds)
                {
                    Problem("a pack has at most " + PackLimits.Kinds + " kinds, this one has " + kinds.Count);
                }

                if (pack.KindLane != null)
                {
                    LaneOf(pack.KindLane, "kindLane");
                }

                foreach (var rule in pack.Rules)
                {
                    if (rule is EventRule e)
                    {
                        Declare(events, "event", e.Name);
                    }
                }

                if (events.Count > PackLimits.Events)
                {
                    Problem("a pack has at most " + PackLimits.Events + " events, this one has " + events.Count);
                }

                foreach (var card in pack.Cards)
                {
                    Declare(cards, "card", card.Name);
                    LaneOf(card.Lane, "card '" + card.Name + "'");
                }

                foreach (var template in pack.Templates)
                {
                    Declare(templates, "template", template.Name);
                }

                if (templates.Count == 0)
                {
                    Problem("a pack needs at least one template");
                }

                CheckTimelines();
                return problems.Count == 0;
            }

            void CheckTimelines()
            {
                var glue = pack.Timelines;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < glue.Cards.Count; i++)
                {
                    var where = "timelines.cards[" + i + "]";
                    var card = glue.Cards[i];
                    if (card == null || !cards.ContainsKey(card))
                    {
                        Problem(where + ": unknown card '" + card + "'");
                    }
                    else if (!seen.Add(card))
                    {
                        Problem(where + ": card '" + card + "' is listed twice");
                    }
                }

                seen.Clear();
                for (var i = 0; i < glue.Outbox.Count; i++)
                {
                    var where = "timelines.outbox[" + i + "]";
                    var outbox = glue.Outbox[i];
                    if (outbox == null)
                    {
                        Problem(where + " is null");
                        continue;
                    }

                    ChannelOf(outbox.Channel, where);
                    if (outbox.Channel != null && !seen.Add(outbox.Channel))
                    {
                        Problem(where + ": channel '" + outbox.Channel + "' is listed twice");
                    }

                    if (outbox.Target != null)
                    {
                        LaneOf(outbox.Target, where + ".target");
                    }
                }

                for (var i = 0; i < glue.Starts.Count; i++)
                {
                    var where = "timelines.starts[" + i + "]";
                    var start = glue.Starts[i];
                    if (start == null)
                    {
                        Problem(where + " is null");
                        continue;
                    }

                    if (start.Event == null || !events.ContainsKey(start.Event))
                    {
                        Problem(where + ": unknown event '" + start.Event + "'");
                    }

                    if (string.IsNullOrEmpty(start.Timeline))
                    {
                        Problem(where + ": a start names its timeline");
                    }
                }
            }

            int LaneOf(string name, string where)
            {
                if (name != null && lanes.TryGetValue(name, out var lane) && name[0] != '@')
                {
                    return lane;
                }

                Problem(where + ": unknown lane '" + name + "'");
                return 0;
            }

            int ChannelOf(string name, string where)
            {
                if (name != null && channels.TryGetValue(name, out var channel))
                {
                    return channel;
                }

                Problem(where + ": unknown channel '" + name + "'");
                return 0;
            }

            int KindOf(string? name, string where)
            {
                if (name == null)
                {
                    return 0;
                }

                if (kinds.TryGetValue(name, out var kind))
                {
                    return kind;
                }

                Problem(where + ": unknown kind '" + name + "'");
                return 0;
            }

            void AtomsOf(Cond cond, string where, List<Atom> into)
            {
                if (cond.Op == CondOp.All)
                {
                    if (cond.Parts.Length == 0)
                    {
                        Problem(where + ": all() needs at least one test");
                    }

                    foreach (var part in cond.Parts)
                    {
                        AtomsOf(part, where, into);
                    }

                    return;
                }

                var atom = new Atom { Negated = cond.Negated ? 1 : 0, Other = -1, Value = cond.Value };
                switch (cond.Op)
                {
                    case CondOp.Event:
                    case CondOp.Entered:
                        var bit = 0;
                        if (cond.Lane == null || !events.TryGetValue(cond.Lane, out bit))
                        {
                            Problem(where + ": unknown event '" + cond.Lane + "'");
                        }

                        atom.Op = AtomOp.Bit;
                        atom.Lane = cond.Op == CondOp.Event ? eventsLane : enteredLane;
                        atom.Value = bit;
                        break;
                    case CondOp.Bit:
                        atom.Op = AtomOp.Bit;
                        atom.Lane = LaneOf(cond.Lane, where);
                        if (cond.Value < 0 || cond.Value > 31)
                        {
                            Problem(where + ": bit index " + cond.Value + " is outside 0 to 31");
                        }

                        break;
                    case CondOp.Below:
                        atom.Op = AtomOp.Below;
                        atom.Lane = LaneOf(cond.Lane, where);
                        atom.Other = LaneOf(cond.Other!, where);
                        if (cond.Value < 0 || cond.Value > 100)
                        {
                            Problem(where + ": below() percent " + cond.Value + " is outside 0 to 100");
                        }

                        break;
                    default:
                        atom.Op = (int)cond.Op;
                        atom.Lane = LaneOf(cond.Lane, where);
                        if (cond.Other != null)
                        {
                            atom.Other = LaneOf(cond.Other, where);
                        }

                        break;
                }

                into.Add(atom);
            }

            public int CondOf(Cond? cond, string where, params Cond[] extra)
            {
                var parts = new List<Atom>();
                foreach (var e in extra)
                {
                    AtomsOf(e, where, parts);
                }

                if (cond != null)
                {
                    AtomsOf(cond, where, parts);
                }

                if (parts.Count == 0)
                {
                    return -1;
                }

                parts.Sort((a, b) =>
                {
                    var c = a.Lane.CompareTo(b.Lane);
                    c = c != 0 ? c : a.Op.CompareTo(b.Op);
                    c = c != 0 ? c : a.Other.CompareTo(b.Other);
                    c = c != 0 ? c : a.Value.CompareTo(b.Value);
                    return c != 0 ? c : a.Negated.CompareTo(b.Negated);
                });
                for (var i = parts.Count - 1; i > 0; i--)
                {
                    var a = parts[i];
                    var b = parts[i - 1];
                    if (a.Lane == b.Lane && a.Op == b.Op && a.Other == b.Other && a.Value == b.Value && a.Negated == b.Negated)
                    {
                        parts.RemoveAt(i);
                    }
                }

                var key = string.Join(";", parts.ConvertAll(a => a.Op + "," + a.Negated + "," + a.Lane + "," + a.Other + "," + a.Value));
                if (condByKey.TryGetValue(key, out var index))
                {
                    return index;
                }

                index = conds.Count;
                conds.Add(new CondRange { First = atoms.Count, Count = parts.Count });
                atoms.AddRange(parts);
                condByKey[key] = index;
                return index;
            }

            ulong ChannelBits(string[]? names, string where)
            {
                var bits = 0ul;
                if (names == null)
                {
                    for (var c = 0; c < channels.Count; c++)
                    {
                        bits |= 1ul << c;
                    }

                    return bits;
                }

                foreach (var name in names)
                {
                    bits |= 1ul << ChannelOf(name, where);
                }

                return bits;
            }

            public CompiledPack Build()
            {
                var denies = new List<DenyEntry>();
                var masks = new List<MaskEntry>();
                var scales = new List<ScaleEntry>();
                var scaleTimes = new List<(ulong Channels, int Times)>();
                var converts = new List<ConvertEntry>();
                var absorbs = new List<AbsorbEntry>();
                var absorbChannels = new List<int>();
                var flows = new List<FlowTerm>();
                var bounds = new List<BoundsEntry>();
                var eventEntries = new List<EventEntry>();
                var written = new List<int>();
                var allAdmit = 0ul;
                for (var c = 0; c < channels.Count; c++)
                {
                    for (var k = 0; k < kinds.Count; k++)
                    {
                        allAdmit |= 1ul << (c * PackLimits.Kinds + k);
                    }
                }

                for (var r = 0; r < pack.Rules.Count; r++)
                {
                    var where = "rule " + r;
                    switch (pack.Rules[r])
                    {
                        case DenyRule deny:
                        {
                            var channelBits = ChannelBits(deny.Channels, where);
                            var kindBits = 0ul;
                            if (deny.Kinds == null)
                            {
                                kindBits = (1ul << kinds.Count) - 1;
                            }
                            else
                            {
                                foreach (var kind in deny.Kinds)
                                {
                                    kindBits |= 1ul << KindOf(kind, where);
                                }
                            }

                            var bits = 0ul;
                            for (var c = 0; c < channels.Count; c++)
                            {
                                if ((channelBits >> c & 1) != 0)
                                {
                                    bits |= kindBits << (c * PackLimits.Kinds);
                                }
                            }

                            denies.Add(new DenyEntry { Cond = CondOf(deny.When, where), Bits = bits });
                            break;
                        }

                        case DenyByMaskRule mask:
                            masks.Add(new MaskEntry { Lane = LaneOf(mask.Lane, where), ByKind = mask.ByKind ? 1 : 0 });
                            break;
                        case ScaleRule scale:
                            if (scale.Channels == null)
                            {
                                Problem(where + ": scale needs its channels");
                            }

                            if (scale.Times < 0 || scale.Times > PackLimits.ScaleProduct)
                            {
                                Problem(where + ": scale times " + scale.Times + " is outside 0 to " + PackLimits.ScaleProduct);
                            }

                            scales.Add(new ScaleEntry { Cond = CondOf(scale.When, where), SourceCond = CondOf(scale.SourceWhen, where) });
                            scaleTimes.Add((scale.Channels != null ? ChannelBits(scale.Channels, where) : 0ul, scale.Times));
                            break;
                        case ConvertRule convert:
                        {
                            var den = convert.Denominator;
                            var shift = 0;
                            while (shift < 31 && 1 << shift < den)
                            {
                                shift++;
                            }

                            if (den < 1 || den > PackLimits.ConvertDenominator || 1 << shift != den)
                            {
                                Problem(where + ": convert denominator " + den + " must be a power of two from 1 to " + PackLimits.ConvertDenominator);
                            }

                            if (convert.Numerator < 0 || convert.Numerator > den)
                            {
                                Problem(where + ": convert numerator " + convert.Numerator + " must be from 0 to the denominator " + den);
                            }

                            var from = ChannelOf(convert.From, where);
                            var to = ChannelOf(convert.To, where);
                            if (from == to)
                            {
                                Problem(where + ": convert from and to are the same channel");
                            }

                            converts.Add(new ConvertEntry { Cond = CondOf(convert.When, where), From = from, To = to, Numerator = convert.Numerator, Shift = shift });
                            break;
                        }

                        case AbsorbRule absorb:
                        {
                            var budget = LaneOf(absorb.Budget, where);
                            var listedChannels = absorb.Channels ?? Array.Empty<string>();
                            if (absorb.Channels == null)
                            {
                                Problem(where + ": absorb needs its channels");
                            }

                            absorbs.Add(new AbsorbEntry { Budget = budget, First = absorbChannels.Count, Count = listedChannels.Length });
                            var seen = 0ul;
                            foreach (var name in listedChannels)
                            {
                                var c = ChannelOf(name, where);
                                if ((seen >> c & 1) != 0)
                                {
                                    Problem(where + ": channel '" + name + "' is listed twice");
                                }

                                seen |= 1ul << c;
                                absorbChannels.Add(c);
                            }

                            Write(written, budget);
                            break;
                        }

                        case FlowRule flow:
                        {
                            var lane = LaneOf(flow.Lane, where);
                            var seen = 0ul;
                            foreach (var (names, sign) in new[] { (flow.Add, 1), (flow.Subtract, -1) })
                            {
                                if (names == null)
                                {
                                    Problem(where + ": flow " + (sign > 0 ? "add" : "subtract") + " is null (use an empty list)");
                                    continue;
                                }

                                foreach (var name in names)
                                {
                                    var c = ChannelOf(name, where);
                                    if ((seen >> c & 1) != 0)
                                    {
                                        Problem(where + ": channel '" + name + "' is listed twice");
                                    }

                                    seen |= 1ul << c;
                                    flows.Add(new FlowTerm { Lane = lane, Channel = c, Sign = sign });
                                }
                            }

                            Write(written, lane);
                            break;
                        }

                        case BoundsRule b:
                        {
                            var entry = new BoundsEntry { Lane = LaneOf(b.Lane, where), FloorLane = -1, CeilingLane = -1 };
                            if (b.Floor.Present)
                            {
                                entry.HasFloor = 1;
                                entry.FloorLane = b.Floor.Lane != null ? LaneOf(b.Floor.Lane, where) : -1;
                                entry.Floor = b.Floor.Value;
                            }

                            if (b.Ceiling.Present)
                            {
                                entry.HasCeiling = 1;
                                entry.CeilingLane = b.Ceiling.Lane != null ? LaneOf(b.Ceiling.Lane, where) : -1;
                                entry.Ceiling = b.Ceiling.Value;
                            }

                            bounds.Add(entry);
                            Write(written, entry.Lane);
                            break;
                        }

                        case EventRule e:
                            eventEntries.Add(new EventEntry { Enter = CondOf(e.Enter, where), Stay = e.Stay != null ? CondOf(e.Stay, where) : -2 });
                            break;
                    }
                }

                if (scales.Count > PackLimits.ScaleRules)
                {
                    Problem("a pack has at most " + PackLimits.ScaleRules + " scale rules, this one has " + scales.Count);
                }

                var factors = new long[channels.Count * 256];
                for (var c = 0; c < channels.Count; c++)
                {
                    var product = 1L;
                    for (var r = 0; r < scaleTimes.Count; r++)
                    {
                        if ((scaleTimes[r].Channels >> c & 1) != 0)
                        {
                            product *= Math.Max(1, scaleTimes[r].Times);
                        }
                    }

                    if (product > PackLimits.ScaleProduct)
                    {
                        Problem("scale rules on channel '" + pack.Channels[c] + "' multiply to " + product + ", above " + PackLimits.ScaleProduct);
                    }

                    for (var m = 0; m < 256; m++)
                    {
                        var factor = 1L;
                        for (var r = 0; r < scaleTimes.Count && r < PackLimits.ScaleRules; r++)
                        {
                            if ((m >> r & 1) != 0 && (scaleTimes[r].Channels >> c & 1) != 0)
                            {
                                factor *= scaleTimes[r].Times;
                            }
                        }

                        factors[c * 256 + m] = factor;
                    }
                }

                var noSource = 0u;
                for (var r = 0; r < scales.Count && r < PackLimits.ScaleRules; r++)
                {
                    if (scales[r].SourceCond < 0)
                    {
                        noSource |= 1u << r;
                    }
                }

                var templateBuilds = new List<TemplateBuild>();
                foreach (var template in pack.Templates)
                {
                    templateBuilds.Add(BuildTemplate(template));
                }

                if (problems.Count > 0)
                {
                    throw new PackException(problems);
                }

                var tables = Native.Zeroed<PackTables>(1);
                tables->LaneCount = lanes.Count;
                tables->ChannelCount = channels.Count;
                tables->KindCount = kinds.Count;
                tables->ClassCount = channels.Count * 2;
                tables->EventCount = events.Count;
                tables->EventsLane = eventsLane;
                tables->EnteredLane = enteredLane;
                tables->KindLane = pack.KindLane != null ? lanes[pack.KindLane] : -1;
                tables->AllAdmit = allAdmit;
                tables->Atoms = Copy(atoms);
                tables->Conds = Copy(conds);
                tables->CondCount = conds.Count;
                tables->Denies = Copy(denies);
                tables->DenyCount = denies.Count;
                tables->Masks = Copy(masks);
                tables->MaskCount = masks.Count;
                tables->Scales = Copy(scales);
                tables->ScaleCount = scales.Count;
                tables->NoSourceFlags = noSource;
                tables->Factors = Copy(new List<long>(factors));
                tables->Converts = Copy(converts);
                tables->ConvertCount = converts.Count;
                tables->Absorbs = Copy(absorbs);
                tables->AbsorbCount = absorbs.Count;
                tables->AbsorbChannels = Copy(absorbChannels);
                tables->Flows = Copy(flows);
                tables->FlowCount = flows.Count;
                tables->Bounds = Copy(bounds);
                tables->BoundsCount = bounds.Count;
                tables->Written = Copy(written);
                tables->WrittenCount = written.Count;
                tables->Events = Copy(eventEntries);
                tables->TemplateCount = templateBuilds.Count;
                tables->Templates = Native.Zeroed<TemplateTable>(templateBuilds.Count);
                for (var t = 0; t < templateBuilds.Count; t++)
                {
                    var build = templateBuilds[t];
                    var into = tables->Templates + t;
                    into->Defaults = Copy(new List<int>(build.Defaults));
                    into->Preds = Copy(build.Preds);
                    into->PredCount = build.Preds.Count;
                    into->Groups = Copy(build.Groups);
                    into->GroupCount = build.Groups.Count;
                    into->Terms = Copy(build.Terms);
                    into->TermCount = build.Terms.Count;
                    into->TimesLanes = Copy(build.TimesLanes);
                    into->TimesLaneCount = build.TimesLanes.Count;
                }

                return new CompiledPack(pack, tables, Names(lanes), Names(channels), Names(kinds), Names(events), Names(templates), Names(cards));
            }

            static string[] Names(Dictionary<string, int> map)
            {
                var names = new string[map.Count];
                foreach (var pair in map)
                {
                    names[pair.Value] = pair.Key;
                }

                return names;
            }

            static void Write(List<int> written, int lane)
            {
                if (!written.Contains(lane))
                {
                    written.Add(lane);
                }
            }

            sealed class TemplateBuild
            {
                public int[] Defaults = Array.Empty<int>();
                public List<int> Preds = new List<int>();
                public List<OpGroup> Groups = new List<OpGroup>();
                public List<int> TimesLanes = new List<int>();
                public List<OpTerm> Terms = new List<OpTerm>();
            }

            TemplateBuild BuildTemplate(TemplateSpec template)
            {
                var where = "template '" + template.Name + "'";
                var build = new TemplateBuild { Defaults = new int[lanes.Count] };
                for (var l = 0; l < pack.Lanes.Count; l++)
                {
                    build.Defaults[l] = pack.Lanes[l].Default;
                }

                foreach (var pair in template.Values)
                {
                    build.Defaults[LaneOf(pair.Key, where)] = pair.Value;
                }

                foreach (var pair in template.Cards)
                {
                    if (!cards.TryGetValue(pair.Key, out var card))
                    {
                        Problem(where + ": unknown card '" + pair.Key + "'");
                        continue;
                    }

                    build.Defaults[LaneOf(pack.Cards[card].Lane, where)] = pair.Value;
                }

                var ops = new List<(OpSpec Op, int Cond, string Where)>();
                foreach (var op in template.Ops)
                {
                    ops.Add((op, CondOf(op.When, where), where));
                }

                foreach (var card in pack.Cards)
                {
                    var cardWhere = "card '" + card.Name + "'";
                    foreach (var op in card.Ops)
                    {
                        ops.Add((op, CondOf(op.When, cardWhere, Cond.Gt(card.Lane, 0)), cardWhere));
                    }
                }

                var groupKeys = new SortedDictionary<int, SortedDictionary<(int Pred, int Lane, int Sign), (long Amount, long Gain, long Loss)>>();
                var predIndex = new Dictionary<int, int>();
                var budget = 0L;
                foreach (var (op, cond, owner) in ops)
                {
                    var opWhere = owner + " op on '" + op.Channel + "'";
                    var channel = ChannelOf(op.Channel, opWhere);
                    var kind = KindOf(op.Kind, opWhere);
                    if (op.Amount < -PackLimits.OpAmount || op.Amount > PackLimits.OpAmount)
                    {
                        Problem(opWhere + ": amount " + op.Amount + " is outside plus or minus " + PackLimits.OpAmount);
                    }

                    budget += Math.Abs((long)op.Amount);
                    var lane = op.Times != null ? LaneOf(op.Times, opWhere) : -1;
                    var pred = -1;
                    if (cond >= 0 && !predIndex.TryGetValue(cond, out pred))
                    {
                        pred = build.Preds.Count;
                        predIndex[cond] = pred;
                        build.Preds.Add(cond);
                    }

                    var groupKey = channel * PackLimits.Kinds + kind;
                    if (!groupKeys.TryGetValue(groupKey, out var terms))
                    {
                        terms = new SortedDictionary<(int, int, int), (long, long, long)>();
                        groupKeys[groupKey] = terms;
                    }

                    var sign = lane >= 0 ? (op.Amount < 0 ? -1 : 1) : 0;
                    var key = (pred, lane, sign);
                    terms.TryGetValue(key, out var sum);
                    if (lane >= 0)
                    {
                        sum.Amount += op.Amount;
                    }
                    else if (op.Amount > 0)
                    {
                        sum.Gain += op.Amount;
                    }
                    else
                    {
                        sum.Loss += op.Amount;
                    }

                    terms[key] = sum;
                }

                if (budget > PackLimits.TemplateAmountBudget)
                {
                    Problem(where + ": the ops of this template and all cards add up to " + budget.ToString(CultureInfo.InvariantCulture) + " in absolute amount, above " + PackLimits.TemplateAmountBudget);
                }

                foreach (var pair in groupKeys)
                {
                    var group = new OpGroup { Channel = pair.Key / PackLimits.Kinds, AdmitShift = pair.Key, TermFirst = build.Terms.Count };
                    for (var category = 1; category <= 3; category++)
                    {
                        foreach (var term in pair.Value)
                        {
                            var key = term.Key;
                            if (key.Lane < 0 && key.Pred < 0)
                            {
                                if (category == 1)
                                {
                                    group.ConstGain += term.Value.Gain;
                                    group.ConstLoss += term.Value.Loss;
                                }

                                continue;
                            }

                            var of = key.Lane < 0 ? 1 : key.Pred < 0 ? 2 : 3;
                            if (of == category)
                            {
                                build.Terms.Add(new OpTerm { Pred = key.Pred, Lane = key.Lane, Amount = term.Value.Amount, Gain = term.Value.Gain, Loss = term.Value.Loss });
                            }
                        }

                        if (category == 1)
                        {
                            group.CondConstEnd = build.Terms.Count - group.TermFirst;
                        }
                        else if (category == 2)
                        {
                            group.LaneEnd = build.Terms.Count - group.TermFirst;
                        }
                    }

                    group.TermCount = build.Terms.Count - group.TermFirst;
                    long condGain = 0;
                    long condLoss = 0;
                    for (var t = group.TermFirst; t < build.Terms.Count; t++)
                    {
                        var term = build.Terms[t];
                        if (term.Lane < 0)
                        {
                            condGain += term.Gain;
                            condLoss -= term.Loss;
                        }
                        else
                        {
                            group.LaneAmount += Math.Abs(term.Amount);
                            if (!build.TimesLanes.Contains(term.Lane))
                            {
                                build.TimesLanes.Add(term.Lane);
                            }
                        }
                    }

                    group.CondBound = Math.Max(condGain, condLoss);
                    build.Groups.Add(group);
                }

                return build;
            }

            static T* Copy<T>(List<T> values) where T : unmanaged
            {
                var copy = Native.Zeroed<T>(values.Count);
                for (var i = 0; i < values.Count; i++)
                {
                    copy[i] = values[i];
                }

                return copy;
            }
        }
    }
}
