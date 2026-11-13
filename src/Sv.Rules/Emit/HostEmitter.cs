using System.Collections.Generic;
using System.Text;

namespace Sv.Rules.Emit
{
    public static class HostEmitter
    {
        public const string KernelsFile = "HostKernels.g.cs";
        public const string KernelsType = "HostKernels";
        public const string PackFile = "PackData.g.cs";
        public const string PackType = "PackData";
        public const string JobsFile = "PackJobs.g.cs";
        public const string JobsType = "PackJobs";
        public const string WorldType = "PackWorld";

        public static readonly string[] Phases =
        {
            "SealCount", "SealPlanTotals", "SealPlanOffsets", "SealScatter", "SealOrder", "SealCompact", "LogExamine", "Decide", "Commit", "EdgeEmits", "Digest",
        };

        public static List<KeyValuePair<string, string>> Unity(PackPlan plan, string rootNamespace)
        {
            var files = PackEmitter.Emit(plan, rootNamespace);
            files.Add(new KeyValuePair<string, string>(KernelsFile, Kernels(rootNamespace)));
            files.Add(new KeyValuePair<string, string>(PackFile, Pack(PackTables.Of(plan), rootNamespace)));
            files.Add(new KeyValuePair<string, string>(JobsFile, Jobs(rootNamespace)));
            return files;
        }

        public static string Kernels(string rootNamespace)
        {
            var text = new StringBuilder();
            text.Append("using Sv;\n\n");
            text.Append("namespace ").Append(rootNamespace).Append('\n');
            text.Append("{\n");
            text.Append("    public unsafe struct ").Append(KernelsType).Append(" : global::Sv.Host.IPackKernels\n");
            text.Append("    {\n");
            Member(text, "public ulong PackHash => Layout.PackHash;");
            Member(text, "public ulong CompiledAgainst => Layout.CompiledAgainst;");
            Member(text, "public int ScratchLanes => Kernels.ScratchLanes;");
            Member(text, "public int TotalLanes => Kernels.TotalLanes;");
            Member(text, "public int DeltaLanes => Kernels.DeltaLanes;");
            Member(text, "public int EventCount => Kernels.EventCount;");
            Member(text, "public int LanesWrittenPerChunk => Kernels.LanesWrittenPerChunk;");
            Member(text, "public int DeltaTarget(int deltaLane) => Kernels.DeltaTarget(deltaLane);");
            Member(text, "public int FloorStat(int deltaLane) => Kernels.FloorStat(deltaLane);");
            Member(text, "public int CeilingStat(int deltaLane) => Kernels.CeilingStat(deltaLane);");
            Member(text, "public void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling) => Kernels.Bounds(world, chunk, deltaLane, floor, ceiling);");
            Member(text, "public int EventId(int ordinal) => Kernels.EventId(ordinal);");
            Member(text, "public bool Forwards(int template) => Kernels.Forwards(template);");
            Member(text, "public int Forward(in WorldView world, int slot, int channel) => Kernels.Forward(world, slot, channel);");
            Member(text, "public ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next) => Kernels.Decide(world, chunk, capacity, effects, count, scratch, written, totals, records, ref next);");
            Member(text, "public ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges) => Kernels.Events(world, ref writer, chunk, recheck, scratch, edges);");
            Member(text, "public void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next) => Kernels.EdgeEmits(world, chunk, capacity, edges, scratch, ref next);");
            text.Append("    }\n");
            text.Append("}\n");
            return text.ToString();
        }

        static void Member(StringBuilder text, string member) => text.Append("        ").Append(member).Append('\n');

