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
using UnityEngine.UI;

namespace LayerAwareBloom.Editor
{
    [InitializeOnLoad]
    public static class BloomUIVerification
    {
        const string Marker = "Temp/RunBloomUIVerification";
        const string Report = "Temp/BloomUIVerification.txt";
        const int Size = 256;
        static BloomUIVerification() => EditorApplication.delayCall += RunRequested;
        static void RunRequested()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RunRequested; return; }
            File.Delete(Marker);
            Run();
        }

        // Unity clears Temp on batch-mode exit, so retain reports outside it in validation copies.
        public static void RunBatch()
        {
            Run();
            BloomTransparencyVerification.Run();
            Directory.CreateDirectory("VerificationResults");
            foreach (var path in Directory.GetFiles("Temp", "Bloom*.*"))
                if (Path.GetExtension(path) == ".txt" || Path.GetExtension(path) == ".png")
                    File.Copy(path, Path.Combine("VerificationResults", Path.GetFileName(path)), true);
            bool passed = File.ReadAllText(Report).TrimEnd().EndsWith("PASS") &&
                File.ReadAllText("Temp/BloomTransparencyVerification.txt").TrimEnd().EndsWith("PASS");
            EditorApplication.Exit(passed ? 0 : 1);
        }

        [MenuItem("Tools/Layer Aware Bloom/Verify UI Sources")]
        public static void Run()
        {
            var feature = BloomVerificationContext.FindFeature();
            using var rendererSettings = BloomVerificationContext.UseRendererSettings(feature);
            string original = EditorJsonUtility.ToJson(feature);
            bool wasDirty = EditorUtility.IsDirty(feature);
            var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(new RenderTextureDescriptor(Size, Size, GraphicsFormat.R16G16B16A16_SFloat, 24));
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            Sprite sprite = null;
            var opaque = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            File.WriteAllText(Report, "UI bloom render regression\n");
            try
            {
                var cameraObject = new GameObject("UI Verification Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.transform.position = new Vector3(0, 0, -5);
                camera.orthographic = true; camera.orthographicSize = 2; camera.aspect = 1;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = true; camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                rt.Create();
                for (int i = 0; i < feature.Groups.Count; i++) feature.Groups[i].enabled = i == 0;
                var first = feature.Groups[0]; var second = feature.Groups[1];
                first.renderingLayers = 2; first.threshold = 1; first.intensity = 1;
                first.tint = Color.white; first.clamp = 65472;
                feature.ColorGrading.enabled = false;
                feature.ShowFullscreen = true; feature.PreviewGroup = 0; SetBloom(feature, false);
                var canvasObject = new GameObject("UI Test Canvas", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(4, 4);
                var inherited = canvasObject.AddComponent<UIBloomSource>();
                inherited.renderingLayers = 2; inherited.emissionMultiplier = 4;
                var image = Add<Image>(canvas.transform, "Selected Image", new Vector2(2, 2));
                var pixels = new Color[32 * 32];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    pixels[y * 32 + x] = new Color(1, 1, 1,
                        Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(16, 16)) < 10 ? 1 : 0);
                texture.SetPixels(pixels); texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f);
                image.sprite = sprite;
                string originalMaterialState = EditorJsonUtility.ToJson(image.material);
                Color[] CaptureNow() => Capture(camera, rt);
                var baseline = CaptureNow();
                File.AppendAllText(Report, $"Initial peak: {Peak(baseline)}, mesh: {image.canvasRenderer.GetMesh()?.vertexCount}, alpha: {image.canvasRenderer.GetInheritedAlpha()}\n");
                Require(Peak(baseline) > 3.9f, "World Space Image contributes HDR emission through parent selection");
                Require(baseline[128 * Size + 183].r < .01f, "Sprite transparent corners stay black");
                Save("Temp/BloomUISource.png", baseline);
                var normalMaterial = image.material;
                var normalColor = image.canvasRenderer.GetColor();

                var canvasGroup = canvasObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = .5f;
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - .5f) < .03f, "CanvasGroup fade attenuates source once");
                image.canvasRenderer.SetAlpha(.5f);
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - .25f) < .03f, "CanvasRenderer alpha combines with CanvasGroup alpha");
                image.canvasRenderer.SetAlpha(1); canvasGroup.alpha = 1;
                image.color = new Color(1, 1, 1, .5f);
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - .5f) < .03f, "Graphic alpha is preserved");
                image.color = Color.white;
                image.color = new Color(.5f, .5f, .5f, 1);
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - Mathf.GammaToLinearSpace(.5f)) < .02f,
                    "Graphic RGB is converted from sRGB to linear HDR");
                image.color = Color.white;

                var selection = image.gameObject.AddComponent<UIBloomSource>();
                selection.renderingLayers = 0;
                Require(Peak(CaptureNow()) < .001f, "Empty child selection overrides inherited membership");
                first.renderingLayers = 1;
                var defaultLayer = CaptureNow();
                File.AppendAllText(Report, $"Default layer unselected peak: {Peak(defaultLayer)}\n");
                Save("Temp/BloomUIDefaultLayer.png", defaultLayer);
                Require(Peak(defaultLayer) < .001f, "Unselected native CanvasRenderer does not leak into default-layer source");
                first.renderingLayers = 2;
                selection.renderingLayers = 6; selection.emissionMultiplier = 4;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(first), second);
                second.enabled = true; second.renderingLayers = 4;
                Require(Peak(CaptureNow()) > 3.9f, "Overlapping UI belongs to first group");
                feature.PreviewGroup = 1;
                Require(Peak(CaptureNow()) < .001f, "Overlapping UI is excluded from second group preview");
                first.enabled = false;
                Require(Peak(CaptureNow()) > 3.9f, "UI falls back to next enabled group");
                first.enabled = true; feature.PreviewGroup = 0;
                second.enabled = false; selection.renderingLayers = 2;
                selection.enabled = false;
                Require(Peak(CaptureNow()) < .001f, "Disabled child selection opts out of parent bloom");
                selection.enabled = true;

                first.renderingLayers = 1; selection.renderingLayers = 1;
                image.color = new Color(1, 1, 1, .5f);
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - .5f) < .03f,
                    "Default rendering layer does not capture UI twice");
                first.renderingLayers = 2; selection.renderingLayers = 2; image.color = Color.white;

                var cover = Add<RawImage>(canvas.transform, "Unselected Cover", new Vector2(2, 2));
                cover.gameObject.AddComponent<UIBloomSource>().renderingLayers = 0;
                cover.color = Color.blue;
                Require(Peak(CaptureNow()) < .001f, "Unselected foreground UI hides selected UI without emitting");
                cover.color = new Color(0, 0, 1, .5f);
                Require(Mathf.Abs(Peak(CaptureNow()) / Peak(baseline) - .5f) < .03f, "Translucent foreground UI attenuates bloom");
                cover.gameObject.SetActive(false);

                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(blocker, scene);
                blocker.transform.position = new Vector3(0, 0, -1);
                blocker.transform.localScale = new Vector3(3, 3, .2f);
                blocker.GetComponent<Renderer>().sharedMaterial = opaque;
                blocker.GetComponent<Renderer>().renderingLayerMask = 1;
                Require(Peak(CaptureNow()) < .001f, "Scene depth occludes World Space UI");
                blocker.SetActive(false);

                var clipObject = new GameObject("Scroll View Clip", typeof(RectTransform), typeof(RectMask2D));
                clipObject.transform.SetParent(canvas.transform, false);
                clipObject.GetComponent<RectTransform>().sizeDelta = new Vector2(.5f, 2);
                image.transform.SetParent(clipObject.transform, false);
                var clipped = CaptureNow();
                Require(clipped[128 * Size + 128].r > 3.9f && clipped[128 * Size + 150].r < .001f,
                    "RectMask2D clips generated source");
                clipObject.GetComponent<RectMask2D>().softness = new Vector2Int(8, 0);
                Require(CaptureNow()[128 * Size + 142].r < clipped[128 * Size + 142].r,
                    "RectMask2D softness attenuates the edge");
                image.transform.SetParent(canvas.transform, false);
                clipObject.SetActive(false);

                var maskImage = Add<Image>(canvas.transform, "Sprite Mask", new Vector2(1, 1));
                maskImage.sprite = sprite;
                var mask = maskImage.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
                image.transform.SetParent(maskImage.transform, false);
                image.sprite = null;
                var masked = CaptureNow();
                Require(masked[128 * Size + 128].r > 3.9f && masked[148 * Size + 148].r < .001f,
                    "Hidden Mask graphic clips children using sprite alpha");
                Save("Temp/BloomUIMasked.png", masked);
                feature.ShowFullscreen = false;
                var normalMasked = CaptureNow();
                SetBloom(feature, true); first.intensity = 0;
                var wrappedMasked = CaptureNow();
                Require(Peak(normalMasked) > .1f && normalMasked.Zip(wrappedMasked,
                    (a, b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))).Max() < .003f,
                    "Native UI wrapper preserves normal masked rendering pixel-for-pixel");
                Save("Temp/BloomUINormalMasked.png", wrappedMasked);
                first.intensity = 1; SetBloom(feature, false); feature.ShowFullscreen = true;
                var innerMaskImage = Add<Image>(maskImage.transform, "Inner Mask", new Vector2(.4f, 2));
                innerMaskImage.rectTransform.anchoredPosition = new Vector2(.2f, 0);
                innerMaskImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                image.transform.SetParent(innerMaskImage.transform, false);
                var nestedMask = CaptureNow();
                Require(nestedMask[128 * Size + 138].r > 3.9f && nestedMask[128 * Size + 118].r < .001f,
                    "Nested Masks intersect and restore stencil between draws");
                image.transform.SetParent(canvas.transform, false);
                maskImage.gameObject.SetActive(false);
                image.sprite = sprite;

                image.gameObject.SetActive(false);
                var raw = Add<RawImage>(canvas.transform, "Selected Raw Image", new Vector2(2, 2));
                raw.texture = texture;
                var rawSource = CaptureNow();
                Require(Peak(rawSource) > 3.9f && rawSource[128 * Size + 183].r < .001f,
                    "RawImage preserves texture alpha and inherited selection");
                raw.gameObject.SetActive(false);
                var text = Add<Text>(canvas.transform, "Legacy Text", new Vector2(200, 100));
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 64; text.text = "UI"; text.alignment = TextAnchor.MiddleCenter;
                text.transform.localScale = Vector3.one * .02f;
                Require(Peak(CaptureNow()) > 1, "Legacy Text captures glyph coverage");
                text.gameObject.SetActive(false);
                image.gameObject.SetActive(true);

                SetBloom(feature, true);
                var glow = CaptureNow();
                Require(Peak(glow) > .05f && glow[128 * Size + 180].r > .001f, "UI produces blur outside source geometry");
                Save("Temp/BloomUIGlow.png", glow);
                second.enabled = true; selection.renderingLayers = 6; feature.PreviewGroup = -1;
                var combined = CaptureNow();
                Require(combined.Zip(glow, (a, b) => Mathf.Abs(a.r - b.r)).Max() < .001f,
                    "Two matching groups do not double UI bloom");
                second.enabled = false; selection.renderingLayers = 2; feature.PreviewGroup = 0; SetBloom(feature, false);

                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.planeDistance = 2;
                image.rectTransform.sizeDelta = new Vector2(128, 128);
                var screenSpace = CaptureNow();
                Require(Peak(screenSpace) > 3.9f && screenSpace[128 * Size + 128].r > 3.9f,
                    "Screen Space Camera UI aligns with its camera");
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Require(Peak(CaptureNow()) < .001f, "Screen Space Overlay is ignored");
                Require(image.material == normalMaterial && image.canvasRenderer.GetColor() == normalColor,
                    "Capture leaves original UI material and renderer color unchanged");
                Require(EditorJsonUtility.ToJson(image.material) == originalMaterialState,
                    "Original UI material serialization is unchanged");
                Require(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/LayerAwareBloom/UIBloomSource")), "UI shader compiles");
                Require(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/LayerAwareBloom/UIBloomNative")), "Native UI wrapper shader compiles");
                File.AppendAllText(Report, "PASS\n");
                Debug.Log("Layer Aware Bloom: UI verification passed. See " + Report);
            }
            catch (Exception error) { File.AppendAllText(Report, "FAIL " + error + "\n"); Debug.LogException(error); }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original, feature);
                if (!wasDirty) EditorUtility.ClearDirty(feature);
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(opaque);
                SceneView.RepaintAll();
            }
        }

        static T Add<T>(Transform parent, string name, Vector2 size) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = size;
            return go.AddComponent<T>();
        }
        static float Peak(Color[] pixels) => pixels.Max(c => Mathf.Max(c.r, c.g, c.b));
        static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(Report, "OK: " + message + "\n"); }
        static void SetBloom(RenderingLayerBloomFeature feature, bool enabled)
        {
            var serialized = new SerializedObject(feature);
            serialized.FindProperty("enableBloom").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static Color[] Capture(Camera camera, RenderTexture target)
        {
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
            var pixels = readback.GetPixels();
            UnityEngine.Object.DestroyImmediate(readback);
            if (pixels.Any(c => float.IsNaN(c.r) || float.IsInfinity(c.r))) throw new Exception("Nonfinite output");
            return pixels;
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
