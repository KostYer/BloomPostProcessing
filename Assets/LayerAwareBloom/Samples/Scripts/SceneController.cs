using System.Collections.Generic;
using LayerAwareBloom;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public class SceneController : MonoBehaviour
{
    private const float LabelFadeDuration = 0.15f;
    [SerializeField] private Transform _pivot;
    [Tooltip("Movement roots. Only each root's child mesh rotates.")]
    [SerializeField] private List<Transform> _cubes = new List<Transform>();
    [SerializeField, Min(0f)] private float _moveSpeedMin = 0.5f;
    [SerializeField, Min(0f)] private float _moveSpeedMax = 2f;
    [SerializeField, Min(0f)] private float _rotspeedMin = 15f;
    [SerializeField, Min(0f)] private float _rotspeedMax = 90f;
    [Tooltip("Overall movement speed cap in units per second. Zero disables the cap.")]
    [SerializeField, Min(0f)] private float _movementspeedMax = 3f;
    [Tooltip("Maximum distance of each cube's center from the pivot in world units.")]
    [SerializeField, Min(0f)] private float _maxDistance = 10f;

    [Tooltip("Camera to orbit around the pivot. Uses the Main Camera when unassigned.")]
    [SerializeField] private Camera _orbitCamera;
    [Tooltip("Camera orbit speed in degrees per second. Negative values reverse direction.")]
    [SerializeField] private float _cameraOrbitSpeed = 10f;

    [Header("Bloom status labels")]
    [Tooltip("Bloom feature whose settings drive the sample's no-bloom labels.")]
    [SerializeField] private RenderingLayerBloomFeature _bloomFeature;

    private readonly List<CubeMotion> _motions = new List<CubeMotion>();
    private Vector3 _cameraStartOffset;
    private float _cameraOrbitAngle;
    private bool _cameraRotating = true;
    private LayerBloomVolume _sceneBloom;

    private sealed class CubeMotion
    {
        public Transform Cube;
        public Transform Mesh;
        public MeshRenderer Renderer;
        public BloomLabel[] Labels;
        public Vector3 OrbitRight;
        public Vector3 OrbitForward;
        public float OrbitRadius;
        public float OrbitAngle;
        public float MoveSpeed;
        public Vector3 RotationAxis;
        public float RotationSpeed;
    }

    private sealed class BloomLabel
    {
        public TMP_Text Text;
        public CanvasGroup Group;
        public bool OriginalEnabled, OriginalActive;
        public float OriginalAlpha;
        public float StartAlpha, TargetAlpha;
        public double FadeStartedAt;

        public void ResetFade()
        {
            if (Group == null) return;
            // Original visibility is only for restoration, never the start of a fade-in.
            Group.alpha = 0f;
            StartAlpha = TargetAlpha = 0f;
            FadeStartedAt = Time.timeAsDouble;
        }
    }

    private void Start()
    {
        Vector3 center = _pivot != null ? _pivot.position : transform.position;
        if (_orbitCamera == null)
            _orbitCamera = Camera.main;

        if (_orbitCamera != null)
            _cameraStartOffset = _orbitCamera.transform.position - center;

        if (_cubes == null)
            return;

        float radius = Mathf.Max(0f, _maxDistance);
        var initializedCubes = new HashSet<Transform>();
        foreach (Transform cube in _cubes)
        {
            if (cube == null || cube == _pivot || cube == transform || !initializedCubes.Add(cube))
                continue;

            // A random plane and a volume-weighted radius distribute cubes inside the sphere.
            Quaternion orbitRotation = Random.rotation;
            MeshRenderer meshRenderer = cube.GetComponentInChildren<MeshRenderer>(true);
            var motion = new CubeMotion
            {
                Cube = cube,
                Mesh = meshRenderer != null && meshRenderer.transform != cube
                    ? meshRenderer.transform : null,
                Renderer = meshRenderer,
                OrbitRight = orbitRotation * Vector3.right,
                OrbitForward = orbitRotation * Vector3.forward,
                OrbitRadius = radius * Mathf.Pow(Random.value, 1f / 3f),
                MoveSpeed = RandomSpeed(_moveSpeedMin, _moveSpeedMax),
                RotationAxis = Random.onUnitSphere,
                RotationSpeed = RandomSpeed(_rotspeedMin, _rotspeedMax)
            };
            var texts = cube.GetComponentsInChildren<TMP_Text>(true);
            motion.Labels = new BloomLabel[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                var group = texts[i].GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = texts[i].gameObject.AddComponent<CanvasGroup>();
                    group.interactable = false;
                    group.blocksRaycasts = false;
                }
                motion.Labels[i] = new BloomLabel {
                    Text = texts[i], Group = group, OriginalAlpha = group.alpha,
                    OriginalEnabled = texts[i].enabled, OriginalActive = texts[i].gameObject.activeSelf
                };
                motion.Labels[i].ResetFade();
            }
            cube.position = center + motion.OrbitRight * motion.OrbitRadius;
            _motions.Add(motion);
        }
    }

    private void Update()
    {
        Vector3 center = _pivot != null ? _pivot.position : transform.position;
        float radius = Mathf.Max(0f, _maxDistance);
        float deltaTime = Time.deltaTime;

        foreach (CubeMotion motion in _motions)
        {
            if (motion.Cube == null)
                continue;

            float speed = _movementspeedMax > 0f
                ? Mathf.Min(motion.MoveSpeed, _movementspeedMax)
                : motion.MoveSpeed;
            float orbitRadius = Mathf.Min(motion.OrbitRadius, radius);

            // Angular speed = linear speed / radius, so each cube travels at a constant speed.
            if (orbitRadius > 0f)
                motion.OrbitAngle = Mathf.Repeat(
                    motion.OrbitAngle + speed / orbitRadius * deltaTime, Mathf.PI * 2f);

            Vector3 orbitDirection = motion.OrbitRight * Mathf.Cos(motion.OrbitAngle)
                + motion.OrbitForward * Mathf.Sin(motion.OrbitAngle);
            motion.Cube.position = center + orbitDirection * orbitRadius;
            if (motion.Mesh != null)
                motion.Mesh.Rotate(motion.RotationAxis, motion.RotationSpeed * deltaTime, Space.World);
        }
    }

    private void OnEnable()
    {
        // Start handles the first enable, after the cube references are cached.
        if (_motions.Count > 0)
        {
            foreach (var motion in _motions)
                foreach (var label in motion.Labels)
                    label.ResetFade();
        }
    }

    private void OnDisable()
    {
        foreach (CubeMotion motion in _motions)
            foreach (var label in motion.Labels)
                if (label.Text != null)
                {
                    if (label.Group != null) label.Group.alpha = label.OriginalAlpha;
                    label.Text.enabled = label.OriginalEnabled;
                    label.Text.gameObject.SetActive(label.OriginalActive);
                }
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

    private void UpdateBloomLabels()
    {
        if (_bloomFeature == null)
            return;

        uint enabledLayers = 0;
        if (_bloomFeature.isActive)
        {
            if (_bloomFeature.SettingsSource == BloomSettingsSource.SceneVolumes)
            {
                ResolveSceneBloom();
                if (_sceneBloom != null && _sceneBloom.enableBloom.value)
                    for (int i = 0; i < RenderingLayerBloomFeature.GroupCount; i++)
                    {
                        var group = _sceneBloom.GetGroup(i);
                        if (group.enabled.value && group.intensity.value > 0) enabledLayers |= (uint)group.renderingLayers.value;
                    }
            }
            else
                foreach (var group in _bloomFeature.Groups)
                    if (group.enabled && group.intensity > 0f)
                        enabledLayers |= (uint)group.renderingLayers;
        }

        foreach (CubeMotion motion in _motions)
        {
            if (motion.Renderer == null)
                continue;

            uint layers = motion.Renderer.renderingLayerMask;
            // Read the actual mesh mask each frame, not the cube's list index or
            // the last cycle selection. Any enabled matching group supplies bloom.
            bool noBloom = (layers & enabledLayers) == 0;
            foreach (var label in motion.Labels)
                if (label.Text != null && label.Group != null)
                {
                    // Keep the text active through both fades; visibility comes from alpha.
                    label.Text.enabled = true;
                    if (!label.Text.gameObject.activeSelf)
                        label.Text.gameObject.SetActive(true);
                    float target = noBloom ? 1f : 0f;
                    if (label.TargetAlpha != target)
                    {
                        label.StartAlpha = label.Group.alpha;
                        label.TargetAlpha = target;
                        label.FadeStartedAt = Time.timeAsDouble;
                    }
                    // Start at t=0 on the change frame. Its deltaTime belongs to the
                    // previous state and must not consume the beginning of this fade.
                    float progress = (float)((Time.timeAsDouble - label.FadeStartedAt) / LabelFadeDuration);
                    label.Group.alpha = Mathf.Lerp(label.StartAlpha, label.TargetAlpha,
                        Mathf.Clamp01(progress));
                }
        }
    }

    private void LateUpdate()
    {
        UpdateBloomLabels();

        if (_orbitCamera == null || _cameraStartOffset.sqrMagnitude < 0.000001f)
            return;

        Transform cameraTransform = _orbitCamera.transform;
        if (cameraTransform == _pivot || cameraTransform == transform)
            return;

        Vector3 center = _pivot != null ? _pivot.position : transform.position;
        if (_cameraRotating)
            _cameraOrbitAngle = Mathf.Repeat(_cameraOrbitAngle + _cameraOrbitSpeed * Time.deltaTime, 360f);
        Vector3 offset = Quaternion.AngleAxis(_cameraOrbitAngle, Vector3.up) * _cameraStartOffset;
        cameraTransform.position = center + offset;

        // Keep the pivot in view while preserving the starting orbit radius and height.
        Vector3 up = Mathf.Abs(Vector3.Dot(offset.normalized, Vector3.up)) > 0.999f
            ? Vector3.forward
            : Vector3.up;
        cameraTransform.LookAt(center, up);
    }

    private void OnGUI()
    {
        if (_orbitCamera == null)
            return;

        string label = _cameraRotating ? "Stop Rotation" : "Rotate";
        if (GUI.Button(new Rect(16f, 16f, 160f, 40f), label))
            _cameraRotating = !_cameraRotating;
    }

    private static float RandomSpeed(float minimum, float maximum)
    {
        float lower = Mathf.Max(0f, Mathf.Min(minimum, maximum));
        float upper = Mathf.Max(lower, Mathf.Max(minimum, maximum));
        return Random.Range(lower, upper);
    }
}
