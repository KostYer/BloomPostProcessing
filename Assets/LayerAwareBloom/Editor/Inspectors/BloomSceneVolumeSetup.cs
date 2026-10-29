using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LayerAwareBloom.Editor
{
    public static class BloomSceneVolumeSetup
    {
        [MenuItem("Tools/Layer Aware Bloom/Create Scene Volume from Renderer Settings")]
        public static void CreateForActiveRenderer()
        {
            Create(BloomVerificationContext.FindFeature());
        }

        public static Volume Create(RenderingLayerBloomFeature feature, string scenePath = null)
        {
            var scene = SceneManager.GetActiveScene();
            var existing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Volume>(true))
                .FirstOrDefault(v => v.sharedProfile != null && v.sharedProfile.Has<LayerBloomVolume>());
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("This scene already has a Layer Aware Bloom profile. Select its overrides to edit the scene settings.", existing);
                return existing;
            }
            scenePath ??= scene.path;
            string directory = string.IsNullOrEmpty(scenePath) ? "Assets" : System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
            string sceneName = string.IsNullOrEmpty(scenePath) ? "Scene" : System.IO.Path.GetFileNameWithoutExtension(scenePath);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{sceneName}_LayerAwareBloom.asset");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<LayerBloomVolume>(true);
            bloom.CopyFrom(feature);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            var tone = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(feature)).OfType<HdrTonemappingFeature>().FirstOrDefault();
            if (tone != null)
            {
                var mapping = profile.Add<LayerTonemappingVolume>(true);
                mapping.mode.Override(tone.isActive ? tone.Mode : HdrTonemappingMode.None);
                AssetDatabase.AddObjectToAsset(mapping, profile);
                Undo.RecordObject(tone, "Use Scene Volume Settings");
                tone.SettingsSource = BloomSettingsSource.SceneVolumes;
                EditorUtility.SetDirty(tone);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            Undo.RecordObject(feature, "Use Scene Volume Settings");
            feature.SettingsSource = BloomSettingsSource.SceneVolumes;
            EditorUtility.SetDirty(feature);
            var go = new GameObject("Layer Aware Bloom Volume");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create Scene Bloom Volume");
            var volume = Undo.AddComponent<Volume>(go);
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = go;
            SceneView.RepaintAll();
            return volume;
        }
    }
}
