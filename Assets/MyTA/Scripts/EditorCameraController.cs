using UnityEngine;

[RequireComponent(typeof(Camera))]
public class EditorCameraController : MonoBehaviour
{
    [Header("飞行速度")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 2f;
    public float scrollSpeed = 10f;

    [Header("Shift 加速倍率")]
    public float speedMultiplier = 3f;

    private float _pitch;
    private float _yaw;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        _pitch = angles.x;
        _yaw = angles.y;
    }

    void Update()
    {
        // 鼠标右键按住：旋转视角
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _yaw += Input.GetAxis("Mouse X") * rotateSpeed;
            _pitch -= Input.GetAxis("Mouse Y") * rotateSpeed;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);

            transform.eulerAngles = new Vector3(_pitch, _yaw, 0);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 输入控制
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        float upDown = 0;

        // QE 上下
        if (Input.GetKey(KeyCode.E)) upDown = 1;
        if (Input.GetKey(KeyCode.Q)) upDown = -1;

        // 移动方向
        Vector3 dir = transform.right * h + transform.forward * v + transform.up * upDown;
        if (dir.magnitude > 1f)
            dir.Normalize();

        // 速度
        float finalSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            finalSpeed *= speedMultiplier;

        // 滚轮调整速度
        finalSpeed += Input.GetAxis("Mouse ScrollWheel") * scrollSpeed;
        finalSpeed = Mathf.Max(finalSpeed, 0.5f);

        // 移动
        transform.Translate(dir * finalSpeed * Time.deltaTime, Space.World);
    }
}