using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sv.Columns.Tests;

namespace Sv.Fuzz;

public sealed class Mutator
{
    static readonly long[] Numbers =
    {
        0, 1, -1, 2, 3, 7, 15, 16, 17, 30, 31, 32, 33, 61, 62, 63, 64, 100, 101, 255, 256, 4096, 32767, 32768, -32767, -32768, 65535, 65536, 65537,
        (1 << 20) - 1, 1 << 20, (1 << 20) + 1, 1 << 22, (1 << 27) - 1, 1 << 27, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1,
        2147483648L, -2147483649L, long.MaxValue, long.MinValue,
    };

    static readonly string[] Words =
    {
        "", " ", "@events", "@entered", "none", "None", "_", "__", "a", "A", "é", "É", "ß", "ǅ", "ﬁ", "Ⅻ", "٣", "a b", "a\"b", "a\\b", "a\nb", "\u2028",
        "\u0000", "\uD800", "and", "not", "of", "event", "entered", "bit", "class", "int", "value__", "Value", "Card", "Out", "x1Card", "Health",
        "health", "HEALTH", "Physical", "physical", "Dead", "dead", "true", "null", "0", "-1", "1e3", "\t",
    };

    static readonly string[] CondJunk =
    {
        "and", "not", "<", ">", "<=", ">=", "==", "!=", "=", "!", "%", "of", "event", "entered", "bit", "0", "-1", "100%", "101%", "-5%", "2147483648",
        "-2147483648", "2147483647", "99999999999", "+", "-", "+5", "32", "31", "x", "and and", "not not",
    };

    readonly Dice dice;

    public Mutator(ulong seed)
    {
        dice = new Dice(seed);
    }

    public Dice Dice => dice;

