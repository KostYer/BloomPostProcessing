using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LayerAwareBloom
{
    [Serializable]
    public sealed class LayerTonemappingModeParameter : VolumeParameter<HdrTonemappingMode>
    {
        public LayerTonemappingModeParameter(HdrTonemappingMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Layer Aware Bloom/Tonemapping")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LayerTonemappingVolume : VolumeComponent, IPostProcessComponent
    {
        public LayerTonemappingModeParameter mode = new(HdrTonemappingMode.None);
        public bool IsActive() => mode.value != HdrTonemappingMode.None;
    }
}
