using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sv.Columns.Entities.Timelines;
using Sv.Columns.Entities.TlTests.Glue;
using Tl.Unity;
using Unity.Burst;
using Unity.Core;
using Unity.Entities;

namespace Sv.Columns.Entities.TlTests
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public partial class LowHealthScriptSystem : SystemBase
    {
        public const int HurtTick = 2;
        public const int HealTick = 6;
        public const int Ticks = 10;

        public Entity Unit;
        public int Tick;
        public readonly List<string> Seen = new List<string>();

        public bool Done => Tick >= Ticks;

        protected override void OnUpdate()
        {
            if (Done)
            {
                World.GetExistingSystemManaged<ColumnsFixedStepGroup>().Enabled = false;
                return;
            }

            if (Tick == HurtTick)
            {
                EntityManager.SetComponentData(Unit, new FireOut { Value = 75 });
            }

            if (Tick == HealTick)
            {
                EntityManager.SetComponentData(Unit, new HealingOut { Value = 20 });
            }

            if (EntityManager.HasBuffer<TimelineRequest>(Unit))
            {
                var requests = EntityManager.GetBuffer<TimelineRequest>(Unit);
                for (var i = 0; i < requests.Length; i++)
                {
                    Seen.Add(Tick + " " + requests[i].Op + " " + requests[i].Timeline);
                }

                requests.Clear();
            }

            Tick++;
        }
    }

    public sealed class ColumnsTimelineRequestTests
    {
        const float FixedStep = 1f / 60f;
        const ushort Poison = 7;

        static string Pack() => File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns.entities/Tests/Glue/Pack~/arena-tl.json"));

        bool synchronousBefore;

        [SetUp]
        public void SetUp()
        {
            synchronousBefore = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        }

        [TearDown]
        public void TearDown() => BurstCompiler.Options.EnableBurstCompileSynchronously = synchronousBefore;

        [Test]
        public void An_sv_event_that_enters_starts_its_timeline_and_one_that_leaves_stops_it()
        {
            var json = Pack();
            var blob = ColumnsPacks.Create(json);
            using var pack = PackJson.Read(json).Compile();
            var world = new World("sv columns timeline requests");
            try
            {
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type>
                {
                    typeof(FixedStepSimulationSystemGroup),
                    typeof(ColumnsFixedStepGroup),
                    typeof(ColumnsSendGroup),
                    typeof(ColumnsEventGroup),
                    typeof(ColumnsFixedStepSystem),
                    typeof(ColumnsOutboxSystem<FireOut>),
                    typeof(ColumnsOutboxSystem<HealingOut>),
                    typeof(ColumnsTimelineRequestSystem),
                    typeof(LowHealthScriptSystem),
                });
                world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
                var manager = world.EntityManager;
                var config = manager.CreateEntity(typeof(ColumnsWorldConfig), typeof(ColumnsFixedStep));
                manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 256, Writers = 1, ResolveParts = 1, Kernel = FrameKernel.Rows });
                TimelineRules.AddTo(ColumnsTimelines.Rules(manager, config), pack, name => name == "poison" ? Poison : (ushort)0);
                var unit = manager.CreateEntity(typeof(ColumnsTemplate), typeof(FireOut), typeof(HealingOut));
                manager.SetComponentData(unit, new ColumnsTemplate { Value = pack.Template("dummy") });
                manager.AddBuffer<TimelineRequest>(unit);
                var script = world.GetExistingSystemManaged<LowHealthScriptSystem>();
                script.Unit = unit;
                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                for (var render = 0; render < LowHealthScriptSystem.Ticks * 4 && !script.Done; render++)
                {
                    world.SetTime(new TimeData(render / 30.0, 1f / 30f));
                    simulation.Update();
                }

                manager.CompleteAllTrackedJobs();
                Assert.IsTrue(script.Done, "the script did not reach its last tick");
                var startTick = LowHealthScriptSystem.HurtTick + 1;
                var stopTick = LowHealthScriptSystem.HealTick + 1;
                CollectionAssert.AreEqual(new[] { startTick + " Start " + Poison, stopTick + " Stop " + Poison }, script.Seen, "LowHealth enters on the hurt tick's frame and leaves on the heal tick's frame");
                var requests = world.Unmanaged.GetUnsafeSystemRef<ColumnsTimelineRequestSystem>(world.GetExistingSystem<ColumnsTimelineRequestSystem>());
                Assert.AreEqual(1L, requests.Started);
                Assert.AreEqual(1L, requests.Stopped);
                Assert.IsTrue(manager.HasComponent<ColumnsTimelineHeld>(unit), "the request system did not track the unit's events");
                if (BurstCompiler.IsEnabled)
                {
                    Assert.AreEqual(1, requests.Probe, "the timeline request system ran managed");
                }
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }
    }
}
