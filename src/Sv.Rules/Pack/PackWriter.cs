using System;
using System.Text;

namespace Sv.Rules
{
    public static class PackWriter
    {
        public static PackSource Write(ContentPack pack) =>
            new PackSource(Values(pack.Schema), Rules(pack), Cards(pack.Schema), Templates(pack.Schema));

        static JsonWriter Begin(int nextLane, bool withCounter, ulong seed = 0ul)
        {
            var writer = new JsonWriter();
            writer.BeginObject().Key("schema").Int(PackReader.SchemaVersion).KeyDone();
            if (withCounter)
            {
                writer.Key("nextLane").Int(nextLane).KeyDone();
            }

            if (seed != 0ul)
            {
                writer.Key("seed").UInt(seed).KeyDone();
            }

            writer.Key("rows").BeginArray();
            return writer;
        }

        static string End(JsonWriter writer)
        {
            writer.Line().EndArray().KeyDone().EndObject().Line();
            return writer.ToString();
        }

        public static string Values(PackSchema schema)
        {
            var writer = Begin(schema.NextLane, true, schema.Seed);
            for (var i = 0; i < schema.Stats.Length; i++)
            {
                var row = schema.Stats[i];
                writer.Line().BeginObject().Key("id").Int(row.Id).KeyDone().Key("name").String(row.Name).KeyDone().Key("class").String("stat").KeyDone()
                    .Key("default").Scaled(row.IntDefault, row.Scale).KeyDone();
                if (row.Scale != StatLaw.MinScale)
                {
                    writer.Key("scale").Int(row.Scale).KeyDone();
                }

                if (row.Derive.Length > 0)
                {
                    writer.Key("derive").BeginArray();
                    for (var d = 0; d < row.Derive.Length; d++)
                    {
                        var source = row.Derive[d];
                        var from = schema.Find(ValueClass.Stat, source.From);
                        writer.BeginObject().Key("from").String(from == null ? string.Empty : from.Name).KeyDone();
                        if (source.IsCurve)
                        {
                            var fromScale = from == null ? StatLaw.MinScale : from.Scale;
                            writer.Key("min").Scaled(source.CurveMin, fromScale).KeyDone()
                                .Key("step").Scaled(source.CurveStep, fromScale).KeyDone()
                                .Key("curve").BeginArray();
                            for (var k = 0; k < source.Curve.Length; k++)
                            {
                                writer.Scaled(source.Curve[k], row.Scale);
                            }

                            writer.EndArray().KeyDone();
                        }
                        else
                        {
                            writer.Key("coefficient").Scaled(source.Coefficient, StatLaw.Whole).KeyDone();
                        }

                        writer.EndObject();
                    }

                    writer.EndArray().KeyDone();
                }

                writer.EndObject();
            }

            for (var i = 0; i < schema.Intrinsics.Length; i++)
            {
                var row = schema.Intrinsics[i];
                writer.Line().BeginObject().Key("id").Int(row.Id).KeyDone().Key("name").String(row.Name).KeyDone().Key("class").String("intrinsic").KeyDone()
                    .Key("default").Scaled(row.IntDefault, row.Scale).KeyDone();
                if (row.Scale != StatLaw.MinScale)
                {
                    writer.Key("scale").Int(row.Scale).KeyDone();
                }

                if (row.Role != IntrinsicRole.Plain)
                {
                    writer.Key("role").String(RoleName(row.Role)).KeyDone();
                }

                writer.EndObject();
            }

            for (var i = 0; i < schema.Events.Length; i++)
            {
                var row = schema.Events[i];
                writer.Line().BeginObject().Key("lane").Int(LaneNumbers.LaneOfEvent(row.Id)).KeyDone().Key("word").Int(LaneNumbers.WordOfEvent(row.Id)).KeyDone()
                    .Key("name").String(row.Name).KeyDone().Key("class").String("event").KeyDone().Key("default").Bool(row.EventDefault).KeyDone();
                if (row.Starts.Length > 0)
                {
                    writer.Key("starts").String(row.Starts).KeyDone();
                }

                writer.EndObject();
            }

            for (var i = 0; i < schema.Channels.Length; i++)
            {
                var row = schema.Channels[i];
                writer.Line().BeginObject().Key("id").Int(row.Id).KeyDone().Key("name").String(row.Name).KeyDone().Key("class").String("channel").KeyDone();
                if (row.Timeline)
                {
                    writer.Key("timeline").Bool(true).KeyDone();
                }

                if (row.Into >= 0)
                {
                    writer.Key("into").String(schema.NameOf(ValueClass.Intrinsic, row.Into)).KeyDone();
                }

                if (row.Assigns)
                {
                    writer.Key("mode").String(AssignModes.NameOf(row.IntoMode)).KeyDone();
                }

                writer.EndObject();
            }

            for (var i = 0; i < schema.Kinds.Length; i++)
            {
                var row = schema.Kinds[i];
                writer.Line().BeginObject().Key("id").Int(row.Id).KeyDone().Key("name").String(row.Name).KeyDone().Key("class").String("kind").KeyDone().EndObject();
            }

            return End(writer);
        }

