using UnityEditor;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(RenderingLayerSilhouetteFeature))]
    public sealed class RenderingLayerSilhouetteFeatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("renderingLayers"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("silhouetteShader"));

            var fullscreen = serializedObject.FindProperty("showFullscreen");
            EditorGUILayout.Space();
            if (GUILayout.Button(fullscreen.boolValue ? "Hide BW Fullscreen" : "Show BW Fullscreen"))
                fullscreen.boolValue = !fullscreen.boolValue;

            EditorGUILayout.HelpBox(
                "White: visible opaque surfaces in the selected Rendering Layers. Black: everything else. " +
                "Preview applies to Game and Scene views. Standard opaque meshes only; custom vertex deformation is not supported yet.",
                MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
