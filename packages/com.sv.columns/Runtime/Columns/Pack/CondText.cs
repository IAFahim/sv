using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sv.Columns
{
    public static class CondText
    {
        public static Cond Parse(string text)
        {
            var words = Split(text);
            var parts = new List<Cond>();
            var at = 0;
            while (true)
            {
                parts.Add(Atom(words, ref at, text));
                if (at == words.Count)
                {
                    break;
                }

                if (words[at] != "and")
                {
                    throw new FormatException("expected 'and' at '" + words[at] + "' in \"" + text + "\"");
                }

                at++;
            }

            return parts.Count == 1 ? parts[0] : Cond.All(parts.ToArray());
        }

        static Cond Atom(List<string> words, ref int at, string text)
        {
            var negated = false;
            if (Word(words, at) == "not" && !IsComparison(Word(words, at + 1)))
            {
                negated = true;
                at++;
            }

            Cond atom;
            var first = Need(words, ref at, text);
            var keyword = !IsComparison(Word(words, at));
            if (keyword && (first == "event" || first == "entered"))
            {
                var name = Need(words, ref at, text);
                atom = first == "event" ? Cond.Event(name) : Cond.Entered(name);
            }
            else if (keyword && first == "bit")
            {
                var lane = Need(words, ref at, text);
                atom = Cond.Bit(lane, Number(Need(words, ref at, text), text));
            }
            else
            {
                var op = Need(words, ref at, text);
                var right = Need(words, ref at, text);
                if (op == "<" && right.EndsWith("%", StringComparison.Ordinal))
                {
                    var percent = Number(right.Substring(0, right.Length - 1), text);
                    if (Word(words, at) == "of" && at + 1 < words.Count && words[at + 1] != "and")
                    {
                        at++;
                    }

                    atom = Cond.Below(first, Need(words, ref at, text), percent);
                }
                else
                {
                    var compare = op switch
                    {
                        ">" => CondOp.Gt,
                        ">=" => CondOp.Ge,
                        "<" => CondOp.Lt,
                        "<=" => CondOp.Le,
                        "==" => CondOp.Eq,
                        "!=" => CondOp.Ne,
                        _ => throw new FormatException("unknown comparison '" + op + "' in \"" + text + "\" (use > >= < <= == !=)"),
                    };
                    atom = IsNumber(right) ? Cond.Compare(compare, first, Number(right, text)) : Cond.Compare(compare, first, right);
                }
            }

            return negated ? Cond.Not(atom) : atom;
        }

        static List<string> Split(string text)
        {
            var words = new List<string>();
            var at = 0;
            while (at < text.Length)
            {
                var c = text[at];
                if (char.IsWhiteSpace(c))
                {
                    at++;
                    continue;
                }

                var start = at;
                if (c == '<' || c == '>' || c == '=' || c == '!')
                {
                    at++;
                    if (at < text.Length && text[at] == '=')
                    {
                        at++;
                    }
                }
                else
                {
                    while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] != '<' && text[at] != '>' && text[at] != '=' && text[at] != '!')
                    {
                        at++;
                    }
                }

                words.Add(text.Substring(start, at - start));
            }

            return words;
        }

        static string? Word(List<string> words, int at) => at < words.Count ? words[at] : null;

        static string Need(List<string> words, ref int at, string text)
        {
            if (at >= words.Count)
            {
                throw new FormatException("condition \"" + text + "\" ends too early");
            }

            return words[at++];
        }

        static bool IsComparison(string? word) => word == ">" || word == ">=" || word == "<" || word == "<=" || word == "==" || word == "!=";

        static bool IsNumber(string word) => word.Length > 0 && (char.IsDigit(word[0]) || ((word[0] == '-' || word[0] == '+') && word.Length > 1));

        static int Number(string word, string text)
        {
            if (!int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException("expected a whole number at '" + word + "' in \"" + text + "\"");
            }

            return value;
        }
    }
}
