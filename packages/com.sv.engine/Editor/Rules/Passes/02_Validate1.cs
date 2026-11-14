namespace Sv.Rules.Passes
{
    public sealed class Validate1Pass : CompilerPass
    {
        public override string Id => "validate1";

        public override void Run(CompileUnit unit)
        {
            var planned = PackCompiler.Plan(unit.Pack!);
            unit.Absorb(planned.Diagnostics);
            unit.Plan = planned.Value;
            if (unit.Plan != null)
            {
                unit.PackHash = unit.Plan.Hash;
            }
        }
    }
}
