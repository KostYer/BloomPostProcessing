using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    [CustomEditor(typeof(LayerBloomVolume))]
    public sealed class LayerBloomVolumeEditor : VolumeComponentEditor
    {
        private static readonly string[] Fields = { "enabled", "renderingLayers", "threshold", "intensity", "tint",
            "clamp", "scatter", "maxIterations", "downscale", "highQualityFiltering" };

        public override void OnEnable()
        {
            base.OnEnable();
            // Wait until Unity has finished adding the bloom component to its profile.
            EditorApplication.delayCall += CompleteInitialSetup;
        }

        private void CompleteInitialSetup()
        {
            if (this == null || target == null || !enableOverrides || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            BloomTonemappingSetup.CompleteInitialSetup((LayerBloomVolume)target);
        }

        public override void OnInspectorGUI()
        {
            PropertyField(Unpack(serializedObject.FindProperty("enableBloom")));
            for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++)
            {
                var group = serializedObject.FindProperty($"group{i + 1}");
                group.isExpanded = EditorGUILayout.Foldout(group.isExpanded, $"Group {i + 1}", true);
                if (!group.isExpanded) continue;
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (string field in Fields) PropertyField(Unpack(group.FindPropertyRelative(field)));
                    var additions = group.FindPropertyRelative("softKnee");
                    additions.isExpanded = EditorGUILayout.Foldout(additions.isExpanded, "Optional Additions", true);
                    if (additions.isExpanded)
                        foreach (string field in new[] { "softKnee", "radius", "stopNaN" })
                            PropertyField(Unpack(group.FindPropertyRelative(field)));
                    if (GUILayout.Button("Reset Group to Defaults"))
                        group.boxedValue = new LayerBloomGroupParameters();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("HDR Color Grading", EditorStyles.boldLabel);
            foreach (string field in new[] { "enableColorGrading", "exposure", "contrast", "saturation", "colorFilter" })
                PropertyField(Unpack(serializedObject.FindProperty(field)));
            EditorGUILayout.HelpBox("Enable the override checkboxes for the settings this profile should control. " +
                "A bloom group needs Rendering Layers and positive Intensity. Numeric settings blend with Volume weight; " +
                "layer masks, switches and quality modes change discretely. Unity Tonemapping requires camera Post Processing.", MessageType.Info);
        }
    }

}
