using UnityEngine;

public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Camera")]
    public float distance = 4f;
    public float mouseSensitivity = 3f;
    public float smoothTime = 0.05f;

    [Header("Pitch Limit")]
    public float minPitch = -25f;
    public float maxPitch = 65f;

    [Header("Cursor")]
    public bool lockCursor = true;

    private float _yaw;
    private float _pitch;
    private Vector3 _smoothVelocity;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;

        _yaw = angles.y;
        _pitch = angles.x;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        _yaw += mouseX;
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        Quaternion cameraRotation = Quaternion.Euler(_pitch, _yaw, 0f);

        Vector3 lookTarget = target.position + targetOffset;
        Vector3 desiredPosition = lookTarget - cameraRotation * Vector3.forward * distance;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _smoothVelocity,
            smoothTime
        );

        transform.rotation = cameraRotation;
    }
}