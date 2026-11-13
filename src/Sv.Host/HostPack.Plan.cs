using System;
using Sv.Rules;

namespace Sv.Host
{
    public sealed partial class HostPack
    {
        public static HostPack From(PackPlan plan)
        {
            var tables = PackTables.Of(plan);
            return new HostPack(tables.Classes, tables.Channels, tables.Stats, tables.TemplateValues, tables.Cards, tables.Sources, tables.CurveValues, tables.PackHash);
        }

        public int TemplateIndexOf(PackSchema schema, int templateId)
        {
            for (var t = 0; t < schema.Templates.Length; t++)
            {
                if (schema.Templates[t].Id == templateId)
                {
                    return t;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(templateId), "sv: the pack has no template " + templateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
