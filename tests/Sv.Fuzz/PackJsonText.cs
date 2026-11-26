using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Sv.Columns;

namespace Sv.Fuzz;

public static class PackJsonText
{
    public static string CondToText(Cond cond)
    {
        if (cond.Op == CondOp.All)
        {
            return string.Join(" and ", cond.Parts.Select(CondToText));
        }

        var atom = cond.Op switch
        {
            CondOp.Below => cond.Lane + " < " + Number(cond.Value) + "% of " + cond.Other,
            CondOp.Bit => "bit " + cond.Lane + " " + Number(cond.Value),
            CondOp.Event => "event " + cond.Lane,
            CondOp.Entered => "entered " + cond.Lane,
            _ => cond.Lane + " " + Compare(cond.Op) + " " + (cond.Other ?? Number(cond.Value)),
        };
        return cond.Negated ? "not " + atom : atom;
    }

    static string Compare(CondOp op) => op switch
    {
        CondOp.Gt => ">",
        CondOp.Ge => ">=",
        CondOp.Lt => "<",
        CondOp.Le => "<=",
        CondOp.Eq => "==",
        _ => "!=",
    };

    static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static JsonObject ToNode(ColumnPack pack)
    {
        var root = new JsonObject
        {
            ["lanes"] = new JsonArray(pack.Lanes.Select(l => (JsonNode)new JsonObject { ["name"] = l.Name, ["default"] = l.Default }).ToArray()),
            ["channels"] = Names(pack.Channels),
            ["kinds"] = Names(pack.Kinds),
        };
        if (pack.KindLane != null)
        {
            root["kindLane"] = pack.KindLane;
        }

        root["cards"] = new JsonArray(pack.Cards.Select(c => (JsonNode)new JsonObject { ["name"] = c.Name, ["lane"] = c.Lane, ["ops"] = Ops(c.Ops) }).ToArray());
        root["templates"] = new JsonArray(pack.Templates.Select(t =>
        {
            var node = new JsonObject { ["name"] = t.Name };
            if (t.Values.Count > 0)
            {
                node["values"] = Map(t.Values);
            }

            if (t.Cards.Count > 0)
            {
                node["cards"] = Map(t.Cards);
            }

            if (t.Ops.Count > 0)
            {
                node["ops"] = Ops(t.Ops);
            }

            return (JsonNode)node;
        }).ToArray());
        root["rules"] = new JsonArray(pack.Rules.Select(Rule).ToArray());
        var glue = pack.Timelines;
        if (glue.Cards.Count + glue.Outbox.Count + glue.Starts.Count > 0)
        {
            root["timelines"] = new JsonObject
            {
                ["cards"] = Names(glue.Cards),
                ["outbox"] = new JsonArray(glue.Outbox.Select(o =>
                {
                    var node = new JsonObject { ["channel"] = o.Channel };
                    if (o.Target != null)
                    {
                        node["target"] = o.Target;
                    }

                    return (JsonNode)node;
                }).ToArray()),
                ["starts"] = new JsonArray(glue.Starts.Select(s => (JsonNode)new JsonObject { ["event"] = s.Event, ["timeline"] = s.Timeline }).ToArray()),
            };
        }

        return root;
    }

    static JsonArray Names(IEnumerable<string> names) => new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());

    static JsonObject Map(Dictionary<string, int> map)
    {
        var node = new JsonObject();
        foreach (var pair in map)
        {
            node[pair.Key] = pair.Value;
        }

        return node;
    }

    static JsonArray Ops(List<OpSpec> ops) => new JsonArray(ops.Select(o =>
    {
        var node = new JsonObject { ["channel"] = o.Channel, ["amount"] = o.Amount };
        if (o.Times != null)
        {
            node["times"] = o.Times;
        }

        if (o.When != null)
        {
            node["when"] = CondToText(o.When);
        }

        if (o.Kind != null)
        {
            node["kind"] = o.Kind;
        }

        return (JsonNode)node;
    }).ToArray());

    static JsonNode Rule(RuleSpec rule)
    {
        JsonObject body;
        string kind;
        switch (rule)
        {
            case DenyRule d:
                kind = "deny";
                body = new JsonObject();
                if (d.Channels != null)
                {
                    body["channels"] = Names(d.Channels);
                }

                if (d.Kinds != null)
                {
                    body["kinds"] = Names(d.Kinds);
                }

                When(body, "when", d.When);
                break;
            case DenyByMaskRule m:
                kind = "denyByMask";
                body = new JsonObject { ["lane"] = m.Lane, ["byKind"] = m.ByKind };
                break;
            case ScaleRule s:
                kind = "scale";
                body = new JsonObject { ["channels"] = Names(s.Channels), ["times"] = s.Times };
                When(body, "when", s.When);
                When(body, "sourceWhen", s.SourceWhen);
                break;
            case ConvertRule c:
                kind = "convert";
                body = new JsonObject { ["from"] = c.From, ["to"] = c.To, ["numerator"] = c.Numerator, ["denominator"] = c.Denominator };
                When(body, "when", c.When);
                break;
            case AbsorbRule a:
                kind = "absorb";
                body = new JsonObject { ["budget"] = a.Budget, ["channels"] = Names(a.Channels) };
                break;
            case FlowRule f:
                kind = "flow";
                body = new JsonObject { ["lane"] = f.Lane, ["add"] = Names(f.Add), ["subtract"] = Names(f.Subtract) };
                break;
            case BoundsRule b:
                kind = "bounds";
                body = new JsonObject { ["lane"] = b.Lane, ["floor"] = Bound(b.Floor), ["ceiling"] = Bound(b.Ceiling) };
                break;
            case EventRule e:
                kind = "event";
                body = new JsonObject { ["name"] = e.Name, ["enter"] = CondToText(e.Enter) };
                When(body, "stay", e.Stay);
                break;
            default:
                throw new InvalidOperationException("unknown rule " + rule.GetType().Name);
        }

        return new JsonObject { [kind] = body };
    }

    static JsonNode? Bound(Bound bound) => !bound.Present ? null : bound.Lane != null ? JsonValue.Create(bound.Lane) : JsonValue.Create(bound.Value);

    static void When(JsonObject body, string key, Cond? cond)
    {
        if (cond != null)
        {
            body[key] = CondToText(cond);
        }
    }

    public static string Hex(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            builder.Append(c < 32 || c > 126 ? "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) : c.ToString());
        }

        return builder.ToString();
    }
}
