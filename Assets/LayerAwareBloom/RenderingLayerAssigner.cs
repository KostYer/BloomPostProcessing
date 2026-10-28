using UnityEngine;
using UnityEngine.Rendering;

namespace LayerAwareBloom
{
    /// <summary>Inspector helper for assigning Rendering Layers to an entire effect hierarchy.</summary>
    [DisallowMultipleComponent, AddComponentMenu("Rendering/Rendering Layer Assigner")]
    public sealed class RenderingLayerAssigner : MonoBehaviour
    {
        [Tooltip("Replaces the Rendering Layer Mask on this object and all child renderers when Apply is pressed. " +
            "Include Default or other lighting layers here if they should be retained.")]
        public RenderingLayerMask renderingLayers = 1;
    }
}
