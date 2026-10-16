using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom
{
    // Step 1 only: a visible-surface mask, with an optional fullscreen preview.
    public sealed class RenderingLayerSilhouetteFeature : ScriptableRendererFeature
    {
        [SerializeField] private RenderingLayerMask renderingLayers = 1;
        [SerializeField, HideInInspector] private bool showFullscreen;
        [SerializeField] private Shader silhouetteShader;

        private Material material;
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
            if (silhouetteShader == null)
                silhouetteShader = Shader.Find("Hidden/LayerAwareBloom/Silhouette");
            material = silhouetteShader != null ? CoreUtils.CreateEngineMaterial(silhouetteShader) : null;
            silhouettePass = new SilhouettePass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            previewPass = new PreviewPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (material == null || cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraType != CameraType.Game && cameraData.cameraType != CameraType.SceneView))
                return;

            silhouettePass.Setup(material, renderingLayers);
            renderer.EnqueuePass(silhouettePass);
            if (showFullscreen)
            {
                previewPass.Setup(material);
                renderer.EnqueuePass(previewPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
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

            private Material material;
            private uint renderingLayerMask;

            private sealed class PassData
            {
                public RendererListHandle rendererList;
            }

            public void Setup(Material maskMaterial, uint layerMask)
            {
                material = maskMaterial;
                renderingLayerMask = layerMask;
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
                frameData.Create<SilhouetteFrameData>().texture = mask;

                var drawing = RenderingUtils.CreateDrawingSettings(
                    ShaderTags, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                drawing.overrideMaterial = material;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, -1, renderingLayerMask);
                var rendererList = renderGraph.CreateRendererList(
                    new RendererListParams(rendering.cullResults, drawing, filtering));

                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "Rendering Layer Silhouette", out var data);
                data.rendererList = rendererList;
                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                // Keep the debug texture inspectable even when fullscreen preview is off.
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(passData.rendererList));
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
