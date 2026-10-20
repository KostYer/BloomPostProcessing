using UnityEditor;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(RenderingLayerBloomFeature))]
    public sealed class RenderingLayerBloomFeatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bloomShader"));
            EditorGUILayout.Space();
            var blur = serializedObject.FindProperty("enableBloom");
            EditorGUILayout.PropertyField(blur);
            using (new EditorGUI.DisabledScope(!blur.boolValue))
            {
                var groups = serializedObject.FindProperty("groups");
                for (int i = 0; i < groups.arraySize; i++)
                    EditorGUILayout.PropertyField(groups.GetArrayElementAtIndex(i), new GUIContent($"Group {i + 1}"), true);
            }

            EditorGUILayout.Space();
            var grading = serializedObject.FindProperty("colorGrading");
            EditorGUILayout.LabelField("HDR Color Grading", EditorStyles.boldLabel);
            var gradingEnabled = grading.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(gradingEnabled, new GUIContent("Enable Color Grading"));
            using (new EditorGUI.DisabledScope(!gradingEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(grading.FindPropertyRelative("exposure"));
                EditorGUILayout.PropertyField(grading.FindPropertyRelative("contrast"));
                EditorGUILayout.PropertyField(grading.FindPropertyRelative("saturation"));
                EditorGUILayout.PropertyField(grading.FindPropertyRelative("colorFilter"));
            }
            EditorGUILayout.HelpBox(
                "Grades the whole scene after adding bloom, including objects outside the bloom layers. " +
                "Works with Enable Bloom off. No tonemapping is applied. Exposure 0, Contrast 0, Saturation 0, " +
                "and a white filter leave colors unchanged. Fullscreen debug previews bypass grading.",
                MessageType.Info);

            var fullscreen = serializedObject.FindProperty("showFullscreen");
            EditorGUILayout.Space();
            if (GUILayout.Button(fullscreen.boolValue ? "Hide Bloom Fullscreen" : "Show Bloom Fullscreen"))
                fullscreen.boolValue = !fullscreen.boolValue;
            if (fullscreen.boolValue)
            {
                var group = serializedObject.FindProperty("previewGroup");
                group.intValue = EditorGUILayout.Popup("Preview", group.intValue + 1,
                    new[] { "All Groups (source: Group 1)", "Group 1", "Group 2", "Group 3" }) - 1;
            }

            EditorGUILayout.HelpBox(
                "Each enabled group selects visible opaque surfaces through Rendering Layers and has its own HDR bloom pyramid. Objects matching several groups contribute to each; halos add together. Tint multiplies source RGB after filtering. " +
                "Built-in post-processing is optional. Bloom is added to the scene before any enabled color grading or tonemapping. Intensity scales the added bloom. " +
                "Disable Enable Bloom to remove glow; color grading has its own switch. Inspect the selected HDR source with the fullscreen preview. " +
                "The fullscreen preview is bloom-only, after post-processing, without tone mapping; bright values can clip on screen. " +
                "Enable HDR on the camera and URP asset. Standard opaque meshes only; custom vertex deformation is not supported yet.",
                MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
