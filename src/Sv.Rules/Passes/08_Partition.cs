using System.Globalization;

namespace Sv.Rules.Passes
{
    public sealed class PartitionPass : CompilerPass
    {
        public override string Id => "partition";

        public override void Run(CompileUnit unit)
        {
            var kernel = unit.Kernel;
            for (var i = 0; i < kernel.Admit.Count; i++)
            {
                Cut(kernel.Admit[i], "Admit", kernel.MethodCap);
            }

            for (var i = 0; i < kernel.SelfAdmit.Count; i++)
            {
                Cut(kernel.SelfAdmit[i], "SelfAdmit", kernel.MethodCap);
            }

            Cut(kernel.AdmitAny, "Admit", kernel.MethodCap);
            Cut(kernel.SelfAdmitAny, "SelfAdmit", kernel.MethodCap);
        }

        static void Cut(GateChannel dispatch, string prefix, int cap)
        {
            dispatch.Method = prefix + (dispatch.Channel < 0 ? "Any" : "Channel" + dispatch.Channel.ToString(CultureInfo.InvariantCulture));
            GateBlock? block = null;
            for (var i = 0; i < dispatch.Cases.Count; i++)
            {
                var gateCase = dispatch.Cases[i];
                if (block == null || block.Guard != gateCase.When.Guard || block.Cases.Count >= cap)
                {
                    block = new GateBlock(gateCase.When.Guard);
                    block.Method = dispatch.Method + "Group" + dispatch.Blocks.Count.ToString(CultureInfo.InvariantCulture);
                    dispatch.Blocks.Add(block);
                }

                block.Cases.Add(gateCase);
            }
        }
    }
}
