using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom
{
    public enum BloomSettingsSource { SceneVolumes = 0, RendererAsset = 1 }

    [Serializable]
    public sealed class LayerBloomDownscaleParameter : VolumeParameter<BloomDownscale>
    {
        public LayerBloomDownscaleParameter(BloomDownscale value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class LayerBloomGroupParameters
    {
        public BoolParameter enabled = new(true);
        public RenderingLayerMaskParameter renderingLayers = new(0);
        public MinFloatParameter threshold = new(0.9f, 0);
        // Zero is the stack baseline, allowing intensity to fade with Volume weight/blend distance.
        public MinFloatParameter intensity = new(0, 0);
        public ColorParameter tint = new(Color.white, false, false, true);
        public MinFloatParameter clamp = new(65472, 0);
        public ClampedFloatParameter scatter = new(0.7f, 0, 1);
        public NoInterpClampedIntParameter maxIterations = new(6, 2, 8);
        public LayerBloomDownscaleParameter downscale = new(BloomDownscale.Half);
        public BoolParameter highQualityFiltering = new(false);
        public ClampedFloatParameter softKnee = new(0.5f, 0, 1);
        public ClampedFloatParameter radius = new(1, 0.5f, 1.5f);
        public BoolParameter stopNaN = new(false);

        internal void CopyTo(RenderingLayerBloomFeature.BloomGroupSettings target)
        {
            target.enabled = enabled.value; target.renderingLayers = renderingLayers.value;
            target.threshold = threshold.value; target.intensity = intensity.value; target.tint = tint.value;
            target.clamp = clamp.value; target.scatter = scatter.value; target.maxIterations = maxIterations.value;
            target.downscale = downscale.value; target.highQualityFiltering = highQualityFiltering.value;
            target.softKnee = softKnee.value; target.radius = radius.value; target.stopNaN = stopNaN.value;
        }

        public void CopyFrom(RenderingLayerBloomFeature.BloomGroupSettings source)
        {
            enabled.Override(source.enabled); renderingLayers.Override(source.renderingLayers);
            threshold.Override(source.threshold); intensity.Override(source.intensity); tint.Override(source.tint);
            clamp.Override(source.clamp); scatter.Override(source.scatter); maxIterations.Override(source.maxIterations);
            downscale.Override(source.downscale); highQualityFiltering.Override(source.highQualityFiltering);
            softKnee.Override(source.softKnee); radius.Override(source.radius); stopNaN.Override(source.stopNaN);
        }
    }

    [Serializable, VolumeComponentMenu("Layer Aware Bloom/Bloom")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LayerBloomVolume : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enableBloom = new(true);
        // Explicit fields: VolumeComponent discovers nested parameters, but does not traverse arrays.
        public LayerBloomGroupParameters group1 = new();
        public LayerBloomGroupParameters group2 = new();
        public LayerBloomGroupParameters group3 = new();
        public LayerBloomGroupParameters group4 = new();
        public LayerBloomGroupParameters group5 = new();
        public LayerBloomGroupParameters group6 = new();
        public BoolParameter enableColorGrading = new(false);
        public ClampedFloatParameter exposure = new(0, -10, 10);
        public ClampedFloatParameter contrast = new(0, -100, 100);
        public ClampedFloatParameter saturation = new(0, -100, 100);
        public ColorParameter colorFilter = new(Color.white, false, false, true);

        public LayerBloomGroupParameters GetGroup(int index) => index switch
        {
            0 => group1, 1 => group2, 2 => group3, 3 => group4, 4 => group5, 5 => group6,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public bool IsActive()
        {
            if (enableColorGrading.value) return true;
            if (!enableBloom.value) return false;
            for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++)
            {
                var g = GetGroup(i);
                if (g.enabled.value && (uint)g.renderingLayers.value != 0 && g.intensity.value > 0 &&
                    g.clamp.value > 0 && g.tint.value.maxColorComponent > 0) return true;
            }
            return false;
        }

        internal void CopyTo(RenderingLayerBloomFeature.BloomGroupSettings[] groups,
            RenderingLayerBloomFeature.HdrColorGradingSettings grading)
        {
            for (int i = 0; i < groups.Length; i++) GetGroup(i).CopyTo(groups[i]);
            grading.enabled = enableColorGrading.value; grading.exposure = exposure.value;
            grading.contrast = contrast.value; grading.saturation = saturation.value; grading.colorFilter = colorFilter.value;
        }

        public void CopyFrom(RenderingLayerBloomFeature feature)
        {
            enableBloom.Override(feature.BloomEnabled);
            for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++) GetGroup(i).CopyFrom(feature.Groups[i]);
            var grading = feature.ColorGrading;
            enableColorGrading.Override(grading.enabled); exposure.Override(grading.exposure);
            contrast.Override(grading.contrast); saturation.Override(grading.saturation); colorFilter.Override(grading.colorFilter);
        }
    }
}
