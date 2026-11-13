using System.Collections.Generic;
using System.Globalization;

namespace Sv.Rules.Passes
{
    public sealed class OrderPass : CompilerPass
    {
        public const int MaxGroups = 64;

        public override string Id => "order";

        public override void Run(CompileUnit unit)
        {
            var groups = new SortedDictionary<int, int>();
            var gates = unit.Layers.Gates;
            for (var g = 0; g < gates.Count; g++)
            {
                var rule = gates[g].Rule;
                if (g > 0 && gates[g - 1].Order >= gates[g].Order)
                {
                    unit.Diagnostics.Error(DiagnosticCode.AdmitTie, PackSource.RulesFile, rule.Offset, rule.Id, gates[g - 1].Rule.Id, "gate order is not strictly by priority then id");
                }

                if (rule.Group >= 0 && rule.Shapes.Length > 0 && !groups.ContainsKey(rule.Group))
                {
                    groups.Add(rule.Group, groups.Count);
                }
            }

            if (groups.Count > MaxGroups)
            {
                unit.Diagnostics.Error(DiagnosticCode.BadValue, PackSource.RulesFile, 0, -1, -1, "a pack holds at most " + MaxGroups.ToString(CultureInfo.InvariantCulture) + " exclusive shape groups; it has " + groups.Count.ToString(CultureInfo.InvariantCulture));
                return;
            }

            for (var g = 0; g < gates.Count; g++)
            {
                var rule = gates[g].Rule;
                if (rule.Group >= 0 && rule.Shapes.Length > 0)
                {
                    gates[g].GroupBit = groups[rule.Group];
                }
            }
        }
    }
}
