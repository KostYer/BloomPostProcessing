using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom.Editor
{
    // Actual GPU filter checks; only runs on an explicit menu/marker request.
    [InitializeOnLoad]
    public static class BloomAdditionsVerification
    {
        const string Marker = "Temp/RunBloomAdditionsVerification";
        const string Report = "Temp/BloomAdditionsVerification.txt";
        const int Size = 64;
        static BloomAdditionsVerification() => EditorApplication.delayCall += RunRequested;

        static void RunRequested()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RunRequested; return; }
            File.Delete(Marker);
            Run();
        }

        [MenuItem("Tools/Layer Aware Bloom/Verify Optional Additions")]
        public static void Run()
        {
            File.WriteAllText(Report, "Optional bloom additions: GPU and serialization checks\n");
            var previous = RenderTexture.active;
            bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var shader = Shader.Find("Hidden/LayerAwareBloom/RenderingLayerBloom");
            var material = new Material(shader);
            var source = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Shader baselineShader = null;
            Material baseline = null;
            RenderingLayerBloomFeature scratch = null;
            try
            {
                scratch = ScriptableObject.CreateInstance<RenderingLayerBloomFeature>();
                JsonUtility.FromJsonOverwrite("{\"groups\":[{\"name\":\"Legacy\",\"threshold\":2,\"intensity\":3}]}", scratch);
                Require(scratch.Groups[0].softKnee == .5f && scratch.Groups[0].radius == 1 && !scratch.Groups[0].stopNaN,
                    "Legacy serialized group receives neutral defaults");
                Require(scratch.Groups.All(g => g.softKnee == .5f && g.radius == 1 && !g.stopNaN),
                    "New groups receive neutral defaults");
                scratch.Groups[0].softKnee = .9f; scratch.Groups[0].radius = 1.4f; scratch.Groups[0].stopNaN = true;
                scratch.Groups[1].radius = .8f;
                var serialized = new SerializedObject(scratch);
                RenderingLayerBloomFeatureEditor.ResetGroup(serialized.FindProperty("groups").GetArrayElementAtIndex(0), 0);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(scratch.Groups[0].softKnee == .5f && scratch.Groups[0].radius == 1 && !scratch.Groups[0].stopNaN &&
                    scratch.Groups[0].threshold == .9f && scratch.Groups[0].intensity == 1 && scratch.Groups[1].radius == .8f,
                    "Inspector reset restores core settings and additions without changing another group");
                Require(JsonUtility.ToJson(scratch.Groups[0]) == JsonUtility.ToJson(
                    new RenderingLayerBloomFeature.BloomGroupSettings { name = "Group 1" }),
                    "First group reset restores every field to fresh-feature defaults");
                serialized.Update();
                RenderingLayerBloomFeatureEditor.ResetGroup(serialized.FindProperty("groups").GetArrayElementAtIndex(1), 1);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(JsonUtility.ToJson(scratch.Groups[1]) == JsonUtility.ToJson(
                    new RenderingLayerBloomFeature.BloomGroupSettings { name = "Group 2", enabled = false, renderingLayers = 0 }),
                    "Spare group reset restores disabled, empty-layer defaults");

                string baselinePath = "Temp/BloomBeforeAdditions.shader.txt";
                if (File.Exists(baselinePath))
                {
                    baselineShader = ShaderUtil.CreateShaderAsset(File.ReadAllText(baselinePath).Replace(
                        "Hidden/LayerAwareBloom/RenderingLayerBloom", "Hidden/LayerAwareBloom/VerificationBaseline"));
                    baseline = new Material(baselineShader);
                    var pixels = Enumerable.Range(0, Size * Size).Select(i =>
                        new Color((i * 17 % 83) * .1f, (i * 31 % 97) * .05f, (i * 7 % 43) * .15f, 1)).ToArray();
                    source.SetPixels(pixels); source.Apply();
                    foreach (bool hq in new[] { false, true })
                    {
                        SetQuality(material, hq); SetQuality(baseline, hq);
                        foreach (int pass in new[] { 2, 3, 4, 5 })
                        {
                            var before = Draw(baseline, source, pass, .5f, 1);
                            var after = Draw(material, source, pass, .5f, 1);
                            Require(MaxDifference(before, after) <= 1e-6f, $"Default pass {pass}, HQ={hq}: matches pre-change shader");
                        }
                        foreach (int pass in new[] { 1, 7 })
                            for (int count = 0; count <= RenderingLayerBloomFeature.GroupCount; count++)
                                Require(MaxDifference(Draw(baseline, source, pass, .5f, 1, count),
                                    Draw(material, source, pass, .5f, 1, count)) <= 1e-6f,
                                    $"Composite/preview pass {pass}, HQ={hq}, groups={count}: matches pre-change shader");
                    }
                    Require(!ShaderUtil.ShaderHasError(baselineShader), "Baseline shader compiled");
                }
                else File.AppendAllText(Report, "SKIP pre-change comparison: no Temp/BloomBeforeAdditions.shader.txt\n");

                foreach (bool hq in new[] { false, true })
                {
                    SetQuality(material, hq);
                    source.SetPixels(Enumerable.Repeat(new Color(.9f, .9f, .9f, 1), Size * Size).ToArray()); source.Apply();
                    var hard = Draw(material, source, 2, 0, 1);
                    var soft = Draw(material, source, 2, .5f, 1);
                    Require(hard.All(c => c.r == 0) && soft.All(c => c.r > .01f), $"Soft knee affects near-threshold light, HQ={hq}");
                }

                var impulse = new Color[Size * Size]; impulse[(Size / 2) * Size + Size / 2] = Color.white;
                source.SetPixels(impulse); source.Apply();
                foreach (int pass in new[] { 3, 4 })
                {
                    double tight = Spread(Draw(material, source, pass, .5f, .5f));
                    double normal = Spread(Draw(material, source, pass, .5f, 1));
                    double wide = Spread(Draw(material, source, pass, .5f, 1.5f));
                    Require(tight < normal && normal < wide, $"Radius changes blur spread in pass {pass}: {tight:F3} < {normal:F3} < {wide:F3}");
                }

                var dirty = Enumerable.Repeat(new Color(2, .5f, 8, 1), Size * Size).ToArray();
                dirty[100].r = float.NaN; dirty[200].g = float.PositiveInfinity; dirty[300].b = float.NegativeInfinity;
                source.SetPixels(dirty); source.Apply();
                var clean = Draw(material, source, 8, .5f, 1);
                Require(clean.All(Finite), "Sanitizer removes NaN and both infinities");
                Require(new[] { 100, 200, 300 }.All(i => clean[i].r == 0 && clean[i].g == 0 && clean[i].b == 0),
                    "Invalid pixels become black");
                Require(Enumerable.Range(0, dirty.Length).Where(i => i != 100 && i != 200 && i != 300)
                    .All(i => clean[i] == dirty[i]), "Sanitizer preserves all finite pixels exactly");
                source.SetPixels(clean); source.Apply();
                foreach (bool hq in new[] { false, true })
                {
                    SetQuality(material, hq);
                    Require(Draw(material, source, 2, .5f, 1).All(Finite), $"Clean source stays finite in prefilter, HQ={hq}");
                }
                Require(!ShaderUtil.ShaderHasError(shader), "Bloom shader compiled");
                VerifyRenderGraph();
                File.AppendAllText(Report, "PASS\n");
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL " + e + "\n"); Debug.LogException(e); }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                if (baselineShader != null) UnityEngine.Object.DestroyImmediate(baselineShader);
                if (scratch != null) UnityEngine.Object.DestroyImmediate(scratch);
                SceneView.RepaintAll();
            }
        }

        static void VerifyRenderGraph()
        {
            var feature = BloomVerificationContext.FindFeature();
            string original = EditorJsonUtility.ToJson(feature);
            bool dirty = EditorUtility.IsDirty(feature);
            try
            {
                foreach (var group in feature.Groups)
                { group.softKnee = .5f; group.radius = 1; group.stopNaN = false; }
                BloomUIVerification.Run();
                Require(File.ReadAllText("Temp/BloomUIVerification.txt").TrimEnd().EndsWith("PASS"), "UI integration with defaults");
                BloomTransparencyVerification.Run();
                Require(File.ReadAllText("Temp/BloomTransparencyVerification.txt").TrimEnd().EndsWith("PASS"), "Transparency integration with defaults");
                foreach (var group in feature.Groups)
                { group.softKnee = .75f; group.radius = 1.25f; group.stopNaN = true; }
                BloomUIVerification.Run();
                Require(File.ReadAllText("Temp/BloomUIVerification.txt").TrimEnd().EndsWith("PASS"), "UI integration with additions enabled");
                BloomTransparencyVerification.Run();
                Require(File.ReadAllText("Temp/BloomTransparencyVerification.txt").TrimEnd().EndsWith("PASS"), "Transparency integration with additions enabled");
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original, feature);
                feature.Create();
                if (!dirty) EditorUtility.ClearDirty(feature);
            }
        }

        static void SetQuality(Material material, bool enabled)
        { if (enabled) material.EnableKeyword("_LAYER_BLOOM_HQ"); else material.DisableKeyword("_LAYER_BLOOM_HQ"); }
        static bool Finite(Color c) => !float.IsNaN(c.r) && !float.IsInfinity(c.r) &&
            !float.IsNaN(c.g) && !float.IsInfinity(c.g) && !float.IsNaN(c.b) && !float.IsInfinity(c.b);
        static float MaxDifference(Color[] a, Color[] b) => a.Zip(b, (x, y) =>
            Mathf.Max(Mathf.Abs(x.r - y.r), Mathf.Abs(x.g - y.g), Mathf.Abs(x.b - y.b))).Max();
        static double Spread(Color[] pixels)
        {
            double sum = 0, moment = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % Size - Size / 2, y = i / Size - Size / 2;
                sum += pixels[i].r; moment += pixels[i].r * (x * x + y * y);
            }
            return moment / sum;
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(Report, "OK " + message + "\n"); }

        static Color[] Draw(Material material, Texture source, int pass, float knee, float radius, int bloomCount = 0)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(new RenderTextureDescriptor(Size, Size, GraphicsFormat.R32G32B32A32_SFloat, 0));
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            var command = new CommandBuffer();
            try
            {
                target.Create();
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_BlitTexture", source);
                properties.SetTexture("_SourceTexLowMip", source);
                properties.SetVector("_BlitTexture_TexelSize", new Vector4(1f / Size, 1f / Size, Size, Size));
                properties.SetVector("_SourceTexLowMip_TexelSize", new Vector4(1f / Size, 1f / Size, Size, Size));
                properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                properties.SetVector("_Params", new Vector4(.68f, 65472, 1, knee));
                properties.SetFloat("_BloomRadius", radius);
                properties.SetInt("_LayerBloomCount", bloomCount);
                properties.SetFloat("_PreviewSource", 0);
                properties.SetVector("_LayerGradingParams", new Vector4(1, 0, 1, 0));
                for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++)
                {
                    properties.SetTexture("_LayerBloomTexture" + i, source);
                    properties.SetVector("_LayerBloomTexture" + i + "_TexelSize", new Vector4(1f / Size, 1f / Size, Size, Size));
                    properties.SetVector("_LayerBloomWeight" + i, new Vector4(.1f * (i + 1), .2f, .3f,
                        material.IsKeywordEnabled("_LAYER_BLOOM_HQ") ? 1 : 0));
                }
                command.SetRenderTarget(target); command.SetViewport(new Rect(0, 0, Size, Size));
                // This isolated draw can run before URP has rendered its first camera.
                // Supply the full-size RTHandle scale normally established by URP.
                var previousScale = Shader.GetGlobalVector("_RTHandleScale");
                command.SetGlobalVector("_RTHandleScale", Vector4.one);
                command.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 1, properties);
                command.SetGlobalVector("_RTHandleScale", previousScale);
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                command.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback);
            }
        }
    }
}
