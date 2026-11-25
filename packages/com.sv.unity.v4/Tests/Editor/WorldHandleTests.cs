using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.Reference;
using Sv.Unity;
using Unity.Entities;

namespace Sv.Unity.Tests
{
    public sealed class WorldHandleTests
    {
        [SetUp]
        public void SetUp() => SvWorlds.DisposeAll();

        [TearDown]
        public void TearDown() => SvWorlds.DisposeAll();

        [Test]
        public void Disposing_the_ecs_world_frees_the_handle_it_held()
        {
            var ecs = new global::Unity.Entities.World("sv-handle-release-test");
            ecs.GetOrCreateSystem<SvStepSystem>();
            var weak = PublishAndDrop(ecs.EntityManager);
            ecs.Dispose();
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.That(weak.IsAlive, Is.False, "the sv world stayed reachable after its ECS world was disposed");
        }

        [Test]
        public void Unpublish_after_the_step_system_is_destroyed_frees_nothing_twice()
        {
            using var world = PackWorld.Create(new HostOptions { Workers = 1, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 });
            using var ecs = new global::Unity.Entities.World("sv-handle-unpublish-test");
            var system = ecs.GetOrCreateSystem<SvStepSystem>();
            var manager = ecs.EntityManager;
            var handle = SvEntities.Publish(manager, world);
            ecs.DestroySystem(system);
            Assert.That(manager.GetComponentData<SvWorldHandle>(handle).Value, Is.EqualTo(IntPtr.Zero));
            SvEntities.Unpublish(manager, handle);
            Assert.That(manager.Exists(handle), Is.False);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference PublishAndDrop(EntityManager manager)
        {
            var world = PackWorld.Create(new HostOptions { Workers = 1, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 });
            SvEntities.Publish(manager, world);
            var weak = new WeakReference(world);
            world.Dispose();
            return weak;
        }
    }
}
