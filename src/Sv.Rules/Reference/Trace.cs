using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sv.Rules.Reference
{
    public sealed class Trace
    {
        readonly List<string> lines = new List<string>();

        public IReadOnlyList<string> Lines => lines;

        public void Add(string line) => lines.Add(line);

        public bool Has(string start)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(start, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i]).Append('\n');
            }

            return builder.ToString();
        }

        public static string Slot(int slot) => slot == RefRecord.NoSource ? "none" : "#" + slot.ToString(CultureInfo.InvariantCulture);

        public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Real(float value) => JsonWriter.FloatText(value);

        public static string Record(PackSchema schema, RefRecord record) =>
            Slot(record.Target) + " from " + Slot(record.Source) + " kind " + schema.NameOf(ValueClass.Kind, record.Kind)
            + " " + schema.NameOf(ValueClass.Channel, record.Channel) + " " + Number(record.Amount) + " x" + Number(record.Hits)
            + " " + record.Origin.ToString() + " depth " + Number(record.Depth);

        public static string Rule(Rule rule) => rule.Name + "(" + Number(rule.Id) + ")";
    }
}
