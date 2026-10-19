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
                "Bloom is added to the scene before post-processing, so enabled color grading and tonemapping affect the combined image. Intensity scales the added bloom. " +
                "Disable Enable Bloom to restore the scene, or inspect the selected HDR source with the fullscreen preview. " +
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
