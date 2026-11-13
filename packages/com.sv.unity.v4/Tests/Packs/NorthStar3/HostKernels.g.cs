using Sv;

namespace Sv.Kernels.NorthStar3
{
    public unsafe struct HostKernels : global::Sv.Host.IPackKernels
    {
        public ulong PackHash => Layout.PackHash;
        public ulong CompiledAgainst => Layout.CompiledAgainst;
        public int ScratchLanes => Kernels.ScratchLanes;
        public int TotalLanes => Kernels.TotalLanes;
        public int DeltaLanes => Kernels.DeltaLanes;
        public int EventCount => Kernels.EventCount;
        public int LanesWrittenPerChunk => Kernels.LanesWrittenPerChunk;
        public int DeltaTarget(int deltaLane) => Kernels.DeltaTarget(deltaLane);
        public int FloorStat(int deltaLane) => Kernels.FloorStat(deltaLane);
        public int CeilingStat(int deltaLane) => Kernels.CeilingStat(deltaLane);
        public void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling) => Kernels.Bounds(world, chunk, deltaLane, floor, ceiling);
        public int EventId(int ordinal) => Kernels.EventId(ordinal);
        public bool Forwards(int template) => Kernels.Forwards(template);
        public int Forward(in WorldView world, int slot, int channel) => Kernels.Forward(world, slot, channel);
        public ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next) => Kernels.Decide(world, chunk, capacity, effects, count, scratch, written, totals, records, ref next);
        public ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges) => Kernels.Events(world, ref writer, chunk, recheck, scratch, edges);
        public void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next) => Kernels.EdgeEmits(world, chunk, capacity, edges, scratch, ref next);
    }
}
