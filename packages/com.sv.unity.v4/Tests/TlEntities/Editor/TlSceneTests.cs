using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Sv.Host;
using Sv.Kernels.NorthStar3;
using Sv.Kernels.NorthStar3.Glue;
using Sv.Unity.Timelines;
using Tl.Generated;
using Tl.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using EcsWorld = Unity.Entities.World;
using Object = UnityEngine.Object;

namespace Sv.Unity.TlEntitiesTests
{
    public sealed class TlSceneTests
    {
        const float FixedStep = 1f / 60f;

        readonly List<Object> made = new List<Object>();
        int jobWorkersBefore;
        bool synchronousBefore;

        [SetUp]
        public void SetUp()
        {
            SvWorlds.DisposeAll();
            jobWorkersBefore = JobsUtility.JobWorkerCount;
            synchronousBefore = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in made)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            made.Clear();
            BurstCompiler.Options.EnableBurstCompileSynchronously = synchronousBefore;
            JobsUtility.JobWorkerCount = jobWorkersBefore;
            SvWorlds.DisposeAll();
        }

        public static IEnumerable<TestCaseData> Scenes()
        {
            foreach (var renderFps in new[] { 30, 60, 144 })
            {
                yield return new TestCaseData("poison_status_on_player", TlEntitiesTwinTests.PoisonStartsOnFrameOne, renderFps).SetName("TlScene(poison_status_on_player, " + renderFps + " fps)");
                yield return new TestCaseData("low_health_requests_poison", TlEntitiesTwinTests.NoPoisonStartFromTheTest, renderFps).SetName("TlScene(low_health_requests_poison, " + renderFps + " fps)");
            }
        }

