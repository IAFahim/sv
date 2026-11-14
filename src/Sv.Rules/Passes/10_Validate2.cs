using System.Globalization;

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
