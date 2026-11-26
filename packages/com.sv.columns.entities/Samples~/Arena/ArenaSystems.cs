using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Columns.Entities.Samples.Arena
{
    public struct ArenaAttack : IComponentData
    {
        public Entity Target;
        public int Channel;
        public int Amount;
    }

    public struct ArenaHealth : IComponentData
    {
        public int Lane;
        public int Value;
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ColumnsStepSystem))]
    public partial struct ArenaAttackSystem : ISystem
    {
        ComponentLookup<ColumnsSlot> slots;

        public void OnCreate(ref SystemState state)
        {
            slots = state.GetComponentLookup<ColumnsSlot>(true);
            state.RequireForUpdate<ColumnsState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            slots.Update(ref state);
            state.Dependency = new AttackJob
            {
                Sender = SystemAPI.GetSingleton<ColumnsState>().Sender,
                Slots = slots,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct AttackJob : IJobEntity
        {
            public ColumnsRecords.ParallelWriter Sender;

            [ReadOnly]
            public ComponentLookup<ColumnsSlot> Slots;

            void Execute(in ArenaAttack attack, in ColumnsSlot slot)
            {
                if (Slots.TryGetComponent(attack.Target, out var target))
                {
                    Sender.SendFrom(slot.Value, target.Value, attack.Channel, attack.Amount);
                }
            }
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct ArenaHealthSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<ColumnsState>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ReadJob { View = SystemAPI.GetSingleton<ColumnsState>().View }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct ReadJob : IJobEntity
        {
            public ColumnView View;

            void Execute(ref ArenaHealth health, in ColumnsSlot slot) => health.Value = View.Get(slot.Value, health.Lane);
        }
    }
}
