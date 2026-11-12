using System;
using System.Globalization;
using System.Text;

namespace Sv.Rules
{
    public enum JsonKind : byte
    {
        Null = 0,
        False = 1,
        True = 2,
        Number = 3,
        String = 4,
        Array = 5,
        Object = 6,
    }

    public sealed class JsonValue
    {
        static readonly JsonValue[] NoItems = new JsonValue[0];
        static readonly string[] NoKeys = new string[0];

        public JsonKind Kind { get; }

        public string Text { get; }

        public JsonValue[] Items { get; }

        public string[] Keys { get; }

        public int Offset { get; }

        JsonValue(JsonKind kind, string text, JsonValue[] items, string[] keys, int offset)
        {
            Kind = kind;
            Text = text;
            Items = items;
            Keys = keys;
            Offset = offset;
        }

        public static JsonValue Literal(JsonKind kind, int offset) => new JsonValue(kind, string.Empty, NoItems, NoKeys, offset);

        public static JsonValue Number(string text, int offset) => new JsonValue(JsonKind.Number, text, NoItems, NoKeys, offset);

        public static JsonValue String(string text, int offset) => new JsonValue(JsonKind.String, text, NoItems, NoKeys, offset);

        public static JsonValue Array(JsonValue[] items, int offset) => new JsonValue(JsonKind.Array, string.Empty, items, NoKeys, offset);

        public static JsonValue Object(string[] keys, JsonValue[] values, int offset) => new JsonValue(JsonKind.Object, string.Empty, values, keys, offset);

        public bool IsBool => Kind == JsonKind.True || Kind == JsonKind.False;

        public JsonValue? Find(string key)
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                if (string.CompareOrdinal(Keys[i], key) == 0)
                {
                    return Items[i];
                }
            }