        static string RoleName(IntrinsicRole role)
        {
            switch (role)
            {
                case IntrinsicRole.Kind: return "kind";
                case IntrinsicRole.Owner: return "owner";
                case IntrinsicRole.Reference: return "reference";
                default: return "plain";
            }
        }

        static readonly string[] ShapeNames = { "added", "additive", "multi", "cap" };

        static int ShapeScale(ShapeKind kind, int scale) => kind == ShapeKind.Additive || kind == ShapeKind.Multi ? StatLaw.Whole : scale;

        static int ScaleOf(PackSchema schema, ValueClass valueClass, int id) => schema.Find(valueClass, id)?.Scale ?? StatLaw.MinScale;

        public static string Cards(PackSchema schema)
        {
            var writer = Begin(0, false);
            for (var i = 0; i < schema.Cards.Length; i++)
            {
                var card = schema.Cards[i];
                writer.Line().BeginObject().Key("id").Int(card.Id).KeyDone().Key("name").String(card.Name).KeyDone()
                    .Key("value").String(schema.NameOf(ValueClass.Intrinsic, card.Intrinsic)).KeyDone();
                if (card.Modifiers.Length > 0)
                {
                    writer.Key("modifiers").BeginArray();
                    for (var m = 0; m < card.Modifiers.Length; m++)
                    {
                        var modifier = card.Modifiers[m];
                        writer.BeginObject().Key("stat").String(schema.NameOf(ValueClass.Stat, modifier.Stat)).KeyDone()
                            .Key(ShapeNames[(int)modifier.Kind]).Scaled(modifier.Value, ShapeScale(modifier.Kind, ScaleOf(schema, ValueClass.Stat, modifier.Stat))).KeyDone();
                        if (modifier.PerCount)
                        {
                            writer.Key("perCount").Bool(true).KeyDone();
                        }

                        if (modifier.Layer != StatLayer.Core)
                        {
                            writer.Key("layer").String("external").KeyDone();
                        }

                        writer.EndObject();
                    }

                    writer.EndArray().KeyDone();
                }

                writer.EndObject();
            }

            return End(writer);
        }

        public static string Templates(PackSchema schema)
        {
            var writer = Begin(0, false);
            for (var i = 0; i < schema.Templates.Length; i++)
            {
                var template = schema.Templates[i];
                writer.Line().BeginObject().Key("id").Int(template.Id).KeyDone().Key("name").String(template.Name).KeyDone();
                if (template.Settings.Length > 0)
                {
                    writer.Key("values").BeginObject();
                    for (var s = 0; s < template.Settings.Length; s++)
                    {
                        var setting = template.Settings[s];
                        writer.Key(schema.NameOf(setting.Class, setting.Id));
                        switch (setting.Class)
                        {
                            case ValueClass.Event: writer.Bool(setting.Level); break;
                            default: writer.Scaled(setting.Int, ScaleOf(schema, setting.Class, setting.Id)); break;
                        }

                        writer.KeyDone();
                    }

                    writer.EndObject().KeyDone();
                }

                if (template.Forwards.Length > 0)
                {
                    writer.Key("forwards").BeginArray();
                    for (var f = 0; f < template.Forwards.Length; f++)
                    {
                        var forward = template.Forwards[f];
                        var via = forward.Via == schema.OwnerIntrinsic ? "Owner" : schema.NameOf(ValueClass.Intrinsic, forward.Via);
                        writer.BeginObject().Key("channel").String(schema.NameOf(ValueClass.Channel, forward.Channel)).KeyDone().Key("to").String(via).KeyDone().EndObject();
                    }

                    writer.EndArray().KeyDone();
                }

                writer.EndObject();
            }

            return End(writer);
        }

