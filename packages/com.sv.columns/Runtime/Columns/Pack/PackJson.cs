using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sv.Columns
{
    public static class PackJson
    {
        public static ColumnPack Read(string json)
        {
            var problems = new List<string>();
            object? root;
            try
            {
                root = new Parser(json).Document();
            }
            catch (FormatException error)
            {
                throw new PackException(new[] { error.Message });
            }

            var pack = new ColumnPack();
            var top = Object(root, "pack", problems);
            if (top == null)
            {
                throw new PackException(problems);
            }

            Known(top, "pack", problems, "lanes", "channels", "kinds", "kindLane", "cards", "templates", "rules", "timelines");
            foreach (var (item, where) in Items(top, "lanes", "pack", problems))
            {
                var lane = Object(item, where, problems);
                if (lane != null)
                {
                    Known(lane, where, problems, "name", "default");
                    pack.Lanes.Add(new LaneSpec(Text(lane, "name", where, problems) ?? string.Empty, Int(lane, "default", where, problems) ?? 0));
                }
            }

            pack.Channels.AddRange(Names(top, "channels", "pack", problems));
            pack.Kinds.AddRange(Names(top, "kinds", "pack", problems));
            pack.KindLane = Field(top, "kindLane") != null ? Text(top, "kindLane", "pack", problems) : null;
            foreach (var (item, where) in Items(top, "cards", "pack", problems))
            {
                var card = Object(item, where, problems);
                if (card != null)
                {
                    Known(card, where, problems, "name", "lane", "ops");
                    var spec = new CardSpec(Text(card, "name", where, problems) ?? string.Empty, Text(card, "lane", where, problems) ?? string.Empty);
                    spec.Ops.AddRange(Ops(card, where, problems));
                    pack.Cards.Add(spec);
                }
            }

            foreach (var (item, where) in Items(top, "templates", "pack", problems))
            {
                var template = Object(item, where, problems);
                if (template == null)
                {
                    continue;
                }

                Known(template, where, problems, "name", "values", "cards", "ops");
                var spec = new TemplateSpec(Text(template, "name", where, problems) ?? string.Empty);
                foreach (var (key, value) in Pairs(template, "values", where, problems))
                {
                    spec.Values[key] = value;
                }

                foreach (var (key, value) in Pairs(template, "cards", where, problems))
                {
                    spec.Cards[key] = value;
                }

                spec.Ops.AddRange(Ops(template, where, problems));
                pack.Templates.Add(spec);
            }

            foreach (var (item, where) in Items(top, "rules", "pack", problems))
            {
                var rule = Rule(item, where, problems);
                if (rule != null)
                {
                    pack.Rules.Add(rule);
                }
            }

            Timelines(top, pack.Timelines, problems);
            if (problems.Count > 0)
            {
                throw new PackException(problems);
            }

            return pack;
        }

        static void Timelines(List<KeyValuePair<string, object?>> top, TimelineGlue glue, List<string> problems)
        {
            if (Field(top, "timelines") == null)
            {
                return;
            }

            var timelines = Object(Field(top, "timelines"), "pack.timelines", problems);
            if (timelines == null)
            {
                return;
            }

            Known(timelines, "pack.timelines", problems, "cards", "outbox", "starts");
            glue.Cards.AddRange(Names(timelines, "cards", "pack.timelines", problems));
            foreach (var (item, where) in Items(timelines, "outbox", "pack.timelines", problems))
            {
                var outbox = Object(item, where, problems);
                if (outbox != null)
                {
                    Known(outbox, where, problems, "channel", "target");
                    glue.Outbox.Add(new TimelineOutbox(Text(outbox, "channel", where, problems) ?? string.Empty, Field(outbox, "target") != null ? Text(outbox, "target", where, problems) : null));
                }
            }

            foreach (var (item, where) in Items(timelines, "starts", "pack.timelines", problems))
            {
                var start = Object(item, where, problems);
                if (start != null)
                {
                    Known(start, where, problems, "event", "timeline");
                    glue.Starts.Add(new TimelineStart(Text(start, "event", where, problems) ?? string.Empty, Text(start, "timeline", where, problems) ?? string.Empty));
                }
            }
        }

        static RuleSpec? Rule(object? item, string where, List<string> problems)
        {
            var wrapper = Object(item, where, problems);
            if (wrapper == null)
            {
                return null;
            }

            if (wrapper.Count != 1)
            {
                problems.Add(where + ": a rule is an object with exactly one key, its kind");
                return null;
            }

            var kind = wrapper[0].Key;
            var at = where + "." + kind;
            var body = Object(wrapper[0].Value, at, problems);
            if (body == null)
            {
                return null;
            }

            switch (kind)
            {
                case "deny":
                    Known(body, at, problems, "channels", "kinds", "when");
                    return new DenyRule(Field(body, "channels") != null ? Names(body, "channels", at, problems).ToArray() : null, Field(body, "kinds") != null ? Names(body, "kinds", at, problems).ToArray() : null, When(body, "when", at, problems));
                case "denyByMask":
                    Known(body, at, problems, "lane", "byKind");
                    return new DenyByMaskRule(Text(body, "lane", at, problems) ?? string.Empty, Bool(body, "byKind", at, problems));
                case "scale":
                    Known(body, at, problems, "channels", "times", "when", "sourceWhen");
                    return new ScaleRule(Names(body, "channels", at, problems).ToArray(), Int(body, "times", at, problems) ?? 1, When(body, "when", at, problems), When(body, "sourceWhen", at, problems));
                case "convert":
                    Known(body, at, problems, "from", "to", "numerator", "denominator", "when");
                    return new ConvertRule(Text(body, "from", at, problems) ?? string.Empty, Text(body, "to", at, problems) ?? string.Empty, Int(body, "numerator", at, problems) ?? 1, Int(body, "denominator", at, problems) ?? 1, When(body, "when", at, problems));
                case "absorb":
                    Known(body, at, problems, "budget", "channels");
                    return new AbsorbRule(Text(body, "budget", at, problems) ?? string.Empty, Names(body, "channels", at, problems).ToArray());
                case "flow":
                    Known(body, at, problems, "lane", "add", "subtract");
                    return new FlowRule(Text(body, "lane", at, problems) ?? string.Empty, Field(body, "add") != null ? Names(body, "add", at, problems).ToArray() : Array.Empty<string>(), Field(body, "subtract") != null ? Names(body, "subtract", at, problems).ToArray() : Array.Empty<string>());
                case "bounds":
                    Known(body, at, problems, "lane", "floor", "ceiling");
                    return new BoundsRule(Text(body, "lane", at, problems) ?? string.Empty, BoundOf(body, "floor", at, problems), BoundOf(body, "ceiling", at, problems));
                case "event":
                    Known(body, at, problems, "name", "enter", "stay");
                    return new EventRule(Text(body, "name", at, problems) ?? string.Empty, When(body, "enter", at, problems) ?? Cond.All(), When(body, "stay", at, problems));
                default:
                    problems.Add(where + ": unknown rule kind '" + kind + "' (deny, denyByMask, scale, convert, absorb, flow, bounds, event)");
                    return null;
            }
        }

        static Bound BoundOf(List<KeyValuePair<string, object?>> body, string key, string where, List<string> problems)
        {
            var value = Field(body, key);
            switch (value)
            {
                case null:
                    return Bound.None;
                case long number when number >= int.MinValue && number <= int.MaxValue:
                    return Bound.Of((int)number);
                case string lane:
                    return Bound.Of(lane);
                default:
                    problems.Add(where + "." + key + ": a bound is a whole number, a lane name or null");
                    return Bound.None;
            }
        }

        static List<OpSpec> Ops(List<KeyValuePair<string, object?>> owner, string where, List<string> problems)
        {
            var ops = new List<OpSpec>();
            foreach (var (item, at) in Items(owner, "ops", where, problems))
            {
                var op = Object(item, at, problems);
                if (op != null)
                {
                    Known(op, at, problems, "channel", "amount", "times", "when", "kind");
                    ops.Add(new OpSpec(
                        Text(op, "channel", at, problems) ?? string.Empty,
                        Int(op, "amount", at, problems) ?? 0,
                        Field(op, "times") != null ? Text(op, "times", at, problems) : null,
                        When(op, "when", at, problems),
                        Field(op, "kind") != null ? Text(op, "kind", at, problems) : null));
                }
            }

            return ops;
        }

        static Cond? When(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var value = Field(owner, key);
            if (value == null)
            {
                return null;
            }

            if (value is not string text)
            {
                problems.Add(where + "." + key + ": a condition is a string such as \"Health < 30% of MaxHealth and Execute > 0\"");
                return null;
            }

            try
            {
                return CondText.Parse(text);
            }
            catch (FormatException error)
            {
                problems.Add(where + "." + key + ": " + error.Message);
                return null;
            }
        }

        static void Known(List<KeyValuePair<string, object?>> owner, string where, List<string> problems, params string[] keys)
        {
            foreach (var pair in owner)
            {
                if (Array.IndexOf(keys, pair.Key) < 0)
                {
                    problems.Add(where + ": unknown key '" + pair.Key + "' (expected " + string.Join(", ", keys) + ")");
                }
            }
        }

        static object? Field(List<KeyValuePair<string, object?>> owner, string key)
        {
            foreach (var pair in owner)
            {
                if (pair.Key == key)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        static List<KeyValuePair<string, object?>>? Object(object? value, string where, List<string> problems)
        {
            if (value is List<KeyValuePair<string, object?>> map)
            {
                return map;
            }

            problems.Add(where + ": expected an object");
            return null;
        }

        static IEnumerable<(object? Item, string Where)> Items(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var value = Field(owner, key);
            if (value == null)
            {
                yield break;
            }

            if (value is not List<object?> list)
            {
                problems.Add(where + "." + key + ": expected an array");
                yield break;
            }

            for (var i = 0; i < list.Count; i++)
            {
                yield return (list[i], where + "." + key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }

        static List<string> Names(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var names = new List<string>();
            foreach (var (item, at) in Items(owner, key, where, problems))
            {
                if (item is string name)
                {
                    names.Add(name);
                }
                else
                {
                    problems.Add(at + ": expected a name");
                }
            }

            return names;
        }

        static IEnumerable<(string Key, int Value)> Pairs(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var value = Field(owner, key);
            if (value == null)
            {
                yield break;
            }

            var map = Object(value, where + "." + key, problems);
            if (map == null)
            {
                yield break;
            }

            foreach (var pair in map)
            {
                if (pair.Value is long number && number >= int.MinValue && number <= int.MaxValue)
                {
                    yield return (pair.Key, (int)number);
                }
                else
                {
                    problems.Add(where + "." + key + "." + pair.Key + ": expected a whole number in int32 range");
                }
            }
        }

        static string? Text(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            if (Field(owner, key) is string text)
            {
                return text;
            }

            problems.Add(where + "." + key + ": expected a string");
            return null;
        }

        static int? Int(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var value = Field(owner, key);
            if (value == null)
            {
                return null;
            }

            if (value is long number && number >= int.MinValue && number <= int.MaxValue)
            {
                return (int)number;
            }

            problems.Add(where + "." + key + ": expected a whole number in int32 range");
            return null;
        }

        static bool Bool(List<KeyValuePair<string, object?>> owner, string key, string where, List<string> problems)
        {
            var value = Field(owner, key);
            if (value == null)
            {
                return false;
            }

            if (value is bool flag)
            {
                return flag;
            }

            problems.Add(where + "." + key + ": expected true or false");
            return false;
        }

        sealed class Parser
        {
            public const int DepthLimit = 64;
            readonly string text;
            int at;
            int depth;

            public Parser(string text)
            {
                this.text = text;
            }

            public object? Document()
            {
                var value = Value();
                Space();
                if (at != text.Length)
                {
                    Fail("unexpected text after the pack");
                }

                return value;
            }

            object? Value()
            {
                Space();
                if (at >= text.Length)
                {
                    Fail("unexpected end of text");
                }

                var c = text[at];
                switch (c)
                {
                    case '{':
                        Nest();
                        var map = ObjectValue();
                        depth--;
                        return map;
                    case '[':
                        Nest();
                        var list = ArrayValue();
                        depth--;
                        return list;
                    case '"':
                        return StringValue();
                    case 't':
                        Word("true");
                        return true;
                    case 'f':
                        Word("false");
                        return false;
                    case 'n':
                        Word("null");
                        return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return Number();
                        }

                        Fail("unexpected character '" + c + "'");
                        return null;
                }
            }

            void Nest()
            {
                if (++depth > DepthLimit)
                {
                    Fail("nesting deeper than " + DepthLimit);
                }
            }

            List<KeyValuePair<string, object?>> ObjectValue()
            {
                var map = new List<KeyValuePair<string, object?>>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                at++;
                Space();
                if (Peek('}'))
                {
                    at++;
                    return map;
                }

                while (true)
                {
                    Space();
                    if (!Peek('"'))
                    {
                        Fail("expected a key");
                    }

                    var key = StringValue();
                    if (!keys.Add(key))
                    {
                        Fail("duplicate key '" + key + "'");
                    }

                    Space();
                    Expect(':');
                    map.Add(new KeyValuePair<string, object?>(key, Value()));
                    Space();
                    if (Peek(','))
                    {
                        at++;
                        continue;
                    }

                    Expect('}');
                    return map;
                }
            }

            List<object?> ArrayValue()
            {
                var list = new List<object?>();
                at++;
                Space();
                if (Peek(']'))
                {
                    at++;
                    return list;
                }

                while (true)
                {
                    list.Add(Value());
                    Space();
                    if (Peek(','))
                    {
                        at++;
                        continue;
                    }

                    Expect(']');
                    return list;
                }
            }

            string StringValue()
            {
                at++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (at >= text.Length)
                    {
                        Fail("unterminated string");
                    }

                    var c = text[at++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (at >= text.Length)
                    {
                        Fail("unterminated string");
                    }

                    var escape = text[at++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escape);
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'u':
                            var code = 0;
                            if (at + 4 > text.Length || !int.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                Fail("bad \\u escape");
                            }

                            builder.Append((char)code);
                            at += 4;
                            break;
                        default:
                            Fail("bad escape '\\" + escape + "'");
                            break;
                    }
                }
            }

            object Number()
            {
                var start = at;
                if (Peek('-'))
                {
                    at++;
                }

                while (at < text.Length && text[at] >= '0' && text[at] <= '9')
                {
                    at++;
                }

                if (at < text.Length && (text[at] == '.' || text[at] == 'e' || text[at] == 'E'))
                {
                    Fail("numbers in a pack are whole numbers");
                }

                if (!long.TryParse(text.Substring(start, at - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                {
                    Fail("bad number '" + text.Substring(start, at - start) + "'");
                }

                return value;
            }

            void Word(string word)
            {
                if (string.CompareOrdinal(text, at, word, 0, word.Length) != 0)
                {
                    Fail("expected '" + word + "'");
                }

                at += word.Length;
            }

            void Space()
            {
                while (at < text.Length && char.IsWhiteSpace(text[at]))
                {
                    at++;
                }
            }

            bool Peek(char c) => at < text.Length && text[at] == c;

            void Expect(char c)
            {
                if (!Peek(c))
                {
                    Fail("expected '" + c + "'");
                }

                at++;
            }

            void Fail(string message)
            {
                var line = 1;
                var column = 1;
                for (var i = 0; i < at && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                throw new FormatException("json line " + line.ToString(CultureInfo.InvariantCulture) + " column " + column.ToString(CultureInfo.InvariantCulture) + ": " + message);
            }
        }
    }
}
