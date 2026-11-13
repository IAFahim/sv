using Sv.Host;

namespace Sv.Unity
{
    public abstract unsafe class SvJobs : IPhaseRunner
    {
        int* probes = (int*)Memory.AllocZeroed(FramePhases.Count, sizeof(int));

        public int ProbeOf(FramePhase phase) => probes == null ? SvProbe.Unseen : probes[(int)phase];

        public void ClearProbes()
        {
            for (var phase = 0; phase < FramePhases.Count; phase++)
            {
                probes[phase] = SvProbe.Unseen;
            }
        }

        protected int* ProbeAt(FramePhase phase) => probes + (int)phase;

        public abstract void Run(FramePhase phase, FrameContext* context, int parts);

        public void Dispose()
        {
            Memory.Free(probes);
            probes = null;
        }
    }
}
