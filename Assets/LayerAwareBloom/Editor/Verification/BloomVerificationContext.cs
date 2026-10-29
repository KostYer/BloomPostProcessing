using System;
using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom.Editor
{
    internal static class BloomVerificationContext
    {
        internal static IDisposable UseRendererSettings(RenderingLayerBloomFeature feature) => new RendererSettingsScope(feature);

        private sealed class RendererSettingsScope : IDisposable
        {
            private readonly RenderingLayerBloomFeature feature;
            private readonly BloomSettingsSource previous;
            public RendererSettingsScope(RenderingLayerBloomFeature feature)
            {
                this.feature = feature; previous = feature.SettingsSource;
                feature.SettingsSource = BloomSettingsSource.RendererAsset;
            }
            public void Dispose() => feature.SettingsSource = previous;
        }

        internal static RenderingLayerBloomFeature FindFeature()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("Activate a URP asset before running bloom verification.");
            // Verification cameras use the pipeline's default renderer, just like a new camera.
            var serialized = new UnityEditor.SerializedObject(pipeline);
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            var feature = renderer?.rendererFeatures.OfType<RenderingLayerBloomFeature>().FirstOrDefault(f => f.isActive);
            if (feature == null) throw new InvalidOperationException("Add and enable Rendering Layer Bloom on the active URP asset's default Universal Renderer before running verification.");
            return feature;
        }
    }
}
