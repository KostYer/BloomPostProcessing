using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom.Editor
{
    /// <summary>Editor-only companion setup; rendering uses URP's standard Tonemapping.</summary>
    internal static class BloomTonemappingSetup
    {
        internal static Tonemapping EnsureTonemapping(VolumeProfile profile)
        {
            if (profile.TryGet<Tonemapping>(out var existing)) return existing;
            Undo.RecordObject(profile, "Add Unity Tonemapping");
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);
            Undo.RegisterCreatedObjectUndo(tone, "Add Unity Tonemapping");
            if (AssetDatabase.Contains(profile)) AssetDatabase.AddObjectToAsset(tone, profile);
            profile.Reset();
            EditorUtility.SetDirty(tone);
            EditorUtility.SetDirty(profile);
            return tone;
        }

        internal static void CompleteInitialSetup(LayerBloomVolume bloom)
        {
            var state = new SerializedObject(bloom);
            var completed = state.FindProperty("tonemappingSetupComplete");
            if (completed.boolValue) return;
            var profile = Resources.FindObjectsOfTypeAll<VolumeProfile>()
                .FirstOrDefault(p => p.components.Contains(bloom));
            if (profile == null) return;
            EnsureTonemapping(profile);
            completed.boolValue = true;
            state.ApplyModifiedProperties();
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (volume.sharedProfile == profile || (volume.HasInstantiatedProfile() && volume.profile == profile))
                    EnablePostProcessing(volume);
        }

        internal static void EnablePostProcessing(Volume volume)
        {
            if (!volume.gameObject.scene.IsValid() || EditorUtility.IsPersistent(volume)) return;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null && pipeline.colorGradingMode != ColorGradingMode.HighDynamicRange)
            {
                Undo.RecordObject(pipeline, "Enable HDR Color Grading");
                pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
                EditorUtility.SetDirty(pipeline);
            }
            foreach (var root in volume.gameObject.scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    var data = camera.GetComponent<UniversalAdditionalCameraData>();
                    if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                    if ((data.volumeLayerMask.value & (1 << volume.gameObject.layer)) == 0 || data.renderPostProcessing)
                        continue;
                    Undo.RecordObject(data, "Enable Camera Post Processing");
                    data.renderPostProcessing = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                    EditorUtility.SetDirty(data);
                    EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                }
        }
    }
}
