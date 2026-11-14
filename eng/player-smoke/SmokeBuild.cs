using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeBuild
{
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" },
            locationPathName = System.Environment.GetEnvironmentVariable("SMOKE_OUT") ?? "Build/smoke.x86_64",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("SVSMOKE build " + report.summary.result + " errors " + report.summary.totalErrors + " size " + report.summary.totalSize);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
