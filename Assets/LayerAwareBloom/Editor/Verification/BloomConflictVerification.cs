using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LayerAwareBloom.Editor
{
    public static class BloomConflictVerification
    {
        const string Report = "Temp/BloomConflictVerification.txt";
        [MenuItem("Tools/Layer Aware Bloom/Verify Double Bloom Warning")]
        public static void Run()
        {
            var feature = BloomVerificationContext.FindFeature();
            using var rendererSettings = BloomVerificationContext.UseRendererSettings(feature);
            string original = EditorJsonUtility.ToJson(feature);
            bool wasDirty = EditorUtility.IsDirty(feature);
            // VolumeManager intentionally culls preview-stage Volumes. Use an isolated additive
            // scene so this check exercises the same evaluated stack as a normal game camera.
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var target = new RenderTexture(new RenderTextureDescriptor(64, 64, GraphicsFormat.R16G16B16A16_SFloat, 24));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Camera camera = null;
            bool observed = false, lastConflict = false;
            int warningCount = 0;
            Action<RenderingLayerBloomFeature, Camera, bool, bool> observer = (f, c, conflict, hdr) =>
            { if (f == feature && c == camera) { observed = true; lastConflict = conflict; } };
            Application.LogCallback logger = (message, stack, type) =>
            { if (type == LogType.Warning && message.Contains("Unity Bloom and Layer Aware Bloom")) warningCount++; };
            RenderingLayerBloomFeature.EditorCameraSetupEvaluated += observer;
            Application.logMessageReceived += logger;
            File.WriteAllText(Report, "Double bloom warning: actual per-camera render/Volume evaluation\n");
            try
            {
                var go = new GameObject("Bloom Conflict Verification Camera"); SceneManager.MoveGameObjectToScene(go, scene);
                camera = go.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = true; camera.allowMSAA = false;
                var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
                data.volumeLayerMask = 1 << 30; data.volumeTrigger = camera.transform;
                var volumeGo = new GameObject("Bloom Conflict Verification Volume"); volumeGo.layer = 30;
                SceneManager.MoveGameObjectToScene(volumeGo, scene);
                var volume = volumeGo.AddComponent<Volume>(); volume.isGlobal = true; volume.weight = 1; volume.priority = 10000;
                volume.sharedProfile = profile;
                var bloom = profile.Add<Bloom>(true); bloom.intensity.value = 1;
                foreach (var group in feature.Groups) group.enabled = false;
                var first = feature.Groups[0]; first.enabled = true; first.renderingLayers = 2;
                first.intensity = 1; first.clamp = 65472; first.tint = Color.white;
                feature.ShowFullscreen = false;
                SetEnabled(feature, true); target.Create();
                void Check(bool expected, string label)
                {
                    observed = false;
                    // StandardRequest runs URP's camera Volume update. SingleCameraRequest
                    // intentionally reuses the existing stack and cannot test Volume blending.
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    if (!observed || lastConflict != expected)
                    {
                        var evaluated = VolumeManager.instance.stack.GetComponent<Bloom>();
                        throw new Exception($"{label}: observed={observed}, conflict={lastConflict}, evaluated intensity={evaluated.intensity.value}, active={evaluated.active}, camera PP={data.renderPostProcessing}, group intensity={feature.Groups[0].intensity}, layers={(uint)feature.Groups[0].renderingLayers}");
                    }
                    File.AppendAllText(Report, "OK " + label + "\n");
                }
                Check(true, "Both effects active");
                Check(true, "Repeated frame retains conflict");
                if (warningCount != 1) throw new Exception("Expected exactly one warning for repeated conflicting frames; got " + warningCount);
                bloom.intensity.value = 0; Check(false, "Zero Unity Bloom intensity");
                bloom.intensity.value = 1; bloom.active = false; Check(false, "Disabled Unity Bloom override");
                bloom.active = true; volume.weight = 0; Check(false, "Zero Volume weight");
                volume.weight = 1; data.volumeLayerMask = 0; Check(false, "Camera excludes Volume layer");
                data.volumeLayerMask = 1 << 30; data.renderPostProcessing = false; Check(false, "Camera post processing disabled");
                data.renderPostProcessing = true; first.intensity = 0; Check(false, "Our group has zero output");
                first.intensity = 1; SetEnabled(feature, false); Check(false, "Our bloom disabled");
                SetEnabled(feature, true); volume.isGlobal = false;
                volumeGo.AddComponent<BoxCollider>().size = Vector3.one;
                volume.UpdateColliders();
                volumeGo.transform.position = new Vector3(100, 0, 0); volume.blendDistance = 0;
                Physics.SyncTransforms();
                Check(false, "Camera outside local Volume");
                volumeGo.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                Check(true, "Camera inside local Volume");
                File.AppendAllText(Report, "PASS\n");
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL " + e); Debug.LogException(e); }
            finally
            {
                RenderingLayerBloomFeature.EditorCameraSetupEvaluated -= observer;
                Application.logMessageReceived -= logger;
                EditorJsonUtility.FromJsonOverwrite(original, feature); feature.Create();
                if (!wasDirty) EditorUtility.ClearDirty(feature);
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(profile);
            }
        }
        static void SetEnabled(RenderingLayerBloomFeature feature, bool enabled)
        {
            var serialized = new SerializedObject(feature); serialized.FindProperty("enableBloom").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