        public static string Pack(PackTables tables, string rootNamespace)
        {
            var code = new CodeWriter();
            code.Line("using Sv;");
            code.Line("using Sv.Host;");
            code.Blank();
            code.Open("namespace " + rootNamespace);
            code.Open("public static class " + PackType);
            code.Open("public static HostPack Create()");
            code.Line("var classes = new LaneClass[" + Names.Number(tables.Classes.Length) + "];");
            for (var lane = 0; lane < tables.Classes.Length; lane++)
            {
                if (tables.Classes[lane] != LaneClass.Retired)
                {
                    code.Line("classes[" + Names.Number(lane) + "] = LaneClass." + tables.Classes[lane].ToString() + ";");
                }
            }

            code.Line("var stats = new StatDef[" + Names.Number(tables.Stats.Length) + "];");
            for (var i = 0; i < tables.Stats.Length; i++)
            {
                var stat = tables.Stats[i];
                code.Line("stats[" + Names.Number(i) + "] = new StatDef(" + Names.Int(stat.Lane) + ", " + Names.Int(stat.Base) + ", " + Names.Int(stat.Scale) + ");");
            }

            code.Line("var templates = new int[" + Names.Number(tables.TemplateValues.Length) + "][];");
            for (var t = 0; t < tables.TemplateValues.Length; t++)
            {
                code.Line("templates[" + Names.Number(t) + "] = " + Ints(tables.TemplateValues[t]) + ";");
            }

            code.Line("var cards = new CardTerm[" + Names.Number(tables.Cards.Length) + "];");
            for (var i = 0; i < tables.Cards.Length; i++)
            {
                var card = tables.Cards[i];
                code.Line("cards[" + Names.Number(i) + "] = new CardTerm(" + Names.Int(card.Stat) + ", " + Names.Int(card.Card) + ", ShapeKind." + card.Kind.ToString() + ", "
                    + Names.Int(card.Value) + ", " + (card.PerCount ? "true" : "false") + ", StatLayer." + card.Layer.ToString() + ");");
            }

            code.Line("var sources = new StatSource[" + Names.Number(tables.Sources.Length) + "];");
            for (var i = 0; i < tables.Sources.Length; i++)
            {
                var source = tables.Sources[i];
                code.Line("sources[" + Names.Number(i) + "] = new StatSource(" + Names.Int(source.Stat) + ", " + Names.Int(source.From) + ", " + Names.Int(source.Coefficient) + ", "
                    + Names.Int(source.CurveStart) + ", " + Names.Int(source.CurveLength) + ", " + Names.Int(source.CurveMin) + ", " + Names.Int(source.CurveStep) + ");");
            }

            code.Line("var curves = " + Ints(tables.CurveValues) + ";");
            code.Line("return new HostPack(classes, " + Names.Int(tables.Channels) + ", stats, templates, cards, sources, curves, " + Names.Hex(tables.PackHash) + ");");
            code.Close();
            code.Close();
            code.Close();
            return code.ToString();
        }

        static string Ints(int[] values)
        {
            if (values.Length == 0)
            {
                return "new int[0]";
            }

            var text = new StringBuilder("new int[] { ");
            for (var i = 0; i < values.Length; i++)
            {
                text.Append(i == 0 ? string.Empty : ", ").Append(Names.Int(values[i]));
            }

            return text.Append(" }").ToString();
        }

        public static string Jobs(string rootNamespace)
        {
            var code = new CodeWriter();
            code.Line("using Sv.Host;");
            code.Line("using Sv.Unity;");
            code.Line("using Unity.Burst;");
            code.Line("using Unity.Collections.LowLevel.Unsafe;");
            code.Line("using Unity.Jobs;");
            code.Blank();
            code.Open("namespace " + rootNamespace);
            for (var phase = 0; phase < Phases.Length; phase++)
            {
                var name = Phases[phase];
                code.Line("[BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]");
                code.Open("public unsafe struct " + name + "Job : IJobParallelFor");
                code.Line("[NativeDisableUnsafePtrRestriction] public FrameContext* Context;");
                code.Line("[NativeDisableUnsafePtrRestriction] public int* Probe;");
                code.Blank();
                code.Open("public void Execute(int part)");
                code.Line("SvProbe.Mark(Probe);");
                code.Line("Phases<" + KernelsType + ">." + name + "(Context, part);");
                code.Close();
                code.Close();
                code.Blank();
            }

            code.Open("public sealed unsafe class " + JobsType + " : SvJobs");
            code.Open("public override void Run(FramePhase phase, FrameContext* context, int parts)");
            code.Open("switch (phase)");
            for (var phase = 0; phase < Phases.Length; phase++)
            {
                var name = Phases[phase];
                code.Line("case FramePhase." + name + ": new " + name + "Job { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;");
            }

            code.Close();
            code.Close();
            code.Close();
            code.Blank();
            code.Open("public static class " + WorldType);
            code.Line("public static SvWorld<" + KernelsType + "> Create(HostOptions options) => new SvWorld<" + KernelsType + ">(" + PackType + ".Create(), options, new " + JobsType + "());");
            code.Close();
            code.Close();
            return code.ToString();
        }
    }
}
