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
            var blur = serializedObject.FindProperty("blurMask");
            EditorGUILayout.PropertyField(blur);
            using (new EditorGUI.DisabledScope(!blur.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("scatter"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxIterations"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("downscale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("highQualityFiltering"));
            }

            var fullscreen = serializedObject.FindProperty("showFullscreen");
            EditorGUILayout.Space();
            if (GUILayout.Button(fullscreen.boolValue ? "Hide BW Fullscreen" : "Show BW Fullscreen"))
                fullscreen.boolValue = !fullscreen.boolValue;

            EditorGUILayout.HelpBox(
                "Visible selected surfaces form a white mask. Blur uses URP's Gaussian bloom pyramid and shows a soft grayscale mask on black. " +
                "Disable Blur Mask to compare the original mask. Max Iterations limits pyramid levels; HQ uses a 13-sample prefilter and bicubic upsampling. " +
                "Standard opaque meshes only; custom vertex deformation is not supported yet.",
                MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
