using System.Collections.Generic;
using System.Globalization;
using Sv.Rules.Emit;

namespace Sv.Rules.Passes
{
    public sealed class Validate2Pass : CompilerPass
    {
        public override string Id => "validate2";

        public override void Run(CompileUnit unit)
        {
            var pack = unit.Pack!;
            var schema = pack.Schema;
            var rules = PackWriter.AllRules(pack);
            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule.Layer == RuleLayer.Chain || unit.EmittedRules.Contains(rule.Id))
                {
                    continue;
                }

                unit.Diagnostics.Warning(DiagnosticCode.Shadowed, PackSource.RulesFile, rule.Offset, rule.Id, -1,
                    rule.Name + " (" + rule.Id.ToString(CultureInfo.InvariantCulture) + ") compiles to nothing: its condition is never true or it changes nothing");
            }

            for (var c = ValueClass.Stat; c <= ValueClass.Kind; c++)
            {
                var rows = schema.Of(c);
                var seen = new Dictionary<string, string>();
                var limit = c == ValueClass.Channel || c == ValueClass.Kind ? 255 : 65535;
                for (var i = 0; i < rows.Length; i++)
                {
                    var name = Names.Identifier(rows[i].Name);
                    if (seen.TryGetValue(name, out var other))
                    {
                        unit.Diagnostics.Error(DiagnosticCode.DuplicateName, PackSource.ValuesFile, rows[i].Offset, -1, -1, rows[i].Name + " and " + other + " both become the identifier " + name);
                    }
                    else
                    {
                        seen.Add(name, rows[i].Name);
                    }

                    if (rows[i].Id < 0 || rows[i].Id > limit)
                    {
                        unit.Diagnostics.Error(DiagnosticCode.IdOutOfRange, PackSource.ValuesFile, rows[i].Offset, -1, -1, rows[i].Name + " has number " + rows[i].Id.ToString(CultureInfo.InvariantCulture) + ", above " + limit.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            if (unit.Options.Previous != null)
            {
                unit.Absorb(PackCompiler.Evolution(unit.Options.Previous, schema));
            }

            if (unit.PackHash != PackHash.Of(pack))
            {
                unit.Diagnostics.Error(DiagnosticCode.LaneCounter, PackSource.ValuesFile, 0, -1, -1, "PackHash changed between validation and emission");
            }

            if (unit.Files.Count < 2)
            {
                unit.Diagnostics.Error(DiagnosticCode.MissingKey, PackSource.RulesFile, 0, -1, -1, "the emitter wrote no Values or Kernels file");
            }
        }
    }
}
