using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom.Samples
{
    // Inert in normal use. The release validator opts in with -bloomSmokeReport <absolute path>.
    public sealed class BloomPlayerSmokeCheck : MonoBehaviour
    {
        public RenderingLayerBloomFeature feature;
        public Camera targetCamera;
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-bloomSmokeReport");
            if (flag < 0 || flag + 1 >= args.Length) yield break;
            for (int i = 0; i < 8; i++) yield return null;
            string report = args[flag + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            var previous = RenderTexture.active;
            var target = new RenderTexture(new RenderTextureDescriptor(256, 256, GraphicsFormat.R16G16B16A16_SFloat, 24));
            string original = JsonUtility.ToJson(feature);
            try
            {
                // This filter smoke test intentionally drives the legacy settings API directly.
                // Scene Volume isolation/blending has a separate verification fixture.
                feature.SettingsSource = BloomSettingsSource.RendererAsset;
                target.Create();
                foreach (string shaderName in new[] { "RenderingLayerBloom", "UIBloomSource", "UIBloomNative" })
                {
                    var shader = Shader.Find("Hidden/LayerAwareBloom/" + shaderName);
                    if (shader == null || !shader.isSupported) throw new Exception("Missing/unsupported shader: " + shaderName);
                }
                feature.ShowFullscreen = true;
                feature.PreviewGroup = 0;
                foreach (bool hq in new[] { false, true })
                {
                    var group = feature.Groups[0];
                    group.enabled = true; group.highQualityFiltering = hq;
                    group.radius = hq ? 1.25f : 1; group.softKnee = hq ? .75f : .5f; group.stopNaN = hq;
                    var pixels = Capture(target);
                    if (pixels.Any(c => !Finite(c.r) || !Finite(c.g) || !Finite(c.b))) throw new Exception("Nonfinite player output");
                    if (pixels.Max(c => Mathf.Max(c.r, c.g, c.b)) < .05f) throw new Exception("Bloom is absent in player");
                    if (hq)
                    {
                        var screenshot = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
                        screenshot.SetPixels(pixels.Select(c => c.gamma).ToArray()); screenshot.Apply();
                        File.WriteAllBytes(Path.ChangeExtension(report, ".png"), screenshot.EncodeToPNG());
                        Destroy(screenshot);
                    }
                }
                foreach (var group in feature.Groups) group.enabled = false;
                if (Capture(target).Max(c => Mathf.Max(c.r, c.g, c.b)) > .001f) throw new Exception("Disabled groups still produce bloom");
                File.WriteAllText(report, $"PASS: player bloom, disabled groups, both quality variants, optional additions, shader inclusion.\nUnity {Application.unityVersion}; {SystemInfo.graphicsDeviceType}; {QualitySettings.activeColorSpace}\n");
                Application.Quit(0);
            }
            catch (Exception e) { File.WriteAllText(report, "FAIL " + e); Application.Quit(1); }
            finally { JsonUtility.FromJsonOverwrite(original, feature); RenderTexture.active = previous; Destroy(target); }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        Color[] Capture(RenderTexture target)
        {
            RenderPipeline.SubmitRenderRequest(targetCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels(); Destroy(image); return pixels;
        }
    }
}
