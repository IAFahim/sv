using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Columns.Entities.Timelines
{
    [BurstCompile]
    [UpdateInGroup(typeof(ColumnsEventGroup))]
    public partial struct ColumnsTimelineRequestSystem : ISystem
    {
        EntityQuery configs;
        EntityQuery states;
        EntityQuery players;
        EntityQuery newPlayers;
        BufferLookup<global::Tl.Unity.TimelineRequest> requests;

        public int Probe;

        public long Started;

        public long Stopped;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            configs = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsWorldConfig, ColumnsFixedStep, ColumnsTimelineRule>().Build(ref state);
            states = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsState>().Build(ref state);
            players = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsSlot, global::Tl.Unity.TimelineRequest>().WithAllRW<ColumnsTimelineHeld>().Build(ref state);
            newPlayers = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsSlot, global::Tl.Unity.TimelineRequest>().WithAbsent<ColumnsTimelineHeld>().Build(ref state);
            requests = state.GetBufferLookup<global::Tl.Unity.TimelineRequest>(false);
            state.RequireForUpdate(configs);
            state.RequireForUpdate(states);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ColumnsCells.Witness(ref Probe);
            var manager = state.EntityManager;
            if (!newPlayers.IsEmpty)
            {
                manager.AddComponent<ColumnsTimelineHeld>(newPlayers);
            }

            var table = manager.GetBuffer<ColumnsTimelineRule>(configs.GetSingletonEntity(), true);
            if (table.Length == 0 || players.IsEmpty)
            {
                return;
            }

            states.CompleteDependency();
            players.CompleteDependency();
            var view = states.GetSingleton<ColumnsState>().View;
            var entities = players.ToEntityArray(Allocator.Temp);
            var slots = players.ToComponentDataArray<ColumnsSlot>(Allocator.Temp);
            var held = players.ToComponentDataArray<ColumnsTimelineHeld>(Allocator.Temp);
            requests.Update(ref state);
            for (var i = 0; i < entities.Length; i++)
            {
                var slot = slots[i].Value;
                if (!view.IsAlive(slot))
                {
                    continue;
                }

                var before = held[i].Events;
                var now = 0;
                for (var r = 0; r < table.Length; r++)
                {
                    var rule = table[r];
                    var bit = 1 << (rule.Event & 31);
                    var on = view.Event(slot, rule.Event);
                    if (on)
                    {
                        now |= bit;
                    }

                    if (rule.Timeline == 0)
                    {
                        continue;
                    }

                    if (view.Entered(slot, rule.Event))
                    {
                        global::Tl.TlRequests.Start(requests[entities[i]], rule.Timeline);
                        Started++;
                    }
                    else if (!on && (before & bit) != 0)
                    {
                        global::Tl.TlRequests.Stop(requests[entities[i]], rule.Timeline);
                        Stopped++;
                    }
                }

                held[i] = new ColumnsTimelineHeld { Events = now };
            }

            players.CopyFromComponentDataArray(held);
        }
    }
}
