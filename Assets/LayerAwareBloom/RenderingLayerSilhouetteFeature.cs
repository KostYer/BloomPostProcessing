using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom
{
    // Visible-surface mask and URP Gaussian bloom blur, displayed as a grayscale silhouette.
    public sealed class RenderingLayerSilhouetteFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderingLayerMask renderingLayers = 1;
        [SerializeField, HideInInspector] private bool showFullscreen;
        [SerializeField] private Shader silhouetteShader;
        [SerializeField] private bool blurSilhouette = true;
        [SerializeField, Range(0, 1)] private float scatter = 0.7f;
        [SerializeField, Range(2, 8)] private int maxIterations = 6;
        [SerializeField] private BloomDownscaleMode downscale = BloomDownscaleMode.Half;
        [SerializeField] private bool highQualityFiltering;

        private Material material;
        private Material bloomMaterial;
        private Material bloomMaterialHQ;
        private SilhouettePass silhouettePass;
        private PreviewPass previewPass;

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
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(bloomMaterial);
            CoreUtils.Destroy(bloomMaterialHQ);
            if (silhouetteShader == null)
                silhouetteShader = Shader.Find("Hidden/LayerAwareBloom/Silhouette");
            material = silhouetteShader != null ? CoreUtils.CreateEngineMaterial(silhouetteShader) : null;
            bloomMaterial = silhouetteShader != null ? CoreUtils.CreateEngineMaterial(silhouetteShader) : null;
            bloomMaterialHQ = silhouetteShader != null ? CoreUtils.CreateEngineMaterial(silhouetteShader) : null;
            if (bloomMaterial != null)
            {
                // Separate immutable variants avoid changing keywords between recorded draws.
                bloomMaterial.SetFloat("_SilhouetteBloomPreview", 1);
                bloomMaterialHQ.SetFloat("_SilhouetteBloomPreview", 1);
                bloomMaterialHQ.EnableKeyword("_BLOOM_HQ");
            }
            silhouettePass = new SilhouettePass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            previewPass = new PreviewPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (material == null || cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView))
                return;

            var filterMaterial = highQualityFiltering ? bloomMaterialHQ : bloomMaterial;
            silhouettePass.Setup(material, renderingLayers, blurSilhouette ? filterMaterial : null,
                scatter, maxIterations, downscale);
            renderer.EnqueuePass(silhouettePass);
            if (showFullscreen)
            {
                previewPass.Setup(blurSilhouette ? filterMaterial : material);
                renderer.EnqueuePass(previewPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(bloomMaterial);
            CoreUtils.Destroy(bloomMaterialHQ);
            material = null;
            bloomMaterial = null;
            bloomMaterialHQ = null;
        }

        // Handles are shared only within this camera's current graph, never across frames.
        public sealed class SilhouetteFrameData : ContextItem
        {
            public TextureHandle texture;
            public override void Reset() => texture = TextureHandle.nullHandle;
        }

        private sealed class SilhouettePass : ScriptableRenderPass
        {
            private static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalGBuffer")
            };

            private const int MaxLevels = 8;
            // Shader passes 0/1 draw the silhouette and preview. These four contain the URP Gaussian filters in Silhouette.shader.
            private const int Prefilter = 2, BlurHorizontal = 3, BlurVertical = 4, Upsample = 5;
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitTexelSize = Shader.PropertyToID("_BlitTexture_TexelSize");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int LowMip = Shader.PropertyToID("_SourceTexLowMip");
            private static readonly int LowMipTexelSize = Shader.PropertyToID("_SourceTexLowMip_TexelSize");
            private static readonly int Parameters = Shader.PropertyToID("_Params");

            private readonly TextureHandle[] down = new TextureHandle[MaxLevels];
            private readonly TextureHandle[] up = new TextureHandle[MaxLevels];
            private Material filterMaterial;
            private float scatter;
            private int maxIterations;
            private BloomDownscaleMode downscale;

            private Material material;
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
                material = maskMaterial;
                renderingLayerMask = layerMask;
                filterMaterial = blurMaterial;
                scatter = Mathf.Clamp01(scatterValue);
                maxIterations = Mathf.Clamp(iterations, 2, MaxLevels);
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
                descriptor.name = "Rendering Layer Silhouette (BW)";
                descriptor.format = GraphicsFormat.R8G8B8A8_UNorm;
                descriptor.isShadowMap = false;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.black;
                descriptor.filterMode = FilterMode.Point;
                var mask = renderGraph.CreateTexture(descriptor);
                var silhouette = frameData.Create<SilhouetteFrameData>();
                silhouette.texture = mask;

                var drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                drawing.overrideMaterial = material;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, renderingLayerMask);
                var rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(rendering.cullResults, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Silhouette", out var data))
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
                // PreviewPass reads whichever texture this existing frame-data item holds.
                if (filterMaterial != null)
                    silhouette.texture = RecordBlur(renderGraph, mask);
            }

            private TextureHandle RecordBlur(RenderGraph renderGraph, TextureHandle source)
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
                    descriptor.name = $"Silhouette Bloom Down {i}";
                    down[i] = renderGraph.CreateTexture(descriptor);
                    descriptor.name = $"Silhouette Bloom Up {i}";
                    up[i] = renderGraph.CreateTexture(descriptor);
                    width = Mathf.Max(1, width >> 1);
                    height = Mathf.Max(1, height >> 1);
                }

                // Threshold/knee zero: the BW source is already selected, so do not discard its white pixels.
                // Scatter remapping is the same as URP's Gaussian bloom.
                var parameters = new Vector4(Mathf.Lerp(0.05f, 0.95f, scatter), 65472f, 0, 0);
                RecordFilter(renderGraph, "Silhouette Bloom Prefilter", source, down[0], Prefilter, parameters);
                for (int i = 1; i < levels; i++)
                {
                    // As in URP: downsample + horizontal Gaussian, then vertical Gaussian at the same size.
                    RecordFilter(renderGraph, $"Silhouette Bloom Down {i} Horizontal", down[i - 1], up[i], BlurHorizontal, parameters);
                    RecordFilter(renderGraph, $"Silhouette Bloom Down {i} Vertical", up[i], down[i], BlurVertical, parameters);
                }

                var result = down[levels - 1];
                for (int i = levels - 2; i >= 0; i--)
                {
                    RecordFilter(renderGraph, $"Silhouette Bloom Up {i}", down[i], up[i], Upsample, parameters, result);
                    result = up[i];
                }

                return result;
            }

            private void RecordFilter(RenderGraph graph, string name, TextureHandle source,TextureHandle destination, int shaderPass, Vector4 parameters, TextureHandle lowMip = default)
            {
                using var builder = graph.AddRasterRenderPass<PassData>(name, out var data);
                data.source = source;
                data.lowMip = lowMip;
                data.material = filterMaterial;
                data.shaderPass = shaderPass;
                data.parameters = parameters;
                data.sourceTexelSize = TexelSize(graph.GetTextureDesc(source));
                builder.UseTexture(source, AccessFlags.Read);
                if (lowMip.IsValid())
                {
                    builder.UseTexture(lowMip, AccessFlags.Read);
                    data.lowMipTexelSize = TexelSize(graph.GetTextureDesc(lowMip));
                }
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                // This debug feature also keeps its pyramid available in Frame Debugger when preview is hidden.
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<PassData>(ExecuteFilter);
            }

            private static Vector4 TexelSize(TextureDesc descriptor) =>
                new(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height);

            private static void ExecuteFilter(PassData data, RasterGraphContext context)
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

        private sealed class PreviewPass : ScriptableRenderPass
        {
            private Material material;

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public void Setup(Material previewMaterial) => material = previewMaterial;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var mask = frameData.Get<SilhouetteFrameData>().texture;
                if (!mask.IsValid())
                    return;

                var resources = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Show Silhouette Fullscreen", out var data);
                data.source = mask;
                data.material = material;
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
