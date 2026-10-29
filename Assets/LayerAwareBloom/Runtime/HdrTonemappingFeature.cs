using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom
{
    // Owned by this package; no dependency on URP's Volume tonemapping component.
    public enum HdrTonemappingMode
    {
        None = 0,
        Neutral = 1,
        ACES = 2
    }

    [DisallowMultipleRendererFeature("HDR Tonemapping")]
    public sealed class HdrTonemappingFeature : ScriptableRendererFeature
    {
        [SerializeField] private BloomSettingsSource settingsSource = BloomSettingsSource.SceneVolumes;
        public BloomSettingsSource SettingsSource { get => settingsSource; set => settingsSource = value; }
        [SerializeField] private Shader tonemappingShader;
        [Tooltip("Maps the whole HDR image to an SDR display range after bloom and grading. " +
            "Use one tonemapper: disable the built-in Tonemapping override when this is enabled.")]
        [SerializeField] private HdrTonemappingMode mode = HdrTonemappingMode.Neutral;

        private Material material;
        private TonemappingPass pass;

        public HdrTonemappingMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public override void Create()
        {
            CoreUtils.Destroy(material);
            if (tonemappingShader == null)
                tonemappingShader = Shader.Find("Hidden/LayerAwareBloom/HdrTonemapping");
            material = tonemappingShader != null ? CoreUtils.CreateEngineMaterial(tonemappingShader) : null;
            pass = new TonemappingPass
            {
                // Run after both our HDR bloom/grade and any optional URP bloom. This injection
                // point is also executed when camera post-processing is disabled.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
                requiresIntermediateTexture = true
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            var mode = this.mode;
            if (settingsSource == BloomSettingsSource.SceneVolumes)
            {
                var volume = VolumeManager.instance.stack?.GetComponent<LayerTonemappingVolume>();
                mode = volume != null && volume.active ? volume.mode.value : HdrTonemappingMode.None;
            }
            if (material == null || mode == HdrTonemappingMode.None ||
                camera.renderType != CameraRenderType.Base ||
                (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                return;
            pass.Setup(material, mode);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class TonemappingPass : ScriptableRenderPass
        {
            private static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int ModeId = Shader.PropertyToID("_LayerTonemappingMode");
            private Material material;
            private HdrTonemappingMode mode;

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
                public HdrTonemappingMode mode;
                public readonly MaterialPropertyBlock properties = new();
            }

            public void Setup(Material material, HdrTonemappingMode mode)
            {
                this.material = material;
                this.mode = mode;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                    return;

                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "HDR Tonemapping Output";
                // Do not inherit the camera's packed R11G11B10 HDR format here. Its coarse
                // mantissa quantizes the mapped glow gradients before the final display conversion.
                // Keep the SDR result in FP16 until the final blit; this also retains alpha.
                descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                var destination = graph.CreateTexture(descriptor);

                using (var builder = graph.AddRasterRenderPass<PassData>("HDR Tonemapping", out var data))
                {
                    data.source = source;
                    data.material = material;
                    data.mode = mode;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                    {
                        var properties = passData.properties;
                        properties.Clear();
                        RTHandle sourceTexture = passData.source;
                        properties.SetTexture(BlitTexture, sourceTexture.rt);
                        properties.SetVector(BlitScaleBias, new Vector4(1, 1, 0, 0));
                        properties.SetFloat(ModeId, (int)passData.mode);
                        context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0,
                            MeshTopology.Triangles, 3, 1, properties);
                    });
                }
                resources.cameraColor = destination;
            }
        }
    }
}
