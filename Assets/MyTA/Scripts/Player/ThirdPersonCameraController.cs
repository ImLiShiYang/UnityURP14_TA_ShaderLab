using UnityEngine;

/// <summary>
/// 第三人称相机控制器。
///
/// 这个脚本的作用：
/// 1. 让相机围绕 target 旋转。
/// 2. 鼠标左右控制水平旋转 yaw。
/// 3. 鼠标上下控制俯仰角 pitch。
/// 4. 相机始终看向 target.position + targetOffset。
/// 5. 相机和目标保持一定 distance。
///
/// 重点参数：
/// - initialPitch：初始俯视角。
/// - minPitch / maxPitch：允许玩家用鼠标调整的俯仰角范围。
/// - targetOffset.y：相机看向角色的高度。
/// - distance：相机离目标的距离。
/// </summary>
public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]

    /// <summary>
    /// 相机跟随的目标。
    /// 一般绑定玩家角色 Transform。
    /// </summary>
    public Transform target;

    /// <summary>
    /// 相机实际看向的位置偏移。
    ///
    /// 相机不是直接看 target.position，
    /// 而是看 target.position + targetOffset。
    ///
    /// 例如：
    /// targetOffset = (0, 0.8, 0)
    /// 表示相机看向角色脚底往上 0.8 米的位置。
    ///
    /// y 越大：
    /// - 相机看向角色更高的位置
    /// - 画面更偏向角色上半身
    ///
    /// y 越小：
    /// - 相机看向角色更低的位置
    /// - 更容易看到地面和脚印
    ///
    /// 注意：
    /// 这个参数不是“俯视角”，
    /// 但它会影响镜头看起来高不高。
    /// </summary>
    public Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

    [Header("Camera")]

    /// <summary>
    /// 相机距离目标点的距离。
    ///
    /// 值越大：
    /// - 相机离角色越远
    /// - 看到的范围越大
    ///
    /// 值越小：
    /// - 相机离角色越近
    /// - 角色和脚印看起来更大
    ///
    /// 如果你想更近地观察脚印，可以适当减小这个值。
    /// </summary>
    public float distance = 3.0f;

    /// <summary>
    /// 鼠标灵敏度。
    ///
    /// 值越大，鼠标移动一点，相机旋转越多。
    /// </summary>
    public float mouseSensitivity = 3f;

    /// <summary>
    /// 相机位置平滑时间。
    ///
    /// 值越小：
    /// - 相机跟随更快
    /// - 但可能更硬
    ///
    /// 值越大：
    /// - 相机移动更平滑
    /// - 但会有一点延迟感
    /// </summary>
    public float smoothTime = 0.05f;

    [Header("Initial View")]

    /// <summary>
    /// 是否使用自定义初始视角。
    ///
    /// true：
    /// - 使用 initialYaw 和 initialPitch 作为开局视角。
    ///
    /// false：
    /// - 使用相机当前 Transform 的旋转角度作为开局视角。
    /// </summary>
    public bool useInitialView = true;

    /// <summary>
    /// 初始水平旋转角。
    ///
    /// 控制游戏开始时相机绕角色的水平朝向。
    ///
    /// 例如：
    /// 0   = 看向世界 Z 方向
    /// 90  = 从侧面看
    /// 180 = 从反方向看
    ///
    /// 一般你可以先保持 0，
    /// 让玩家进入游戏后用鼠标自己转。
    /// </summary>
    public float initialYaw = 0f;

    /// <summary>
    /// 初始俯视角。
    ///
    /// 这是控制“开局镜头俯视程度”的主要参数。
    ///
    /// 数值越大：
    /// - 相机越从上往下看
    /// - 越容易看到地面
    /// - 但太大会变成俯视游戏视角
    ///
    /// 数值越小：
    /// - 相机越接近平视
    /// - 更像常规第三人称视角
    ///
    /// 推荐范围：
    /// 10 ~ 18：比较平视
    /// 18 ~ 28：普通第三人称
    /// 28 ~ 40：明显俯视
    ///
    /// 你觉得当前镜头太高、太俯视，
    /// 就优先降低这个值。
    /// </summary>
    public float initialPitch = 18f;

    [Header("Pitch Limit")]

    /// <summary>
    /// 最小俯仰角。
    ///
    /// 控制玩家最多能把镜头往上抬到什么程度。
    ///
    /// 负数表示可以稍微从下往上看。
    ///
    /// 例如：
    /// -10：允许轻微仰视
    /// 0：不允许仰视，只能平视或俯视
    /// </summary>
    public float minPitch = -10f;

    /// <summary>
    /// 最大俯仰角。
    ///
    /// 这是限制“玩家最多能把镜头调得多俯视”的参数。
    ///
    /// 数值越大：
    /// - 鼠标往下拉时，相机越能从高处往下看
    ///
    /// 数值越小：
    /// - 相机会更接近平视
    /// - 不容易变成很高的俯视视角
    ///
    /// 如果你觉得镜头太高、太俯视，
    /// 重点降低这个值。
    ///
    /// 推荐：
    /// 25 ~ 35：普通第三人称
    /// 35 ~ 50：偏俯视
    /// 60+：很容易变成俯视视角
    /// </summary>
    public float maxPitch = 35f;

    [Header("Cursor")]

    /// <summary>
    /// 是否锁定鼠标。
    ///
    /// true：
    /// - 鼠标隐藏并锁定在游戏窗口中间。
    /// - 适合正式游玩。
    ///
    /// false：
    /// - 鼠标可见。
    /// - 适合调试 UI。
    /// </summary>
    public bool lockCursor = true;

    /// <summary>
    /// 当前相机的水平旋转角。
    /// 鼠标左右移动会改变这个值。
    /// </summary>
    private float _yaw;

    /// <summary>
    /// 当前相机的俯仰角。
    ///
    /// 这是运行时真正控制俯视角的变量。
    /// 鼠标上下移动会改变它。
    ///
    /// 但它会被限制在：
    /// minPitch ~ maxPitch
    /// </summary>
    private float _pitch;

    /// <summary>
    /// SmoothDamp 使用的速度缓存。
    /// 用来让相机位置平滑移动。
    /// </summary>
    private Vector3 _smoothVelocity;

    private void Start()
    {
        if (useInitialView)
        {
            // 使用 Inspector 中设置的初始水平角和俯视角。
            _yaw = initialYaw;

            // 初始俯视角也要限制在 minPitch ~ maxPitch 之间，
            // 避免 Inspector 填了一个超出范围的值。
            _pitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        }
        else
        {
            // 不使用自定义初始视角时，
            // 读取当前相机 Transform 的旋转作为初始角度。
            Vector3 angles = transform.eulerAngles;

            _yaw = angles.y;

            // Unity 的 eulerAngles.x 是 0~360。
            // 例如 -10 度会显示成 350。
            // 所以这里要转换成 -180~180 的形式。
            _pitch = NormalizePitch(angles.x);

            // 限制到允许范围。
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

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

        // 获取鼠标输入。
        //
        // Mouse X：
        // 控制左右旋转，也就是 yaw。
        //
        // Mouse Y：
        // 控制上下旋转，也就是 pitch。
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // 鼠标左右移动，改变水平旋转。
        _yaw += mouseX;

        // 鼠标上下移动，改变俯仰角。
        //
        // 这里用 -= 是常见第三人称操作：
        // 鼠标往上推，镜头往上看；
        // 鼠标往下拉，镜头往下看。
        _pitch -= mouseY;

        // 限制俯仰角，防止相机翻转，
        // 也防止玩家把镜头拉得过于俯视。
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // 根据当前 yaw / pitch 生成相机旋转。
        Quaternion cameraRotation = Quaternion.Euler(_pitch, _yaw, 0f);

        // 计算相机要看向的目标点。
        //
        // 不是直接看 target.position，
        // 而是看 target.position + targetOffset。
        Vector3 lookTarget = target.position + targetOffset;

        // 计算相机期望位置。
        //
        // cameraRotation * Vector3.forward：
        // 表示相机当前朝向的前方。
        //
        // lookTarget - forward * distance：
        // 表示让相机站在目标点后方 distance 的位置。
        Vector3 desiredPosition = lookTarget - cameraRotation * Vector3.forward * distance;

        // 平滑移动到期望位置。
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref _smoothVelocity,
            smoothTime
        );

        // 应用相机旋转。
        transform.rotation = cameraRotation;
    }

    /// <summary>
    /// 把 Unity 的 0~360 欧拉角转换成 -180~180。
    ///
    /// 例如：
    /// 350 度会转换成 -10 度。
    ///
    /// 这样 minPitch / maxPitch 才好理解。
    /// </summary>
    private float NormalizePitch(float pitch)
    {
        if (pitch > 180f)
            pitch -= 360f;

        return pitch;
    }
}