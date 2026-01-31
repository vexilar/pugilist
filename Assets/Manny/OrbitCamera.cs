using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Camera (child of Player). Uses the Look action to orbit the camera
/// around the player on the XZ plane at a fixed height, limited to a semi-circle
/// (cannot rotate past the left or right side view, ±90° from behind).
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Same Input Action asset used by PlayerController (e.g. InputSystem_Actions). Uses Player/Look.")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Orbit")]
    [Tooltip("Horizontal distance from player (XZ radius).")]
    [SerializeField] private float orbitRadius = 8f;
    [Tooltip("Local Y offset above the player (height of camera).")]
    [SerializeField] private float heightOffset = 3f;
    [Tooltip("Orbit angle limits: ±90 = from behind to left/right side. Negative = left, positive = right.")]
    [SerializeField] private float minOrbitAngle = -90f;
    [SerializeField] private float maxOrbitAngle = 90f;

    [Header("Sensitivity")]
    [Tooltip("Degrees per pixel (mouse).")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [Tooltip("Degrees per second at full stick (gamepad).")]
    [SerializeField] private float stickSensitivity = 90f;
    [Tooltip("Threshold: |Look.x| above this is treated as mouse delta, else gamepad.")]
    [SerializeField] private float mouseVsStickThreshold = 2f;

    [Header("Look At")]
    [Tooltip("Local Y offset of the point the camera looks at (above player origin).")]
    [SerializeField] private float lookAtHeightOffset = 1.5f;

    private Transform _player;
    private InputAction _lookAction;
    private float _orbitAngle;

    private void Awake()
    {
        _player = transform.parent;
        if (_player == null)
            Debug.LogWarning("OrbitCamera: no parent found; assign a parent Player.");

        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            _lookAction = playerMap?.FindAction("Look");
        }

        InitializeOrbitFromCurrentPosition();
    }

    private void InitializeOrbitFromCurrentPosition()
    {
        if (_player == null) return;

        Vector3 local = transform.localPosition;
        float x = local.x;
        float z = local.z;
        if (Mathf.Abs(x) < 0.001f && Mathf.Abs(z) < 0.001f)
            _orbitAngle = 0f;
        else
            _orbitAngle = Mathf.Atan2(x, -z) * Mathf.Rad2Deg;

        _orbitAngle = Mathf.Clamp(_orbitAngle, minOrbitAngle, maxOrbitAngle);
    }

    private void OnEnable()
    {
        _lookAction?.Enable();
    }

    private void OnDisable()
    {
        _lookAction?.Disable();
    }

    private void Update()
    {
        if (_player == null) return;

        float lookX = _lookAction != null ? _lookAction.ReadValue<Vector2>().x : 0f;

        float delta;
        if (Mathf.Abs(lookX) > mouseVsStickThreshold)
            delta = lookX * mouseSensitivity;
        else
            delta = lookX * stickSensitivity * Time.deltaTime;

        _orbitAngle = Mathf.Clamp(_orbitAngle + delta, minOrbitAngle, maxOrbitAngle);

        ApplyOrbit();
    }

    private void ApplyOrbit()
    {
        float rad = _orbitAngle * Mathf.Deg2Rad;
        float x = Mathf.Sin(rad) * orbitRadius;
        float z = -Mathf.Cos(rad) * orbitRadius;

        transform.localPosition = new Vector3(x, heightOffset, z);

        Vector3 lookAtLocal = new Vector3(0f, lookAtHeightOffset, 0f);
        Vector3 lookAtWorld = _player.TransformPoint(lookAtLocal);
        transform.LookAt(lookAtWorld);
    }
}
