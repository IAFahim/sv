using System.Collections;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sv.Unity.PlayTests
{
    public struct PlayHp : IComponentData { public int Value; }
    public struct PlayHpDelta : IComponentData { public int Value; }

    public class SvPlayModeTests
    {
        [UnityTest]
        public IEnumerator TheFrameSystemsRunInTheDefaultWorldPlayerLoop()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            Assert.IsNotNull(world, "the default world exists in Play mode");
            var schema = SchemaBuilder.Create().Intrinsic("hp").Build();
            var board = Board.Create(schema, 64);
            var links = new SvLinkRegistry();
            links.Intrinsic<PlayHp, PlayHpDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.Of(100));
            var host = SvHost.Install(world, board, links);
            var manager = world.EntityManager;
            var entity = manager.CreateEntity(typeof(SvSlot), typeof(PlayHp), typeof(PlayHpDelta));
            manager.SetComponentData(entity, new PlayHp { Value = 50 });
            host.Bind(entity);
            var tick = host.Tick;
            var until = Time.realtimeSinceStartup + 5f;
            while (host.Tick < tick + 2 && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            manager.CompleteAllTrackedJobs();
            manager.SetComponentData(entity, new PlayHpDelta { Value = 70 });
            tick = host.Tick;
            while (host.Tick < tick + 2 && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            manager.CompleteAllTrackedJobs();
            Assert.GreaterOrEqual(host.Tick, tick + 2, "the sv systems ticked in the player loop");
            Assert.AreEqual(100, manager.GetComponentData<PlayHp>(entity).Value, "sum-then-clamp at the ceiling");
            Assert.AreEqual(0, manager.GetComponentData<PlayHpDelta>(entity).Value);
            Assert.AreEqual(0, SvProbe.ManagedRuns, "no sv job ran managed");
            var begin = world.GetExistingSystem<SvBeginSystem>();
            var end = world.GetExistingSystem<SvEndSystem>();
            world.GetExistingSystemManaged<SimulationSystemGroup>().RemoveSystemFromUpdateList(begin);
            world.GetExistingSystemManaged<SimulationSystemGroup>().RemoveSystemFromUpdateList(end);
            manager.DestroyEntity(entity);
            host.Dispose();
            world.DestroySystem(begin);
            world.DestroySystem(end);
            board.Dispose();
        }
    }
}
