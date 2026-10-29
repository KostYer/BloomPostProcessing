using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LayerAwareBloom.Editor
{
    // Explicit GPU comparison against the installed URP, on identical input and settings.
    [InitializeOnLoad]
    public static class BloomParityVerification
    {
        const string Marker = "Temp/RunBloomParityVerification", Report = "Temp/BloomParityVerification.txt";
        const int Size = 512;
        static BloomParityVerification() => EditorApplication.delayCall += Requested;
        static void Requested()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Requested; return; }
            File.Delete(Marker); Run();
        }
        public static void Run()
        {
            var feature = BloomVerificationContext.FindFeature();
            string original = EditorJsonUtility.ToJson(feature); bool dirty = EditorUtility.IsDirty(feature);
            var oldScene = SceneManager.GetActiveScene(); var oldRT = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var target = new RenderTexture(new RenderTextureDescriptor(Size, Size, GraphicsFormat.R16G16B16A16_SFloat, 24));
            var material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            File.WriteAllText(Report, "Unity Gaussian bloom / Layer Aware Bloom parity\n");
            try
            {
                SceneManager.SetActiveScene(scene);
                var camera = new GameObject("Bloom parity camera").AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(10000, 10000, -5); camera.orthographic = true; camera.orthographicSize = 2;
                camera.aspect = 1; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = true; camera.allowMSAA = false; camera.cullingMask = 1 << 28;
                var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
                data.volumeLayerMask = 1 << 30; data.volumeTrigger = camera.transform;
                var go = new GameObject("Bloom parity Volume"); go.layer = 30;
                var volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100000; volume.sharedProfile = profile;
                var unity = profile.Add<Bloom>(true); unity.threshold.value = .9f; unity.scatter.value = .724f;
                unity.intensity.value = 0; unity.maxIterations.value = 6; unity.downscale.value = BloomDownscaleMode.Half;
                unity.filter.value = BloomFilterMode.Gaussian; unity.tint.value = Color.white; unity.clamp.value = 65472;
                profile.Add<Tonemapping>(true).mode.value = TonemappingMode.None;
                profile.Add<ColorAdjustments>(true);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer = 28;
                quad.transform.position = new Vector3(10000, 10000, 0); quad.transform.localScale = Vector3.one * .7f;
                quad.GetComponent<Renderer>().sharedMaterial = material; quad.GetComponent<Renderer>().renderingLayerMask = 4;
                material.SetColor("_BaseColor", new Color(1, 1, 1, .45f));
                material.SetColor("_EmissionColor", new Color(.08f, 4, .45f, 1));
                material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.EnableKeyword("_EMISSION");
                material.renderQueue = (int)RenderQueue.Transparent;
                feature.SettingsSource = BloomSettingsSource.RendererAsset; feature.ShowFullscreen = false;
                feature.ColorGrading.enabled = false;
                var settings = new SerializedObject(feature); settings.FindProperty("enableBloom").boolValue = true; settings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var g in feature.Groups) g.enabled = false;
                var group = feature.Groups[1]; group.enabled = true; group.renderingLayers = 4;
                group.threshold = .9f; group.scatter = .724f; group.intensity = 9; group.clamp = 65472;
                group.tint = Color.white; group.maxIterations = 6; group.downscale = BloomDownscale.Half;
                group.radius = 1; group.softKnee = .5f; group.stopNaN = false;
                target.Create();
                Color[] Capture()
                {
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    RenderTexture.active = target;
                    var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
                    texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); texture.Apply();
                    var pixels = texture.GetPixels(); Object.DestroyImmediate(texture); return pixels;
                }
                foreach (bool hq in new[] { false, true })
                {
                    group.highQualityFiltering = hq; unity.highQualityFiltering.value = hq;
                    feature.SetActive(false); unity.intensity.value = 9; var reference = Capture();
                    unity.intensity.value = 0; feature.SetActive(true); var actual = Capture();
                    Save($"Temp/BloomParity-Unity-{hq}.png", reference); Save($"Temp/BloomParity-Ours-{hq}.png", actual);
                    float max = reference.Zip(actual, (a,b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b))).Max();
                    float mean = reference.Zip(actual, (a,b) => (Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/3).Average();
                    File.AppendAllText(Report, $"HQ={hq}: max RGB difference={max}, mean={mean}\n");
                    if (max > .05f) throw new Exception($"Bloom parity mismatch, HQ={hq}: {max}");
                }
                File.AppendAllText(Report, "PASS\n");
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e + "\n"); }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original, feature); feature.Create(); if (!dirty) EditorUtility.ClearDirty(feature);
                RenderTexture.active = oldRT; SceneManager.SetActiveScene(oldScene); EditorSceneManager.CloseScene(scene, true);
                foreach (var component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile); Object.DestroyImmediate(material); Object.DestroyImmediate(target);
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
            }
        }
        static void Save(string path, Color[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels.Select(c => new Color(c.gamma.r, c.gamma.g, c.gamma.b, 1)).ToArray()); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        }
    }
}