            return null;
        }

        public bool IsInteger
        {
            get
            {
                if (Kind != JsonKind.Number)
                {
                    return false;
                }

                for (var i = 0; i < Text.Length; i++)
                {
                    var c = Text[i];
                    if (c == '.' || c == 'e' || c == 'E')
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool TryInt(out int value)
        {
            value = 0;
            if (!IsInteger || !long.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wide))
            {
                return false;
            }

            if (wide < int.MinValue || wide > int.MaxValue)
            {
                return false;
            }

            value = (int)wide;
            return true;
        }

        public bool TryFloat(out float value)
        {
            value = 0f;
            if (Kind != JsonKind.Number || !float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            if (float.IsNaN(parsed) || float.IsInfinity(parsed))
            {
                return false;
            }

            value = parsed;
            return true;
        }
    }

    public readonly struct JsonParse
    {
        public JsonValue? Value { get; }

        public int ErrorOffset { get; }

        public string Error { get; }

        public JsonParse(JsonValue? value, int errorOffset, string error)
        {
            Value = value;
            ErrorOffset = errorOffset;
            Error = error;
        }

        public bool Ok => Value != null;
    }

    public static class JsonReader
    {
        public const int DepthLimit = 64;

        public static JsonParse Parse(string text)
        {
            var at = 0;
            var error = string.Empty;
            var errorAt = -1;
            var value = ReadValue(text, ref at, 0, ref error, ref errorAt);
            if (value == null)
            {
                return new JsonParse(null, errorAt, error);
            }

            SkipSpace(text, ref at);
            if (at != text.Length)
            {
                return new JsonParse(null, at, "trailing characters after the document");
            }

            return new JsonParse(value, -1, string.Empty);
        }

        static void SkipSpace(string text, ref int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\n' || text[at] == '\r'))
            {
                at++;
            }
        }

        static JsonValue? Fail(ref string error, ref int errorAt, int at, string message)
        {
            if (errorAt < 0)
            {
                error = message;
                errorAt = at;
            }

            return null;
        }

        static JsonValue? ReadValue(string text, ref int at, int depth, ref string error, ref int errorAt)
        {
            if (depth > DepthLimit)
            {
                return Fail(ref error, ref errorAt, at, "nesting deeper than " + DepthLimit.ToString(CultureInfo.InvariantCulture));
            }

            SkipSpace(text, ref at);
            if (at >= text.Length)
            {
                return Fail(ref error, ref errorAt, at, "unexpected end of document");
            }

            var start = at;
            var c = text[at];
            if (c == '{')
            {
                return ReadObject(text, ref at, depth, ref error, ref errorAt);
            }

            if (c == '[')
            {
                return ReadArray(text, ref at, depth, ref error, ref errorAt);
            }

            if (c == '"')
            {
                var s = ReadString(text, ref at, ref error, ref errorAt);
                return s == null ? null : JsonValue.String(s, start);
            }

            if (Matches(text, at, "true"))
            {
                at += 4;
                return JsonValue.Literal(JsonKind.True, start);
            }

            if (Matches(text, at, "false"))
            {
                at += 5;
                return JsonValue.Literal(JsonKind.False, start);
            }

            if (Matches(text, at, "null"))
            {
                at += 4;
                return JsonValue.Literal(JsonKind.Null, start);
            }

            if (c == '-' || (c >= '0' && c <= '9'))
            {
                return ReadNumber(text, ref at, ref error, ref errorAt);
            }

            return Fail(ref error, ref errorAt, at, "unexpected character '" + c + "'");
        }

        static bool Matches(string text, int at, string word) =>
            at + word.Length <= text.Length && string.CompareOrdinal(text, at, word, 0, word.Length) == 0;

        static JsonValue? ReadObject(string text, ref int at, int depth, ref string error, ref int errorAt)
        {
            var start = at;
            at++;
            var keys = new Rows<string>();
            var values = new Rows<JsonValue>();
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == '}')
            {
                at++;
                return JsonValue.Object(keys.ToArray(), values.ToArray(), start);
            }

            while (true)
            {
                SkipSpace(text, ref at);
                if (at >= text.Length || text[at] != '"')
                {
                    return Fail(ref error, ref errorAt, at, "expected a key string");
                }

                var keyAt = at;
                var key = ReadString(text, ref at, ref error, ref errorAt);
                if (key == null)
                {
                    return null;
                }

                for (var i = 0; i < keys.Count; i++)
                {
                    if (string.CompareOrdinal(keys[i], key) == 0)
                    {
                        return Fail(ref error, ref errorAt, keyAt, "duplicate key \"" + key + "\"");
                    }
                }

                SkipSpace(text, ref at);
                if (at >= text.Length || text[at] != ':')
                {
                    return Fail(ref error, ref errorAt, at, "expected ':'");
                }

                at++;
                var value = ReadValue(text, ref at, depth + 1, ref error, ref errorAt);
                if (value == null)
                {
                    return null;
                }

                keys.Add(key);
                values.Add(value);
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }

                if (at < text.Length && text[at] == '}')
                {
                    at++;
                    return JsonValue.Object(keys.ToArray(), values.ToArray(), start);
                }

                return Fail(ref error, ref errorAt, at, "expected ',' or '}'");
            }
        }

        static JsonValue? ReadArray(string text, ref int at, int depth, ref string error, ref int errorAt)
        {
            var start = at;
            at++;
            var items = new Rows<JsonValue>();
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == ']')
            {
                at++;
                return JsonValue.Array(items.ToArray(), start);
            }

            while (true)
            {
                var value = ReadValue(text, ref at, depth + 1, ref error, ref errorAt);
                if (value == null)
                {
                    return null;
                }

                items.Add(value);
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }

                if (at < text.Length && text[at] == ']')
                {
                    at++;
                    return JsonValue.Array(items.ToArray(), start);
                }

                return Fail(ref error, ref errorAt, at, "expected ',' or ']'");
            }
        }

        static string? ReadString(string text, ref int at, ref string error, ref int errorAt)
        {
            at++;
            var builder = new StringBuilder();
            while (at < text.Length)
            {
                var c = text[at];
                if (c == '"')
                {
                    at++;
                    return builder.ToString();
                }

                if (c < ' ')
                {
                    Fail(ref error, ref errorAt, at, "control character in string");
                    return null;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    at++;
                    continue;
                }

                if (at + 1 >= text.Length)
                {
                    break;
                }

                var escape = text[at + 1];
                at += 2;
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length || !int.TryParse(text.Substring(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            Fail(ref error, ref errorAt, at, "bad \\u escape");
                            return null;
                        }

                        builder.Append((char)code);
                        at += 4;
                        break;
                    default:
                        Fail(ref error, ref errorAt, at - 1, "bad escape");
                        return null;
                }
            }

            Fail(ref error, ref errorAt, at, "unterminated string");
            return null;
        }

        static JsonValue? ReadNumber(string text, ref int at, ref string error, ref int errorAt)
        {
            var start = at;
            if (text[at] == '-')
            {
                at++;
            }

            var digits = 0;
            while (at < text.Length && text[at] >= '0' && text[at] <= '9')
            {
                at++;
                digits++;
            }

            if (digits == 0)
            {
                return Fail(ref error, ref errorAt, start, "bad number");
            }

            if (at < text.Length && text[at] == '.')
            {
                at++;
                var fraction = 0;
                while (at < text.Length && text[at] >= '0' && text[at] <= '9')
                {
                    at++;
                    fraction++;
                }

                if (fraction == 0)
                {
                    return Fail(ref error, ref errorAt, start, "bad number");
                }
            }

            if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
            {
                at++;
                if (at < text.Length && (text[at] == '+' || text[at] == '-'))
                {
                    at++;
                }

                var exponent = 0;
                while (at < text.Length && text[at] >= '0' && text[at] <= '9')
                {
                    at++;
                    exponent++;
                }

                if (exponent == 0)
                {
                    return Fail(ref error, ref errorAt, start, "bad number");
                }
            }

            return JsonValue.Number(text.Substring(start, at - start), start);
        }
    }

    public sealed class JsonWriter
    {
        readonly StringBuilder builder = new StringBuilder();
        readonly Rows<bool> needsComma = new Rows<bool>();
        bool[] commaStack = new bool[16];
        int depth;

        public override string ToString() => builder.ToString();

        void BeforeValue()
        {
            if (depth > 0)
            {
                if (commaStack[depth - 1])
                {
                    builder.Append(',');
                }

                commaStack[depth - 1] = true;
            }
        }

        void Push()
        {
            if (depth == commaStack.Length)
            {
                Array.Resize(ref commaStack, depth * 2);
            }

            commaStack[depth] = false;
            depth++;
        }

        public JsonWriter BeginObject()
        {
            BeforeValue();
            builder.Append('{');
            Push();
            return this;
        }

        public JsonWriter EndObject()
        {
            depth--;
            builder.Append('}');
            return this;
        }

        public JsonWriter BeginArray()
        {
            BeforeValue();
            builder.Append('[');
            Push();
            return this;
        }

        public JsonWriter EndArray()
        {
            depth--;
            builder.Append(']');
            return this;
        }

        public JsonWriter Line()
        {
            builder.Append('\n');
            return this;
        }

        public JsonWriter Key(string key)
        {
            BeforeValue();
            AppendString(key);
            builder.Append(':');
            commaStack[depth - 1] = false;
            return this;
        }

        public JsonWriter KeyDone()
        {
            commaStack[depth - 1] = true;
            return this;
        }

        public JsonWriter String(string value)
        {
            BeforeValue();
            AppendString(value);
            return this;
        }

        public JsonWriter Int(long value)
        {
            BeforeValue();
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter Float(float value)
        {
            BeforeValue();
            builder.Append(FloatText(value));
            return this;
        }

        public JsonWriter Bool(bool value)
        {
            BeforeValue();
            builder.Append(value ? "true" : "false");
            return this;
        }

        public static string FloatText(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '.' || c == 'E' || c == 'e' || c == 'N' || c == 'I')
                {
                    return text;
                }
            }

            return text + ".0";
        }

        void AppendString(string value)
        {
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '"')
                {
                    builder.Append("\\\"");
                }
                else if (c == '\\')
                {
                    builder.Append("\\\\");
                }
                else if (c < ' ')
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(c);
                }
            }

            builder.Append('"');
        }
    }
}
