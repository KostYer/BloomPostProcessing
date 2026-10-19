using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace LayerAwareBloom
{
    // Visible Rendering Layer selection, HDR source extraction, and URP Gaussian bloom.
    public sealed class RenderingLayerBloomFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderingLayerMask renderingLayers = 1;
        [SerializeField, HideInInspector] private bool showFullscreen;
        [FormerlySerializedAs("silhouetteShader")]
        [SerializeField] private Shader bloomShader;
        [FormerlySerializedAs("blurSilhouette")]
        [FormerlySerializedAs("blurMask")]
        [SerializeField] private bool enableBloom = true;
        [SerializeField, Min(0)] private float threshold = 0.9f;
        [SerializeField, Min(0)] private float intensity = 1f;
        [SerializeField, Min(0)] private float clamp = 65472f;
        [SerializeField, Range(0, 1)] private float scatter = 0.7f;
        [SerializeField, Range(2, 8)] private int maxIterations = 6;
        [SerializeField] private BloomDownscaleMode downscale = BloomDownscaleMode.Half;
        [SerializeField] private bool highQualityFiltering;

        private Material maskMaterial;
        private Material blurMaterial;
        private Material highQualityBlurMaterial;
        private BloomMaskPass bloomMaskPass;
        private BloomCompositePass bloomCompositePass;
        private FullscreenPreviewPass fullscreenPreviewPass;

        public RenderingLayerMask RenderingLayers
        {
            get => renderingLayers;
            set => renderingLayers = value;
        }

        public bool ShowFullscreen
        {
            get => showFullscreen;
            set => showFullscreen = value;
        }

        public override void Create()
        {
            CoreUtils.Destroy(maskMaterial);
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
                blurMaterial.SetFloat("_PreviewUsesBloomEncoding", 1);
                highQualityBlurMaterial.SetFloat("_PreviewUsesBloomEncoding", 1);
                highQualityBlurMaterial.EnableKeyword("_BLOOM_HQ");
            }
            bloomMaskPass = new BloomMaskPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
                // HDR extraction samples active camera color, which must be a texture rather than the backbuffer.
                requiresIntermediateTexture = true
            };
            bloomCompositePass = new BloomCompositePass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing,
                requiresIntermediateTexture = true
            };
            fullscreenPreviewPass = new FullscreenPreviewPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (maskMaterial == null || cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView))
                return;
            if (!enableBloom && !showFullscreen)
                return;

            var filterMaterial = highQualityFiltering ? highQualityBlurMaterial : blurMaterial;
            bloomMaskPass.Setup(maskMaterial, renderingLayers, enableBloom ? filterMaterial : null,
                scatter, maxIterations, downscale, threshold, clamp);
            renderer.EnqueuePass(bloomMaskPass);
            if (showFullscreen)
            {
                fullscreenPreviewPass.Setup(enableBloom ? filterMaterial : maskMaterial,
                    enableBloom ? Mathf.Max(0, intensity) : 1f);
                renderer.EnqueuePass(fullscreenPreviewPass);
            }
            else if (enableBloom && intensity > 0)
            {
                bloomCompositePass.Setup(filterMaterial, intensity);
                renderer.EnqueuePass(bloomCompositePass);
            }
        }

        protected override void Dispose(bool disposing)
        {
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
            public TextureHandle texture;
            public override void Reset() => texture = TextureHandle.nullHandle;
        }

        private sealed class BloomMaskPass : ScriptableRenderPass
        {
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

            private readonly TextureHandle[] downsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private readonly TextureHandle[] upsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private Material filterMaterial;
            private float scatter;
            private int maxIterations;
            private BloomDownscaleMode downscale;
            private float threshold;
            private float clamp;

            private Material maskMaterial;
            private uint renderingLayerMask;

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
                float scatterValue, int iterations, BloomDownscaleMode scale, float thresholdValue, float clampValue)
            {
                this.maskMaterial = maskMaterial;
                renderingLayerMask = layerMask;
                filterMaterial = blurMaterial;
                scatter = Mathf.Clamp01(scatterValue);
                maxIterations = Mathf.Clamp(iterations, 2, MaxPyramidLevels);
                downscale = scale;
                threshold = Mathf.Max(0, thresholdValue);
                clamp = Mathf.Max(0, clampValue);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                    return;
                var camera = frameData.Get<UniversalCameraData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();

                // Match depth dimensions and sample count so the attachments are compatible.
                var descriptor = renderGraph.GetTextureDesc(resources.activeDepthTexture);
                descriptor.name = "Rendering Layer Bloom Mask (BW)";
                descriptor.format = GraphicsFormat.R8G8B8A8_UNorm;
                descriptor.isShadowMap = false;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.black;
                descriptor.filterMode = FilterMode.Point;
                var mask = renderGraph.CreateTexture(descriptor);
                var bloomFrameData = frameData.Create<BloomFrameData>();
                bloomFrameData.texture = mask;

                var drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                drawing.overrideMaterial = maskMaterial;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, renderingLayerMask);
                var rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(rendering.cullResults, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Bloom Mask", out var data))
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
                var source = RecordSourceExtraction(renderGraph, resources.activeColorTexture, mask);
                bloomFrameData.texture = filterMaterial != null ? RecordBloomPyramid(renderGraph, source) : source;
            }

            private TextureHandle RecordSourceExtraction(RenderGraph graph, TextureHandle cameraColor, TextureHandle mask)
            {
                var descriptor = graph.GetTextureDesc(cameraColor);
                descriptor.name = "Rendering Layer Bloom HDR Source";
                descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.wrapMode = TextureWrapMode.Clamp;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                var source = graph.CreateTexture(descriptor);

                using var builder = graph.AddRasterRenderPass<PassData>("Rendering Layer Bloom Extract HDR", out var data);
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
                int shift = downscale == BloomDownscaleMode.Quarter ? 2 : 1;
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
                    descriptor.name = $"Rendering Layer Bloom Down {i}";
                    downsamplePyramid[i] = renderGraph.CreateTexture(descriptor);
                    descriptor.name = $"Rendering Layer Bloom Up {i}";
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
                using var builder = graph.AddRasterRenderPass<PassData>(name, out var data);
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

        private sealed class BloomCompositePass : ScriptableRenderPass
        {
            private const int Composite = 7;
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int BloomTexture = Shader.PropertyToID("_LayerBloomTexture");
            private static readonly int BloomIntensity = Shader.PropertyToID("_BloomIntensity");
            private Material material;
            private float intensity;

            private sealed class PassData
            {
                public TextureHandle scene;
                public TextureHandle bloom;
                public Material material;
                public float intensity;
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material material, float intensity)
            {
                this.material = material;
                this.intensity = intensity;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                if (!frameData.Contains<BloomFrameData>())
                    return;
                var bloom = frameData.Get<BloomFrameData>().texture;
                var resources = frameData.Get<UniversalResourceData>();
                if (!bloom.IsValid() || resources.isActiveTargetBackBuffer)
                    return;

                var scene = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(scene);
                descriptor.name = "Rendering Layer Bloom Scene Composite";
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                var destination = graph.CreateTexture(descriptor);

                using (var builder = graph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Bloom Composite", out var data))
                {
                    data.scene = scene;
                    data.bloom = bloom;
                    data.material = material;
                    data.intensity = intensity;
                    builder.UseTexture(scene, AccessFlags.Read);
                    builder.UseTexture(bloom, AccessFlags.Read);
                    // Read the scene and write a separate texture; never sample our own render target.
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                    {
                        var properties = passData.properties;
                        properties.Clear();
                        RTHandle sceneTexture = passData.scene;
                        RTHandle bloomTexture = passData.bloom;
                        properties.SetTexture(BlitTexture, sceneTexture.rt);
                        properties.SetTexture(BloomTexture, bloomTexture.rt);
                        properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                        properties.SetFloat(BloomIntensity, passData.intensity);
                        context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, Composite,
                            MeshTopology.Triangles, 3, 1, properties);
                    });
                }

                // Subsequent URP passes (including grading/tonemapping) consume the combined HDR image.
                resources.cameraColor = destination;
            }
        }

        private sealed class FullscreenPreviewPass : ScriptableRenderPass
        {
            private Material previewMaterial;
            private float intensity;
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int BloomIntensity = Shader.PropertyToID("_BloomIntensity");

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
                public float intensity;
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material previewMaterial, float intensity)
            {
                this.previewMaterial = previewMaterial;
                this.intensity = intensity;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!frameData.Contains<BloomFrameData>())
                    return;
                var source = frameData.Get<BloomFrameData>().texture;
                if (!source.IsValid())
                    return;

                var resources = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Bloom Fullscreen Preview", out var data);
                data.source = source;
                data.material = previewMaterial;
                data.intensity = intensity;
                builder.UseTexture(source, AccessFlags.Read);
                // Only overwrite the camera output; do not sample it or modify scene depth.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    var properties = passData.properties;
                    properties.Clear();
                    RTHandle sourceTexture = passData.source;
                    properties.SetTexture(BlitTexture, sourceTexture.rt);
                    properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                    properties.SetFloat(BloomIntensity, passData.intensity);
                    context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 1,
                        MeshTopology.Triangles, 3, 1, properties);
                });
            }
        }
    }
}
