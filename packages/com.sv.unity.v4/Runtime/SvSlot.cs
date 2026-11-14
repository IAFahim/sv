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

    public static class SvEntities
    {
        public static Entity Publish(EntityManager manager, ISvWorld world)
        {
            var entity = manager.CreateEntity(typeof(SvWorldHandle));
            manager.SetComponentData(entity, new SvWorldHandle { Value = GCHandle.ToIntPtr(GCHandle.Alloc(world)) });
            return entity;
        }

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

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct SvStepSystem : ISystem
    {
        EntityQuery handles;
        EntityQuery released;

        public void OnCreate(ref SystemState state)
        {
            handles = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle>().Build(ref state);
            released = new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlotCleanup>().WithAbsent<SvSlot>().Build(ref state);
            state.RequireForUpdate(handles);
        }

        public void OnUpdate(ref SystemState state)
        {
            var world = SvEntities.WorldOf(handles.GetSingleton<SvWorldHandle>());
            if (world == null || world.IsDisposed)
            {
                return;
            }

            if (!released.IsEmpty)
            {
                var gone = released.ToComponentDataArray<SvSlotCleanup>(Allocator.Temp);
                for (var at = 0; at < gone.Length; at++)
                {
                    world.Kill(gone[at].Value);
                }

                state.EntityManager.RemoveComponent<SvSlotCleanup>(released);
            }

            world.Step();
        }
    }
}
