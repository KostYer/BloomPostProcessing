using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace LayerAwareBloom
{
    // Feature-owned settings. Explicit values preserve existing serialized Half/Quarter selections.
    public enum BloomDownscale
    {
        Half = 0,
        Quarter = 1
    }

    // Visible Rendering Layer selection, HDR source extraction, and URP Gaussian bloom.
    public sealed class RenderingLayerBloomFeature : ScriptableRendererFeature
    {
        public const int GroupCount = 6;

        [System.Serializable]
        public sealed class BloomGroupSettings
        {
            public string name = "Bloom Group";
            public bool enabled = true;
            public RenderingLayerMask renderingLayers = 1;
            [Min(0)] public float threshold = 0.9f;
            [Min(0)] public float intensity = 1f;
            [ColorUsage(false, false)] public Color tint = Color.white;
            [Min(0)] public float clamp = 65472f;
            [Range(0, 1)] public float scatter = 0.7f;
            [Range(2, 8)] public int maxIterations = 6;
            public BloomDownscale downscale = BloomDownscale.Half;
            public bool highQualityFiltering;
        }

        [System.Serializable]
        public sealed class HdrColorGradingSettings
        {
            public bool enabled;
            [Tooltip("Exposure in stops. +1 doubles brightness; -1 halves it.")]
            [Range(-10, 10)] public float exposure;
            [Tooltip("Luminance contrast around linear 18% gray. Zero is unchanged.")]
            [Range(-100, 100)] public float contrast;
            [Tooltip("-100 is grayscale; zero is unchanged.")]
            [Range(-100, 100)] public float saturation;
            [Tooltip("Multiplies the whole scene and bloom in linear space. White is unchanged.")]
            [ColorUsage(false, false)] public Color colorFilter = Color.white;
        }

        [SerializeField] private BloomGroupSettings[] groups;
        [SerializeField] private HdrColorGradingSettings colorGrading = new();
        // Legacy fields migrate existing renderer assets into Group 1 on first load.
        [SerializeField, HideInInspector] private RenderingLayerMask renderingLayers = 1;
        [SerializeField, HideInInspector] private bool showFullscreen;
        [FormerlySerializedAs("silhouetteShader")]
        [SerializeField] private Shader bloomShader;
        [SerializeField] private Shader uiBloomShader;
        [SerializeField] private Shader uiNativeShader;
        [FormerlySerializedAs("blurSilhouette")]
        [FormerlySerializedAs("blurMask")]
        [SerializeField] private bool enableBloom = true;
        [SerializeField, HideInInspector] private float threshold = 0.9f;
        [SerializeField, HideInInspector] private float intensity = 1f;
        [SerializeField, HideInInspector] private float clamp = 65472f;
        [SerializeField, HideInInspector] private float scatter = 0.7f;
        [SerializeField, HideInInspector] private int maxIterations = 6;
        [SerializeField, HideInInspector] private BloomDownscale downscale = BloomDownscale.Half;
        [SerializeField, HideInInspector] private bool highQualityFiltering;
        [SerializeField, HideInInspector] private int previewGroup = -1;

        private Material maskMaterial;
        private Material blurMaterial;
        private Material highQualityBlurMaterial;
        private UIBloomCapture uiCapture;
        private BloomMaskPass[] bloomMaskPasses;
        private BloomOutputPass bloomCompositePass;
        private BloomOutputPass fullscreenPreviewPass;
        private ScriptableRenderContext currentContext;
        private Camera contextCamera;
        private readonly List<Renderer> temporarilyHidden = new();

        public IReadOnlyList<BloomGroupSettings> Groups { get { EnsureGroups(); return groups; } }
        public HdrColorGradingSettings ColorGrading => colorGrading ??= new HdrColorGradingSettings();

        public RenderingLayerMask RenderingLayers
        {
            get => Groups[0].renderingLayers;
            set { EnsureGroups(); groups[0].renderingLayers = value; }
        }

        public bool ShowFullscreen
        {
            get => showFullscreen;
            set => showFullscreen = value;
        }

        // -1 previews all active groups; 0..5 isolate a group. Source preview defaults to Group 1.
        public int PreviewGroup
        {
            get => previewGroup;
            set => previewGroup = Mathf.Clamp(value, -1, GroupCount - 1);
        }

        private void EnsureGroups()
        {
            if (groups == null || groups.Length == 0)
            {
                groups = new BloomGroupSettings[GroupCount];
                groups[0] = new BloomGroupSettings
                {
                    name = "Group 1", renderingLayers = renderingLayers, threshold = threshold,
                    intensity = intensity, clamp = clamp, scatter = scatter, maxIterations = maxIterations,
                    downscale = downscale, highQualityFiltering = highQualityFiltering
                };
            }
            if (groups.Length != GroupCount) System.Array.Resize(ref groups, GroupCount);
            for (int i = 0; i < GroupCount; i++)
                groups[i] ??= new BloomGroupSettings
                {
                    name = $"Group {i + 1}", enabled = false, renderingLayers = 0
                };
        }

        public override void Create()
        {
            RenderPipelineManager.beginCameraRendering -= CaptureContext;
            RenderPipelineManager.beginCameraRendering += CaptureContext;
            // Register after uGUI's material rebuild callback, before native Canvas batching.
            _ = UnityEngine.UI.CanvasUpdateRegistry.instance;
            Canvas.willRenderCanvases -= PrepareUI;
            Canvas.willRenderCanvases += PrepareUI;
            EnsureGroups();
            CoreUtils.Destroy(maskMaterial);
            uiCapture?.Dispose();
            if (uiBloomShader == null) uiBloomShader = Shader.Find("Hidden/LayerAwareBloom/UIBloomSource");
            if (uiNativeShader == null) uiNativeShader = Shader.Find("Hidden/LayerAwareBloom/UIBloomNative");
            uiCapture = new UIBloomCapture(uiBloomShader, uiNativeShader);
            CoreUtils.Destroy(blurMaterial);
            CoreUtils.Destroy(highQualityBlurMaterial);
            if (bloomShader == null)
                bloomShader = Shader.Find("Hidden/LayerAwareBloom/RenderingLayerBloom");
            maskMaterial = bloomShader != null ? CoreUtils.CreateEngineMaterial(bloomShader) : null;
            blurMaterial = bloomShader != null ? CoreUtils.CreateEngineMaterial(bloomShader) : null;
            highQualityBlurMaterial = bloomShader != null ? CoreUtils.CreateEngineMaterial(bloomShader) : null;
            if (blurMaterial != null)
            {
                // Separate immutable variants avoid changing keywords between recorded draws.
                highQualityBlurMaterial.EnableKeyword("_LAYER_BLOOM_HQ");
            }
            bloomMaskPasses = new BloomMaskPass[GroupCount];
            bloomCompositePass = new BloomOutputPass(false)
            {
                // A renderer scheduling point, independent of whether camera post-processing is enabled.
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing,
                requiresIntermediateTexture = true
            };
            // Debug previews intentionally stay raw, after the separate optional tonemapper.
            fullscreenPreviewPass = new BloomOutputPass(true)
            {
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + 1)
            };
        }

        private void CaptureContext(ScriptableRenderContext context, Camera camera)
        {
            currentContext = context;
            contextCamera = camera;
        }

        private void PrepareUI()
        {
            if (isActive && (enableBloom || showFullscreen)) uiCapture?.PrepareNativeUI();
            else uiCapture?.RestoreNativeMaterials();
        }

        private static Renderer[] CollectCameraRenderers(Camera camera)
        {
            // Preview/prefab scenes are not returned by FindObjectsByType.
            if (camera.scene.IsValid())
            {
                var renderers = new List<Renderer>();
                foreach (var root in camera.scene.GetRootGameObjects())
                    renderers.AddRange(root.GetComponentsInChildren<Renderer>());
                return renderers.ToArray();
            }
            return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (maskMaterial == null || cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView))
                return;
            if (!enableBloom && !showFullscreen && !ColorGrading.enabled)
                return;

            EnsureGroups();
            int selectedPreview = Mathf.Clamp(previewGroup, -1, GroupCount - 1);
            if (!enableBloom) selectedPreview = Mathf.Max(0, selectedPreview);
            bool anyGroup = false;
            uint earlierLayers = 0;
            Renderer[] cameraRenderers = null;
            for (int i = 0; (enableBloom || showFullscreen) && i < GroupCount; i++)
            {
                var group = groups[i];
                if (!group.enabled || (uint)group.renderingLayers == 0 ||
                    (enableBloom && (group.intensity <= 0 || group.clamp <= 0 ||
                        (group.tint.r <= 0 && group.tint.g <= 0 && group.tint.b <= 0))))
                    continue;
                uint excludedLayers = earlierLayers;
                earlierLayers |= (uint)group.renderingLayers;
                // Previewing a later group must not reassign objects owned by an earlier group.
                if (showFullscreen && selectedPreview >= 0 && selectedPreview != i) continue;

                var groupCulling = renderingData.cullResults;
                bool customCulling = false;
                if (excludedLayers != 0 && contextCamera == cameraData.camera)
                {
                    cameraRenderers ??= CollectCameraRenderers(cameraData.camera);
                    temporarilyHidden.Clear();
                    try
                    {
                        foreach (var candidate in cameraRenderers)
                        {
                            if (!candidate.enabled || candidate.forceRenderingOff ||
                                (candidate.renderingLayerMask & excludedLayers) == 0 ||
                                (candidate.renderingLayerMask & (uint)group.renderingLayers) == 0) continue;
                            temporarilyHidden.Add(candidate);
                            candidate.forceRenderingOff = true;
                        }
                        if (temporarilyHidden.Count > 0 && cameraData.camera.TryGetCullingParameters(out var parameters))
                        {
                            renderer.SetupCullingParameters(ref parameters, ref cameraData);
                            parameters.cullingOptions &= ~CullingOptions.ShadowCasters;
                            groupCulling = currentContext.Cull(ref parameters);
                            customCulling = true;
                        }
                    }
                    finally
                    {
                        foreach (var candidate in temporarilyHidden)
                            if (candidate != null) candidate.forceRenderingOff = false;
                        temporarilyHidden.Clear();
                    }
                }
                var filter = group.highQualityFiltering ? highQualityBlurMaterial : blurMaterial;
                // Unconfigured/zero-output slots never allocate a pass or record a pyramid.
                bloomMaskPasses[i] ??= new BloomMaskPass(i)
                {
                    // Opaque color/depth copies must exist for soft particles and distortion.
                    // Capture before normal transparents so unselected VFX cannot leak into the source.
                    renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
                    requiresIntermediateTexture = true
                };
                bloomMaskPasses[i].Setup(maskMaterial, group.renderingLayers, enableBloom ? filter : null,
                    group.scatter, group.maxIterations, group.downscale, group.threshold, group.clamp,
                    group.tint.linear * Mathf.Max(0, group.intensity), groupCulling, customCulling,
                    uiCapture, groups, enableBloom);
                renderer.EnqueuePass(bloomMaskPasses[i]);
                anyGroup = true;
            }
            if (showFullscreen)
            {
                fullscreenPreviewPass.Setup(maskMaterial, !enableBloom, selectedPreview);
                renderer.EnqueuePass(fullscreenPreviewPass);
            }
            else if (anyGroup || ColorGrading.enabled)
            {
                bloomCompositePass.Setup(maskMaterial, false, -1, ColorGrading);
                renderer.EnqueuePass(bloomCompositePass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= CaptureContext;
            Canvas.willRenderCanvases -= PrepareUI;
            uiCapture?.Dispose();
            uiCapture = null;
            CoreUtils.Destroy(maskMaterial);
            CoreUtils.Destroy(blurMaterial);
            CoreUtils.Destroy(highQualityBlurMaterial);
            maskMaterial = null;
            blurMaterial = null;
            highQualityBlurMaterial = null;
        }

        // Handles are shared only within this camera's current graph, never across frames.
        public sealed class BloomFrameData : ContextItem
        {
            public readonly TextureHandle[] textures = new TextureHandle[GroupCount];
            public readonly Vector4[] weights = new Vector4[GroupCount];
            public readonly bool[] highQuality = new bool[GroupCount];
            public override void Reset()
            {
                for (int i = 0; i < GroupCount; i++)
                {
                    textures[i] = TextureHandle.nullHandle;
                    weights[i] = Vector4.zero;
                    highQuality[i] = false;
                }
            }
        }

        private sealed class BloomMaskPass : ScriptableRenderPass
        {
            private readonly int groupIndex;
            private readonly string groupLabel;
            private Vector4 weight;

            public BloomMaskPass(int groupIndex)
            {
                this.groupIndex = groupIndex;
                groupLabel = $"Rendering Layer Bloom Group {groupIndex + 1}";
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
            }
            private static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalGBuffer")
            };

            private const int MaxPyramidLevels = 8;
            // Shader passes 0/1 draw the mask and preview. These four contain the URP Gaussian filters in RenderingLayerBloom.shader.
            private const int Prefilter = 2, BlurHorizontal = 3, BlurVertical = 4, Upsample = 5;
            private const int ExtractSource = 6;
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitTexelSize = Shader.PropertyToID("_BlitTexture_TexelSize");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int LowMip = Shader.PropertyToID("_SourceTexLowMip");
            private static readonly int LowMipTexelSize = Shader.PropertyToID("_SourceTexLowMip_TexelSize");
            private static readonly int Parameters = Shader.PropertyToID("_Params");
            private static readonly int SelectionMask = Shader.PropertyToID("_SelectionMask");
            private static readonly int DrawObjectPassData = Shader.PropertyToID("_DrawObjectPassData");
            private static readonly int AlphaToMaskAvailable = Shader.PropertyToID("_AlphaToMaskAvailable");

            private readonly TextureHandle[] downsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private readonly TextureHandle[] upsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private Material filterMaterial;
            private float scatter;
            private int maxIterations;
            private BloomDownscale downscale;
            private float threshold;
            private float clamp;

            private Material maskMaterial;
            private uint renderingLayerMask;
            private CullingResults groupCulling;
            private bool customCulling;
            private UIBloomCapture uiCapture;
            private IReadOnlyList<BloomGroupSettings> groups;
            private bool bloomEnabled;

            private sealed class PassData
            {
                public RendererListHandle rendererList;

                public TextureHandle source;
                public TextureHandle lowMip;
                public TextureHandle selectionMask;
                public Vector4 sourceTexelSize;
                public Vector4 lowMipTexelSize;
                public Vector4 parameters;
                public Material material;
                public int shaderPass;
                // Render Graph pools PassData. Each draw gets its own bindings, not shared material state.
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material maskMaterial, uint layerMask, Material blurMaterial,
                float scatterValue, int iterations, BloomDownscale scale, float thresholdValue, float clampValue, Color weight,
                CullingResults groupCulling, bool customCulling, UIBloomCapture uiCapture,
                IReadOnlyList<BloomGroupSettings> groups, bool bloomEnabled)
            {
                this.maskMaterial = maskMaterial;
                renderingLayerMask = layerMask;
                filterMaterial = blurMaterial;
                scatter = Mathf.Clamp01(scatterValue);
                maxIterations = Mathf.Clamp(iterations, 2, MaxPyramidLevels);
                downscale = scale;
                threshold = Mathf.Max(0, thresholdValue);
                clamp = Mathf.Max(0, clampValue);
                this.weight = weight;
                this.groupCulling = groupCulling;
                this.customCulling = customCulling;
                this.uiCapture = uiCapture;
                this.groups = groups;
                this.bloomEnabled = bloomEnabled;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                    return;
                var camera = frameData.Get<UniversalCameraData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();
                if (customCulling) CopyLightingIndices(rendering.cullResults, groupCulling);

                // Match depth dimensions and sample count so the attachments are compatible.
                var descriptor = renderGraph.GetTextureDesc(resources.activeDepthTexture);
                descriptor.name = $"{groupLabel} Mask (BW)";
                descriptor.format = GraphicsFormat.R8G8B8A8_UNorm;
                descriptor.isShadowMap = false;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.black;
                descriptor.filterMode = FilterMode.Point;
                var mask = renderGraph.CreateTexture(descriptor);
                var bloomFrameData = frameData.Contains<BloomFrameData>()
                    ? frameData.Get<BloomFrameData>() : frameData.Create<BloomFrameData>();

                var drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                drawing.overrideMaterial = maskMaterial;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, renderingLayerMask);
                var rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(groupCulling, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    $"{groupLabel} Mask", out var data))
                {
                    data.rendererList = rendererList;
                    builder.UseRendererList(rendererList);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    // Keep the debug texture inspectable even when fullscreen preview is off.
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                        context.cmd.DrawRendererList(passData.rendererList));
                }

                // Select full-resolution HDR color BEFORE any spatial filtering. Filtering camera color
                // first would allow bright unselected neighbours to leak into selected source pixels.
                var source = RecordSourceExtraction(renderGraph, resources.activeColorTexture, mask,
                    descriptor.msaaSamples);
                RecordTransparentSource(renderGraph, source, resources, rendering, camera, lights);
                uiCapture?.Record(renderGraph, frameData, source, groupIndex, groups, bloomEnabled);
                bloomFrameData.textures[groupIndex] = filterMaterial != null ? RecordBloomPyramid(renderGraph, source) : source;
                bloomFrameData.weights[groupIndex] = weight;
                bloomFrameData.highQuality[groupIndex] = filterMaterial != null &&
                    filterMaterial.IsKeywordEnabled("_LAYER_BLOOM_HQ");
            }

            private static void CopyLightingIndices(CullingResults cameraCulling, CullingResults selectedCulling)
            {
                // URP's light/probe buffers use the original camera's indices.
                using var originalLights = cameraCulling.GetLightIndexMap(Allocator.Temp);
                using var selectedLightStorage = selectedCulling.GetLightIndexMap(Allocator.Temp);
                var selectedLights = selectedLightStorage;
                var cameraLights = cameraCulling.visibleLights;
                var groupLights = selectedCulling.visibleLights;
                for (int i = 0; i < selectedLights.Length; i++)
                {
                    selectedLights[i] = -1;
                    for (int j = 0; j < cameraLights.Length; j++)
                        if (groupLights[i].light == cameraLights[j].light)
                        { selectedLights[i] = originalLights[j]; break; }
                }
                selectedCulling.SetLightIndexMap(selectedLights);
                using var originalProbes = cameraCulling.GetReflectionProbeIndexMap(Allocator.Temp);
                using var selectedProbeStorage = selectedCulling.GetReflectionProbeIndexMap(Allocator.Temp);
                var selectedProbes = selectedProbeStorage;
                var cameraProbes = cameraCulling.visibleReflectionProbes;
                var groupProbes = selectedCulling.visibleReflectionProbes;
                for (int i = 0; i < selectedProbes.Length; i++)
                {
                    selectedProbes[i] = -1;
                    for (int j = 0; j < cameraProbes.Length; j++)
                        if (groupProbes[i].reflectionProbe == cameraProbes[j].reflectionProbe)
                        { selectedProbes[i] = originalProbes[j]; break; }
                }
                selectedCulling.SetReflectionProbeIndexMap(selectedProbes);
            }

            private void RecordTransparentSource(RenderGraph graph, TextureHandle source,
                UniversalResourceData resources, UniversalRenderingData rendering,
                UniversalCameraData camera, UniversalLightData lights)
            {
                // Retain each material's vertex animation, texture alpha, HDR emission and blend mode.
                // An override silhouette would turn particle billboards into solid glowing rectangles.
                var drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
                var filtering = new FilteringSettings(RenderQueueRange.transparent, -1, renderingLayerMask);
                // These Temp arrays stay alive until Render Graph creates the native renderer list.
                var tags = new NativeArray<ShaderTagId>(2, Allocator.Temp);
                var states = new NativeArray<RenderStateBlock>(2, Allocator.Temp);
                tags[0] = new ShaderTagId("true");
                states[0] = new RenderStateBlock(RenderStateMask.Depth)
                {
                    // Canvas UI is captured separately, even when a group selects the default layer.
                    depthState = new DepthState(false, CompareFunction.Never)
                };
                tags[1] = ShaderTagId.none;
                states[1] = new RenderStateBlock(RenderStateMask.Depth)
                {
                    // Never alter camera depth while drawing the extra bloom source.
                    depthState = new DepthState(false, CompareFunction.LessEqual)
                };
                var rendererList = graph.CreateRendererList(new RendererListParams(
                    groupCulling, drawing, filtering)
                {
                    tagName = new ShaderTagId(UIBloomCapture.MaterialTag),
                    tagValues = tags, stateBlocks = states, isPassTagName = false
                });
                using var builder = graph.AddRasterRenderPass<PassData>(
                    $"{groupLabel} Transparent HDR Source", out var data);
                data.rendererList = rendererList;
                builder.UseRendererList(rendererList);
                // Original shaders may sample camera depth/color, shadows, and other URP globals.
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(source, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    // Match URP's transparent draw globals instead of inheriting opaque-pass state.
                    context.cmd.SetGlobalVector(DrawObjectPassData, Vector4.zero);
                    context.cmd.SetGlobalFloat(AlphaToMaskAvailable, 0);
                    context.cmd.DrawRendererList(passData.rendererList);
                });
            }

            private TextureHandle RecordSourceExtraction(RenderGraph graph, TextureHandle cameraColor,
                TextureHandle mask, MSAASamples samples)
            {
                var descriptor = graph.GetTextureDesc(cameraColor);
                descriptor.name = $"{groupLabel} HDR Source";
                descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                // The transparent draw shares camera depth, so its color sample count must match.
                descriptor.msaaSamples = samples;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.wrapMode = TextureWrapMode.Clamp;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                var source = graph.CreateTexture(descriptor);

                using var builder = graph.AddRasterRenderPass<PassData>($"{groupLabel} Extract HDR", out var data);
                data.source = cameraColor;
                data.selectionMask = mask;
                data.lowMip = TextureHandle.nullHandle;
                data.sourceTexelSize = CalculateTexelSize(graph.GetTextureDesc(cameraColor));
                data.parameters = Vector4.zero;
                data.material = maskMaterial;
                data.shaderPass = ExtractSource;
                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(source, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecuteFilterPass);
                return source;
            }

            private TextureHandle RecordBloomPyramid(RenderGraph renderGraph, TextureHandle source)
            {
                var descriptor = renderGraph.GetTextureDesc(source);
                int shift = downscale == BloomDownscale.Quarter ? 2 : 1;
                int width = Mathf.Max(1, descriptor.width >> shift);
                int height = Mathf.Max(1, descriptor.height >> shift);
                int iterations = Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2f) - 1);
                int levels = Mathf.Clamp(iterations, 1, maxIterations);

                // Floating point preserves the faint blur tails, which an 8-bit mask would quantize away.
                descriptor.format = SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render)
                    && SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Linear)
                    ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.wrapMode = TextureWrapMode.Clamp;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;

                for (int i = 0; i < levels; i++)
                {
                    descriptor.width = width;
                    descriptor.height = height;
                    descriptor.name = $"{groupLabel} Down {i}";
                    downsamplePyramid[i] = renderGraph.CreateTexture(descriptor);
                    descriptor.name = $"{groupLabel} Up {i}";
                    upsamplePyramid[i] = renderGraph.CreateTexture(descriptor);
                    width = Mathf.Max(1, width >> 1);
                    height = Mathf.Max(1, height >> 1);
                }

                // URP exposes threshold in gamma space and fixes the soft knee at half the linear threshold.
                float linearThreshold = Mathf.GammaToLinearSpace(threshold);
                var parameters = new Vector4(Mathf.Lerp(0.05f, 0.95f, scatter), clamp,
                    linearThreshold, linearThreshold * 0.5f);
                RecordFilterPass(renderGraph, "Rendering Layer Bloom Prefilter", source, downsamplePyramid[0], Prefilter, parameters);
                for (int i = 1; i < levels; i++)
                {
                    // As in URP: downsample + horizontal Gaussian, then vertical Gaussian at the same size.
                    RecordFilterPass(renderGraph, $"Rendering Layer Bloom Down {i} Horizontal", downsamplePyramid[i - 1], upsamplePyramid[i], BlurHorizontal, parameters);
                    RecordFilterPass(renderGraph, $"Rendering Layer Bloom Down {i} Vertical", upsamplePyramid[i], downsamplePyramid[i], BlurVertical, parameters);
                }

                var result = downsamplePyramid[levels - 1];
                for (int i = levels - 2; i >= 0; i--)
                {
                    RecordFilterPass(renderGraph, $"Rendering Layer Bloom Up {i}", downsamplePyramid[i], upsamplePyramid[i], Upsample, parameters, result);
                    result = upsamplePyramid[i];
                }

                return result;
            }

            private void RecordFilterPass(RenderGraph graph, string name, TextureHandle source, TextureHandle destination, int shaderPass, Vector4 parameters, TextureHandle lowMip = default)
            {
                using var builder = graph.AddRasterRenderPass<PassData>($"{groupLabel}: {name}", out var data);
                data.source = source;
                data.lowMip = lowMip;
                data.selectionMask = TextureHandle.nullHandle;
                data.material = filterMaterial;
                data.shaderPass = shaderPass;
                data.parameters = parameters;
                data.sourceTexelSize = CalculateTexelSize(graph.GetTextureDesc(source));
                builder.UseTexture(source, AccessFlags.Read);
                if (lowMip.IsValid())
                {
                    builder.UseTexture(lowMip, AccessFlags.Read);
                    data.lowMipTexelSize = CalculateTexelSize(graph.GetTextureDesc(lowMip));
                }
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                // This debug feature also keeps its pyramid available in Frame Debugger when preview is hidden.
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecuteFilterPass);
            }

            private static Vector4 CalculateTexelSize(TextureDesc descriptor) =>
                new(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height);

            private static void ExecuteFilterPass(PassData data, RasterGraphContext context)
            {
                var properties = data.properties;
                properties.Clear();
                RTHandle source = data.source;
                properties.SetTexture(BlitTexture, source.rt);
                properties.SetVector(BlitTexelSize, data.sourceTexelSize);
                properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                properties.SetVector(Parameters, data.parameters);
                if (data.selectionMask.IsValid())
                {
                    RTHandle selectionMask = data.selectionMask;
                    properties.SetTexture(SelectionMask, selectionMask.rt);
                }
                if (data.lowMip.IsValid())
                {
                    RTHandle low = data.lowMip;
                    properties.SetTexture(LowMip, low.rt);
                    properties.SetVector(LowMipTexelSize, data.lowMipTexelSize);
                }

                // The same fullscreen triangle as Blitter, with explicit bindings for BOTH upsample inputs.
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.shaderPass,
                    MeshTopology.Triangles, 3, 1, properties);
            }
        }

        // Both outputs read the same independent pyramids and apply each weight exactly once.
        private sealed class BloomOutputPass : ScriptableRenderPass
        {
            private const int Composite = 7, Preview = 1;
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int PreviewSource = Shader.PropertyToID("_PreviewSource");
            private static readonly int GradingParameters = Shader.PropertyToID("_LayerGradingParams");
            private static readonly int GradingFilter = Shader.PropertyToID("_LayerGradingFilter");
            private static readonly int BloomCount = Shader.PropertyToID("_LayerBloomCount");
            private static readonly int[] BloomTexelSizes =
            {
                Shader.PropertyToID("_LayerBloomTexture0_TexelSize"),
                Shader.PropertyToID("_LayerBloomTexture1_TexelSize"),
                Shader.PropertyToID("_LayerBloomTexture2_TexelSize"),
                Shader.PropertyToID("_LayerBloomTexture3_TexelSize"),
                Shader.PropertyToID("_LayerBloomTexture4_TexelSize"),
                Shader.PropertyToID("_LayerBloomTexture5_TexelSize")
            };
            private static readonly int[] BloomTextures =
            {
                Shader.PropertyToID("_LayerBloomTexture0"), Shader.PropertyToID("_LayerBloomTexture1"),
                Shader.PropertyToID("_LayerBloomTexture2"), Shader.PropertyToID("_LayerBloomTexture3"),
                Shader.PropertyToID("_LayerBloomTexture4"), Shader.PropertyToID("_LayerBloomTexture5")
            };
            private static readonly int[] BloomWeights =
            {
                Shader.PropertyToID("_LayerBloomWeight0"), Shader.PropertyToID("_LayerBloomWeight1"),
                Shader.PropertyToID("_LayerBloomWeight2"), Shader.PropertyToID("_LayerBloomWeight3"),
                Shader.PropertyToID("_LayerBloomWeight4"), Shader.PropertyToID("_LayerBloomWeight5")
            };
            private readonly bool preview;
            private Material material;
            private bool sourcePreview;
            private int previewGroup;
            private Vector4 gradingParameters;
            private Color gradingFilter;

            public BloomOutputPass(bool preview) => this.preview = preview;

            private sealed class PassData
            {
                public TextureHandle source;
                public readonly TextureHandle[] bloom = new TextureHandle[GroupCount];
                public readonly Vector4[] weights = new Vector4[GroupCount];
                public readonly Vector4[] texelSizes = new Vector4[GroupCount];
                public int bloomCount;
                public Material material;
                public int shaderPass;
                public bool sourcePreview;
                public Vector4 gradingParameters;
                public Color gradingFilter;
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material material, bool sourcePreview, int previewGroup,
                HdrColorGradingSettings grading = null)
            {
                this.material = material;
                this.sourcePreview = sourcePreview;
                this.previewGroup = previewGroup;
                bool grade = !preview && grading != null && grading.enabled;
                gradingParameters = grade
                    ? new Vector4(Mathf.Pow(2, Mathf.Clamp(grading.exposure, -10, 10)),
                        1 + Mathf.Clamp(grading.contrast, -100, 100) * 0.01f,
                        1 + Mathf.Clamp(grading.saturation, -100, 100) * 0.01f, 1)
                    : new Vector4(1, 1, 1, 0);
                gradingFilter = grade ? grading.colorFilter.linear : Color.white;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var bloom = frameData.Contains<BloomFrameData>() ? frameData.Get<BloomFrameData>() : null;
                var resources = frameData.Get<UniversalResourceData>();
                if (!preview && (resources.isActiveTargetBackBuffer ||
                    (bloom == null && gradingParameters.w == 0)))
                    return;

                var destination = resources.activeColorTexture;
                var source = preview ? TextureHandle.nullHandle : destination;
                if (preview && sourcePreview && bloom != null)
                    source = bloom.textures[Mathf.Clamp(previewGroup, 0, GroupCount - 1)];
                if (!preview)
                {
                    var descriptor = graph.GetTextureDesc(source);
                    descriptor.name = "Rendering Layer Bloom Scene Composite";
                    // Preserve smooth scene + glow gradients through subsequent grading/tonemapping.
                    // Reusing the camera's packed HDR format here introduces another coarse rounding step.
                    descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                    descriptor.msaaSamples = MSAASamples.None;
                    descriptor.bindTextureMS = false;
                    descriptor.clearBuffer = false;
                    destination = graph.CreateTexture(descriptor);
                }

                using (var builder = graph.AddRasterRenderPass<PassData>(preview
                    ? "Rendering Layer Bloom Fullscreen Preview" : "Rendering Layer Bloom Composite", out var data))
                {
                    data.source = source;
                    data.material = material;
                    data.shaderPass = preview ? Preview : Composite;
                    data.sourcePreview = sourcePreview;
                    data.gradingParameters = gradingParameters;
                    data.gradingFilter = gradingFilter;
                    data.bloomCount = 0;
                    if (source.IsValid()) builder.UseTexture(source, AccessFlags.Read);
                    for (int i = 0; i < GroupCount; i++)
                    {
                        // PassData is pooled: always overwrite every slot, including inactive groups.
                        data.bloom[i] = TextureHandle.nullHandle;
                        data.weights[i] = Vector4.zero;
                        data.texelSizes[i] = Vector4.one;
                    }
                    for (int i = 0; i < GroupCount; i++)
                    {
                        bool include = !sourcePreview && bloom != null && (!preview || previewGroup < 0 || previewGroup == i);
                        if (include && bloom.textures[i].IsValid())
                        {
                            // Pack active groups contiguously; the shader stops before unused slots.
                            int slot = data.bloomCount++;
                            data.bloom[slot] = bloom.textures[i];
                            data.weights[slot] = bloom.weights[i];
                            data.weights[slot].w = bloom.highQuality[i] ? 1 : 0;
                            builder.UseTexture(data.bloom[slot], AccessFlags.Read);
                            var bloomDesc = graph.GetTextureDesc(data.bloom[slot]);
                            data.texelSizes[slot] = new Vector4(1f / bloomDesc.width, 1f / bloomDesc.height,
                                bloomDesc.width, bloomDesc.height);
                        }
                    }
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                    {
                        var properties = passData.properties;
                        properties.Clear();
                        properties.SetTexture(BlitTexture, GetTexture(passData.source));
                        properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                        properties.SetFloat(PreviewSource, passData.sourcePreview ? 1 : 0);
                        properties.SetVector(GradingParameters, passData.gradingParameters);
                        // Already converted to linear in Setup; SetColor would convert it again.
                        properties.SetVector(GradingFilter, passData.gradingFilter);
                        properties.SetInteger(BloomCount, passData.bloomCount);
                        for (int i = 0; i < GroupCount; i++)
                        {
                            properties.SetTexture(BloomTextures[i], GetTexture(passData.bloom[i]));
                            properties.SetVector(BloomWeights[i], passData.weights[i]);
                            properties.SetVector(BloomTexelSizes[i], passData.texelSizes[i]);
                        }
                        context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, passData.shaderPass,
                            MeshTopology.Triangles, 3, 1, properties);
                    });
                }
                if (!preview) resources.cameraColor = destination;
            }

            private static Texture GetTexture(TextureHandle handle)
            {
                if (!handle.IsValid()) return Texture2D.blackTexture;
                RTHandle texture = handle;
                return texture.rt;
            }
        }
    }
}
