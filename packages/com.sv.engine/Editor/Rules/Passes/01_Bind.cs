namespace Sv.Rules.Passes
{
    public sealed class BindPass : CompilerPass
    {
        public override string Id => "bind";

        public override void Run(CompileUnit unit)
        {
            if (unit.Pack != null)
            {
                return;
            }

            if (unit.Source == null)
            {
                unit.Diagnostics.Error(DiagnosticCode.MissingKey, PackSource.ValuesFile, 0, -1, -1, "nothing to compile: no pack source and no pack");
                return;
            }

            var read = PackReader.Read(unit.Source.Value);
            unit.Absorb(read.Diagnostics);
            unit.Pack = read.Value;
        }
    }
}
