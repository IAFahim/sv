using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tl.Generated;
using Tl.Unity;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sv.Unity.TlTests
{
    public class SvTlHookTests
    {
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made)
            {
                if (o != null)
                {
                    UnityEngine.Object.DestroyImmediate(o);
                }
            }

            made.Clear();
        }

        [Test]
        public void ATlKindJobWritesTheTlStateDeltaAndTheSvFoldConsumesItOneFrameLater()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "Drain";
            made.Add(timeline);
            var track = timeline.CreateTrack<TlTrack>(null, "Drain");
            track.Template = new SvTlGame_DrainClip_TlData();
            var clip = track.CreateClip<TlClip>();
            clip.start = 0.5;
            clip.duration = 0.5;
            ((SvTlGame_DrainClip_TlData)((TlClip)clip.asset).Data).Amount = 2;
            var go = new GameObject("Drained");
            made.Add(go);
            var director = go.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;

            using var blobs = new BlobAssetStore(128);
            using var world = Bake(go, blobs);
            var manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SvTlGame.VigorDelta>());
            using var found = query.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, found.Length, "the Drain kind adds its Apply column (the sv delta) to the baked entity");
            var entity = found[0];
            manager.AddComponentData(entity, new SvSlot());
            manager.AddComponentData(entity, new SvTlGame.Vigor { Value = 80 });
            manager.AddComponentData(entity, new SvTlGame.VigorMax { Value = 100f });

            var schema = SchemaBuilder.Create().Stat("vigor_max", 100f).Intrinsic("vigor").Build();
            using var board = Board.Create(schema, 16);
            var links = new SvLinkRegistry();
            links.Stat<SvTlGame.VigorMax>(schema.Stat("vigor_max"));
            links.Intrinsic<SvTlGame.Vigor, SvTlGame.VigorDelta>(schema.Intrinsic("vigor"), SvBound.Of(0), SvBound.OfStat<SvTlGame.VigorMax>());
            links.Link<SvTlGame.VigorDelta>(0, LinkMode.Delta);
            using var host = SvHost.Install(world, board, links);
            host.Bind(entity);

            var jobs = Type.GetType("Tl.Generated.TlAuto_Sv_Unity_TlGame_Jobs, Sv.Unity.TlGame", true);
            var begin = world.GetExistingSystem<SvBeginSystem>();
            var end = world.GetExistingSystem<SvEndSystem>();
            var tl = new[]
            {
                world.CreateSystem<TlLibrarySystem>(),
                world.CreateSystem<TlTimelineRegisterSystem>(),
                world.CreateSystem<TlPlayOnAwakeSystem>(),
                world.CreateSystem<TimelineKindTickSystem>(),
                world.CreateSystem(typeof(TlKindSystem<>).MakeGenericType(jobs)),
            };

            const int ticks = 120;
            var vigor = new int[ticks];
            var delta = new int[ticks];
            for (var tick = 0; tick < ticks; tick++)
            {
                begin.Update(world.Unmanaged);
                manager.CompleteAllTrackedJobs();
                Assert.AreEqual(0, manager.GetComponentData<SvTlGame.VigorDelta>(entity).Value, "the fold clears the delta it consumed");
                foreach (var system in tl)
                {
                    system.Update(world.Unmanaged);
                }

                end.Update(world.Unmanaged);
                manager.CompleteAllTrackedJobs();
                vigor[tick] = manager.GetComponentData<SvTlGame.Vigor>(entity).Value;
                delta[tick] = manager.GetComponentData<SvTlGame.VigorDelta>(entity).Value;
            }

            var first = Array.FindIndex(delta, d => d != 0);
            Assert.Greater(first, 0, "the kind wrote the delta inside the clip window");
            Assert.AreEqual(-2, delta[first]);
            Assert.AreEqual(80, vigor[first], "the delta of tick t is folded at the start of tick t+1");
            Assert.AreEqual(78, vigor[first + 1]);
            var windowTicks = 0;
            foreach (var d in delta)
            {
                windowTicks += d != 0 ? 1 : 0;
            }

            Assert.AreEqual(80 - 2 * windowTicks, vigor[ticks - 1]);
            UnityEngine.Debug.Log($"svtl first={first} window={windowTicks} final={vigor[ticks - 1]} probeManagedRuns={SvProbe.ManagedRuns}");
        }

        static World Bake(GameObject root, BlobAssetStore blobs)
        {
            var hybrid = typeof(Baker<>).Assembly;
            var utility = hybrid.GetType("Unity.Entities.BakingUtility", true);
            var flags = Enum.Parse(utility.GetNestedType("BakingFlags", BindingFlags.Public | BindingFlags.NonPublic), "AssignName");
            var world = new World("SvTlHook");
            var settings = Activator.CreateInstance(hybrid.GetType("Unity.Entities.BakingSettings", true), flags, blobs);
            utility.GetMethod("BakeGameObjects", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { world, new[] { root }, settings });
            return world;
        }
    }
}
