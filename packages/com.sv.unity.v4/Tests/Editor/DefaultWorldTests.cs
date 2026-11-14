using System.Collections;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.Reference;
using Sv.Unity;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sv.Unity.Tests
{
    public sealed class DefaultWorldTests
    {
        const float TimeoutSeconds = 20f;
        const int FramesBeforeDestroy = 10;
        const int FramesAfterDestroy = 3;
        const int HitAmount = 7;

        static HostOptions Options() => new HostOptions { Workers = 2, Parts = 64, EntityCapacity = 1 << 12, RawCapacity = 1 << 12 };

        [UnityTest]
        public IEnumerator The_default_play_mode_world_steps_sv_and_frees_destroyed_slots_like_a_hand_stepped_twin()
        {
            yield return new EnterPlayMode();

            var ecs = global::Unity.Entities.World.DefaultGameObjectInjectionWorld;
            Assert.That(ecs, Is.Not.Null, "Play mode has no default world");
            Assert.That(ecs.Unmanaged.GetExistingUnmanagedSystem<SvStepSystem>() != default(SystemHandle), Is.True, "SvStepSystem was not auto-created in the default world");

            var world = PackWorld.Create(Options());
            var manager = ecs.EntityManager;
            var handle = SvEntities.Publish(manager, world);
            var targetEntity = manager.CreateEntity();
            var sourceEntity = manager.CreateEntity();
            var targetSlot = SvEntities.Attach(manager, targetEntity, world, 0);
            var sourceSlot = SvEntities.Attach(manager, sourceEntity, world, 1);
            world.Send(targetSlot, sourceSlot, (int)Kind.Sniper, (int)Channel.Physical, HitAmount, 1, Origin.Direct);

            var startedAt = Time.realtimeSinceStartup;
            while (world.Frame < FramesBeforeDestroy && Time.realtimeSinceStartup - startedAt < TimeoutSeconds)
            {
                yield return null;
            }

            Assert.That(world.Frame, Is.GreaterThanOrEqualTo(FramesBeforeDestroy), "the default world did not step sv within the timeout");
            var frameAtDestroy = world.Frame;
            manager.DestroyEntity(sourceEntity);

            startedAt = Time.realtimeSinceStartup;
            while (world.Frame < frameAtDestroy + FramesAfterDestroy && Time.realtimeSinceStartup - startedAt < TimeoutSeconds)
            {
                yield return null;
            }

            var framesStepped = world.Frame;
            var digestFromDefaultWorld = world.LastDigest;
            var sourceStillLive = world.Kill(sourceSlot);

            using (var handStepped = PackWorld.Create(Options()))
            {
                var handTarget = handStepped.Spawn(0);
                var handSource = handStepped.Spawn(1);
                Assert.That(handTarget, Is.EqualTo(targetSlot));
                Assert.That(handSource, Is.EqualTo(sourceSlot));
                handStepped.Send(handTarget, handSource, (int)Kind.Sniper, (int)Channel.Physical, HitAmount, 1, Origin.Direct);
                while (handStepped.Frame < frameAtDestroy)
                {
                    handStepped.Step();
                }

                handStepped.Kill(handSource);
                while (handStepped.Frame < framesStepped)
                {
                    handStepped.Step();
                }

                Assert.That(sourceStillLive, Is.False, "SvStepSystem did not kill the slot of the destroyed entity");
                Assert.That(digestFromDefaultWorld, Is.EqualTo(handStepped.LastDigest), $"default world digest at frame {framesStepped} differs from the hand-stepped twin");
            }

            SvEntities.Unpublish(manager, handle);
            world.Dispose();
            yield return new ExitPlayMode();
        }
    }
}
