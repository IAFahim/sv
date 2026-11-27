using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sv.Rules.Emit
{
    public sealed class CodeWriter
    {
        readonly StringBuilder text = new StringBuilder();
        int depth;
        bool pendingBlank;

        public void Line(string line)
        {
            if (pendingBlank)
            {
                text.Append('\n');
                pendingBlank = false;
            }

            text.Append(' ', depth * 4).Append(line).Append('\n');
        }

        public void Open(string head)
        {
            Line(head);
            Line("{");
            depth++;
            pendingBlank = false;
        }

        public void Block()
        {
            Line("{");
            depth++;
            pendingBlank = false;
        }

        public void Close(string tail = "}")
        {
            depth--;
            pendingBlank = false;
            Line(tail);
        }

        public void Blank() => pendingBlank = text.Length > 0;

        public override string ToString() => text.ToString();
    }

    public static class Names
    {
        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal",
            "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        public static string Identifier(string name)
        {
            var text = new StringBuilder(name.Length + 1);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                var plain = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                text.Append(plain ? c : '_');
            }

            if (text.Length == 0 || (text[0] >= '0' && text[0] <= '9'))
            {
                text.Insert(0, '_');
            }

            var identifier = text.ToString();
            return Keywords.Contains(identifier) ? "@" + identifier : identifier;
        }

        public static string Literal(string text)
        {
            var quoted = new StringBuilder(text.Length + 2).Append('"');
            foreach (var c in text)
            {
                if (c == '"' || c == '\\')
                {
                    quoted.Append('\\').Append(c);
                }
                else if (c >= ' ' && c <= '~')
                {
                    quoted.Append(c);
                }
                else
                {
                    quoted.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return quoted.Append('"').ToString();
        }

        public static string Int(int value) => value == int.MinValue ? "int.MinValue" : value.ToString(CultureInfo.InvariantCulture);

        public static string Long(long value) => value == long.MinValue ? "long.MinValue" : value.ToString(CultureInfo.InvariantCulture) + "L";

        public static string Hex(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture) + "ul";

        public static string Float(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return "(" + text + "f)";
        }

        public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
