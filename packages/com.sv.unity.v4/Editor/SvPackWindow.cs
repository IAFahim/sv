using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sv.Unity.Editor
{
    public sealed class SvPackWindow : EditorWindow
    {
        string folder = "";
        string rootNamespace = "Game.Sv";
        string outputFolder = "Assets/Sv/Generated";
        string newName = "";
        int newClass;
        string newDefault = "0";
        int tab;
        Vector2 scroll;
        Vector2 logScroll;
        SvPackAuthoring? authoring;
        readonly List<string> log = new List<string>();

        static readonly string[] ClassNames = { "intrinsic", "stat" };

        [MenuItem("Window/sv/Pack Authoring")]
        static void Open() => GetWindow<SvPackWindow>("sv pack");

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField("Pack folder", folder);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    var picked = EditorUtility.OpenFolderPanel("sv pack folder", folder, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        folder = picked;
                    }
                }

                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    Guard(() => authoring = new SvPackAuthoring(folder));
                }
            }

            if (authoring == null)
            {
                EditorGUILayout.HelpBox("Pick a folder holding values, rules, cards and templates .sv.json files, then Load.", MessageType.Info);
                return;
            }

            tab = GUILayout.Toolbar(tab, SvPackAuthoring.FileNames);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(220));
            authoring.SetText(tab, EditorGUILayout.TextArea(authoring.Text(tab), GUILayout.ExpandHeight(true)));
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                newName = EditorGUILayout.TextField("New value", newName);
                newClass = EditorGUILayout.Popup(newClass, ClassNames, GUILayout.Width(90));
                newDefault = EditorGUILayout.TextField(newDefault, GUILayout.Width(60));
                if (GUILayout.Button("Add value", GUILayout.Width(80)))
                {
                    Guard(() => authoring.AddValue(newName, ClassNames[newClass], newDefault));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    Guard(Validate);
                }

                if (GUILayout.Button("Save"))
                {
                    Guard(() =>
                    {
                        authoring.Save();
                        log.Add("saved " + folder);
                    });
                }

                rootNamespace = EditorGUILayout.TextField(rootNamespace, GUILayout.Width(140));
                outputFolder = EditorGUILayout.TextField(outputFolder, GUILayout.Width(180));
                if (GUILayout.Button("Regenerate"))
                {
                    Guard(Regenerate);
                }
            }

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(110));
            foreach (var line in log)
            {
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        void Validate()
        {
            log.Clear();
            var result = authoring!.Validate();
            log.AddRange(SvPackAuthoring.Lines(result));
            log.Add(result.Value == null ? "pack does not compile" : "pack compiles");
        }

        void Regenerate()
        {
            log.Clear();
            var files = authoring!.Generate(rootNamespace, out var result);
            log.AddRange(SvPackAuthoring.Lines(result));
            if (files == null)
            {
                log.Add("nothing generated, the pack does not compile");
                return;
            }

            SvPackAuthoring.WriteGenerated(outputFolder, files);
            AssetDatabase.Refresh();
            log.Add("wrote " + files.Count + " files to " + Path.GetFullPath(outputFolder));
        }

        void Guard(System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception error) when (error is IOException || error is System.ArgumentException || error is System.InvalidOperationException)
            {
                log.Add(error.Message);
            }
        }
    }
}
