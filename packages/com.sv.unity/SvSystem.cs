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
    /// The v2 frame order (clause 7) as one Burst ISystem. Hosts drain their event channels and
    /// play back structural command buffers before calling Update (the drain seam — sv's events
    /// are host-side). OnUpdate then runs: reconcile at the structural boundary when
    /// <see cref="StructuralChange"/> is set (each chunk's SvRange written, board renumbered), the
    /// chunk job driving intrinsics through the enabled masks, ExpireModifiers at
    /// <see cref="Tick"/> with due crossings collected into <see cref="DueCrossings"/>
    /// (<see cref="DueCrossingCount"/> of them, (tick, id) order), and Recalculate. The board,
    /// clamp, delta, tick, crossing buffer and structural flag arrive from the owning context;
    /// hosts copy this shape.
    /// </summary>
    [BurstCompile]
    public partial struct SvIntrinsicsSystem : ISystem
    {
        public Board Board;

        public UnityPopulation Population;

        public Clamp Clamp;

        public int Delta;

        public long Tick;

        public bool StructuralChange;

        public NativeArray<long> DueCrossings;

        public int DueCrossingCount;

        public int ExpiredLastFrame;

        EntityQuery query;

        ComponentTypeHandle<SvSlot> slotHandle;

        ComponentTypeHandle<SvRange> rangeHandle;

        ComponentTypeHandle<SvMana> manaHandle;

        ComponentLookup<SvMana> manaLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            slotHandle = state.GetComponentTypeHandle<SvSlot>();
            rangeHandle = state.GetComponentTypeHandle<SvRange>();
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
            rangeHandle.Update(ref state);
            manaHandle.Update(ref state);
            manaLookup.Update(ref state);
            if (StructuralChange)
            {
                using var permutation = Population.Reconcile(state.EntityManager);
                StructuralChange = false;
            }

            var sv = new SvChunk(Board, slotHandle, rangeHandle);
            state.Dependency = new IntrinsicsJob
            {
                Sv = sv,
                ManaHandle = manaHandle,
                Delta = Delta,
                Clamp = Clamp,
            }.ScheduleParallel(query, state.Dependency);
            ExpiredLastFrame = Board.ExpireModifiers(Tick);
            DueCrossingCount = Board.TakeDueCrossings(DueCrossings.AsSpan());
            Board.Recalculate();
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
