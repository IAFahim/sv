using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Unity
{
    public struct SvMana : IComponentData
    {
        public int Value;
    }

    /// <summary>
    /// The svSystemBase pattern: handles and lookups Update(ref state) every frame; structural
    /// changes play back at the sync point, reconcile runs at the drain step (clause 7 order),
    /// then the chunk job drives sv intrinsics through SvChunk. Hosts copy this shape; the
    /// board/clamp/delta arrive from the owning context.
    /// </summary>
    [BurstCompile]
    public partial struct SvIntrinsicsSystem : ISystem
    {
        public Board Board;

        public UnityPopulation Population;

        public Clamp Clamp;

        public int Delta;

        EntityQuery query;

        ComponentTypeHandle<SvSlot> slotHandle;

        ComponentTypeHandle<SvMana> manaHandle;

        ComponentLookup<SvMana> manaLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            slotHandle = state.GetComponentTypeHandle<SvSlot>();
            manaHandle = state.GetComponentTypeHandle<SvMana>();
            manaLookup = state.GetComponentLookup<SvMana>();
            query = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<SvMana>()
                .WithAll<SvSlot>()
                .Build(ref state);
            state.RequireForUpdate(query);
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            slotHandle.Update(ref state);
            manaHandle.Update(ref state);
            manaLookup.Update(ref state);
            var sv = new SvChunk(Board, slotHandle);
            state.Dependency = new IntrinsicsJob
            {
                Sv = sv,
                ManaHandle = manaHandle,
                Delta = Delta,
                Clamp = Clamp,
            }.ScheduleParallel(query, state.Dependency);
        }

        [BurstCompile]
        public unsafe struct IntrinsicsJob : IJobChunk
        {
            public SvChunk Sv;

            public ComponentTypeHandle<SvMana> ManaHandle;

            public int Delta;

            public Clamp Clamp;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var applied = stackalloc int[128];
                Sv.RunIntrinsics(ref ManaHandle, chunk, useEnabledMask, chunkEnabledMask, Delta, Clamp, new Span<int>(applied, 128));
            }
        }
    }
}
