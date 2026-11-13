namespace Sv.Host
{
    public unsafe interface IPackKernels
    {
        ulong PackHash { get; }

        ulong CompiledAgainst { get; }

        int ScratchLanes { get; }

        int TotalLanes { get; }

        int DeltaLanes { get; }

        int EventCount { get; }

        int LanesWrittenPerChunk { get; }

        int DeltaTarget(int deltaLane);

        int FloorStat(int deltaLane);

        int CeilingStat(int deltaLane);

        void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling);

        int EventId(int ordinal);

        bool Forwards(int template);

        int Forward(in WorldView world, int slot, int channel);

        ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next);

        ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges);

        void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next);
    }

    public struct PackForward<TKernels> : IForward
        where TKernels : struct, IPackKernels
    {
        public WorldView World;

        public int Forward(int slot, int channel) => default(TKernels).Forward(World, slot, channel);
    }
}
