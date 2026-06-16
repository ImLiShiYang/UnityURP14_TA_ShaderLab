using UnityEngine;

public class CameraControlModeToggle : MonoBehaviour
{
    [Header("References")]
    public Camera controlledCamera;
    public ThirdPersonCameraController thirdPersonCamera;
    public ThirdPersonPlayerController playerController;
    public PlayerAttack playerAttack;

    [Header("Toggle")]
    public KeyCode toggleKey = KeyCode.F1;

    [Header("Free Camera")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 3f;
    public float mouseSensitivity = 2f;
    public float scrollSpeed = 4f;

    private bool _freeCameraMode;
    private bool _thirdPersonCameraWasEnabled;
    private bool _playerControllerWasEnabled;
    private bool _playerAttackWasEnabled;
    private float _yaw;
    private float _pitch;

    private void Awake()
    {
        ResolveReferences();
        CacheEnabledStates();
        SyncFreeCameraAngles();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMode();
        }

        if (_freeCameraMode)
        {
            UpdateFreeCamera();
        }
    }

    public void ToggleMode()
    {
        SetFreeCameraMode(!_freeCameraMode);
    }

    public void SetFreeCameraMode(bool freeCameraMode)
    {
        _freeCameraMode = freeCameraMode;

        if (_freeCameraMode)
        {
            SyncFreeCameraAngles();
        }

        if (thirdPersonCamera != null)
            thirdPersonCamera.enabled = !_freeCameraMode && _thirdPersonCameraWasEnabled;

        if (playerController != null)
            playerController.enabled = !_freeCameraMode && _playerControllerWasEnabled;

        if (playerAttack != null)
            playerAttack.enabled = !_freeCameraMode && _playerAttackWasEnabled;

        ApplyCursorState();
    }

    private void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();

        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (thirdPersonCamera == null && controlledCamera != null)
            thirdPersonCamera = controlledCamera.GetComponent<ThirdPersonCameraController>();

        if (playerController == null)
            playerController = FindObjectOfType<ThirdPersonPlayerController>();

        if (playerAttack == null && playerController != null)
            playerAttack = playerController.GetComponent<PlayerAttack>();

        if (playerAttack == null)
            playerAttack = FindObjectOfType<PlayerAttack>();
    }

    private void CacheEnabledStates()
    {
        _thirdPersonCameraWasEnabled = thirdPersonCamera == null || thirdPersonCamera.enabled;
        _playerControllerWasEnabled = playerController == null || playerController.enabled;
        _playerAttackWasEnabled = playerAttack == null || playerAttack.enabled;
    }

    private void SyncFreeCameraAngles()
    {
        Transform cameraTransform = GetCameraTransform();
        if (cameraTransform == null)
            return;

        Vector3 angles = cameraTransform.eulerAngles;
        _yaw = angles.y;
        _pitch = NormalizePitch(angles.x);
    }

    private void UpdateFreeCamera()
    {
        Transform cameraTransform = GetCameraTransform();
        if (cameraTransform == null)
            return;

        bool rotating = Input.GetMouseButton(1);
        if (rotating)
        {
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            cameraTransform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        ApplyCursorState();

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float upDown = 0f;

        if (Input.GetKey(KeyCode.E))
            upDown += 1f;

        if (Input.GetKey(KeyCode.Q))
            upDown -= 1f;

        Vector3 movement =
            cameraTransform.right * horizontal +
            cameraTransform.forward * vertical +
            Vector3.up * upDown;

        if (movement.sqrMagnitude > 1f)
            movement.Normalize();

        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            speed *= sprintMultiplier;

        speed += Input.GetAxis("Mouse ScrollWheel") * scrollSpeed;
        speed = Mathf.Max(0.1f, speed);

        cameraTransform.position += movement * speed * Time.deltaTime;
    }

    private Transform GetCameraTransform()
    {
        return controlledCamera != null ? controlledCamera.transform : transform;
    }

    private void ApplyCursorState()
    {
        if (_freeCameraMode)
        {
            bool rotating = Input.GetMouseButton(1);
            Cursor.lockState = rotating ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !rotating;
            return;
        }

        bool shouldLock = thirdPersonCamera != null && thirdPersonCamera.lockCursor;
        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }

    private float NormalizePitch(float pitch)
    {
        if (pitch > 180f)
            pitch -= 360f;

        return pitch;
    }
}