        public static string Rules(ContentPack pack)
        {
            var rules = AllRules(pack);
            var writer = Begin(0, false);
            for (var i = 0; i < rules.Length; i++)
            {
                writer.Line();
                Rule(writer, pack, rules[i]);
            }

            return End(writer);
        }

        public static Rule[] AllRules(ContentPack pack)
        {
            var count = pack.Chains.Length + pack.Gates.Length + pack.Emits.Length + pack.Edges.Length + pack.Flows.Length + pack.Folds.Length + pack.Events.Length;
            var rules = new Rule[count];
            var at = 0;
            at = Append(rules, at, pack.Chains);
            at = Append(rules, at, pack.Gates);
            at = Append(rules, at, pack.Emits);
            at = Append(rules, at, pack.Edges);
            at = Append(rules, at, pack.Flows);
            at = Append(rules, at, pack.Folds);
            Append(rules, at, pack.Events);
            var ids = new int[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = rules[i].Id;
            }

            Array.Sort(ids, rules);
            return rules;
        }

        static int Append(Rule[] rules, int at, Rule[] layer)
        {
            for (var i = 0; i < layer.Length; i++)
            {
                rules[at + i] = layer[i];
            }

            return at + layer.Length;
        }

        static void Head(JsonWriter writer, PackSchema schema, Rule rule, string layer)
        {
            writer.BeginObject().Key("id").Int(rule.Id).KeyDone().Key("name").String(rule.Name).KeyDone().Key("layer").String(layer).KeyDone();
            if (rule.Card >= 0)
            {
                writer.Key("card").String(schema.NameOf(ValueClass.Intrinsic, rule.Card)).KeyDone();
            }
        }

        static void When(JsonWriter writer, ContentPack pack, Rule rule)
        {
            if (!rule.When.IsConstantTrue)
            {
                writer.Key("when");
                Expr(writer, pack, rule.When);
                writer.KeyDone();
            }
        }

