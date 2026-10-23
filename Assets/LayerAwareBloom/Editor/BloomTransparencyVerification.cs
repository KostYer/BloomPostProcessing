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

namespace LayerAwareBloom.Editor
{
    // Runs only on explicit menu/marker request; leaves the open scene and renderer settings intact.
    [InitializeOnLoad]
    public static class BloomTransparencyVerification
    {
        const string Marker = "Temp/RunBloomTransparencyVerification";
        const string Report = "Temp/BloomTransparencyVerification.txt";
        const int Size = 256;
        static BloomTransparencyVerification() => EditorApplication.delayCall += RunRequested;

        static void RunRequested()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RunRequested; return; }
            File.Delete(Marker);
            Run();
        }

        [MenuItem("Tools/Layer Aware Bloom/Verify Transparent Sources")]
        public static void Run()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var feature = renderer.rendererFeatures.OfType<RenderingLayerBloomFeature>().First();
            string original = EditorJsonUtility.ToJson(feature);
            bool wasDirty = EditorUtility.IsDirty(feature);
            var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(new RenderTextureDescriptor(Size, Size, GraphicsFormat.R16G16B16A16_SFloat, 24));
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var opaqueMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            File.WriteAllText(Report, "Transparent bloom render regression\n");
            try
            {
                var cameraObject = new GameObject("Bloom Verification Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.aspect = 1;
                camera.transform.position = new Vector3(0, 0, -5);
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                rt.Create();

                for (int i = 0; i < feature.Groups.Count; i++) feature.Groups[i].enabled = i == 0;
                var group = feature.Groups[0];
                group.renderingLayers = 2;
                group.threshold = 1;
                group.intensity = 1;
                group.tint = Color.white;
                group.clamp = 65472;
                feature.ShowFullscreen = true;
                feature.PreviewGroup = 0;
                SetBloom(feature, false);

                var texels = new Color[32 * 32];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    texels[y * 32 + x] = new Color(1, 1, 1,
                        Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(16, 16)) < 10 ? 1 : 0);
                texture.SetPixels(texels); texture.Apply();
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", new Color(8, 2, 1, 1));
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.SetFloat("_Cull", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;

                var particlesObject = new GameObject("Selected Particle");
                SceneManager.MoveGameObjectToScene(particlesObject, scene);
                var particles = particlesObject.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main; main.startLifetime = 100; main.startSpeed = 0; main.startSize = 2;
                var emission = particles.emission; emission.enabled = false;
                var shape = particles.shape; shape.enabled = false;
                particles.Emit(1);
                particles.Simulate(.01f, false, false, false);
                var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                particleRenderer.sharedMaterial = material;
                particleRenderer.renderingLayerMask = 2;
                var selected = Capture(camera, rt);
                Require(selected.Max(c => c.r) > 2, "Selected transparent particle retains HDR emission");
                Require(selected[Size / 2 * Size + Size / 2 + 55].maxColorComponent < .001f,
                    "Transparent billboard corners remain black");
                Save("Temp/BloomParticleSource.png", selected);
                VerifyGroupOwnership(feature, particleRenderer, camera, rt, "transparent particle");

                material.SetColor("_BaseColor", new Color(8, 2, 1, .5f));
                float alphaRatio = Capture(camera, rt).Max(c => c.r) / selected.Max(c => c.r);
                Require(Mathf.Abs(alphaRatio - .5f) < .02f, "Particle opacity scales HDR source");
                material.SetColor("_BaseColor", new Color(8, 2, 1, 1));
                material.SetFloat("_DstBlend", (float)BlendMode.One);
                particles.Emit(1);
                particles.Simulate(.01f, false, false, false);
                float additiveRatio = Capture(camera, rt).Max(c => c.r) / selected.Max(c => c.r);
                Require(Mathf.Abs(additiveRatio - 2) < .05f, "Overlapping additive particles accumulate");
                particles.Clear(); particles.Emit(1); particles.Simulate(.01f, false, false, false);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

                particleRenderer.renderingLayerMask = 1;
                Require(Capture(camera, rt).Max(c => c.r) < .001f, "Unselected particle is excluded");
                particleRenderer.renderingLayerMask = 2;

                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(blocker, scene);
                blocker.transform.position = new Vector3(0, 0, -1);
                blocker.transform.localScale = new Vector3(3, 3, .2f);
                blocker.GetComponent<Renderer>().sharedMaterial = opaqueMaterial;
                blocker.GetComponent<Renderer>().renderingLayerMask = 1;
                Require(Capture(camera, rt).Max(c => c.r) < .001f, "Opaque foreground occludes selected particles");
                blocker.SetActive(false);

                SetBloom(feature, true);
                var bloom = Capture(camera, rt);
                Require(bloom.Max(c => c.r) > .05f, "Selected particle produces bloom");
                Require(bloom[Size / 2 * Size + Size / 2 + 45].r > .001f, "Glow extends outside visible particle");
                Save("Temp/BloomParticleGlow.png", bloom);
                SetBloom(feature, false);
                particlesObject.SetActive(false);

                // Existing opaque selection must still work after moving capture later in the frame.
                blocker.SetActive(true);
                blocker.GetComponent<Renderer>().renderingLayerMask = 2;
                opaqueMaterial.SetColor("_BaseColor", new Color(4, 1, 1, 1));
                var opaqueSource = Capture(camera, rt);
                Require(opaqueSource.Max(c => c.r) > 2, "Opaque HDR source still works");
                VerifyGroupOwnership(feature, blocker.GetComponent<Renderer>(), camera, rt, "opaque mesh");
                particlesObject.SetActive(true);
                particlesObject.transform.position = new Vector3(0, 0, -2);
                particleRenderer.renderingLayerMask = 1;
                var withUnselected = Capture(camera, rt);
                Require(withUnselected.Zip(opaqueSource, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    "Unselected transparent foreground cannot contaminate opaque source");
                particlesObject.SetActive(false);
                blocker.SetActive(false);
                VerifyLitTransparency(feature, camera, rt, scene, opaqueMaterial);

                const string prefabPath = "Assets/Vefects/Free Fire VFX URP/Particles/VFX_Fire_Floor_01.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    Require(prefab.GetComponentsInChildren<Transform>(true).All(t =>
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0),
                        "Fire prefab has no missing scripts");
                    var fireMaterials = prefab.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
                    Require(fireMaterials.Length == 4 && fireMaterials.All(m =>
                        m.shader != null && m.shader.isSupported && !ShaderUtil.ShaderHasError(m.shader)),
                        "All four fire materials and their shaders are valid");
                    Require(prefab.GetComponentsInChildren<AudioSource>(true).All(a => a.clip != null),
                        "Fire audio references are intact");
                    Require(prefab.GetComponentsInChildren<ParticleSystem>(true)
                        .Where(p => p.lights.enabled).All(p => p.lights.light != null),
                        "Particle light templates are intact");
                    Require(prefab.GetComponentsInChildren<Renderer>(true)
                        .All(r => (r.renderingLayerMask & 2) != 0),
                        "Fire prefab children are configured for bloom");
                    var fire = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    foreach (var r in fire.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = 2;
                    foreach (var p in fire.GetComponentsInChildren<ParticleSystem>(true))
                    { p.useAutoRandomSeed = false; p.randomSeed = 123; p.Simulate(3, false, true, true); }
                    var fireSource = Capture(camera, rt);
                    Require(fireSource.Max(c => c.r) > 1, "Imported fire prefab contributes HDR source");
                    Save("Temp/BloomFireSource.png", fireSource);
                    SetBloom(feature, true);
                    var fireBloom = Capture(camera, rt);
                    Require(fireBloom.Max(c => c.r) > .01f, "Imported fire prefab produces bloom");
                    Save("Temp/BloomFireGlow.png", fireBloom);
                }
                Require(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/LayerAwareBloom/RenderingLayerBloom")),
                    "Bloom shader compiles");
                File.AppendAllText(Report, "PASS\n");
                Debug.Log("Layer Aware Bloom: transparent source verification passed. See " + Report);
            }
            catch (Exception error) { File.AppendAllText(Report, "FAIL " + error + "\n"); Debug.LogException(error); }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original, feature);
                if (!wasDirty) EditorUtility.ClearDirty(feature);
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(opaqueMaterial);
                UnityEngine.Object.DestroyImmediate(texture);
                SceneView.RepaintAll();
            }
        }

        static void SetBloom(RenderingLayerBloomFeature feature, bool enabled)
        {
            var serialized = new SerializedObject(feature);
            serialized.FindProperty("enableBloom").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void VerifyGroupOwnership(RenderingLayerBloomFeature feature, Renderer target,
            Camera camera, RenderTexture rt, string label)
        {
            var first = feature.Groups[0];
            var second = feature.Groups[1];
            string savedSecond = JsonUtility.ToJson(second);
            uint savedLayers = target.renderingLayerMask;
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(first), second);
                second.enabled = true;
                second.renderingLayers = 4;
                target.renderingLayerMask = 6;
                feature.PreviewGroup = 0;
                Require(Capture(camera, rt).Max(c => c.r) > 1, label + " belongs to first matching group");
                feature.PreviewGroup = 1;
                Require(Capture(camera, rt).Max(c => c.r) < .001f, label + " excluded from later matching group");
                Require(!target.forceRenderingOff && target.renderingLayerMask == 6,
                    label + " visibility and lighting layers restored");
                target.renderingLayerMask = 4;
                Require(Capture(camera, rt).Max(c => c.r) > 1, label + " exclusive second-group membership works");
                target.renderingLayerMask = 6;
                first.enabled = false;
                Require(Capture(camera, rt).Max(c => c.r) > 1, label + " falls back when first group is disabled");
                first.enabled = true;
                second.renderingLayers = 2;
                Require(Capture(camera, rt).Max(c => c.r) < .001f, label + " identical group masks do not duplicate");

                second.renderingLayers = 4;
                feature.PreviewGroup = -1;
                SetBloom(feature, true);
                second.enabled = false;
                var singleGroupBloom = Capture(camera, rt);
                second.enabled = true;
                var bothGroupsBloom = Capture(camera, rt);
                Require(bothGroupsBloom.Zip(singleGroupBloom, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    label + " combined bloom is unchanged by overlapping second group");
                float savedIntensity = first.intensity;
                first.intensity = 0;
                var fallbackBloom = Capture(camera, rt);
                first.intensity = savedIntensity;
                Require(fallbackBloom.Zip(singleGroupBloom, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    label + " zero-output first group allows second-group bloom");

                // Also compare the visible scene, not just the isolated source/bloom previews.
                feature.ShowFullscreen = false;
                second.enabled = false;
                var singleGroupScene = Capture(camera, rt);
                second.enabled = true;
                var bothGroupsScene = Capture(camera, rt);
                Require(bothGroupsScene.Zip(singleGroupScene, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    label + " normal camera rendering is preserved");
            }
            finally
            {
                first.enabled = true;
                JsonUtility.FromJsonOverwrite(savedSecond, second);
                target.renderingLayerMask = savedLayers;
                feature.PreviewGroup = 0;
                feature.ShowFullscreen = true;
                SetBloom(feature, false);
            }
        }

        static void VerifyLitTransparency(RenderingLayerBloomFeature feature, Camera camera,
            RenderTexture rt, Scene scene, Material unlitMaterial)
        {
            var second = feature.Groups[1];
            string savedSecond = JsonUtility.ToJson(second);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var overlap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var lightObject = new GameObject("Bloom Test Light");
            foreach (var go in new[] { sphere, overlap, lightObject }) SceneManager.MoveGameObjectToScene(go, scene);
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(feature.Groups[0]), second);
                second.enabled = true; second.renderingLayers = 4;
                feature.PreviewGroup = 1;
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                sphere.transform.position = new Vector3(1, 0, 0);
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                sphere.GetComponent<Renderer>().renderingLayerMask = 4;
                overlap.transform.position = new Vector3(-1, 0, 0);
                overlap.GetComponent<Renderer>().sharedMaterial = unlitMaterial;
                overlap.GetComponent<Renderer>().renderingLayerMask = 2;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point; light.range = 8; light.intensity = 20;
                light.renderingLayerMask = -1;
                light.GetUniversalAdditionalLightData().renderingLayers = uint.MaxValue;
                lightObject.transform.position = new Vector3(1, 0, -2);
                var regularCulling = Capture(camera, rt);
                overlap.GetComponent<Renderer>().renderingLayerMask = 6;
                var exclusiveCulling = Capture(camera, rt);
                Require(regularCulling.Max(c => c.r) > .05f, "Transparent lit material receives light");
                Require(exclusiveCulling.Zip(regularCulling, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    "Overlap exclusion preserves lighting on other transparent renderers");
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(savedSecond, second);
                feature.PreviewGroup = 0;
                UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(overlap);
                UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static Color[] Capture(Camera camera, RenderTexture target)
        {
            RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
            var pixels = readback.GetPixels();
            UnityEngine.Object.DestroyImmediate(readback);
            if (pixels.Any(c => float.IsNaN(c.r) || float.IsInfinity(c.r))) throw new Exception("Nonfinite output");
            return pixels;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(Report, "OK: " + message + "\n");
        }

        static void Save(string path, Color[] pixels)
        {
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            image.SetPixels(pixels.Select(c => new Color(c.gamma.r, c.gamma.g, c.gamma.b, 1)).ToArray());
            image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
