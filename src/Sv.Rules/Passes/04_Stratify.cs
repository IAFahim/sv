namespace Sv.Rules.Passes
{
    public sealed class StratifyPass : CompilerPass
    {
        public override string Id => "stratify";

        public override void Run(CompileUnit unit)
        {
            var plan = unit.Plan!;
            var layers = unit.Layers;
            for (var i = 0; i < layers.Emits.Count; i++)
            {
                layers.Emits[i].Stratum = plan.EmitStratum[i];
            }

            for (var s = 0; s < plan.SelfOrder.Length; s++)
            {
                for (var i = 0; i < layers.Emits.Count; i++)
                {
                    if (ReferenceEquals(layers.Emits[i].Rule, plan.SelfOrder[s]))
                    {
                        layers.SelfOrder.Add(layers.Emits[i]);
                        break;
                    }
                }
            }
        }
    }
}
