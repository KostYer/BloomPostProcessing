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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("renderingLayers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bloomShader"));
            EditorGUILayout.Space();
            var blur = serializedObject.FindProperty("enableBloom");
            EditorGUILayout.PropertyField(blur);
            using (new EditorGUI.DisabledScope(!blur.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("threshold"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("intensity"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clamp"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("scatter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxIterations"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("downscale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("highQualityFiltering"));
            }

            var fullscreen = serializedObject.FindProperty("showFullscreen");
            EditorGUILayout.Space();
            if (GUILayout.Button(fullscreen.boolValue ? "Hide Bloom Fullscreen" : "Show Bloom Fullscreen"))
                fullscreen.boolValue = !fullscreen.boolValue;

            EditorGUILayout.HelpBox(
                "Visible selected surfaces supply their HDR scene color before post-processing. Threshold (gamma space), soft knee, clamp and filtering match URP Gaussian bloom. " +
                "Intensity scales the final bloom. Disable Enable Bloom to inspect the selected HDR source. " +
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
