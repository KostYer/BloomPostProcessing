using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LayerAwareBloom.Editor
{
    public static class BloomReleaseTools
    {
        public const string DemoRoot = "Assets/LayerAwareBloom/Samples/GettingStarted";
        public const string ScenePath = DemoRoot + "/GettingStarted.unity";
        public const string PipelinePath = DemoRoot + "/DemoPipeline.asset";
        static readonly string[] ExportRoots = {
            "Assets/LayerAwareBloom/Runtime", "Assets/LayerAwareBloom/Shaders", "Assets/LayerAwareBloom/Editor",
            "Assets/LayerAwareBloom/Documentation", "Assets/LayerAwareBloom/README.md", "Assets/LayerAwareBloom/CHANGELOG.md",
            "Assets/LayerAwareBloom/Third-Party Notices.txt", DemoRoot
        };

        [MenuItem("Tools/Layer Aware Bloom/Release/Create Getting Started Demo")]
        public static void CreateDemo()
        {
            if (File.Exists(ScenePath)) return;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "DemoRenderer";
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(renderer, DemoRoot + "/DemoRenderer.asset");
                var bloom = ScriptableObject.CreateInstance<RenderingLayerBloomFeature>();
                bloom.name = "Rendering Layer Bloom";
                AssetDatabase.AddObjectToAsset(bloom, renderer); renderer.rendererFeatures.Add(bloom); bloom.Create();
                bloom.Groups[0].renderingLayers = 2; bloom.Groups[0].threshold = 1; bloom.Groups[0].intensity = 2;
                bloom.Groups[1].enabled = true; bloom.Groups[1].renderingLayers = 4;
                bloom.Groups[1].intensity = 1; bloom.Groups[1].scatter = .45f;
                var tone = ScriptableObject.CreateInstance<HdrTonemappingFeature>();
                tone.name = "HDR Tonemapping";
                AssetDatabase.AddObjectToAsset(tone, renderer); renderer.rendererFeatures.Add(tone); tone.Create();
                // Keep URP's feature local-file-ID map consistent after adding subassets.
                var rendererState = new SerializedObject(renderer);
                var map = rendererState.FindProperty("m_RendererFeatureMap"); map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < map.arraySize; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long localId);
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
                rendererState.ApplyModifiedPropertiesWithoutUndo();
                var pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "DemoPipeline"; pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                pipeline.supportsCameraDepthTexture = true; pipeline.supportsCameraOpaqueTexture = true;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                foreach (var asset in new UnityEngine.Object[] { bloom, tone, renderer, pipeline })
                { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }

                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 0, -8);
                camera.orthographic = true; camera.orthographicSize = 3; camera.allowHDR = true; camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                for (int i = 0; i < 3; i++)
                {
                    var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    material.name = new[] { "CyanSource", "AmberSource", "UnselectedSource" }[i];
                    material.SetColor("_BaseColor", new[] { new Color(0, 4, 6), new Color(6, 2, 0), new Color(3, 3, 3) }[i]);
                    AssetDatabase.CreateAsset(material, DemoRoot + "/" + material.name + ".mat");
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = new[] { "Group 1 - Wide Cyan Bloom", "Group 2 - Tight Amber Bloom", "Unselected - No Bloom" }[i];
                    cube.transform.position = new Vector3((i - 1) * 2, 0, 0);
                    cube.GetComponent<Renderer>().sharedMaterial = material;
                    cube.GetComponent<Renderer>().renderingLayerMask = new uint[] { 2, 4, 1 }[i];
                }
                // Resolve by MonoScript to keep the editor assembly independent of optional sample assemblies.
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(DemoRoot + "/BloomPlayerSmokeCheck.cs");
                var smoke = camera.gameObject.AddComponent(script.GetClass());
                var smokeState = new SerializedObject(smoke);
                smokeState.FindProperty("feature").objectReferenceValue = bloom;
                smokeState.FindProperty("targetCamera").objectReferenceValue = camera;
                smokeState.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
        }

        [MenuItem("Tools/Layer Aware Bloom/Release/Export Release Candidate")]
        public static void Export()
        {
            CreateDemo();
            Directory.CreateDirectory("ReleaseArtifacts");
            foreach (string path in ExportRoots) if (!File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException("Missing release content: " + path);
            var paths = ExportRoots.SelectMany(p => Directory.Exists(p)
                ? Directory.GetFiles(p, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")) : new[] { p })
                .Select(p => p.Replace('\\', '/')).ToArray();
            // Explicit list, no IncludeDependencies: never bundle existing customer/demo dependencies by accident.
            foreach (string dependency in AssetDatabase.GetDependencies(paths, true))
                if (dependency.StartsWith("Assets/") && !paths.Contains(dependency) && !AssetDatabase.IsValidFolder(dependency))
                    throw new InvalidOperationException("Release depends on an excluded asset: " + dependency);
            AssetDatabase.ExportPackage(paths, "ReleaseArtifacts/LayerAwareBloom-1.0.0-rc1.unitypackage", ExportPackageOptions.Default);
            File.WriteAllLines("ReleaseArtifacts/PackageContents.txt", paths.OrderBy(p => p));
            Debug.Log("Layer Aware Bloom: release candidate exported to ReleaseArtifacts.");
        }

        // Run in the disposable clean validation project only, never changes a customer's pipeline from a menu.
        public static void ValidateAndBuildBatch()
        {
            Directory.CreateDirectory("VerificationResults");
            File.Delete("VerificationResults/Failure.txt");
            try
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });
                EditorSceneManager.OpenScene(ScenePath);
                BloomAdditionsVerification.Run();
                BloomConflictVerification.Run();
                foreach (string report in Directory.GetFiles("Temp", "Bloom*Verification.txt"))
                    File.Copy(report, "VerificationResults/" + Path.GetFileName(report), true);
                foreach (string report in new[] { "BloomAdditionsVerification.txt", "BloomConflictVerification.txt" })
                {
                    if (!File.ReadAllText("Temp/" + report).TrimEnd().EndsWith("PASS")) throw new Exception(report + " failed");
                }
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                    locationPathName = "Player/LayerAwareBloomSmoke.exe", options = BuildOptions.Development
                });
                File.WriteAllText("VerificationResults/Build.txt", $"{result.summary.result}; errors={result.summary.totalErrors}; warnings={result.summary.totalWarnings}\n");
                EditorApplication.Exit(result.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            { File.WriteAllText("VerificationResults/Failure.txt", e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
