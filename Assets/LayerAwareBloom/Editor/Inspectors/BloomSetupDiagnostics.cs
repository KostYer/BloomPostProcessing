using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom.Editor
{
    [InitializeOnLoad]
    internal static class BloomSetupDiagnostics
    {
        internal const string ConflictMessage = "Unity Bloom and Layer Aware Bloom are both active for this camera. " +
            "Their effects accumulate, and Unity Bloom can make objects outside the selected Rendering Layers glow. " +
            "Disable the Bloom override in the camera's Volume configuration, or disable one bloom effect if this is unintended.";
        sealed class State
        {
            public RenderingLayerBloomFeature feature;
            public Camera camera;
            public bool conflict, missingHdr;
            public double time;
        }
        static readonly Dictionary<(int, int), State> States = new();
        static double lastCleanup;

        static BloomSetupDiagnostics()
        {
            RenderingLayerBloomFeature.EditorCameraSetupEvaluated += Observe;
            EditorApplication.update += Cleanup;
        }

        static void Observe(RenderingLayerBloomFeature feature, Camera camera, bool conflict, bool missingHdr)
        {
            if (camera == null) return;
            var key = (feature.GetInstanceID(), camera.GetInstanceID());
            if (!States.TryGetValue(key, out var state))
                States[key] = state = new State { feature = feature, camera = camera };
            if (conflict && !state.conflict)
                Debug.LogWarning($"[Layer Aware Bloom] Type: Double bloom, warning: {ConflictMessage} Camera: {camera.name}.", camera);
            state.conflict = conflict; state.missingHdr = missingHdr;
            state.time = EditorApplication.timeSinceStartup;
        }

        static void Cleanup()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastCleanup < 1) return;
            lastCleanup = now;
            foreach (var pair in States.Where(p => p.Value.feature == null || p.Value.camera == null ||
                !p.Value.feature.isActive || now - p.Value.time > 5).ToArray()) States.Remove(pair.Key);
        }

        internal static void Draw(RenderingLayerBloomFeature feature, SerializedObject serialized)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
                EditorGUILayout.HelpBox("Layer Aware Bloom requires the Universal Render Pipeline.", MessageType.Warning);
            else if (!pipeline.supportsHDR)
                EditorGUILayout.HelpBox("Enable HDR in the active URP asset to preserve bright bloom sources.", MessageType.Warning);
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                EditorGUILayout.HelpBox("Gamma color space is not validated. The supported setup uses Linear color space.", MessageType.Warning);
            if (!feature.isActive || (feature.SettingsSource == BloomSettingsSource.RendererAsset &&
                !serialized.FindProperty("enableBloom").boolValue)) return;
            foreach (string field in new[] { "bloomShader", "uiBloomShader", "uiNativeShader" })
                if (serialized.FindProperty(field).objectReferenceValue == null)
                    EditorGUILayout.HelpBox("A bloom shader reference is missing. Recreate the renderer feature or assign its packaged shaders before building.", MessageType.Warning);
            if (feature.SettingsSource == BloomSettingsSource.RendererAsset)
            {
                var groups = serialized.FindProperty("groups");
                bool anyOutput = false;
                for (int i = 0; i < groups.arraySize; i++)
                {
                    var group = (RenderingLayerBloomFeature.BloomGroupSettings)groups.GetArrayElementAtIndex(i).boxedValue;
                    if (!group.enabled) continue;
                    if ((uint)group.renderingLayers == 0)
                        EditorGUILayout.HelpBox($"Group {i + 1} is enabled but has no Rendering Layers selected.", MessageType.Warning);
                    anyOutput |= (uint)group.renderingLayers != 0 && group.intensity > 0 && group.clamp > 0 &&
                        (group.tint.r > 0 || group.tint.g > 0 || group.tint.b > 0);
                }
                if (!anyOutput) EditorGUILayout.HelpBox("No group currently produces bloom. Enable a group, select its Rendering Layers, and use positive intensity/clamp and a nonblack tint.", MessageType.Info);
            }
            foreach (var state in States.Values.Where(s => s.feature == feature && s.camera != null &&
                EditorApplication.timeSinceStartup - s.time <= 5))
            {
                if (state.conflict) EditorGUILayout.HelpBox($"{state.camera.name}: {ConflictMessage}", MessageType.Warning);
                if (state.missingHdr) EditorGUILayout.HelpBox($"{state.camera.name}: HDR is disabled for this camera. Enable Allow HDR and HDR in the URP asset.", MessageType.Warning);
            }
        }
    }
}
