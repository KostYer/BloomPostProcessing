using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class CubeLabelBillboard : MonoBehaviour
{
    [Tooltip("Label to face the camera. Finds a child TMP text when unassigned.")]
    [SerializeField] private TMP_Text _label;
    [Tooltip("Camera to face. Uses the Main Camera when unassigned.")]
    [SerializeField] private Camera _camera;
    [Tooltip("Fixed world-space height above the cube's movement root.")]
    [SerializeField, Min(0f)] private float _heightOffset = 0.8f;

    private void Awake()
    {
        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);
    }

    private void LateUpdate()
    {
        if (_label == null)
            return;

        // The root moves while its mesh child spins. Anchor to the root rather
        // than the mesh bounds, whose height changes as the mesh rotates.
        _label.transform.position = transform.position + Vector3.up * _heightOffset;

        Camera targetCamera = _camera != null ? _camera : Camera.main;
        if (targetCamera == null)
            return;

        // Run after SceneController's camera orbit. TMP's readable side faces
        // local -Z. Matching the camera keeps the baseline horizontal on screen
        // and the readable side facing the viewer, independent of cube rotation.
        _label.transform.rotation = targetCamera.transform.rotation;
    }
}
