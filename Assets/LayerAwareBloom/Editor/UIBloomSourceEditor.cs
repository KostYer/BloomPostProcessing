using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(UIBloomSource)), CanEditMultipleObjects]
    public sealed class UIBloomSourceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Camera and World Space uGUI: Image, Raw Image and legacy Text with UI/Default. " +
                "Select the same Rendering Layers as a bloom group. The first active matching group wins. " +
                "Emission changes only the bloom source. A child component overrides inherited settings; " +
                "a disabled component or an empty layer mask opts that subtree out.", MessageType.Info);
            foreach (UIBloomSource source in targets)
            {
                var canvas = source.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    EditorGUILayout.HelpBox("Screen Space Overlay is intentionally excluded.", MessageType.Warning);
                var graphics = source.includeChildren ? source.GetComponentsInChildren<Graphic>(true) : source.GetComponents<Graphic>();
                foreach (var graphic in graphics)
                    if (!((graphic is Image || graphic is RawImage || graphic is Text) &&
                        graphic.material != null && graphic.material.shader.name == "UI/Default"))
                    {
                        EditorGUILayout.HelpBox("This selection contains unsupported UI (TextMeshPro or a custom material). " +
                            "UI bloom selection and occlusion are unsupported for those graphics; native default-layer rendering may still contribute.", MessageType.Warning);
                        break;
                    }
            }
        }
    }
}
