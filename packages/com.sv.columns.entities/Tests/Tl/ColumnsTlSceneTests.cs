using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Sv.Columns.Entities.Timelines;
using Sv.Columns.Entities.TlTests.Glue;
using Tl.Generated;
using Tl.Unity;
using Unity.Burst;
using Unity.Core;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Sv.Columns.Entities.TlTests
{
    public sealed class SceneScript
    {
        public const int BurnTick = 2;
        public const int MendTick = 12;
        public const int Ticks = 20;
        public const int ClipTicks = 4;
        public const int MendTicks = 3;
        public const int BurnAmount = 20;
        public const int PoisonLevel = 3;
        public const int MendAmount = 15;

        public readonly List<string> Failures = new List<string>();
        public Entity Unit;
        public int Slot;
        public ushort Burn;
        public ushort Mend;
        public int PoisonTick = -1;
        public ColumnWorld Mirror = null!;
        public CompiledPack Pack = null!;
        public int Tick;
        public int TicksThisRender;

        public bool Done => Tick >= Ticks;

        public bool Burns(int tick) => tick >= BurnTick && tick < BurnTick + ClipTicks;

        public bool Mends(int tick) => tick >= MendTick && tick < MendTick + MendTicks;
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public partial class SceneStartSystem : SystemBase
    {
        public SceneScript Script = null!;

        protected override void OnUpdate()
        {
            var script = Script;
            if (script.Done)
            {
                World.GetExistingSystemManaged<ColumnsFixedStepGroup>().Enabled = false;
                return;
            }

            script.TicksThisRender++;
            if (script.Tick == SceneScript.BurnTick)
            {
                global::Tl.TlRequests.Start(EntityManager.GetBuffer<TimelineRequest>(script.Unit), script.Burn);
            }

            if (script.Tick == SceneScript.MendTick)
            {
                global::Tl.TlRequests.Start(EntityManager.GetBuffer<TimelineRequest>(script.Unit), script.Mend);
            }
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(ColumnsFixedStepGroup))]
    [UpdateAfter(typeof(ColumnsEventGroup))]
    public partial class SceneMirrorSystem : SystemBase
    {
        public SceneScript Script = null!;

        protected override void OnUpdate()
        {
            var script = Script;
            if (script.Done)
            {
                return;
            }

            using (var hosts = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsHostHandle>()))
            {
                EntityManager.CompleteAllTrackedJobs();
                var host = ColumnsStepSystem.HostOf(EntityManager, hosts.GetSingletonEntity());
                host.Complete();
                var mirror = script.Mirror;
                var pack = script.Pack;
                var slot = script.Slot;
                if (script.Burns(script.Tick))
                {
                    mirror.Writer(0).SendFrom(slot, slot, pack.Channel("Fire"), SceneScript.BurnAmount);
                }

                if (script.Mends(script.Tick))
                {
                    mirror.Writer(0).SendFrom(slot, slot, pack.Channel("Healing"), SceneScript.MendAmount);
                }

                if (script.Tick == script.PoisonTick)
                {
                    mirror.Set(slot, pack.CardLane("poison"), SceneScript.PoisonLevel);
                }

                mirror.Step();
                if (script.PoisonTick < 0 && mirror.Entered(slot, pack.Event("LowHealth")))
                {
                    script.PoisonTick = script.Tick + 1;
                }

                if (host.World.Frame != mirror.Frame)
                {
                    script.Failures.Add($"tick {script.Tick}: the host is on frame {host.World.Frame}, the mirror on {mirror.Frame}");
                }

                if (host.World.Digest() != mirror.Digest())
                {
                    script.Failures.Add($"tick {script.Tick}: digest {host.World.Digest():X16} differs from the hand-stepped world {mirror.Digest():X16}");
                }
            }

            script.Tick++;
        }
    }

    public sealed class ColumnsTlSceneTests
    {
        const float FixedStep = 1f / 60f;
        const double TicksPerSecond = 60.0;
        const int Capacity = 256;

        static string Pack() => File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns.entities/Tests/Glue/Pack~/arena-tl.json"));

        readonly List<Object> made = new List<Object>();
        bool synchronousBefore;

        [SetUp]
        public void SetUp()
        {
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
        }

        TimelineAsset Timeline(string name, TlClipData template, int ticks, Action<TlClipData> fill)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = name;
            made.Add(timeline);
            var track = timeline.CreateTrack<TlTrack>(null, name);
            track.Template = template;
            var clip = track.CreateClip<TlClip>();
            clip.start = 0.0;
            clip.duration = ticks / TicksPerSecond;
            fill(((TlClip)clip.asset).Data);
            return timeline;
        }

        GameObject[] Objects(string json)
        {
            var burn = Timeline("burn", new Sv_Columns_Entities_TlTests_Glue_FireClip_TlData(), SceneScript.ClipTicks, data => ((Sv_Columns_Entities_TlTests_Glue_FireClip_TlData)data).Amount = SceneScript.BurnAmount);
            var mend = Timeline("mend", new Sv_Columns_Entities_TlTests_Glue_HealingClip_TlData(), SceneScript.MendTicks, data => ((Sv_Columns_Entities_TlTests_Glue_HealingClip_TlData)data).Amount = SceneScript.MendAmount);
            var poison = Timeline("poison", new Sv_Columns_Entities_TlTests_Glue_PoisonCardClip_TlData(), SceneScript.ClipTicks, data => ((Sv_Columns_Entities_TlTests_Glue_PoisonCardClip_TlData)data).Level = SceneScript.PoisonLevel);
            var packAsset = new TextAsset(json) { name = "arena-tl" };
            made.Add(packAsset);
            var worldObject = new GameObject("columns world");
            made.Add(worldObject);
            var world = worldObject.AddComponent<ColumnsWorldAuthoring>();
            world.Pack = packAsset;
            world.Capacity = Capacity;
            world.Writers = 1;
            world.ResolveParts = 1;
            world.FixedStep = true;
            var unit = new GameObject("unit");
            made.Add(unit);
            var entity = unit.AddComponent<ColumnsEntityAuthoring>();
            entity.World = world;
            entity.Template = "dummy";
            var director = unit.AddComponent<PlayableDirector>();
            director.playableAsset = burn;
            director.playOnAwake = false;
            var also = unit.AddComponent<TlAlsoPlays>();
            also.Timelines.Add(poison);
            also.Timelines.Add(mend);
            return new[] { worldObject, unit };
        }

        static World SceneWorld()
        {
            var world = new World("sv columns tl scene");
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, new List<Type>
            {
                typeof(FixedStepSimulationSystemGroup),
                typeof(global::Tl.TlSimulationGroup),
                typeof(TimelineSimulationGroup),
                typeof(TlFeedGroup),
                typeof(ColumnsFixedStepGroup),
                typeof(ColumnsSendGroup),
                typeof(ColumnsEventGroup),
                typeof(TlLibrarySystem),
                typeof(TlTimelineRegisterSystem),
                typeof(TimelineRequestSystem),
                typeof(TimelineAdvanceSystem),
                typeof(TimelineKindTickSystem),
                typeof(TlKindSystem<TlAuto_Sv_Columns_Entities_TlTestGlue_Jobs>),
                typeof(ColumnsFixedStepSystem),
                typeof(ColumnsCardSystem<PoisonCard>),
                typeof(ColumnsOutboxSystem<FireOut>),
                typeof(ColumnsOutboxSystem<HealingOut>),
                typeof(ColumnsTimelineRequestSystem),
                typeof(SceneStartSystem),
                typeof(SceneMirrorSystem),
            });
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = FixedStep;
            return world;
        }

        static Entity Single<T>(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            Assert.AreEqual(1, query.CalculateEntityCount(), typeof(T).Name + " entities after baking");
            return query.GetSingletonEntity();
        }

        static ushort Find(EntityManager manager, Entity unit, string timeline)
        {
            var set = manager.GetBuffer<TlTimelineSet>(unit);
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

        static IEnumerable<TestCaseData> RenderRates()
        {
            foreach (var renderFps in new[] { 30, 60, 144 })
            {
                yield return new TestCaseData(renderFps).SetName("Baked columns world and director at " + renderFps + " fps");
            }
        }

        [TestCaseSource(nameof(RenderRates))]
        public void A_baked_columns_world_and_director_play_the_generated_tracks_with_every_tick_digest_of_a_hand_stepped_world(int renderFps)
        {
            var json = Pack();
            using var pack = PackJson.Read(json).Compile();
            using var mirror = new ColumnWorld(pack, Capacity, 1, 1);
            using var baked = new Baked(Objects(json));
            var world = SceneWorld();
            try
            {
                var manager = world.EntityManager;
                manager.MoveEntitiesFrom(baked.World.EntityManager);
                var config = Single<ColumnsWorldConfig>(manager);
                var unit = Single<TlTimelineSet>(manager);
                Assert.IsTrue(manager.HasComponent<ColumnsFixedStep>(config), "the world authoring did not bake the fixed step clock tag");
                Assert.IsTrue(manager.HasComponent<ColumnsTemplate>(unit), "the entity authoring did not bake the template on the director's entity");
                Assert.IsTrue(manager.HasComponent<PoisonCard>(unit), "the director baker did not add the poison card cell its kind writes");
                Assert.IsTrue(manager.HasComponent<FireOut>(unit), "the director baker did not add the fire outbox cell its kind writes");
                Assert.IsTrue(manager.HasComponent<HealingOut>(unit), "the director baker did not add the healing outbox cell its kind writes");
                world.GetExistingSystemManaged<InitializationSystemGroup>().Update();
                Assert.IsFalse(manager.IsComponentEnabled<TlUnregistered>(unit), "tl did not register the baked timelines");
                var script = new SceneScript { Unit = unit, Mirror = mirror, Pack = pack, Burn = Find(manager, unit, "burn"), Mend = Find(manager, unit, "mend") };
                Assert.AreNotEqual(0, script.Burn, "the baked director has no burn timeline");
                Assert.AreNotEqual(0, script.Mend, "the baked director has no mend timeline");
                Assert.AreNotEqual(0, Find(manager, unit, "poison"), "the baked director has no poison timeline");
                TimelineRules.AddTo(ColumnsTimelines.Rules(manager, config), pack, name => Find(manager, unit, name));
                script.Slot = mirror.Spawn(pack.Template("dummy"));
                world.GetExistingSystemManaged<SceneStartSystem>().Script = script;
                world.GetExistingSystemManaged<SceneMirrorSystem>().Script = script;
                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                var renderDelta = 1.0 / renderFps;
                var elapsed = 0.0;
                var renders = 0;
                for (; !script.Done && renders < SceneScript.Ticks * 8; renders++)
                {
                    script.TicksThisRender = 0;
                    world.SetTime(new TimeData(elapsed, (float)renderDelta));
                    simulation.Update();
                    elapsed += renderDelta;
                }

                manager.CompleteAllTrackedJobs();
                Assert.That(script.Failures, Is.Empty);
                Assert.IsTrue(script.Done, $"{renders} render frames at {renderFps} fps ran only {script.Tick} of {SceneScript.Ticks} fixed ticks");
                Assert.AreEqual(script.Slot, manager.GetComponentData<ColumnsSlot>(unit).Value, "the step system gave the unit another slot than the hand-stepped world");
                Assert.AreEqual(SceneScript.BurnTick + SceneScript.ClipTicks, script.PoisonTick, "LowHealth entered on another tick than the last burn tick");
                var health = 100 - SceneScript.ClipTicks * SceneScript.BurnAmount - SceneScript.PoisonLevel * 4 + SceneScript.MendTicks * SceneScript.MendAmount;
                Assert.AreEqual(health, mirror.Get(script.Slot, pack.Lane("Health")), "the burn, poison and mend timelines moved health another way than the pack rules say");
                var unmanaged = world.Unmanaged;
                var cards = unmanaged.GetUnsafeSystemRef<ColumnsCardSystem<PoisonCard>>(world.GetExistingSystem<ColumnsCardSystem<PoisonCard>>());
                var fire = unmanaged.GetUnsafeSystemRef<ColumnsOutboxSystem<FireOut>>(world.GetExistingSystem<ColumnsOutboxSystem<FireOut>>());
                var heal = unmanaged.GetUnsafeSystemRef<ColumnsOutboxSystem<HealingOut>>(world.GetExistingSystem<ColumnsOutboxSystem<HealingOut>>());
                var requests = unmanaged.GetUnsafeSystemRef<ColumnsTimelineRequestSystem>(world.GetExistingSystem<ColumnsTimelineRequestSystem>());
                Assert.AreEqual(1L, cards.Set, "the poison track set the card lane another number of times than once");
                Assert.AreEqual((long)SceneScript.ClipTicks, fire.Sent, "the burn track sent another number of fire records than its ticks");
                Assert.AreEqual((long)SceneScript.MendTicks, heal.Sent, "the mend track sent another number of healing records than its ticks");
                Assert.AreEqual(1L, requests.Started, "LowHealth did not start the poison timeline once");
                Assert.AreEqual(1L, requests.Stopped, "LowHealth leaving did not stop the poison timeline once");
                if (BurstCompiler.IsEnabled)
                {
                    Assert.AreEqual(1, cards.Probe, "the card system ran managed");
                    Assert.AreEqual(1, fire.Probe, "the fire outbox system ran managed");
                    Assert.AreEqual(1, heal.Probe, "the heal outbox system ran managed");
                    Assert.AreEqual(1, requests.Probe, "the timeline request system ran managed");
                }
            }
            finally
            {
                world.EntityManager.CompleteAllTrackedJobs();
                world.Dispose();
            }
        }

        sealed class Baked : IDisposable
        {
            public readonly World World;
            readonly BlobAssetStore blobs;

            public Baked(GameObject[] roots)
            {
                var hybrid = typeof(Baker<>).Assembly;
                var utility = hybrid.GetType("Unity.Entities.BakingUtility", true);
                var flags = Enum.Parse(utility.GetNestedType("BakingFlags", BindingFlags.Public | BindingFlags.NonPublic), "AssignName");
                World = new World("sv columns tl scene baking");
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