        static EcsWorld SceneWorld()
        {
            var world = new EcsWorld("sv-tl-scene");
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type>
            {
                typeof(FixedStepSimulationSystemGroup),
                typeof(global::Tl.TlSimulationGroup),
                typeof(TimelineSimulationGroup),
                typeof(TlFeedGroup),
                typeof(SvFixedStepGroup),
                typeof(SvSendGroup),
                typeof(SvEventGroup),
                typeof(TlLibrarySystem),
                typeof(TlTimelineRegisterSystem),
                typeof(TimelineRequestSystem),
                typeof(TimelineAdvanceSystem),
                typeof(TimelineKindTickSystem),
                typeof(TlKindSystem<TlAuto_Sv_NorthStar3_Entities_Jobs>),
                typeof(SvOpenFrameSystem),
                typeof(SvOutboxSystem<PoisonOut>),
                typeof(SvFixedStepSystem),
                typeof(SvTimelineRequestSystem),
                typeof(JournalFeedSystem),
                typeof(OutboxCellRecordSystem),
                typeof(DigestRecordSystem),
            });
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
            return world;
        }

        static int StatusSlot(Replay replay, int poisonStartFrame)
        {
            var spawns = new List<int>();
            foreach (var frame in replay.Frames)
            {
                foreach (var spawn in frame.Spawns)
                {
                    spawns.Add(spawn[1]);
                }
            }

            return poisonStartFrame == TlEntitiesTwinTests.NoPoisonStartFromTheTest ? spawns[0] : spawns[TlEntitiesTwinTests.StatusEntityIsTheSecondSpawn];
        }

        static Entity Single<T>(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1), typeof(T).Name + " entities after baking");
            return query.GetSingletonEntity();
        }

        static ushort Find(EntityManager manager, Entity status, string timeline)
        {
            var set = manager.GetBuffer<TlTimelineSet>(status);
            for (var i = 0; i < set.Length; i++)
            {
                var blob = set[i].Blob;
                if (TlTimelineBlobs.NameOf(ref blob.Value) == timeline)
                {
                    return set[i].Id;
                }
            }

            return 0;
        }

        [TestCaseSource(nameof(Scenes))]
        public void A_baked_director_and_baked_sv_world_play_the_northstar3_journal_with_every_frame_digest_of_the_dotnet_host(string journal, int poisonStartFrame, int renderFps)
        {
            JobsUtility.JobWorkerCount = Math.Min(1, JobsUtility.JobWorkerMaximumCount);
            var replay = TlEntitiesTwinTests.Read(journal);
            var capacity = TlEntitiesTwinTests.Int(File.ReadAllLines(TlEntitiesTwinTests.JournalPath(journal))[3].Split(' ')[1]);
            var poison = TlPoisonScene.PoisonTimeline();
            made.Add(poison);
            var roots = TlPoisonScene.Objects(poison, StatusSlot(replay, poisonStartFrame), capacity, 1);
            made.AddRange(roots);
            using var baked = new Baked(roots);
            var world = SceneWorld();
            ISvWorld? sv = null;
            try
            {
                var manager = world.EntityManager;
                manager.MoveEntitiesFrom(baked.World.EntityManager);
                var config = Single<SvWorldConfig>(manager);
                var status = Single<TlTimelineSet>(manager);
                Assert.That(SvEntities.ClockOf(manager, config), Is.EqualTo(SvClock.FixedStep), "the world authoring did not bake the fixed step clock");
                Assert.That(manager.HasComponent<PoisonOut>(status), Is.True, "the director baker did not add the poison outbox cell its kind writes");
                Assert.That(manager.HasComponent<SvSlot>(status), Is.True, "the slot authoring did not bake the sv slot");
                var options = manager.GetComponentData<SvWorldConfig>(config);
                sv = PackWorld.Create(new HostOptions { Workers = options.Workers, Parts = options.Parts, EntityCapacity = options.Capacity });
                var handle = SvEntities.Publish(manager, sv, config);
                world.GetExistingSystemManaged<InitializationSystemGroup>().Update();
                Assert.That(manager.IsComponentEnabled<TlUnregistered>(status), Is.False, "tl did not register the baked timeline");
                replay.Poison = Find(manager, status, TlPoisonScene.Timeline);
                Assert.That(replay.Poison, Is.Not.Zero, "the baked director has no poison timeline");
                TimelineRules.AddTo(SvTimelines.Rules(manager, handle), name => Find(manager, status, name));
                replay.Sv = sv;
                replay.MakePlayers = false;
                replay.PoisonStartFrame = poisonStartFrame;
                replay.StatusSpawn = poisonStartFrame == TlEntitiesTwinTests.NoPoisonStartFromTheTest ? 0 : TlEntitiesTwinTests.StatusEntityIsTheSecondSpawn;
                replay.PlayerOf[manager.GetComponentData<SvSlot>(status).Value] = status;
                world.GetExistingSystemManaged<JournalFeedSystem>().Replay = replay;
                world.GetExistingSystemManaged<OutboxCellRecordSystem>().Replay = replay;
                world.GetExistingSystemManaged<DigestRecordSystem>().Replay = replay;

                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                var renderDelta = 1.0 / renderFps;
                var elapsed = 0.0;
                var renders = 0;
                for (; !replay.Done && renders < replay.Frames.Count * 8 + 8; renders++)
                {
                    world.SetTime(new TimeData(elapsed, (float)renderDelta));
                    simulation.Update();
                    elapsed += renderDelta;
                }

                manager.CompleteAllTrackedJobs();
                Assert.That(replay.Failures, Is.Empty);
                Assert.That(replay.Done, Is.True, $"{renders} render frames at {renderFps} fps stepped only {replay.Next} of {replay.Frames.Count} journal frames");
                Assert.That(sv.Digest(), Is.EqualTo(replay.Final), "final digest differs from the .NET host");
                Assert.That(replay.TimelineCells, Is.GreaterThan(0), "the baked timeline wrote no outbox cell");
                var unmanaged = world.Unmanaged;
                var outbox = unmanaged.GetUnsafeSystemRef<SvOutboxSystem<PoisonOut>>(world.GetExistingSystem<SvOutboxSystem<PoisonOut>>());
                var requests = unmanaged.GetUnsafeSystemRef<SvTimelineRequestSystem>(world.GetExistingSystem<SvTimelineRequestSystem>());
                Assert.That(outbox.Sent, Is.EqualTo(replay.TimelineCells), "the outbox system sent another number of records than tl wrote cells");
                Assert.That(outbox.Probe, Is.EqualTo(SvProbe.Burst), "the poison outbox system ran as managed code instead of Burst");
                if (poisonStartFrame == TlEntitiesTwinTests.NoPoisonStartFromTheTest)
                {
                    Assert.That(requests.Started, Is.GreaterThan(0), "no sv event started the baked timeline");
                }

                Assert.That(requests.Unmatched, Is.Zero, "an sv event named a slot with no baked player");
            }
            finally
            {
                world.EntityManager.CompleteAllTrackedJobs();
                world.Dispose();
                sv?.Dispose();
            }
        }

        sealed class Baked : IDisposable
        {
            public readonly EcsWorld World;
            readonly BlobAssetStore blobs;

            public Baked(GameObject[] roots)
            {
                var hybrid = typeof(Baker<>).Assembly;
                var utility = hybrid.GetType("Unity.Entities.BakingUtility", true);
                var flags = Enum.Parse(utility.GetNestedType("BakingFlags", BindingFlags.Public | BindingFlags.NonPublic), "AssignName");
                World = new EcsWorld("sv-tl-scene-baking");
                blobs = new BlobAssetStore(128);
                var settings = Activator.CreateInstance(hybrid.GetType("Unity.Entities.BakingSettings", true), flags, blobs);
                utility.GetMethod("BakeGameObjects", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { World, roots, settings! });
            }

            public void Dispose()
            {
                World.Dispose();
                blobs.Dispose();
            }
        }
    }
}