        static void Rule(JsonWriter writer, ContentPack pack, Rule rule)
        {
            var schema = pack.Schema;
            switch (rule)
            {
                case ChainRule chain:
                    Head(writer, schema, chain, "chain");
                    if (chain.Tags.Length > 0)
                    {
                        writer.Key("tags").BeginArray();
                        for (var i = 0; i < chain.Tags.Length; i++)
                        {
                            writer.String(schema.Tags[chain.Tags[i]]);
                        }

                        writer.EndArray().KeyDone();
                    }

                    writer.Key("when");
                    Expr(writer, pack, chain.When);
                    writer.KeyDone().EndObject();
                    break;
                case GateRule gate:
                    Head(writer, schema, gate, "gate");
                    writer.Key("priority").Int(gate.Priority).KeyDone();
                    if (gate.Group >= 0)
                    {
                        writer.Key("group").Int(gate.Group).KeyDone();
                    }

                    When(writer, pack, gate);
                    writer.Key("then").BeginArray();
                    if (gate.Admit != AdmitVerdict.None)
                    {
                        writer.BeginObject().Key(gate.Admit == AdmitVerdict.Deny ? "deny" : "allow").BeginObject().EndObject().KeyDone().EndObject();
                    }

                    for (var i = 0; i < gate.Shapes.Length; i++)
                    {
                        var shape = gate.Shapes[i];
                        writer.BeginObject().Key(ShapeNames[(int)shape.Kind]);
                        if (shape.Cell != null)
                        {
                            writer.BeginObject().Key("cell").String(CellText(schema, shape.Cell)).KeyDone().EndObject();
                        }
                        else
                        {
                            writer.Scaled(shape.Value, ShapeScale(shape.Kind, StatLaw.MinScale));
                        }

                        writer.KeyDone().EndObject();
                    }

                    for (var i = 0; i < gate.Absorbs.Length; i++)
                    {
                        var absorb = gate.Absorbs[i];
                        writer.BeginObject().Key("absorb").BeginObject()
                            .Key("budget").String(schema.NameOf(ValueClass.Intrinsic, absorb.Budget)).KeyDone()
                            .Key("per").String(absorb.Per == AbsorbPer.Hit ? "Hit" : "Amount").KeyDone()
                            .Key("order").String(absorb.Order == AbsorbOrder.BySource ? "BySource" : "ByAmountDescending").KeyDone()
                            .EndObject().KeyDone().EndObject();
                    }

                    for (var i = 0; i < gate.Spends.Length; i++)
                    {
                        writer.BeginObject().Key("spend").String(schema.NameOf(ValueClass.Intrinsic, gate.Spends[i])).KeyDone().EndObject();
                    }

                    writer.EndArray().KeyDone().EndObject();
                    break;
                case EmitRule emit:
                    Head(writer, schema, emit, "emit");
                    writer.Key("depthLimit").Int(emit.DepthLimit).KeyDone();
                    if (emit.NextFrame)
                    {
                        writer.Key("nextFrame").Bool(true).KeyDone();
                    }

                    When(writer, pack, emit);
                    writer.Key("then").BeginArray();
                    for (var i = 0; i < emit.Actions.Length; i++)
                    {
                        Action(writer, schema, emit.Actions[i], emit.Per, false);
                    }

                    writer.EndArray().KeyDone().EndObject();
                    break;
                case EdgeRule edge:
                    Head(writer, schema, edge, "edge");
                    writer.Key("event").String(schema.NameOf(ValueClass.Event, edge.Event)).KeyDone().Key("edge").String(edge.Rising ? "enter" : "leave").KeyDone();
                    When(writer, pack, edge);
                    writer.Key("then").BeginArray();
                    for (var i = 0; i < edge.Actions.Length; i++)
                    {
                        Action(writer, schema, edge.Actions[i], EmitPer.Frame, true);
                    }

                    writer.EndArray().KeyDone().EndObject();
                    break;
                case FlowRule flow:
                    Head(writer, schema, flow, "flow");
                    When(writer, pack, flow);
                    writer.Key("then").BeginArray();
                    var at = 0;
                    while (at < flow.Terms.Length)
                    {
                        var first = flow.Terms[at];
                        var term = first.Assigns ? AssignModes.NameOf(first.Mode) : first.Sign > 0 ? "add" : "subtract";
                        writer.BeginObject().Key(term).BeginArray().String(schema.NameOf(ValueClass.Intrinsic, first.Intrinsic));
                        while (at < flow.Terms.Length && flow.Terms[at].Intrinsic == first.Intrinsic && flow.Terms[at].Sign == first.Sign && flow.Terms[at].Mode == first.Mode)
                        {
                            writer.String(schema.NameOf(ValueClass.Channel, flow.Terms[at].Channel));
                            at++;
                        }

                        writer.EndArray().KeyDone().EndObject();
                    }

                    writer.EndArray().KeyDone().EndObject();
                    break;
                case FoldRule fold:
                    Head(writer, schema, fold, "fold");
                    writer.Key("value").String(schema.NameOf(ValueClass.Intrinsic, fold.Intrinsic)).KeyDone();
                    BoundKey(writer, schema, "floor", fold.Floor);
                    BoundKey(writer, schema, "ceiling", fold.Ceiling);
                    writer.EndObject();
                    break;
                case EventRule raised:
                    Head(writer, schema, raised, "event");
                    writer.Key("event").String(schema.NameOf(ValueClass.Event, raised.Event)).KeyDone().Key("enter");
                    Expr(writer, pack, raised.Enter);
                    writer.KeyDone().Key("stay");
                    Expr(writer, pack, raised.Stay);
                    writer.KeyDone().EndObject();
                    break;
            }
        }

        static void BoundKey(JsonWriter writer, PackSchema schema, string key, Bound bound)
        {
            if (bound.Kind == BoundKind.Constant)
            {
                writer.Key(key).Int(bound.Constant).KeyDone();
            }
            else if (bound.Kind == BoundKind.Stat)
            {
                writer.Key(key).BeginObject().Key("stat").String(schema.NameOf(ValueClass.Stat, bound.Stat)).KeyDone().EndObject().KeyDone();
            }
        }

