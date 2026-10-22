using System.Collections.Generic;
using UnityEngine;

public class SceneController : MonoBehaviour
{
    [SerializeField] private Transform _pivot;
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

    private readonly List<CubeMotion> _motions = new List<CubeMotion>();
    private Vector3 _cameraStartOffset;
    private float _cameraOrbitAngle;
    private bool _cameraRotating = true;

    private sealed class CubeMotion
    {
        public Transform Cube;
        public Vector3 OrbitRight;
        public Vector3 OrbitForward;
        public float OrbitRadius;
        public float OrbitAngle;
        public float MoveSpeed;
        public Vector3 RotationAxis;
        public float RotationSpeed;
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
            var motion = new CubeMotion
            {
                Cube = cube,
                OrbitRight = orbitRotation * Vector3.right,
                OrbitForward = orbitRotation * Vector3.forward,
                OrbitRadius = radius * Mathf.Pow(Random.value, 1f / 3f),
                MoveSpeed = RandomSpeed(_moveSpeedMin, _moveSpeedMax),
                RotationAxis = Random.onUnitSphere,
                RotationSpeed = RandomSpeed(_rotspeedMin, _rotspeedMax)
            };
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
            motion.Cube.Rotate(motion.RotationAxis, motion.RotationSpeed * deltaTime, Space.World);
        }
    }

    private void LateUpdate()
    {
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
