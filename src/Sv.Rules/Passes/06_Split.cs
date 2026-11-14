using System.Collections.Generic;

namespace Sv.Rules.Passes
{
    public sealed class SplitPass : CompilerPass
    {
        public override string Id => "split";

        public override void Run(CompileUnit unit)
        {
            var schema = unit.Schema;
            var layers = unit.Layers;
            for (var i = 0; i < layers.Gates.Count; i++)
            {
                layers.Gates[i].When = Split(layers.Gates[i].When, schema);
            }

            for (var i = 0; i < layers.Emits.Count; i++)
            {
                layers.Emits[i].When = Split(layers.Emits[i].When, schema);
            }

            for (var i = 0; i < layers.Edges.Count; i++)
            {
                layers.Edges[i].When = Split(layers.Edges[i].When, schema);
            }

            for (var i = 0; i < layers.Flows.Count; i++)
            {
                layers.Flows[i].When = Split(layers.Flows[i].When, schema);
            }
        }

        public static Condition Split(Condition condition, PackSchema schema)
        {
            var masks = new List<RowMask>(condition.Masks);
            var rest = new List<Expression>();
            var parts = ExprTools.Conjuncts(condition.Rest);
            for (var i = 0; i < parts.Length; i++)
            {
                if (ExprTools.TryMask(parts[i], schema, out var mask))
                {
                    var known = false;
                    for (var m = 0; m < masks.Count; m++)
                    {
                        known |= masks[m].Same(mask);
                    }

                    if (!known)
                    {
                        masks.Add(mask);
                    }
                }
                else
                {
                    rest.Add(parts[i]);
                }
            }

            return new Condition(condition.Guard, masks.ToArray(), ExprTools.All(rest));
        }
    }
}
