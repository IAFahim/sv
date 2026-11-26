using System;
using UnityEditor;
using UnityEngine;

namespace Sv.Columns.Entities
{
    [CustomEditor(typeof(ColumnsEntityAuthoring))]
    sealed class ColumnsEntityAuthoringEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ColumnsEntityAuthoring.World)));
            var template = serializedObject.FindProperty(nameof(ColumnsEntityAuthoring.Template));
            if (ColumnsPackNames.Read(target as ColumnsEntityAuthoring, out var templates, out _))
            {
                Popup(EditorGUILayout.GetControlRect(), template, templates);
            }
            else
            {
                EditorGUILayout.PropertyField(template);
                EditorGUILayout.HelpBox(ColumnsPackNames.Error, MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ColumnsEntityAuthoring.Lanes)), true);
            serializedObject.ApplyModifiedProperties();
        }

        public static void Popup(Rect rect, SerializedProperty property, string[] names)
        {
            var at = Array.IndexOf(names, property.stringValue);
            var options = names;
            if (at < 0)
            {
                options = new string[names.Length + 1];
                options[0] = property.stringValue.Length == 0 ? "(pick one)" : property.stringValue + " (not in the pack)";
                Array.Copy(names, 0, options, 1, names.Length);
                at = 0;
            }

            var picked = EditorGUI.Popup(rect, property.displayName, at, options);
            if (picked != at)
            {
                property.stringValue = options == names ? names[picked] : names[picked - 1];
            }
        }
    }

    [CustomPropertyDrawer(typeof(ColumnsEntityAuthoring.LaneOverride))]
    sealed class LaneOverrideDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var lane = property.FindPropertyRelative(nameof(ColumnsEntityAuthoring.LaneOverride.Lane));
            var value = property.FindPropertyRelative(nameof(ColumnsEntityAuthoring.LaneOverride.Value));
            var left = new Rect(position.x, position.y, position.width * 0.6f - 4, position.height);
            var right = new Rect(position.x + position.width * 0.6f, position.y, position.width * 0.4f, position.height);
            if (ColumnsPackNames.Read(property.serializedObject.targetObject as ColumnsEntityAuthoring, out _, out var lanes))
            {
                ColumnsEntityAuthoringEditor.Popup(left, lane, lanes);
            }
            else
            {
                EditorGUI.PropertyField(left, lane, GUIContent.none);
            }

            EditorGUI.PropertyField(right, value, GUIContent.none);
        }
    }
}
