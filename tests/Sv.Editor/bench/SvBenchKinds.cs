using Sv.Unity;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace SvBench
{
    public struct BHp : IComponentData { public int Value; }
    public struct BHpDelta : IComponentData { public int Value; }
    public struct BMaxHp : IComponentData { public float Value; }
    public struct BMana : IComponentData { public int Value; }
    public struct BManaDelta : IComponentData { public int Value; }
    public struct BLow : IComponentData, IEnableableComponent { }
    public unsafe struct BFiller : IComponentData { public fixed byte Bytes[288]; }

    public static class BenchProbe
    {
        public static int ManagedRuns;

        [BurstDiscard]
        public static void Mark() => ManagedRuns++;
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct StandInKindJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<SvSlot> Slot;
        [ReadOnly] public ComponentTypeHandle<BHp> Hp;
        [ReadOnly] public ComponentTypeHandle<BMana> Mana;
        [ReadOnly] public ComponentTypeHandle<BMaxHp> MaxHp;
        public ComponentTypeHandle<BHpDelta> HpDelta;
        public ComponentTypeHandle<BManaDelta> ManaDelta;
        public SvInboxWriter Inbox;
        public int Timelines;
        public int WritesTimesTwo;
        public int CrossThreshold;
        public int Rows;
        public bool HotTarget;
        public uint Tick;
        [NativeDisableUnsafePtrRestriction] public long* Sink;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            BenchProbe.Mark();
            var slots = (int*)chunk.GetRequiredComponentDataPtrRO(ref Slot);
            var hp = (int*)chunk.GetRequiredComponentDataPtrRO(ref Hp);
            var mana = (int*)chunk.GetRequiredComponentDataPtrRO(ref Mana);
            var max = (int*)chunk.GetRequiredComponentDataPtrRO(ref MaxHp);
            var hpd = (int*)chunk.GetRequiredComponentDataPtrRW(ref HpDelta);
            var mpd = (int*)chunk.GetRequiredComponentDataPtrRW(ref ManaDelta);
            var count = chunk.Count;
            long reads = 0;
            for (var row = 0; row < count; row++)
            {
                var slot = slots[row];
                var accHp = 0;
                var accMana = 0;
                for (var t = 0; t < Timelines; t++)
                {
                    var id = (uint)(slot * Timelines + t);
                    var h = (id * 0x9E3779B1u) ^ (Tick * 0x85EBCA77u);
                    h ^= h >> 15;
                    h *= 0x2C1B3C6Du;
                    h ^= h >> 12;
                    var writes = (WritesTimesTwo + (int)(id & 1)) >> 1;
                    for (var r = writes; r < 5; r++)
                    {
                        reads += (r & 3) == 0 ? max[row] : (r & 1) == 0 ? hp[row] : mana[row];
                    }

                    for (var j = 0; j < writes; j++)
                    {
                        var amount = ((Tick + (uint)t + (uint)j) & 1) == 0 ? 1 : -1;
                        if (((h >> (j * 7)) & 255) < (uint)CrossThreshold)
                        {
                            Inbox.Send(HotTarget ? 0 : (int)((h >> 3) % (uint)Rows), amount);
                        }
                        else if ((j & 1) == 0)
                        {
                            accHp += amount;
                        }
                        else
                        {
                            accMana += amount;
                        }
                    }
                }

                hpd[row] += accHp;
                mpd[row] += accMana;
            }

            Sink[unfilteredChunkIndex & 1023] = reads;
        }
    }

    [DisableAutoCreation]
    public partial struct StandInKindSystem : ISystem
    {
        public StandInKindJob Job;
        EntityQuery query;

        public void OnCreate(ref SystemState state)
        {
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlot, BHpDelta>().Build(ref state);
            Job.Slot = state.GetComponentTypeHandle<SvSlot>(true);
            Job.Hp = state.GetComponentTypeHandle<BHp>(true);
            Job.Mana = state.GetComponentTypeHandle<BMana>(true);
            Job.MaxHp = state.GetComponentTypeHandle<BMaxHp>(true);
            Job.HpDelta = state.GetComponentTypeHandle<BHpDelta>(false);
            Job.ManaDelta = state.GetComponentTypeHandle<BManaDelta>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            Job.Slot.Update(ref state);
            Job.Hp.Update(ref state);
            Job.Mana.Update(ref state);
            Job.MaxHp.Update(ref state);
            Job.HpDelta.Update(ref state);
            Job.ManaDelta.Update(ref state);
            state.Dependency = Job.ScheduleParallel(query, state.Dependency);
            state.Dependency.Complete();
            Job.Tick++;
        }
    }
}