        static void Action(JsonWriter writer, PackSchema schema, EmitAction action, EmitPer per, bool edge)
        {
            writer.BeginObject().Key("emit").BeginObject();
            string to;
            switch (action.To)
            {
                case EmitTo.Self: to = "Self"; break;
                case EmitTo.Source: to = "Source"; break;
                case EmitTo.Owner: to = "Owner"; break;
                default: to = schema.NameOf(ValueClass.Intrinsic, action.Via); break;
            }

            writer.Key("to").String(to).KeyDone();
            writer.Key("channel").String(action.SameChannel ? "same" : schema.NameOf(ValueClass.Channel, action.Channel)).KeyDone();
            writer.Key("amount");
            switch (action.Amount)
            {
                case AmountKind.Offered: writer.String("Offered"); break;
                case AmountKind.Taken: writer.String("Taken"); break;
                case AmountKind.NotTaken: writer.String("NotTaken"); break;
                case AmountKind.Cell: writer.BeginObject().Key("cell").String(action.Cell == null ? "?" : CellText(schema, action.Cell)).KeyDone().EndObject(); break;
                default: writer.Int(action.Constant); break;
            }

            writer.KeyDone();
            if (action.ScaleStat >= 0)
            {
                writer.Key("scale").String(schema.NameOf(ValueClass.Stat, action.ScaleStat)).KeyDone();
            }
            else if (action.ScaleIntrinsic >= 0)
            {
                writer.Key("scale").String(schema.NameOf(ValueClass.Intrinsic, action.ScaleIntrinsic)).KeyDone();
            }

            if (!edge)
            {
                writer.Key("per").String(PerName(per)).KeyDone();
                if (action.Over.Length > 0)
                {
                    writer.Key("over").BeginArray();
                    for (var i = 0; i < action.Over.Length; i++)
                    {
                        writer.String(schema.NameOf(ValueClass.Channel, (int)action.Over[i]));
                    }

                    writer.EndArray().KeyDone();
                }
            }

            writer.Key("origin").String(action.Origin == RecordOrigin.Reflected ? "Reflected" : "Triggered").KeyDone();
            writer.EndObject().KeyDone().EndObject();
        }

        static string PerName(EmitPer per)
        {
            switch (per)
            {
                case EmitPer.Hit: return "Hit";
                case EmitPer.Source: return "Source";
                case EmitPer.Frame: return "Frame";
                default: return "Record";
            }
        }

        static string FieldName(RecordField field)
        {
            switch (field)
            {
                case RecordField.Kind: return "kind";
                case RecordField.Channel: return "channel";
                case RecordField.Source: return "source";
                case RecordField.Origin: return "origin";
                case RecordField.Depth: return "depth";
                case RecordField.Amount: return "amount";
                case RecordField.Hits: return "hits";
                case RecordField.Taken: return "taken";
                default: return "notTaken";
            }
        }

        static string OpName(Op op)
        {
            switch (op)
            {
                case Op.And: return "and";
                case Op.Or: return "or";
                case Op.Not: return "not";
                case Op.Eq: return "eq";
                case Op.Ne: return "ne";
                case Op.Lt: return "lt";
                case Op.Le: return "le";
                case Op.Gt: return "gt";
                case Op.Ge: return "ge";
                case Op.InSet: return "in";
                case Op.Bit: return "bit";
                case Op.Below: return "below";
                case Op.Add: return "add";
                case Op.Sub: return "sub";
                case Op.Mul: return "mul";
                case Op.Min: return "min";
                case Op.Max: return "max";
                case Op.Neg: return "neg";
                case Op.ToFloat: return "float";
                case Op.BoundOf: return "bound";
                default: return "if";
            }
        }

        static string AggregateName(Measure measure, AggregateGroup group)
        {
            var name = measure == Measure.Offered ? "offered" : measure == Measure.Taken ? "taken" : "notTaken";
            return group == AggregateGroup.Frame ? "frame" + char.ToUpperInvariant(name[0]) + name.Substring(1) : name;
        }

