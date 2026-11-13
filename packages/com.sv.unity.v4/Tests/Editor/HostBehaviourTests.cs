using System;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.Reference;
using Sv.Unity;
using Sv.Unity.Editor;
using Unity.Entities;

namespace Sv.Unity.Tests
{
    public sealed class HostBehaviourTests
    {
        const int WarmFrames = 200;
        const int MeasuredFrames = 32;

        [SetUp]
        public void SetUp() => SvWorlds.DisposeAll();

        [TearDown]
        public void TearDown() => SvWorlds.DisposeAll();

        static HostOptions Options() => new HostOptions { Workers = 1, Parts = 64, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 };

        [Test]
        public void The_main_thread_allocates_nothing_over_warm_frames()
        {
            using var world = PackWorld.Create(Options());
            var target = world.Spawn(0);
            var source = world.Spawn(1);
            void Frame()
            {
                world.Send(target, source, (int)Kind.Sniper, (int)Channel.Physical, 1, 1, Origin.Direct);
                world.Step();
            }

            for (var frame = 0; frame < WarmFrames; frame++)
            {
                Frame();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < MeasuredFrames; frame++)
            {
                Frame();
            }

            var delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(delta, Is.Zero, $"{delta} bytes allocated on the main thread over {MeasuredFrames} warm frames");
        }

        [Test]
        public void The_step_system_steps_the_world_and_kills_released_slots()
        {
            using var world = PackWorld.Create(Options());
            using var ecs = new global::Unity.Entities.World("sv-step-system-test");
            var group = ecs.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(ecs.CreateSystem<SvStepSystem>());
            group.SortSystems();
            var manager = ecs.EntityManager;
            var handle = SvEntities.Publish(manager, world);
            var entity = manager.CreateEntity();
            var slot = SvEntities.Attach(manager, entity, world, 0);

            group.Update();
            Assert.That(world.Frame, Is.EqualTo(1));
            group.Update();
            Assert.That(world.Frame, Is.EqualTo(2));

            manager.DestroyEntity(entity);
            group.Update();
            Assert.That(world.Frame, Is.EqualTo(3));
            Assert.That(world.Kill(slot), Is.False, "the step system should already have killed the released slot");

            SvEntities.Unpublish(manager, handle);
        }

        [Test]
        public void The_reload_hook_disposes_every_world()
        {
            var first = PackWorld.Create(Options());
            var second = PackWorld.Create(Options());
            Assert.That(SvWorlds.Count, Is.EqualTo(2));
            Assert.That(SvReload.BeforeAssemblyReload(), Is.EqualTo(2));
            Assert.That(SvWorlds.Count, Is.Zero);
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.True);
        }
    }
}
