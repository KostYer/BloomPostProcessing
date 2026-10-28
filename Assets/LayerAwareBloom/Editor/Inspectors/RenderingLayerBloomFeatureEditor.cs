using UnityEditor;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(RenderingLayerBloomFeature))]
    public sealed class RenderingLayerBloomFeatureEditor : UnityEditor.Editor
    {
        private static readonly string[] CoreGroupFields =
        {
            "name", "enabled", "renderingLayers", "threshold", "intensity", "tint", "clamp",
            "scatter", "maxIterations", "downscale", "highQualityFiltering"
        };
        private static readonly string[] PreviewOptions = CreatePreviewOptions();

        private static void DrawGroup(SerializedProperty group, int index)
        {
            group.isExpanded = EditorGUILayout.Foldout(group.isExpanded, $"Group {index + 1}", true);
            if (!group.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (string field in CoreGroupFields)
                    EditorGUILayout.PropertyField(group.FindPropertyRelative(field));

                EditorGUILayout.Space();
                string foldoutKey = $"LayerAwareBloom.OptionalAdditions.{group.serializedObject.targetObject.GetInstanceID()}.{index}";
                bool expanded = SessionState.GetBool(foldoutKey, false);
                bool nextExpanded = EditorGUILayout.Foldout(expanded, new GUIContent("Optional Additions",
                    "Show or hide additional bloom controls. Collapsing this section does not disable them."), true);
                if (expanded != nextExpanded) SessionState.SetBool(foldoutKey, nextExpanded);
                if (nextExpanded)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.HelpBox(
                            "Defaults preserve the original Unity-style bloom: Soft Knee 0.5, Radius 1, Stop NaN / Infinity off.",
                            MessageType.Info);
                        EditorGUILayout.PropertyField(group.FindPropertyRelative("softKnee"), new GUIContent("Soft Knee", "Threshold transition width. Default: 0.5 (original Unity-style knee)."));
                        EditorGUILayout.PropertyField(group.FindPropertyRelative("radius"), new GUIContent("Radius", "Blur sample spacing. Default: 1 (original Unity-style blur). Range: 0.5 to 1.5."));
                        EditorGUILayout.PropertyField(group.FindPropertyRelative("stopNaN"), new GUIContent("Stop NaN / Infinity", "Default: off. Adds a full-resolution cleanup pass for this group before blur; does not repair scene color or the raw source preview."));
                    }
                }
                if (GUILayout.Button(new GUIContent("Reset Group to Defaults",
                    "Reset every setting in this group, including its name, enabled state, rendering layers, and optional additions. Other groups are unchanged. Supports Undo.")))
                    ResetGroup(group, index);
            }
        }

        internal static void ResetGroup(SerializedProperty group, int index)
        {
            // Match a freshly created feature: Group 1 uses Default; spare slots start disabled/empty.
            // Set through serialization so the entire reset is a normal Inspector undo operation.
            group.boxedValue = new RenderingLayerBloomFeature.BloomGroupSettings
            {
                name = $"Group {index + 1}",
                enabled = index == 0,
                renderingLayers = index == 0 ? 1u : 0u
            };
        }

        private static string[] CreatePreviewOptions()
        {
            var options = new string[RenderingLayerBloomFeature.GroupCount + 1];
            options[0] = "All Groups (source: Group 1)";
            for (int i = 1; i < options.Length; i++) options[i] = $"Group {i}";
            return options;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bloomShader"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("uiBloomShader"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("uiNativeShader"));
            EditorGUILayout.Space();
            var blur = serializedObject.FindProperty("enableBloom");
            EditorGUILayout.PropertyField(blur);
            using (new EditorGUI.DisabledScope(!blur.boolValue))
            {
                var groups = serializedObject.FindProperty("groups");
                for (int i = 0; i < groups.arraySize; i++)
                    DrawGroup(groups.GetArrayElementAtIndex(i), i);
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
                    PreviewOptions) - 1;
            }

            EditorGUILayout.HelpBox(
                "Each enabled group selects opaque surfaces and transparent renderers through Rendering Layers and has its own HDR bloom pyramid. A renderer matching several active groups contributes only to the first, in inspector order. Group previews preserve that ownership. Tint multiplies source RGB after filtering. " +
                "Disabled groups, empty layer masks, and zero-output bloom settings are skipped before rendering. " +
                "Built-in post-processing is optional. Bloom is added to the scene before any enabled color grading or tonemapping. Intensity scales the added bloom. " +
                "Disable Enable Bloom to remove glow; color grading has its own switch. Inspect the selected HDR source with the fullscreen preview. " +
                "The fullscreen preview is bloom-only, after post-processing, without tone mapping; bright values can clip on screen. " +
                "Enable HDR on the camera and URP asset. Assign the Rendering Layer to the VFX's child renderers as well. Transparent materials retain their alpha and emission; opaque custom vertex deformation is not supported yet.",
                MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