        static void Expr(JsonWriter writer, ContentPack pack, Expression e)
        {
            var schema = pack.Schema;
            switch (e.Op)
            {
                case Op.BoolConst:
                    writer.Bool(e.Integer != 0);
                    return;
                case Op.IntConst:
                    writer.Int(e.Integer);
                    return;
                case Op.FloatConst:
                    writer.Float(e.Real);
                    return;
                case Op.KindConst:
                    writer.BeginObject().Key("kind").String(schema.NameOf(ValueClass.Kind, e.Id)).KeyDone().EndObject();
                    return;
                case Op.ChannelConst:
                    writer.BeginObject().Key("channel").String(schema.NameOf(ValueClass.Channel, e.Id)).KeyDone().EndObject();
                    return;
                case Op.RecordField:
                    writer.String("record." + FieldName(e.Field));
                    return;
                case Op.Cell:
                    writer.String(CellText(schema, e));
                    return;
                case Op.Roll:
                    writer.BeginObject().Key("roll").Int(e.Integer).KeyDone().EndObject();
                    return;

                case Op.ChannelTotal:
                    writer.BeginObject().Key("total").String(schema.NameOf(ValueClass.Channel, e.Id)).KeyDone().EndObject();
                    return;
                case Op.Aggregate:
                    writer.BeginObject().Key(AggregateName(e.Measure, e.Group)).BeginArray();
                    for (var i = 0; i < e.Set.Length; i++)
                    {
                        writer.String(schema.NameOf(ValueClass.Channel, (int)e.Set[i]));
                    }

                    writer.EndArray().KeyDone().EndObject();
                    return;
                case Op.Chain:
                    writer.BeginObject().Key("chain").String(ChainName(pack, e.Id)).KeyDone().EndObject();
                    return;
                case Op.AnyChain:
                    writer.BeginObject().Key("any").String(e.Id >= 0 && e.Id < schema.Tags.Length ? schema.Tags[e.Id] : "?").KeyDone().EndObject();
                    return;
                case Op.Not:
                case Op.Neg:
                case Op.ToFloat:
                case Op.BoundOf:
                    writer.BeginObject().Key(OpName(e.Op));
                    Expr(writer, pack, e.Args[0]);
                    writer.KeyDone().EndObject();
                    return;
                case Op.InSet:
                {
                    writer.BeginObject().Key("in").BeginArray();
                    Expr(writer, pack, e.Args[0]);
                    writer.BeginArray();
                    var field = e.Args[0].Op == Op.RecordField ? e.Args[0].Field : RecordField.Amount;
                    for (var i = 0; i < e.Set.Length; i++)
                    {
                        if (field == RecordField.Kind)
                        {
                            writer.String(schema.NameOf(ValueClass.Kind, (int)e.Set[i]));
                        }
                        else if (field == RecordField.Channel)
                        {
                            writer.String(schema.NameOf(ValueClass.Channel, (int)e.Set[i]));
                        }
                        else
                        {
                            writer.Int(e.Set[i]);
                        }
                    }

                    writer.EndArray().EndArray().KeyDone().EndObject();
                    return;
                }

                default:
                    writer.BeginObject().Key(OpName(e.Op)).BeginArray();
                    for (var i = 0; i < e.Args.Length; i++)
                    {
                        Expr(writer, pack, e.Args[i]);
                    }

                    writer.EndArray().KeyDone().EndObject();
                    return;
            }
        }

        static string ChainName(ContentPack pack, int id) => pack.Chain(id)?.Name ?? "?";

        static string CellText(PackSchema schema, Expression e)
        {
            string subject;
            switch (e.Subject)
            {
                case Subject.Self: subject = "self"; break;
                case Subject.Source: subject = "source"; break;
                case Subject.Owner: subject = "owner"; break;
                default: subject = schema.NameOf(ValueClass.Intrinsic, e.Via); break;
            }

            return subject + "." + schema.NameOf(e.Class, e.Id);
        }
    }

    public static class PackHash
    {
        const ulong Basis = 14695981039346656037ul;
        const ulong Prime = 1099511628211ul;

        public static ulong Of(ContentPack pack) => Of(PackWriter.Write(pack));

        public static ulong Of(in PackSource canonical)
        {
            var hash = Basis;
            hash = Mix(hash, canonical.Values);
            hash = Mix(hash, canonical.Rules);
            hash = Mix(hash, canonical.Cards);
            hash = Mix(hash, canonical.Templates);
            return hash;
        }

        static ulong Mix(ulong hash, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            unchecked
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash = (hash ^ bytes[i]) * Prime;
                }

                hash = (hash ^ 0xFFul) * Prime;
            }

            return hash;
        }
    }
}
