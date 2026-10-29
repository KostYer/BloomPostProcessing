using System.Collections.Generic;
using LayerAwareBloom;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Sample animation: disables one bloom group at a time, then restores it.
/// Optional demo behavior; the bloom renderer does not depend on this component.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Layer Aware Bloom/Samples/Bloom Cycle Demo")]
public sealed class BloomCycleDemo : MonoBehaviour
{
    [Header("Bloom cycle")]
    [SerializeField] private RenderingLayerBloomFeature _bloomFeature;
    [SerializeField] private bool _cycleBloom = true;
    [Tooltip("Seconds each bloom group stays disabled before moving to the next.")]
    [SerializeField, Min(0.1f)] private float _bloomCycleInterval = 3f;

    private readonly List<RenderingLayerBloomFeature.BloomGroupSettings> _cycleGroups = new();
    private readonly List<LayerBloomGroupParameters> _volumeCycleGroups = new();
    private readonly List<bool> _volumeEnabledOverrides = new();
    private LayerBloomVolume _sceneBloom;
    private int CycleGroupCount => _cycleGroups.Count + _volumeCycleGroups.Count;
    private int _disabledBloomGroup = -1;
    private float _bloomCycleElapsed;

    private void OnEnable() => StartBloomCycle();
    private void Update() => UpdateBloomCycle();
    private void OnDisable() => StopBloomCycle();

    private void StartBloomCycle()
    {
        if (!_cycleBloom || _bloomFeature == null || CycleGroupCount > 0)
            return;

        if (_bloomFeature.SettingsSource == BloomSettingsSource.SceneVolumes)
        {
            ResolveSceneBloom();
            if (_sceneBloom != null)
                for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++)
                {
                    var group = _sceneBloom.GetGroup(i);
                    if (!group.enabled.value || (uint)group.renderingLayers.value == 0) continue;
                    _volumeCycleGroups.Add(group);
                    _volumeEnabledOverrides.Add(group.enabled.overrideState);
                }
        }
        else
        {
            // Only cycle groups that were enabled originally; leave other settings alone.
            foreach (var group in _bloomFeature.Groups)
                if (group.enabled && (uint)group.renderingLayers != 0)
                    _cycleGroups.Add(group);
        }

        if (CycleGroupCount == 0)
            return;

        _bloomCycleElapsed = 0f;
        _disabledBloomGroup = -1;
        AdvanceBloomCycle();
    }

    private void UpdateBloomCycle()
    {
        if (!_cycleBloom || _bloomFeature == null)
        {
            StopBloomCycle();
            return;
        }

        StartBloomCycle();
        if (CycleGroupCount == 0)
            return;

        _bloomCycleElapsed += Time.deltaTime;
        float interval = Mathf.Max(0.1f, _bloomCycleInterval);
        if (_bloomCycleElapsed >= interval)
        {
            _bloomCycleElapsed %= interval;
            AdvanceBloomCycle();
        }
    }

    private void AdvanceBloomCycle()
    {
        if (_disabledBloomGroup >= 0)
            SetCycleGroupEnabled(_disabledBloomGroup, true);
        _disabledBloomGroup = (_disabledBloomGroup + 1) % CycleGroupCount;
        SetCycleGroupEnabled(_disabledBloomGroup, false);
    }

    private void SetCycleGroupEnabled(int index, bool enabled)
    {
        if (_volumeCycleGroups.Count > 0) _volumeCycleGroups[index].enabled.Override(enabled);
        else _cycleGroups[index].enabled = enabled;
    }

    private void ResolveSceneBloom()
    {
        if (_sceneBloom != null) return;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var volume in root.GetComponentsInChildren<Volume>())
                if (volume.isActiveAndEnabled && volume.sharedProfile != null && volume.sharedProfile.Has<LayerBloomVolume>())
                {
                    // Volume.profile creates a scene-instance copy; never animate the shared asset.
                    volume.profile.TryGet(out _sceneBloom);
                    return;
                }
    }

    private void StopBloomCycle()
    {
        if (CycleGroupCount == 0)
            return;

        // Renderer-feature settings are assets: restore them when leaving Play mode
        // or disabling this component so the demo never leaves a group switched off.
        foreach (var group in _cycleGroups)
            group.enabled = true;
        for (int i = 0; i < _volumeCycleGroups.Count; i++)
        {
            _volumeCycleGroups[i].enabled.value = true;
            _volumeCycleGroups[i].enabled.overrideState = _volumeEnabledOverrides[i];
        }
        _volumeCycleGroups.Clear();
        _volumeEnabledOverrides.Clear();
        _cycleGroups.Clear();
        _disabledBloomGroup = -1;
        _bloomCycleElapsed = 0f;
    }

}
