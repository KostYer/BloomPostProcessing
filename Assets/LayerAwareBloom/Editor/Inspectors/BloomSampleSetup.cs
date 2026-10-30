using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LayerAwareBloom.Editor
{
    internal static class BloomSampleSetup
    {
        internal const string ShowcasePipelinePath = BloomReleaseTools.SamplesRoot + "/Settings/Rendering/Showcase/ShowcasePipeline.asset";
        private const string MenuPath = "Tools/Layer Aware Bloom/Samples/Use Pipeline for Active Sample";

        [MenuItem(MenuPath)]
        internal static void UseForActiveSample()
        {
            var scene = SceneManager.GetActiveScene();
            if (!IsSampleScene(scene.path)) return;
            UsePipeline(scene.name == "GettingStarted" ? BloomReleaseTools.PipelinePath : ShowcasePipelinePath);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanUseForActiveSample() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            IsSampleScene(SceneManager.GetActiveScene().path);

        private static bool IsSampleScene(string path) => path == BloomReleaseTools.ScenePath ||
            path == BloomReleaseTools.SamplesRoot + "/Scenes/BloomGroups.unity" ||
            path == BloomReleaseTools.SamplesRoot + "/Scenes/BloomSources.unity";

        internal static void UsePipeline(string path)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null) throw new System.IO.FileNotFoundException("Sample pipeline is missing: " + path);
            // Explicit user action: assign both so a Quality override cannot keep the template renderer active.
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log("[Layer Aware Bloom] Sample pipeline configured: " + path);
        }

        internal static void Draw(LayerBloomVolume volume)
        {
            string profile = AssetDatabase.GetAssetPath(volume);
            if (!profile.StartsWith(BloomReleaseTools.SamplesRoot + "/")) return;
            string path = profile == BloomReleaseTools.VolumePath ? BloomReleaseTools.PipelinePath : ShowcasePipelinePath;
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null || GraphicsSettings.currentRenderPipeline == pipeline) return;
            EditorGUILayout.HelpBox("This sample needs its packaged renderer to run Layer Aware Bloom. " +
                "Use the sample pipeline to configure Graphics and the current Quality level.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Use Sample Pipeline")) UsePipeline(path);
        }
    }
}
