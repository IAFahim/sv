using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Columns.Entities
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct ColumnsStepSystem : ISystem
    {
        Entity hostEntity;
        EntityQuery configs;
        EntityQuery spawns;
        EntityQuery despawns;
        EntityQuery orphans;

        public void OnCreate(ref SystemState state)
        {
            configs = state.GetEntityQuery(ComponentType.ReadOnly<ColumnsWorldConfig>());
            spawns = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsTemplate>().WithNone<ColumnsSlotCleanup>().Build(ref state);
            despawns = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsSlotCleanup>().WithNone<ColumnsTemplate>().Build(ref state);
            orphans = new EntityQueryBuilder(Allocator.Temp).WithAll<ColumnsSlot>().WithNone<ColumnsTemplate>().Build(ref state);
            state.GetComponentTypeHandle<ColumnsState>(false);
            state.RequireForUpdate(configs);
        }

        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            if (hostEntity == Entity.Null)
            {
                Create(manager);
            }

            manager.CompleteDependencyBeforeRW<ColumnsState>();
            var host = HostOf(manager, hostEntity);
            host.Complete();
            if (!despawns.IsEmpty)
            {
                Despawn(manager, host.World);
            }

            if (!spawns.IsEmpty)
            {
                Spawn(manager, host.World);
            }

            var world = host.World;
            manager.SetComponentData(hostEntity, new ColumnsState
            {
                View = host.View,
                Sender = host.Sender,
                Frame = world.Frame,
                Dropped = world.Dropped,
                BurstBucket = host.BucketRanBurst,
                BurstResolve = host.ResolveRanBurst,
            });
            state.Dependency = host.Schedule(state.Dependency);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (hostEntity == Entity.Null)
            {
                return;
            }

            var manager = state.EntityManager;
            var entity = hostEntity;
            hostEntity = Entity.Null;
            if (!manager.Exists(entity) || !manager.HasComponent<ColumnsHostHandle>(entity))
            {
                return;
            }

            var pointer = manager.GetComponentData<ColumnsHostHandle>(entity).Value;
            manager.DestroyEntity(entity);
            if (pointer == System.IntPtr.Zero)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(pointer);
            (handle.Target as ColumnsBurstHost)?.Dispose();
            handle.Free();
        }

        public static ColumnsBurstHost HostOf(EntityManager manager, Entity entity) =>
            (ColumnsBurstHost)GCHandle.FromIntPtr(manager.GetComponentData<ColumnsHostHandle>(entity).Value).Target!;

        void Create(EntityManager manager)
        {
            var config = configs.GetSingleton<ColumnsWorldConfig>();
            var pack = PackJson.Read(ColumnsPacks.JsonOf(ref config.Pack.Value)).Compile();
            ColumnsBurstHost host;
            try
            {
                host = new ColumnsBurstHost(pack, config.Capacity, config.Writers < 1 ? 1 : config.Writers, config.ResolveParts, config.Kernel, true);
            }
            catch
            {
                pack.Dispose();
                throw;
            }

            hostEntity = manager.CreateEntity(typeof(ColumnsState), typeof(ColumnsHostHandle));
            manager.SetName(hostEntity, "sv columns");
            manager.SetComponentData(hostEntity, new ColumnsHostHandle { Value = GCHandle.ToIntPtr(GCHandle.Alloc(host)) });
            manager.SetComponentData(hostEntity, new ColumnsState { View = host.View, Sender = host.Sender });
        }

        void Despawn(EntityManager manager, ColumnWorld world)
        {
            var gone = despawns.ToComponentDataArray<ColumnsSlotCleanup>(Allocator.Temp);
            var slots = new NativeArray<int>(gone.Length, Allocator.Temp);
            for (var i = 0; i < gone.Length; i++)
            {
                slots[i] = gone[i].Value;
            }

            slots.Sort();
            for (var i = 0; i < slots.Length; i++)
            {
                world.Despawn(slots[i]);
            }

            manager.RemoveComponent<ColumnsSlot>(orphans);
            manager.RemoveComponent<ColumnsSlotCleanup>(despawns);
        }

        void Spawn(EntityManager manager, ColumnWorld world)
        {
            var entities = spawns.ToEntityArray(Allocator.Temp);
            entities.Sort();
            var slots = new NativeArray<int>(entities.Length, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var slot = world.Spawn(manager.GetComponentData<ColumnsTemplate>(entity).Value);
                slots[i] = slot;
                if (manager.HasBuffer<ColumnsLaneValue>(entity))
                {
                    var values = manager.GetBuffer<ColumnsLaneValue>(entity, true);
                    for (var v = 0; v < values.Length; v++)
                    {
                        world.Set(slot, values[v].Lane, values[v].Value);
                    }
                }
            }

            manager.AddComponent(entities, new ComponentTypeSet(ComponentType.ReadWrite<ColumnsSlot>(), ComponentType.ReadWrite<ColumnsSlotCleanup>()));
            for (var i = 0; i < entities.Length; i++)
            {
                manager.SetComponentData(entities[i], new ColumnsSlot { Value = slots[i] });
                manager.SetComponentData(entities[i], new ColumnsSlotCleanup { Value = slots[i] });
            }
        }
    }
}
