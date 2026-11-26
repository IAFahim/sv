using System.IO;
using Sv.Unity.Authoring;
using Tl.Generated;
using Tl.Unity;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sv.Unity.TlEntitiesTests
{
    public static class TlPoisonScene
    {
        public const string Timeline = "poison";
        public const int Ticks = 4;
        public const double TicksPerSecond = 60.0;
        public const int Amount = 3;
        public const string Folder = "Assets/SvTlPoison";
        public const string SceneName = "Sv Tl Poison.unity";
        public const string ObjectsName = "Sv Tl Poison Objects.unity";
        public const string TimelineName = "poison.playable";

        public static TimelineAsset PoisonTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = Timeline;
            AddPoison(timeline);
            return timeline;
        }

        public static void AddPoison(TimelineAsset timeline)
        {
            var track = timeline.CreateTrack<TlTrack>(null, "Poison");
            track.Template = new Sv_Kernels_NorthStar3_Glue_PoisonClip_TlData();
            var clip = track.CreateClip<TlClip>();
            clip.displayName = "Poison";
            clip.start = 0.0;
            clip.duration = Ticks / TicksPerSecond;
            ((Sv_Kernels_NorthStar3_Glue_PoisonClip_TlData)((TlClip)clip.asset).Data).Amount = Amount;
        }

        public static GameObject[] Objects(TimelineAsset poison, int statusSlot, int capacity, int workers)
        {
            var world = new GameObject("sv world");
            var authoring = world.AddComponent<SvWorldAuthoring>();
            authoring.Clock = SvClock.FixedStep;
            authoring.Capacity = capacity;
            authoring.Workers = workers;
            authoring.Parts = 64;
            var status = new GameObject("Poison status");
            var director = status.AddComponent<PlayableDirector>();
            director.playableAsset = poison;
            director.playOnAwake = false;
            status.AddComponent<SvSlotAuthoring>().Slot = statusSlot;
            return new[] { world, status };
        }

        [MenuItem("sv/Samples/Build Tl Poison Scene")]
        public static void BuildAndOpen()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(Build(Folder));
            }
        }

        public static string Build(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder)!.Replace('\\', '/'), Path.GetFileName(folder));
            }

            var timelinePath = folder + "/" + TimelineName;
            var poison = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (poison == null)
            {
                poison = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(poison, timelinePath);
                AddPoison(poison);
                EditorUtility.SetDirty(poison);
                AssetDatabase.SaveAssets();
            }

            var objects = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Objects(poison, 64, 65536, 1);
            EditorSceneManager.SaveScene(objects, folder + "/" + ObjectsName);
            var main = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var host = new GameObject("Sv Tl Poison Objects");
            var subScene = host.AddComponent<SubScene>();
            subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(folder + "/" + ObjectsName);
            subScene.AutoLoadScene = true;
            var scene = folder + "/" + SceneName;
            EditorSceneManager.SaveScene(main, scene);
            AssetDatabase.SaveAssets();
            return scene;
        }
    }
}
