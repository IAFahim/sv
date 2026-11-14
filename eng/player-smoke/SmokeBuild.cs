using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeBuild
{
    public static void Build()
    {
        var targetName = System.Environment.GetEnvironmentVariable("SMOKE_TARGET") ?? "linux64";
        var backendName = System.Environment.GetEnvironmentVariable("SMOKE_BACKEND") ?? "coreclr";
        var target = targetName == "win64" ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;
        var backend = backendName == "il2cpp" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.CoreCLR;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
        var backendRead = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" },
            locationPathName = System.Environment.GetEnvironmentVariable("SMOKE_OUT") ?? (target == BuildTarget.StandaloneWindows64 ? "Build/smoke.exe" : "Build/smoke.x86_64"),
            target = target,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("SVSMOKE build " + report.summary.result + " target " + target + " backend " + backendRead + " errors " + report.summary.totalErrors + " size " + report.summary.totalSize);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded && backendRead == backend ? 0 : 1);
    }
}
