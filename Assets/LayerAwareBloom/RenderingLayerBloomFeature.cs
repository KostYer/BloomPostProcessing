using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace LayerAwareBloom
{
    // Visible-surface mask and URP Gaussian bloom blur, displayed as a grayscale mask.
    public sealed class RenderingLayerBloomFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderingLayerMask renderingLayers = 1;
        [SerializeField, HideInInspector] private bool showFullscreen;
        [FormerlySerializedAs("silhouetteShader")]
        [SerializeField] private Shader bloomShader;
        [FormerlySerializedAs("blurSilhouette")]
        [SerializeField] private bool blurMask = true;
        [SerializeField, Range(0, 1)] private float scatter = 0.7f;
        [SerializeField, Range(2, 8)] private int maxIterations = 6;
        [SerializeField] private BloomDownscaleMode downscale = BloomDownscaleMode.Half;
        [SerializeField] private bool highQualityFiltering;

        private Material maskMaterial;
        private Material blurMaterial;
        private Material highQualityBlurMaterial;
        private BloomMaskPass bloomMaskPass;
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
            bloomMaskPass = new BloomMaskPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            fullscreenPreviewPass = new FullscreenPreviewPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (maskMaterial == null || cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView))
                return;

            var filterMaterial = highQualityFiltering ? highQualityBlurMaterial : blurMaterial;
            bloomMaskPass.Setup(maskMaterial, renderingLayers, blurMask ? filterMaterial : null,
                scatter, maxIterations, downscale);
            renderer.EnqueuePass(bloomMaskPass);
            if (showFullscreen)
            {
                fullscreenPreviewPass.Setup(blurMask ? filterMaterial : maskMaterial);
                renderer.EnqueuePass(fullscreenPreviewPass);
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
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitTexelSize = Shader.PropertyToID("_BlitTexture_TexelSize");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int LowMip = Shader.PropertyToID("_SourceTexLowMip");
            private static readonly int LowMipTexelSize = Shader.PropertyToID("_SourceTexLowMip_TexelSize");
            private static readonly int Parameters = Shader.PropertyToID("_Params");

            private readonly TextureHandle[] downsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private readonly TextureHandle[] upsamplePyramid = new TextureHandle[MaxPyramidLevels];
            private Material filterMaterial;
            private float scatter;
            private int maxIterations;
            private BloomDownscaleMode downscale;

            private Material maskMaterial;
            private uint renderingLayerMask;

            private sealed class PassData
            {
                public RendererListHandle rendererList;

                public TextureHandle source;
                public TextureHandle lowMip;
                public Vector4 sourceTexelSize;
                public Vector4 lowMipTexelSize;
                public Vector4 parameters;
                public Material material;
                public int shaderPass;
                // Render Graph pools PassData. Each draw gets its own bindings, not shared material state.
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material maskMaterial, uint layerMask, Material blurMaterial,
                float scatterValue, int iterations, BloomDownscaleMode scale)
            {
                this.maskMaterial = maskMaterial;
                renderingLayerMask = layerMask;
                filterMaterial = blurMaterial;
                scatter = Mathf.Clamp01(scatterValue);
                maxIterations = Mathf.Clamp(iterations, 2, MaxPyramidLevels);
                downscale = scale;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
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

                // After closing the mask builder, record the downsample/upsample passes.
                // FullscreenPreviewPass reads whichever texture the frame-data item holds.
                if (filterMaterial != null)
                    bloomFrameData.texture = RecordBloomPyramid(renderGraph, mask);
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

                // Threshold/knee zero: the BW source is already selected, so do not discard its white pixels.
                // Scatter remapping is the same as URP's Gaussian bloom.
                var parameters = new Vector4(Mathf.Lerp(0.05f, 0.95f, scatter), 65472f, 0, 0);
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

        private sealed class FullscreenPreviewPass : ScriptableRenderPass
        {
            private Material previewMaterial;

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public void Setup(Material previewMaterial) => this.previewMaterial = previewMaterial;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var mask = frameData.Get<BloomFrameData>().texture;
                if (!mask.IsValid())
                    return;

                var resources = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Bloom Fullscreen Preview", out var data);
                data.source = mask;
                data.material = previewMaterial;
                builder.UseTexture(mask, AccessFlags.Read);
                // Only overwrite the camera output; do not sample it or modify scene depth.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, passData.source,
                        new Vector4(1, 1, 0, 0), passData.material, 1));
            }
        }
    }
}
