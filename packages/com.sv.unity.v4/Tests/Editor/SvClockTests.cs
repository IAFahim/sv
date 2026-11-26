using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.Reference;
using Unity.Core;
using Unity.Entities;
using EcsWorld = Unity.Entities.World;

namespace Sv.Unity.Tests
{
    public sealed class SvClockTests
    {
        const float FixedStep = 1f / 60f;
        const int Renders = 12;

        [SetUp]
        public void SetUp() => SvWorlds.DisposeAll();

        [TearDown]
        public void TearDown() => SvWorlds.DisposeAll();

        static HostOptions Options() => new HostOptions { Workers = 1, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 };

        static EcsWorld BothClocks()
        {
            var world = new EcsWorld("sv-clock-test");
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type> { typeof(SvStepSystem), typeof(SvOpenFrameSystem), typeof(SvFixedStepSystem) });
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
            return world;
        }

        static void Render(EcsWorld world, int renderFps, int renders)
        {
            var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
            for (var at = 0; at < renders; at++)
            {
                world.SetTime(new TimeData(at / (double)renderFps, 1f / renderFps));
                simulation.Update();
            }
        }

        [Test]
        public void A_render_frame_world_steps_once_per_render_frame_whatever_the_fixed_step()
        {
            using var sv = PackWorld.Create(Options());
            var world = BothClocks();
            try
            {
                SvEntities.Publish(world.EntityManager, sv);
                Render(world, 30, Renders);
                Assert.That(sv.Frame, Is.EqualTo(Renders), "per-frame stepping ran another number of steps than render frames");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void A_fixed_step_world_steps_once_per_fixed_tick_and_never_on_the_render_frame()
        {
            using var sv = PackWorld.Create(Options());
            var world = BothClocks();
            try
            {
                var handle = SvEntities.Publish(world.EntityManager, sv, SvClock.FixedStep);
                Render(world, 30, Renders);
                var expected = 1 + (int)Math.Round((Renders - 1) * (1.0 / 30) / FixedStep);
                Assert.That(sv.Frame, Is.InRange(expected - 1, expected), "fixed stepping at 30 fps did not run two fixed ticks per render frame");
                var published = world.EntityManager.GetComponentData<SvFrame>(handle);
                Assert.That(published.Frame, Is.EqualTo(sv.Frame), "the published frame lags the sv world");
                Assert.That(published.LastDigest, Is.EqualTo(sv.LastDigest), "the published digest lags the sv world");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void The_fixed_step_systems_free_the_handle_when_the_ecs_world_goes()
        {
            using var sv = PackWorld.Create(Options());
            var world = BothClocks();
            var manager = world.EntityManager;
            var handle = SvEntities.Publish(manager, sv, SvClock.FixedStep);
            world.DestroySystem(world.GetExistingSystem<SvFixedStepSystem>());
            Assert.That(manager.GetComponentData<SvWorldHandle>(handle).Value, Is.EqualTo(IntPtr.Zero));
            SvEntities.Unpublish(manager, handle);
            world.Dispose();
        }
    }
}
