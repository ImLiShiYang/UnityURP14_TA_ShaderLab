using UnityEngine;

/// <summary>
/// 第三人称玩家控制器。
/// 
/// 该脚本负责处理玩家角色的移动、转向、冲刺以及重力效果。
/// 需要挂载在带有 CharacterController 组件的角色对象上。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ThirdPersonPlayerController : MonoBehaviour
{
    [Header("Animation")]
    public Animator animator;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    
    public bool HasMoveInput { get; private set; }
    
    [Header("References")]
    // 摄像机 Transform，用于根据摄像机朝向决定角色移动方向
    public Transform cameraTransform;

    [Header("Movement")]
    // 普通移动速度
    public float moveSpeed = 3.5f;

    // 按住 Shift 时的冲刺速度
    public float sprintSpeed = 5.5f;

    // 角色转向平滑时间，数值越小转向越快
    public float rotationSmoothTime = 0.08f;

    [Header("Gravity")]
    // 重力加速度，负数表示向下
    public float gravity = -20f;

    // 角色站在地面时，给一个轻微向下的力，防止角色离地抖动
    public float groundedStickForce = -2f;

    // 当前角色身上的 CharacterController 组件引用
    private CharacterController _controller;

    // Mathf.SmoothDampAngle 使用的转向速度缓存变量
    private float _turnSmoothVelocity;

    // 用来保存角色的垂直方向速度，主要用于重力计算
    private Vector3 _verticalVelocity;

    /// <summary>
    /// Unity 生命周期方法。
    /// 
    /// Awake 会在脚本实例加载时调用，通常用于初始化引用。
    /// 这里主要获取 CharacterController 组件，并在没有手动指定摄像机时自动使用主摄像机。
    /// </summary>
    private void Awake()
    {
        // 获取当前物体上的 CharacterController 组件
        _controller = GetComponent<CharacterController>();

        // 如果没有手动指定 cameraTransform，并且场景中存在 Main Camera，则自动绑定主摄像机
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
        
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    /// <summary>
    /// Unity 生命周期方法。
    /// 
    /// Update 会在每一帧调用一次。
    /// 这里每帧处理角色移动和重力。
    /// </summary>
    private void Update()
    {
        // 处理水平移动和角色转向
        Move();

        // 处理垂直方向的重力
        ApplyGravity();
        
        UpdateAnimation();
    }

    /// <summary>
    /// 处理玩家的移动和转向。
    /// 
    /// 根据键盘输入获取移动方向，并结合摄像机朝向转换为世界空间方向。
    /// 角色会平滑旋转到移动方向，并根据是否按住 Shift 使用普通速度或冲刺速度移动。
    /// </summary>
    private void Move()
    {
        // 获取横向输入，默认对应 A / D 或 左 / 右方向键
        float h = Input.GetAxisRaw("Horizontal");

        // 获取纵向输入，默认对应 W / S 或 上 / 下方向键
        float v = Input.GetAxisRaw("Vertical");

        // 将输入转换为一个方向向量
        // x 对应左右，z 对应前后，y 为 0 表示不处理上下移动
        Vector3 input = new Vector3(h, 0f, v).normalized;

        // 如果没有输入，或者没有摄像机引用，则不执行移动逻辑
        if (input.sqrMagnitude < 0.01f || cameraTransform == null)
            return;

        // 获取摄像机的前方向，并投影到水平面上
        // 这样可以避免摄像机上下倾斜时影响角色移动方向
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;

        // 获取摄像机的右方向，并投影到水平面上
        Vector3 cameraRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;

        // 根据摄像机朝向计算最终移动方向
        // input.z 控制前后，input.x 控制左右
        Vector3 moveDirection = cameraForward * input.z + cameraRight * input.x;

        // 归一化移动方向，防止斜向移动速度变快
        moveDirection.Normalize();

        // 根据移动方向计算目标旋转角度
        // Atan2 用于根据 x 和 z 方向得到角色应该朝向的 Y 轴角度
        float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;

        // 平滑计算当前角度到目标角度之间的过渡角度
        float smoothAngle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            targetAngle,
            ref _turnSmoothVelocity,
            rotationSmoothTime
        );

        // 应用平滑后的角色旋转
        transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);

        // 如果按住左 Shift，则使用冲刺速度，否则使用普通移动速度
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : moveSpeed;

        // 使用 CharacterController 移动角色
        // moveDirection 是方向，currentSpeed 是速度，Time.deltaTime 用于保证帧率无关
        _controller.Move(moveDirection * currentSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 处理角色的重力效果。
    /// 
    /// 当角色在地面上时，给角色一个轻微向下的速度，让角色稳定贴地。
    /// 当角色不在地面上时，持续累加重力，使角色向下掉落。
    /// </summary>
    private void ApplyGravity()
    {
        // 如果角色在地面上，并且当前垂直速度是向下的
        if (_controller.isGrounded && _verticalVelocity.y < 0f)
        {
            // 设置一个较小的向下速度，让角色稳定贴在地面上
            _verticalVelocity.y = groundedStickForce;
        }

        // 根据重力加速度持续改变垂直速度
        _verticalVelocity.y += gravity * Time.deltaTime;

        // 应用垂直方向移动
        _controller.Move(_verticalVelocity * Time.deltaTime);
    }
    
    
    /// <summary>
    /// 更新角色移动动画。
    /// 
    /// 根据玩家输入和是否按住 Shift，向 Animator 传递 MoveSpeed 参数。
    /// MoveSpeed 为 0 时播放 Idle，普通移动时播放 Walking，冲刺时播放 Running。
    /// </summary>
    private void UpdateAnimation()
    {
        if (animator == null)
            return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(h, 0f, v);

        HasMoveInput = input.sqrMagnitude > 0.01f;
        // bool isMoving = input.sqrMagnitude > 0.01f;
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        float animationSpeed = 0f;

        if (HasMoveInput)
        {
            animationSpeed = isSprinting ? 1f : 0.5f;
        }

        animator.SetFloat(MoveSpeedHash, animationSpeed, 0.1f, Time.deltaTime);
    }
}