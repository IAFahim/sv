using Sv.Host;
using Sv.Unity;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Sv.Kernels.Lego
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealCountJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealCount(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealPlanTotalsJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealPlanTotals(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealPlanOffsetsJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealPlanOffsets(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealScatterJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealScatter(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealOrderJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealOrder(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SealCompactJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.SealCompact(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct LogExamineJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.LogExamine(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct DecideJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.Decide(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct CommitJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.Commit(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct EdgeEmitsJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.EdgeEmits(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct DigestJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.Digest(Context, part);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    public unsafe struct SourcesJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public FrameContext* Context;
        [NativeDisableUnsafePtrRestriction] public int* Probe;

        public void Execute(int part)
        {
            SvProbe.Mark(Probe);
            Phases<HostKernels>.Sources(Context, part);
        }
    }

    public sealed unsafe class PackJobs : SvJobs
    {
        public override void Run(FramePhase phase, FrameContext* context, int parts)
        {
            switch (phase)
            {
                case FramePhase.SealCount: new SealCountJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.SealPlanTotals: new SealPlanTotalsJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.SealPlanOffsets: new SealPlanOffsetsJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.SealScatter: new SealScatterJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.SealOrder: new SealOrderJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.SealCompact: new SealCompactJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.LogExamine: new LogExamineJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.Decide: new DecideJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.Commit: new CommitJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.EdgeEmits: new EdgeEmitsJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.Digest: new DigestJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
                case FramePhase.Sources: new SourcesJob { Context = context, Probe = ProbeAt(phase) }.Schedule(parts, 1).Complete(); break;
            }
        }
    }

    public static class PackWorld
    {
        public static SvWorld<HostKernels> Create(HostOptions options) => new SvWorld<HostKernels>(PackData.Create(), options, new PackJobs());
    }
}
