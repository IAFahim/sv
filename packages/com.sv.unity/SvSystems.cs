using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Sv.Unity
{
    public struct SvStageTimes
    {
        public long Sparse;
        public long Scatter;
        public long Fold;
        public long Derive;
        public long Digest;
        public long Send;
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public unsafe partial struct SvBeginSystem : ISystem
    {
        IntPtr host;
        EntityQuery query;
        SvScatterJob scatter;
        SvFoldJob fold;
        SvLaneResetJob reset;
        ComponentLookup<SvSlot> slots;
        ComponentLookup<SvSlotCleanup> cleanups;
        EntityQuery husks;
        int stats;
        public bool Split;
        public SvStageTimes Times;

        public void OnCreate(ref SystemState state)
        {
            host = state.GetEntityQuery(ComponentType.ReadOnly<SvHostRef>()).GetSingleton<SvHostRef>().Handle;
            var sv = SvHost.From(host);
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlot>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);
            var spare = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<SvSlot>());
            stats = sv.Links.Stats.Count;
            scatter = new SvScatterJob { S0 = spare, S1 = spare, S2 = spare, S3 = spare, S4 = spare, S5 = spare, S6 = spare, S7 = spare };
            for (var s = 0; s < stats; s++)
            {
                var handle = state.GetDynamicComponentTypeHandle(sv.Links.Stats[s].Type);
                switch (s)
                {
                    case 0: scatter.S0 = handle; break;
                    case 1: scatter.S1 = handle; break;
                    case 2: scatter.S2 = handle; break;
                    case 3: scatter.S3 = handle; break;
                    case 4: scatter.S4 = handle; break;
                    case 5: scatter.S5 = handle; break;
                    case 6: scatter.S6 = handle; break;
                    case 7: scatter.S7 = handle; break;
                }
            }

            scatter.Storage = state.GetEntityStorageInfoLookup();
            scatter.Writes = sv.Writes;
            scatter.Count = sv.WriteCount;
            scatter.SlotEntity = sv.SlotEntity;
            scatter.HandleOfStat = sv.HandleOfStat;
            fold = new SvFoldJob { Lanes = sv.Links.Intrinsics.Count, Slot = state.GetComponentTypeHandle<SvSlot>(true) };
            fold.L0 = SvLanes.Build(ref state, sv, 0, spare, true);
            fold.L1 = SvLanes.Build(ref state, sv, 1, spare, true);
            fold.L2 = SvLanes.Build(ref state, sv, 2, spare, true);
            fold.L3 = SvLanes.Build(ref state, sv, 3, spare, true);
            reset = new SvLaneResetJob { Sent0 = sv.SentPtr(0), Sent1 = sv.SentPtr(1), Sent2 = sv.SentPtr(2), Sent3 = sv.SentPtr(3) };
            slots = state.GetComponentLookup<SvSlot>(false);
            cleanups = state.GetComponentLookup<SvSlotCleanup>(false);
            husks = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlotCleanup>().WithAbsent<SvSlot>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Dependency.Complete();
            var sv = SvHost.From(host);
            var t0 = SvClock.Now;
            Rows(ref state, sv);
            sv.Sparse();
            var t1 = SvClock.Now;
            while (sv.TakeWrites() > 0)
            {
                UpdateScatter(ref state);
                scatter.Run();
            }

            var handle = state.Dependency;

            var t2 = SvClock.Now;
            sv.ClearEdges();
            SvLanes.Update(ref fold.L0, ref state);
            SvLanes.Update(ref fold.L1, ref state);
            SvLanes.Update(ref fold.L2, ref state);
            SvLanes.Update(ref fold.L3, ref state);
            fold.Slot.Update(ref state);
            fold.Filter = sv.Filter;
            fold.LastVersion = state.LastSystemVersion;
            handle = fold.ScheduleParallel(query, handle);
            handle = reset.Schedule(handle);
            if (Split)
            {
                handle.Complete();
            }

            var t3 = SvClock.Now;
            sv.FoldHandle = handle;
            state.Dependency = handle;
            Times.Sparse = t1 - t0;
            Times.Scatter = t2 - t1;
            Times.Fold = t3 - t2;
        }

        void Rows(ref SystemState state, SvHost sv)
        {
            var manager = state.EntityManager;
            if (sv.PendingRemovals.Length > 0)
            {
                for (var i = sv.PendingRemovals.Length - 1; i >= 0; i--)
                {
                    if (!manager.Exists(sv.PendingRemovals[i]))
                    {
                        sv.PendingRemovals.RemoveAtSwapBack(i);
                    }
                }

                manager.RemoveComponent(sv.PendingRemovals.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<SvSlot>(), ComponentType.ReadWrite<SvSlotCleanup>()));
                sv.PendingRemovals.Clear();
            }

            if (!husks.IsEmptyIgnoreFilter)
            {
                using (var entities = husks.ToEntityArray(Allocator.Temp))
                using (var dead = husks.ToComponentDataArray<SvSlotCleanup>(Allocator.Temp))
                {
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var slot = dead[i].Slot;
                        if (slot >= 0 && slot < sv.Capacity && sv.SlotEntity[slot] == entities[i])
                        {
                            sv.Free(slot);
                        }
                    }
                }

                manager.RemoveComponent<SvSlotCleanup>(husks);
            }

            if (sv.PendingEntities.Length == 0)
            {
                return;
            }

            for (var i = sv.PendingEntities.Length - 1; i >= 0; i--)
            {
                if (!manager.Exists(sv.PendingEntities[i]))
                {
                    sv.DropPending(i);
                }
            }

            manager.AddComponent(sv.PendingEntities.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<SvSlot>(), ComponentType.ReadWrite<SvSlotCleanup>()));
            slots.Update(ref state);
            cleanups.Update(ref state);
            new SvSlotWriteJob { Slots = slots, Cleanups = cleanups, Entities = sv.PendingEntities.AsArray(), Assigned = sv.PendingSlots, SlotEntity = sv.SlotEntity }.Run();
            sv.ClearPending();
        }

        void UpdateScatter(ref SystemState state)
        {
            scatter.S0.Update(ref state);
            scatter.S1.Update(ref state);
            scatter.S2.Update(ref state);
            scatter.S3.Update(ref state);
            scatter.S4.Update(ref state);
            scatter.S5.Update(ref state);
            scatter.S6.Update(ref state);
            scatter.S7.Update(ref state);
            scatter.Storage.Update(ref state);
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public unsafe partial struct SvEndSystem : ISystem
    {
        IntPtr host;
        EntityQuery query;
        SvDigestJob digest;
        SvSendJob send0;
        SvSendJob send1;
        SvSendJob send2;
        SvSendJob send3;
        int sends;
        public SvStageTimes Times;

        public void OnCreate(ref SystemState state)
        {
            host = state.GetEntityQuery(ComponentType.ReadOnly<SvHostRef>()).GetSingleton<SvHostRef>().Handle;
            var sv = SvHost.From(host);
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlot>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);
            var spare = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<SvSlot>());
            digest = new SvDigestJob { Lanes = sv.Links.Intrinsics.Count, Slot = state.GetComponentTypeHandle<SvSlot>(true), Partial = sv.Partial };
            digest.L0 = SvLanes.Build(ref state, sv, 0, spare, false);
            digest.L1 = SvLanes.Build(ref state, sv, 1, spare, false);
            digest.L2 = SvLanes.Build(ref state, sv, 2, spare, false);
            digest.L3 = SvLanes.Build(ref state, sv, 3, spare, false);
            sends = sv.Links.Sends.Count;
            send0 = BuildSend(ref state, sv, 0, spare);
            send1 = BuildSend(ref state, sv, 1, spare);
            send2 = BuildSend(ref state, sv, 2, spare);
            send3 = BuildSend(ref state, sv, 3, spare);
        }

        static SvSendJob BuildSend(ref SystemState state, SvHost sv, int index, DynamicComponentTypeHandle spare)
        {
            if (index >= sv.Links.Sends.Count)
            {
                return new SvSendJob { Effect = spare, Target = spare };
            }

            var spec = sv.Links.Sends[index];
            return new SvSendJob
            {
                Effect = state.GetDynamicComponentTypeHandle(spec.Effect),
                Target = state.GetDynamicComponentTypeHandle(spec.Target),
                Inbox = sv.InboxWriter(spec.Intrinsic),
                Capacity = sv.Capacity,
            };
        }

        JobHandle Send(ref SvSendJob job, ref SystemState state, SvHost sv, JobHandle after)
        {
            job.Effect.Update(ref state);
            job.Target.Update(ref state);
            job.LastVersion = state.LastSystemVersion;
            job.Filter = sv.Filter;
            return job.ScheduleParallel(query, after);
        }

        public void OnUpdate(ref SystemState state)
        {
            var sv = SvHost.From(host);
            var ts = SvClock.Now;
            if (sends > 0)
            {
                var sent = JobHandle.CombineDependencies(state.Dependency, sv.FoldHandle);
                sent = Send(ref send0, ref state, sv, sent);
                if (sends > 1) sent = Send(ref send1, ref state, sv, sent);
                if (sends > 2) sent = Send(ref send2, ref state, sv, sent);
                if (sends > 3) sent = Send(ref send3, ref state, sv, sent);
                sent.Complete();
                state.Dependency = sent;
            }

            var t0 = SvClock.Now;
            var derive = default(JobHandle);
            for (var k = 0; k < sv.Links.Intrinsics.Count; k++)
            {
                if (sv.Edges[k] == null)
                {
                    continue;
                }

                derive = JobHandle.CombineDependencies(derive, new SvDeriveJob
                {
                    Edges = sv.Edges[k],
                    State = sv.States[k],
                    EdgeCount = sv.EdgeCountPtr(k),
                    Events = sv.EventBuffers[k],
                    Channel = sv.ChannelPtr(k),
                    Capacity = sv.Links.Intrinsics[k].Condition.Capacity,
                    Words = sv.Words,
                }.Schedule(sv.FoldHandle));
            }

            derive.Complete();
            var t1 = SvClock.Now;
            if (sv.DigestEvery > 0 && sv.Tick % sv.DigestEvery == 0)
            {
                UnsafeUtility.MemClear(sv.Partial, sv.PartialCount * 8L);
                SvLanes.Update(ref digest.L0, ref state);
                SvLanes.Update(ref digest.L1, ref state);
                SvLanes.Update(ref digest.L2, ref state);
                SvLanes.Update(ref digest.L3, ref state);
                digest.Slot.Update(ref state);
                state.Dependency = digest.ScheduleParallel(query, JobHandle.CombineDependencies(state.Dependency, sv.FoldHandle));
                state.Dependency.Complete();
                var cells = 0ul;
                unchecked
                {
                    for (var i = 0; i < sv.PartialCount; i += 8)
                    {
                        cells += sv.Partial[i];
                    }

                    sv.CellDigestValue = cells;
                    sv.Digest = sv.Board.Digest + cells;
                    sv.DigestTick = sv.Tick;
                }
            }

            var t2 = SvClock.Now;
            sv.Tick++;
            Times.Send = t0 - ts;
            Times.Derive = t1 - t0;
            Times.Digest = t2 - t1;
        }
    }

    public static unsafe class SvLanes
    {
        public static SvLane Build(ref SystemState state, SvHost sv, int k, DynamicComponentTypeHandle spare, bool write)
        {
            var lane = new SvLane { Value = spare, Delta = spare, Ceiling = spare, Floor = spare, Gate = spare };
            if (k >= sv.Links.Intrinsics.Count)
            {
                return lane;
            }

            var spec = sv.Links.Intrinsics[k];
            lane.Column = spec.Column;
            lane.Value = state.GetDynamicComponentTypeHandle(write ? spec.Value : ComponentType.ReadOnly(spec.Value.TypeIndex));
            lane.Delta = write ? state.GetDynamicComponentTypeHandle(spec.Delta) : spare;
            lane.HasCeiling = spec.Ceiling.FromStat;
            lane.CeilingConstant = spec.Ceiling.Constant;
            lane.HasFloor = spec.Floor.FromStat;
            lane.FloorConstant = spec.Floor.Constant;
            if (write && spec.Ceiling.FromStat)
            {
                lane.Ceiling = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(spec.Ceiling.Stat.TypeIndex));
            }

            if (write && spec.Floor.FromStat)
            {
                lane.Floor = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(spec.Floor.Stat.TypeIndex));
            }

            if (write && spec.HasCondition)
            {
                lane.HasCondition = true;
                lane.EnterNumerator = spec.Condition.EnterNumerator;
                lane.EnterDenominator = spec.Condition.EnterDenominator;
                lane.StayNumerator = spec.Condition.StayNumerator;
                lane.StayDenominator = spec.Condition.StayDenominator;
                lane.State = sv.States[k];
                lane.Edges = sv.Edges[k];
                lane.EdgeCount = sv.EdgeCountPtr(k);
                lane.HasGate = spec.Condition.HasGate;
                if (spec.Condition.HasGate)
                {
                    lane.Gate = state.GetDynamicComponentTypeHandle(spec.Condition.Gate);
                }
            }

            if (write)
            {
                lane.Inbox = sv.Inboxes[k];
                lane.Sent = sv.SentPtr(k);
            }

            return lane;
        }

        public static void Update(ref SvLane lane, ref SystemState state)
        {
            lane.Value.Update(ref state);
            lane.Delta.Update(ref state);
            lane.Ceiling.Update(ref state);
            lane.Floor.Update(ref state);
            lane.Gate.Update(ref state);
        }
    }

    public static class SvClock
    {
        public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();
    }
}
