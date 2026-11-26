using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Columns.Entities
{
    public interface IColumnsCardCell
    {
        string Card { get; }

        int Level { get; set; }
    }

    public interface IColumnsOutboxCell
    {
        string Channel { get; }

        string? TargetLane { get; }

        int Amount { get; set; }
    }

    public static class ColumnsCells
    {
        public const int Self = -1;

        public static ColumnsBurstHost? Host(EntityManager manager, EntityQuery hosts)
        {
            if (hosts.IsEmpty)
            {
                return null;
            }

            return ColumnsStepSystem.HostOf(manager, hosts.GetSingletonEntity());
        }

        public static int Target(in ColumnView view, int slot, int targetLane)
        {
            var aimed = targetLane < 0 ? 0 : view.Get(slot, targetLane);
            return aimed <= 0 ? slot : aimed - 1;
        }

        [BurstDiscard]
        internal static void Managed(ref int burst) => burst = 0;

        public static void Witness(ref int probe)
        {
            var burst = 1;
            Managed(ref burst);
            probe = burst;
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(ColumnsSendGroup))]
    public partial struct ColumnsCardSystem<TCell> : ISystem, ISystemStartStop
        where TCell : unmanaged, IComponentData, IColumnsCardCell
    {
        EntityQuery cells;
        EntityQuery states;
        EntityQuery fixedConfigs;
        ComponentTypeHandle<TCell> cellHandle;
        ComponentTypeHandle<ColumnsSlot> slotHandle;
        int lane;

        public int Probe;

        public long Set;

        public void OnCreate(ref SystemState state)
        {
            cells = new EntityQueryBuilder(Allocator.Temp).WithAllRW<TCell>().WithAll<ColumnsSlot>().Build(ref state);
            states = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsState, ColumnsHostHandle>().Build(ref state);
            fixedConfigs = ColumnsStepper.Configs(ref state, ColumnsClock.FixedStep);
            cellHandle = state.GetComponentTypeHandle<TCell>(false);
            slotHandle = state.GetComponentTypeHandle<ColumnsSlot>(true);
            state.RequireForUpdate(states);
            state.RequireForUpdate(fixedConfigs);
            state.RequireForUpdate(cells);
        }

        public void OnStartRunning(ref SystemState state) =>
            lane = ColumnsCells.Host(state.EntityManager, states)!.World.Pack.CardLane(default(TCell).Card);

        public void OnStopRunning(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ColumnsCells.Witness(ref Probe);
            states.CompleteDependency();
            cells.CompleteDependency();
            var sets = states.GetSingleton<ColumnsState>().Sets;
            cellHandle.Update(ref state);
            slotHandle.Update(ref state);
            var chunks = cells.ToArchetypeChunkArray(Allocator.Temp);
            for (var c = 0; c < chunks.Length; c++)
            {
                var values = chunks[c].GetNativeArray(ref cellHandle);
                var slots = chunks[c].GetNativeArray(ref slotHandle);
                for (var row = 0; row < values.Length; row++)
                {
                    var cell = values[row];
                    if (cell.Level == 0)
                    {
                        continue;
                    }

                    sets.Set(slots[row].Value, lane, cell.Level);
                    Set++;
                    cell.Level = 0;
                    values[row] = cell;
                }
            }
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(ColumnsSendGroup))]
    public partial struct ColumnsOutboxSystem<TCell> : ISystem, ISystemStartStop
        where TCell : unmanaged, IComponentData, IColumnsOutboxCell
    {
        EntityQuery cells;
        EntityQuery states;
        EntityQuery fixedConfigs;
        ComponentTypeHandle<TCell> cellHandle;
        ComponentTypeHandle<ColumnsSlot> slotHandle;
        int channel;
        int targetLane;

        public int Probe;

        public long Sent;

        public void OnCreate(ref SystemState state)
        {
            cells = new EntityQueryBuilder(Allocator.Temp).WithAllRW<TCell>().WithAll<ColumnsSlot>().Build(ref state);
            states = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsState, ColumnsHostHandle>().Build(ref state);
            fixedConfigs = ColumnsStepper.Configs(ref state, ColumnsClock.FixedStep);
            cellHandle = state.GetComponentTypeHandle<TCell>(false);
            slotHandle = state.GetComponentTypeHandle<ColumnsSlot>(true);
            state.RequireForUpdate(states);
            state.RequireForUpdate(fixedConfigs);
            state.RequireForUpdate(cells);
        }

        public void OnStartRunning(ref SystemState state)
        {
            var pack = ColumnsCells.Host(state.EntityManager, states)!.World.Pack;
            var shape = default(TCell);
            channel = pack.Channel(shape.Channel);
            targetLane = string.IsNullOrEmpty(shape.TargetLane) ? ColumnsCells.Self : pack.Lane(shape.TargetLane!);
        }

        public void OnStopRunning(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ColumnsCells.Witness(ref Probe);
            states.CompleteDependency();
            cells.CompleteDependency();
            var columns = states.GetSingleton<ColumnsState>();
            var sender = columns.Sender;
            cellHandle.Update(ref state);
            slotHandle.Update(ref state);
            var chunks = cells.ToArchetypeChunkArray(Allocator.Temp);
            for (var c = 0; c < chunks.Length; c++)
            {
                var values = chunks[c].GetNativeArray(ref cellHandle);
                var slots = chunks[c].GetNativeArray(ref slotHandle);
                for (var row = 0; row < values.Length; row++)
                {
                    var cell = values[row];
                    if (cell.Amount == 0)
                    {
                        continue;
                    }

                    var source = slots[row].Value;
                    sender.SendFrom(source, ColumnsCells.Target(columns.View, source, targetLane), channel, cell.Amount);
                    Sent++;
                    cell.Amount = 0;
                    values[row] = cell;
                }
            }
        }
    }
}
