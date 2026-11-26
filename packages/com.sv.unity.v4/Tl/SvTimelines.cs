using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Unity.Timelines
{
    [InternalBufferCapacity(4)]
    public struct SvTimelineRule : IBufferElementData
    {
        public int Event;
        public ushort Timeline;
    }

    public static class SvTimelines
    {
        public static DynamicBuffer<SvTimelineRule> Rules(EntityManager manager, Entity svWorldHandle) =>
            manager.HasBuffer<SvTimelineRule>(svWorldHandle) ? manager.GetBuffer<SvTimelineRule>(svWorldHandle) : manager.AddBuffer<SvTimelineRule>(svWorldHandle);
    }

    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct SvTimelineClockSystem : ISystem
    {
        EntityQuery renderFrameWorlds;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            renderFrameWorlds = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle>().WithAbsent<SvFixedStep>().Build(ref state);
            state.RequireForUpdate(renderFrameWorlds);
            state.RequireForUpdate<global::Tl.Unity.TimelineLibrary>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => state.EntityManager.AddComponent<SvFixedStep>(renderFrameWorlds);
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SvEventGroup))]
    public unsafe partial struct SvTimelineRequestSystem : ISystem
    {
        EntityQuery worlds;
        EntityQuery players;
        BufferLookup<global::Tl.Unity.TimelineRequest> requests;
        BufferLookup<SvTimelineRule> rules;

        public int Probe;

        public long Started;

        public long Stopped;

        public long Unmatched;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            worlds = new EntityQueryBuilder(Allocator.Temp).WithAll<SvFrame, SvFixedStep, SvTimelineRule>().Build(ref state);
            players = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlot>().WithAllRW<global::Tl.Unity.TimelineRequest>().Build(ref state);
            requests = state.GetBufferLookup<global::Tl.Unity.TimelineRequest>(false);
            rules = state.GetBufferLookup<SvTimelineRule>(true);
            state.RequireForUpdate(worlds);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SvProbe.Witness(ref Probe);
            var world = worlds.GetSingletonEntity();
            var frame = state.EntityManager.GetComponentData<SvFrame>(world);
            if (!frame.IsOpen || frame.EventCount == 0)
            {
                return;
            }

            rules.Update(ref state);
            var table = rules[world];
            if (table.Length == 0)
            {
                return;
            }

            players.CompleteDependency();
            var entities = players.ToEntityArray(Allocator.Temp);
            var slots = players.ToComponentDataArray<SvSlot>(Allocator.Temp);
            var bySlot = new NativeHashMap<int, Entity>(entities.Length, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                bySlot[slots[i].Value] = entities[i];
            }

            requests.Update(ref state);
            for (var at = 0; at < frame.EventCount; at++)
            {
                var raised = frame.Events[at];
                for (var r = 0; r < table.Length; r++)
                {
                    var rule = table[r];
                    if (rule.Event != raised.Event || rule.Timeline == 0)
                    {
                        continue;
                    }

                    if (!bySlot.TryGetValue(raised.Slot, out var player))
                    {
                        Unmatched++;
                        continue;
                    }

                    var playerRequests = requests[player];
                    if (raised.Entered)
                    {
                        global::Tl.TlRequests.Start(playerRequests, rule.Timeline);
                        Started++;
                    }
                    else
                    {
                        global::Tl.TlRequests.Stop(playerRequests, rule.Timeline);
                        Stopped++;
                    }
                }
            }
        }
    }
}
