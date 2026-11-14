using System;

namespace Sv.Host
{
    public enum FramePhase : byte
    {
        SealCount = 0,
        SealPlanTotals = 1,
        SealPlanOffsets = 2,
        SealScatter = 3,
        SealOrder = 4,
        SealCompact = 5,
        LogExamine = 6,
        Decide = 7,
        Commit = 8,
        EdgeEmits = 9,
        Digest = 10,
        Sources = 11,
    }

    public static class FramePhases
    {
        public const int Count = 12;
    }

    public unsafe interface IPhaseRunner : IDisposable
    {
        void Run(FramePhase phase, FrameContext* context, int parts);
    }

    public sealed unsafe class WorkerRunner<TKernels> : IPhaseRunner
        where TKernels : struct, IPackKernels
    {
        readonly Workers workers;

        public WorkerRunner(int workerCount, double spinMicroseconds = Workers.DefaultSpinMicroseconds)
        {
            workers = new Workers(workerCount, spinMicroseconds);
        }

        public int Count => workers.Count;

        internal long WorkerAllocatedBytes => workers.WorkerAllocatedBytes;

        public void Run(FramePhase phase, FrameContext* context, int parts) => workers.Run(BodyOf(phase), context, parts);

        static delegate*<void*, int, void> BodyOf(FramePhase phase)
        {
            switch (phase)
            {
                case FramePhase.SealCount: return &Phases<TKernels>.SealCount;
                case FramePhase.SealPlanTotals: return &Phases<TKernels>.SealPlanTotals;
                case FramePhase.SealPlanOffsets: return &Phases<TKernels>.SealPlanOffsets;
                case FramePhase.SealScatter: return &Phases<TKernels>.SealScatter;
                case FramePhase.SealOrder: return &Phases<TKernels>.SealOrder;
                case FramePhase.SealCompact: return &Phases<TKernels>.SealCompact;
                case FramePhase.LogExamine: return &Phases<TKernels>.LogExamine;
                case FramePhase.Decide: return &Phases<TKernels>.Decide;
                case FramePhase.Commit: return &Phases<TKernels>.Commit;
                case FramePhase.EdgeEmits: return &Phases<TKernels>.EdgeEmits;
                case FramePhase.Sources: return &Phases<TKernels>.Sources;
                default: return &Phases<TKernels>.Digest;
            }
        }

        public void Dispose() => workers.Dispose();
    }
}