    public string Mutate(JsonNode seed, int count)
    {
        var root = seed.DeepClone();
        var names = new List<string>();
        Collect(root, names);
        var raw = dice.Chance(15);
        var steps = raw ? Math.Max(1, count - 1) : count;
        for (var i = 0; i < steps; i++)
        {
            root = MutateTree(root, names) ?? new JsonObject();
        }

        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = dice.Chance(30) });
        return raw ? MutateText(text) : text;
    }

    static void Collect(JsonNode? node, List<string> names)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    Collect(pair.Value, names);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, names);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (!text.Contains(' '))
                {
                    names.Add(text);
                }

                break;
        }
    }

    JsonNode? MutateTree(JsonNode root, List<string> names)
    {
        var all = new List<JsonNode?>();
        Walk(root, all);
        var target = all[dice.Next(all.Count)];
        if (target == null || target == root && dice.Chance(80))
        {
            target = all[dice.Next(all.Count)];
        }

        if (target == null)
        {
            return root;
        }

        var replacement = dice.Next(16) switch
        {
            0 => Remove(target),
            1 => Duplicate(target),
            2 => (JsonNode?)JsonValue.Create(Numbers[dice.Next(Numbers.Length)]),
            3 => JsonValue.Create(Word(names)),
            4 => JsonValue.Create(Condition(names)),
            5 => null,
            6 => new JsonArray(),
            7 => new JsonObject(),
            8 => JsonValue.Create(dice.Chance(50)),
            9 => AddKey(target, names),
            10 => RenameKey(target, names),
            11 => Swap(root, target, all),
            12 => JsonValue.Create(dice.Range(-3, 70)),
            13 => Grow(target),
            14 => JsonValue.Create(Condition(names) + " " + CondJunk[dice.Next(CondJunk.Length)] + " " + Word(names)),
            _ => JsonValue.Create(Case(Word(names))),
        };
        if (ReferenceEquals(replacement, target))
        {
            return root;
        }

        if (target == root)
        {
            return replacement;
        }

        Replace(target, replacement);
        return root;
    }

    static void Walk(JsonNode? node, List<JsonNode?> all)
    {
        all.Add(node);
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value != null)
                    {
                        Walk(pair.Value, all);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    if (item != null)
                    {
                        Walk(item, all);
                    }
                }

                break;
        }
    }

    static void Replace(JsonNode target, JsonNode? replacement)
    {
        switch (target.Parent)
        {
            case JsonObject obj:
                var key = obj.First(p => ReferenceEquals(p.Value, target)).Key;
                obj[key] = replacement;
                break;
            case JsonArray array:
                var index = array.IndexOf(target);
                array[index] = replacement;
                break;
        }
    }

    JsonNode Remove(JsonNode target)
    {
        switch (target.Parent)
        {
            case JsonObject obj:
                obj.Remove(obj.First(p => ReferenceEquals(p.Value, target)).Key);
                break;
            case JsonArray array:
                array.Remove(target);
                break;
        }

        return target;
    }

    JsonNode Duplicate(JsonNode target)
    {
        if (target.Parent is JsonArray array)
        {
            array.Insert(dice.Next(array.Count + 1), target.DeepClone());
        }

        return target;
    }

    JsonNode Grow(JsonNode target)
    {
        if (target is JsonArray array && array.Count > 0)
        {
            var times = dice.Chance(50) ? dice.Range(1, 8) : dice.Range(20, 80);
            for (var i = 0; i < times; i++)
            {
                array.Add(array[dice.Next(array.Count)]?.DeepClone());
            }
        }

        return target;
    }

    JsonNode AddKey(JsonNode target, List<string> names)
    {
        if (target is JsonObject obj)
        {
            var key = dice.Chance(50) ? Word(names) : dice.Pick(new[] { "when", "times", "kind", "stay", "sourceWhen", "target", "values", "cards", "ops", "timelines", "kindLane", "floor", "ceiling", "byKind", "default" });
            if (!obj.ContainsKey(key))
            {
                obj[key] = dice.Next(3) switch
                {
                    0 => JsonValue.Create(Word(names)),
                    1 => JsonValue.Create(Numbers[dice.Next(Numbers.Length)]),
                    _ => JsonValue.Create(Condition(names)),
                };
            }
        }

        return target;
    }

    JsonNode? RenameKey(JsonNode target, List<string> names)
    {
        if (target.Parent is JsonObject obj)
        {
            var key = obj.First(p => ReferenceEquals(p.Value, target)).Key;
            var renamed = dice.Chance(50) ? Case(key) : Word(names);
            if (!obj.ContainsKey(renamed))
            {
                obj.Remove(key);
                obj[renamed] = target;
            }
        }

        return target;
    }

    JsonNode? Swap(JsonNode root, JsonNode target, List<JsonNode?> all)
    {
        var other = all[dice.Next(all.Count)];
        if (other == null || other == root || other == target || IsAncestor(other, target) || IsAncestor(target, other))
        {
            return target;
        }

        return other.DeepClone();
    }

    static bool IsAncestor(JsonNode maybeParent, JsonNode node)
    {
        for (var at = node.Parent; at != null; at = at.Parent)
        {
            if (ReferenceEquals(at, maybeParent))
            {
                return true;
            }
        }

        return false;
    }

    string Word(List<string> names) => names.Count > 0 && dice.Chance(60) ? names[dice.Next(names.Count)] : Words[dice.Next(Words.Length)];

    string Case(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        var chars = word.ToCharArray();
        var i = dice.Next(chars.Length);
        chars[i] = char.IsUpper(chars[i]) ? char.ToLowerInvariant(chars[i]) : char.ToUpperInvariant(chars[i]);
        return new string(chars);
    }

    public string Condition(List<string> names)
    {
        var parts = new List<string>();
        for (var i = dice.Range(1, 4); i > 0; i--)
        {
            var lane = Word(names);
            var atom = dice.Next(8) switch
            {
                0 => lane + " < " + dice.Range(-10, 120) + "% of " + Word(names),
                1 => "bit " + lane + " " + dice.Range(-2, 40),
                2 => "event " + lane,
                3 => "entered " + lane,
                4 => lane + " " + dice.Pick(new[] { ">", ">=", "<", "<=", "==", "!=" }) + " " + Word(names),
                5 => lane + dice.Pick(new[] { ">", ">=", "<", "<=", "==", "!=" }) + Numbers[dice.Next(Numbers.Length)],
                6 => string.Join(" ", Enumerable.Range(0, dice.Range(1, 5)).Select(_ => dice.Chance(50) ? CondJunk[dice.Next(CondJunk.Length)] : Word(names))),
                _ => lane + " " + dice.Pick(new[] { ">", ">=", "<", "<=", "==", "!=" }) + " " + dice.Range(-50, 50),
            };
            parts.Add(dice.Chance(20) ? "not " + atom : atom);
        }

        return string.Join(dice.Chance(90) ? " and " : " ", parts);
    }

    string MutateText(string text)
    {
        var builder = new StringBuilder(text);
        for (var i = dice.Range(1, 4); i > 0; i--)
        {
            if (builder.Length == 0)
            {
                builder.Append('{');
            }

            var at = dice.Next(builder.Length);
            switch (dice.Next(9))
            {
                case 0:
                    builder.Remove(at, Math.Min(builder.Length - at, dice.Range(1, 8)));
                    break;
                case 1:
                    builder.Insert(at, dice.Pick(new[] { "{", "}", "[", "]", ",", ":", "\"", "\\", "\\u", "\\u12", "\\uD800", "-", "0", "e5", ".5", "nul", "tru", "\u0000", "\u00A0", "\u2028", "//", "/*" }));
                    break;
                case 2:
                    builder.Length = at;
                    break;
                case 3:
                    builder[at] = (char)dice.Next(128);
                    break;
                case 4:
                    builder.Insert(at, new string('[', dice.Range(60, 70)));
                    break;
                case 5:
                    builder.Insert(at, new string('9', dice.Range(18, 40)));
                    break;
                case 6:
                    var copyAt = dice.Next(builder.Length);
                    var copy = builder.ToString(copyAt, Math.Min(builder.Length - copyAt, dice.Range(1, 40)));
                    builder.Insert(at, copy);
                    break;
                case 7:
                    builder.Insert(at, " \t\r\n\u000B\u000C\u0085\u3000".Substring(dice.Next(8), 1));
                    break;
                default:
                    builder[at] = (char)dice.Range(0x80, 0xFFFF);
                    break;
            }
        }

        return builder.ToString();
    }
}
