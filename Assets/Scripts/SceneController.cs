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

    private readonly List<CubeMotion> _motions = new List<CubeMotion>();

    private sealed class CubeMotion
    {
        public Transform Cube;
        public Vector3 Direction;
        public float MoveSpeed;
        public Vector3 RotationAxis;
        public float RotationSpeed;
    }

    private void Start()
    {
        if (_cubes == null)
            return;

        var initializedCubes = new HashSet<Transform>();
        foreach (Transform cube in _cubes)
        {
            if (cube == null || cube == _pivot || cube == transform || !initializedCubes.Add(cube))
                continue;

            _motions.Add(new CubeMotion
            {
                Cube = cube,
                Direction = Random.onUnitSphere,
                MoveSpeed = RandomSpeed(_moveSpeedMin, _moveSpeedMax),
                RotationAxis = Random.onUnitSphere,
                RotationSpeed = RandomSpeed(_rotspeedMin, _rotspeedMax)
            });
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
            Vector3 position = motion.Cube.position + motion.Direction * (speed * deltaTime);
            Vector3 offset = position - center;

            if (radius == 0f)
            {
                position = center;
            }
            else if (offset.sqrMagnitude > radius * radius)
            {
                Vector3 normal = offset.normalized;
                position = center + normal * radius;

                // Only bounce outward motion, including when the pivot has moved.
                if (Vector3.Dot(motion.Direction, normal) > 0f)
                    motion.Direction = Vector3.Reflect(motion.Direction, normal);
            }

            motion.Cube.position = position;
            motion.Cube.Rotate(motion.RotationAxis, motion.RotationSpeed * deltaTime, Space.World);
        }
    }

    private static float RandomSpeed(float minimum, float maximum)
    {
        float lower = Mathf.Max(0f, Mathf.Min(minimum, maximum));
        float upper = Mathf.Max(lower, Mathf.Max(minimum, maximum));
        return Random.Range(lower, upper);
    }
}
