using UnityEngine;
using UnityEngine.Rendering;

namespace LayerAwareBloom
{
    /// <summary>Bloom selection for uGUI Camera and World Space canvases.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Rendering/UI Bloom Source")]
    public sealed class UIBloomSource : MonoBehaviour
    {
        [Tooltip("Uses the bloom feature's existing Rendering Layers. The first active matching group wins.")]
        public RenderingLayerMask renderingLayers = 2;
        [Tooltip("Apply to descendant UI graphics. A closer UI Bloom Source overrides this component.")]
        public bool includeChildren = true;
        [Min(0), Tooltip("Brightness in the bloom source only. Does not change the normal UI color.")]
        public float emissionMultiplier = 2;

        internal static UIBloomSource Resolve(Transform target)
        {
            for (var current = target; current != null; current = current.parent)
                if (current.TryGetComponent<UIBloomSource>(out var source) &&
                    (current == target || source.includeChildren))
                    // A disabled override deliberately opts this subtree out of inherited bloom.
                    return source;
            return null;
        }
    }
}
