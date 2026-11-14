using System.Globalization;
using System.Text;
using Sv.Rules.Emit;

namespace Sv.Rules.Passes
{
    public sealed class EmitPass : CompilerPass
    {
        public const string RuleMapFile = "Rules.map.txt";

        public override string Id => "emit";

        public override void Run(CompileUnit unit)
        {
            unit.Files.Add(new EmittedFile(ValuesEmitter.FileName, ValuesEmitter.Emit(unit.Schema, unit.PackHash, unit.Options.Namespace)));
            unit.Files.Add(new EmittedFile(KernelEmitter.FileName, KernelEmitter.Emit(unit)));
            var map = new StringBuilder();
            var rules = PackWriter.AllRules(unit.Pack!);
            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                var line = rule.Id.ToString(CultureInfo.InvariantCulture) + "\t" + rule.Name + "\t" + rule.Layer.ToString() + "\t" + PackSource.RulesFile + "\t"
                    + rule.Offset.ToString(CultureInfo.InvariantCulture) + "\t" + (unit.EmittedRules.Contains(rule.Id) ? "compiled" : "folded");
                unit.RuleMap.Add(line);
                map.Append(line).Append('\n');
            }

            unit.Files.Add(new EmittedFile(RuleMapFile, map.ToString()));
        }
    }
}
