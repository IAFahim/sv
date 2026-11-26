using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Unity
{
    public struct SvSlot : IComponentData
    {
        public int Value;
    }

    public struct SvSlotCleanup : ICleanupComponentData
    {
        public int Value;
    }

    public struct SvWorldHandle : IComponentData
    {
        public IntPtr Value;
    }

    public struct SvWorldConfig : IComponentData
    {
        public int Capacity;
        public int Workers;
        public int Parts;
    }

    public static class SvEntities
    {
        public static Entity Publish(EntityManager manager, ISvWorld world) => Publish(manager, world, SvClock.RenderFrame);

        public static Entity Publish(EntityManager manager, ISvWorld world, SvClock clock)
        {
            var entity = manager.CreateEntity(typeof(SvWorldHandle), typeof(SvFrame));
            manager.SetComponentData(entity, new SvWorldHandle { Value = GCHandle.ToIntPtr(GCHandle.Alloc(world)) });
            if (clock == SvClock.FixedStep)
            {
                manager.AddComponent<SvFixedStep>(entity);
            }

            return entity;
        }

        public static Entity Publish(EntityManager manager, ISvWorld world, Entity config)
        {
            manager.AddComponentData(config, new SvWorldHandle { Value = GCHandle.ToIntPtr(GCHandle.Alloc(world)) });
            manager.AddComponent<SvFrame>(config);
            return config;
        }

        public static SvClock ClockOf(EntityManager manager, Entity config) => manager.HasComponent<SvFixedStep>(config) ? SvClock.FixedStep : SvClock.RenderFrame;

        public static void Unpublish(EntityManager manager, Entity handleEntity)
        {
            var handle = manager.GetComponentData<SvWorldHandle>(handleEntity);
            if (handle.Value != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(handle.Value).Free();
            }

            manager.DestroyEntity(handleEntity);
        }

        public static ISvWorld? WorldOf(in SvWorldHandle handle) =>
            handle.Value == IntPtr.Zero ? null : GCHandle.FromIntPtr(handle.Value).Target as ISvWorld;

        public static int Attach(EntityManager manager, Entity entity, ISvWorld world, int template)
        {
            var slot = world.Spawn(template);
            manager.AddComponentData(entity, new SvSlot { Value = slot });
            manager.AddComponentData(entity, new SvSlotCleanup { Value = slot });
            return slot;
        }
    }

    public static class SvHandles
    {
        public static EntityQuery ReleasedQuery(ref SystemState state) =>
            new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlotCleanup>().WithAbsent<SvSlot>().Build(ref state);

        public static void KillReleased(EntityManager manager, EntityQuery released, ISvWorld world)
        {
            if (released.IsEmpty)
            {
                return;
            }

            var gone = released.ToComponentDataArray<SvSlotCleanup>(Allocator.Temp);
            for (var at = 0; at < gone.Length; at++)
            {
                world.Kill(gone[at].Value);
            }

            manager.RemoveComponent<SvSlotCleanup>(released);
        }

        public static void FreeAll(EntityManager manager, EntityQuery handles)
        {
            var entities = handles.ToEntityArray(Allocator.Temp);
            for (var at = 0; at < entities.Length; at++)
            {
                if (!manager.Exists(entities[at]) || !manager.HasComponent<SvWorldHandle>(entities[at]))
                {
                    continue;
                }

                var handle = manager.GetComponentData<SvWorldHandle>(entities[at]);
                if (handle.Value != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(handle.Value).Free();
                    manager.SetComponentData(entities[at], new SvWorldHandle { Value = IntPtr.Zero });
                }
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct SvStepSystem : ISystem
    {
        EntityQuery handles;
        EntityQuery all;
        EntityQuery released;

        public void OnCreate(ref SystemState state)
        {
            handles = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle>().WithAbsent<SvFixedStep>().Build(ref state);
            all = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle>().Build(ref state);
            released = SvHandles.ReleasedQuery(ref state);
            state.RequireForUpdate(handles);
        }

        public void OnUpdate(ref SystemState state)
        {
            var world = SvEntities.WorldOf(handles.GetSingleton<SvWorldHandle>());
            if (world == null || world.IsDisposed)
            {
                return;
            }

            SvHandles.KillReleased(state.EntityManager, released, world);
            world.Step();
        }

        public void OnDestroy(ref SystemState state) => SvHandles.FreeAll(state.EntityManager, all);
    }
}
