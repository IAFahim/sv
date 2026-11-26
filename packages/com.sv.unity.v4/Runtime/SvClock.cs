using Sv.Host;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity
{
    public enum SvClock
    {
        RenderFrame,
        FixedStep,
    }

    public struct SvFixedStep : IComponentData
    {
    }

    public unsafe struct SvFrame : IComponentData
    {
        public WorldView World;

        [NativeDisableUnsafePtrRestriction]
        public EffectWriter* Sender;

        [NativeDisableUnsafePtrRestriction]
        public HostEvent* Events;

        public int EventCount;

        public long Frame;

        public ulong LastDigest;

        public bool IsOpen => Sender != null;
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
    public partial class SvFixedStepGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SvFixedStepGroup))]
    public partial class SvSendGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SvFixedStepGroup))]
    [UpdateBefore(typeof(SvSendGroup))]
    public partial struct SvOpenFrameSystem : ISystem
    {
        EntityQuery handles;
        EntityQuery released;

        public void OnCreate(ref SystemState state)
        {
            handles = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle, SvFixedStep>().WithAllRW<SvFrame>().Build(ref state);
            released = SvHandles.ReleasedQuery(ref state);
            state.RequireForUpdate(handles);
        }

        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            var entity = handles.GetSingletonEntity();
            var world = SvEntities.WorldOf(manager.GetComponentData<SvWorldHandle>(entity));
            manager.CompleteDependencyBeforeRW<SvFrame>();
            if (world == null || world.IsDisposed)
            {
                manager.SetComponentData(entity, default(SvFrame));
                return;
            }

            SvHandles.KillReleased(manager, released, world);
            manager.SetComponentData(entity, world.View());
        }
    }

    [UpdateInGroup(typeof(SvFixedStepGroup))]
    [UpdateAfter(typeof(SvSendGroup))]
    public partial struct SvFixedStepSystem : ISystem
    {
        EntityQuery handles;
        EntityQuery all;

        public void OnCreate(ref SystemState state)
        {
            handles = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle, SvFixedStep>().WithAllRW<SvFrame>().Build(ref state);
            all = new EntityQueryBuilder(Allocator.Temp).WithAll<SvWorldHandle>().Build(ref state);
            state.RequireForUpdate(handles);
        }

        public void OnUpdate(ref SystemState state)
        {
            var manager = state.EntityManager;
            var entity = handles.GetSingletonEntity();
            var world = SvEntities.WorldOf(manager.GetComponentData<SvWorldHandle>(entity));
            manager.CompleteDependencyBeforeRW<SvFrame>();
            if (world == null || world.IsDisposed)
            {
                manager.SetComponentData(entity, default(SvFrame));
                return;
            }

            world.Step();
            manager.SetComponentData(entity, world.View());
        }

        public void OnDestroy(ref SystemState state) => SvHandles.FreeAll(state.EntityManager, all);
    }

    [UpdateInGroup(typeof(SvFixedStepGroup))]
    [UpdateAfter(typeof(SvFixedStepSystem))]
    public partial class SvEventGroup : ComponentSystemGroup
    {
    }
}
