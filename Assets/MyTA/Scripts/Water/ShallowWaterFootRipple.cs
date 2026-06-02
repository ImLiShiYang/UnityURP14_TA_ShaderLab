using UnityEngine;

/// <summary>
/// 浅水区脚步水波生成器。
///
/// 这个脚本专门负责“角色脚踩浅水时生成水波”。
///
/// 它不负责：
/// 1. 角色移动
/// 2. 脚印贴花
/// 3. 雪地压痕
/// 4. 水面波动模拟本身
///
/// 它只做一件事：
/// 当左脚 / 右脚动画事件触发时，
/// 检测脚下是否有水面，
/// 如果有，就调用 SurfaceWave.AddRippleWorld() 往水面写入一个扰动点。
///
/// 推荐配合 FootstepEventReceiver 使用：
/// Animation Event
/// → FootstepEventReceiver.SpawnLeftFootprint()
/// → ShallowWaterFootRipple.SpawnLeftWaterRipple()
/// </summary>
public class ShallowWaterFootRipple : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]

    [Tooltip("角色根节点，用来获取角色前进方向。一般是 Player 根物体。")]
    public Transform characterRoot;

    [Tooltip("角色 Animator。用于自动查找 Foot / Toes 骨骼，以及读取 MoveSpeed。")]
    public Animator animator;

    [Tooltip("玩家移动控制器。用于判断当前是否真的有移动输入。")]
    public ThirdPersonPlayerController playerController;

    [Tooltip("交互水面脚本，也就是挂 SurfaceWave 的水面对象。")]
    public SurfaceWave waterSurface;


    // ============================================================
    // Foot Bones
    // ============================================================

    [Header("Foot Bones")]

    [Tooltip("左脚 Foot 骨骼。Humanoid 模型通常可以通过 HumanBodyBones.LeftFoot 自动获取。")]
    public Transform leftFoot;

    [Tooltip("右脚 Foot 骨骼。Humanoid 模型通常可以通过 HumanBodyBones.RightFoot 自动获取。")]
    public Transform rightFoot;

    [Tooltip("左脚 Toes 骨骼。用于更准确地计算脚掌中心。")]
    public Transform leftToes;

    [Tooltip("右脚 Toes 骨骼。用于更准确地计算脚掌中心。")]
    public Transform rightToes;

    [Tooltip(
        "Foot 和 Toes 之间的插值比例。\n" +
        "0 = 使用 Foot 骨骼点。\n" +
        "1 = 使用 Toes 骨骼点。\n" +
        "0.5~0.7 通常更接近脚掌中心。\n\n" +
        "脚踝点通常偏后，脚趾点偏前，所以用 Lerp 取中间更适合生成水波。"
    )]
    [Range(0f, 1f)]
    public float toeBlend = 0.6f;


    // ============================================================
    // Water Raycast
    // ============================================================

    [Header("Water Raycast")]

    [Tooltip("水面 Layer。建议给水面单独建一个 Water 层，避免 Raycast 打到地面或角色自己。")]
    public LayerMask waterMask;

    [Tooltip(
        "Raycast 起点相对脚掌中心向上的高度。\n" +
        "例如脚掌中心在水面附近时，从脚掌上方 0.25 米开始向下检测水面。"
    )]
    [Min(0f)]
    public float rayStartHeight = 0.25f;

    [Tooltip(
        "从脚掌中心向下检测水面的距离。\n" +
        "最终检测距离 = rayStartHeight + rayDistance。\n\n" +
        "如果角色脚离水面稍远也想检测到，可以增大这个值。"
    )]
    [Min(0f)]
    public float rayDistance = 0.8f;

    [Tooltip(
        "脚掌中心高出水面的最大允许距离。\n" +
        "超过这个值就认为脚离水面太远，不生成水波。\n\n" +
        "这个参数可以避免脚抬起来时也在水面上打波纹。"
    )]
    [Min(0f)]
    public float maxFootHeightAboveWater = 0.18f;

    [Tooltip(
        "Raycast 是否检测 Trigger。\n" +
        "如果水面 MeshCollider 设置成 Is Trigger，需要使用 Collide。\n" +
        "如果水面 Collider 不是 Trigger，也可以保持 Collide。"
    )]
    public QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;


    // ============================================================
    // Ripple Placement
    // ============================================================

    [Header("Ripple Placement")]

    [Tooltip(
        "水波位置沿角色前方的偏移。\n" +
        "因为 Foot 骨骼通常在脚踝附近，水波直接生成在脚踝下方可能偏后。\n" +
        "加一点 forwardOffset 可以让波纹更靠近脚掌前半部分。"
    )]
    public float forwardOffset = 0.04f;


    // ============================================================
    // Ripple Strength
    // ============================================================

    [Header("Ripple Strength")]

    [Tooltip(
        "走路时水波强度。\n" +
        "1 = 白色波峰，向上扰动。\n" +
        "-1 = 黑色波谷，向下扰动。\n" +
        "浅水脚步一般用 0.25~0.45。"
    )]
    [Range(-1f, 1f)]
    public float walkStrength = 0.35f;

    [Tooltip(
        "跑步时水波强度。\n" +
        "跑步脚步更重，通常比走路强。"
    )]
    [Range(-1f, 1f)]
    public float runStrength = 0.55f;

    [Tooltip(
        "走路时水波半径。\n" +
        "单位不是世界单位，而是输入贴图像素半径。\n" +
        "值越大，写入 _InputTex 的扰动区域越大。"
    )]
    [Min(1)]
    public int walkRadius = 2;

    [Tooltip("跑步时水波半径。跑步通常可以比走路稍大。")]
    [Min(1)]
    public int runRadius = 3;

    [Tooltip(
        "Animator MoveSpeed 大于该值时认为角色在跑步。\n" +
        "你的 Blend Tree 里通常：\n" +
        "0 = Idle\n" +
        "0.5 = Walk\n" +
        "1 = Run\n" +
        "所以 0.75 可以作为 Walk / Run 的分界。"
    )]
    public float runMoveSpeedThreshold = 0.75f;


    // ============================================================
    // Spawn Conditions
    // ============================================================

    [Header("Spawn Conditions")]

    [Tooltip(
        "Animator MoveSpeed 小于该值时不生成水波。\n" +
        "用于避免角色 Idle 或动画混合到接近静止时还生成水波。"
    )]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip(
        "同一只脚生成水波的最小间隔。\n" +
        "用于防止动画事件重复触发，或者动画 Blend Tree 切换时同一只脚短时间内重复打水波。"
    )]
    public float minTimeBetweenSameFoot = 0.12f;


    // ============================================================
    // Debug
    // ============================================================

    [Header("Debug")]

    [Tooltip("是否打印调试日志。")]
    public bool logDebug = false;

    [Tooltip("是否在 Scene View 中绘制最近一次水面检测射线。")]
    public bool drawDebugGizmos = true;


    // ============================================================
    // Runtime State
    // ============================================================

    // 上一次左脚生成水波的时间。
    // 初始给一个很小的值，保证第一次可以正常触发。
    private float _lastLeftRippleTime = -999f;

    // 上一次右脚生成水波的时间。
    private float _lastRightRippleTime = -999f;

    // 以下变量用于 Gizmos 调试显示最近一次 Raycast。
    private Vector3 _lastRayOrigin;
    private Vector3 _lastRayEnd;
    private Vector3 _lastWaterHitPoint;
    private bool _lastRayHit;


    /// <summary>
    /// 初始化引用。
    ///
    /// 这个脚本通常挂在 Player 根物体上。
    /// 如果 Inspector 没手动拖引用，就尝试自动查找：
    /// - Animator
    /// - 左右脚 Foot / Toes 骨骼
    /// - ThirdPersonPlayerController
    /// </summary>
    private void Awake()
    {
        // 如果没有指定角色根节点，默认用当前物体。
        if (characterRoot == null)
        {
            characterRoot = transform;
        }

        // 如果没有指定 Animator，就从子物体里找。
        // 一般角色模型和 Animator 会在 Player 的子物体上。
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // 如果是 Humanoid Avatar，可以通过 HumanBodyBones 自动找到 Foot / Toes。
        if (animator != null)
        {
            if (leftFoot == null)
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            if (rightFoot == null)
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            if (leftToes == null)
                leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);

            if (rightToes == null)
                rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
        }

        // 自动查找玩家移动控制器。
        // 优先从父物体找，因为这个脚本可能挂在模型子物体上。
        if (playerController == null)
        {
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

            if (playerController == null)
                playerController = GetComponent<ThirdPersonPlayerController>();
        }
    }


    /// <summary>
    /// 左脚动画事件调用。
    ///
    /// 这个方法通常由 FootstepEventReceiver 转发调用：
    /// FootstepEventReceiver.SpawnLeftFootprint()
    /// → ShallowWaterFootRipple.SpawnLeftWaterRipple()
    /// </summary>
    public void SpawnLeftWaterRipple()
    {
        Debug.Log("[ShallowWaterFootRipple] 收到左脚水波事件", this);
        
        // 防止左脚短时间内重复生成水波。
        if (Time.time - _lastLeftRippleTime < minTimeBetweenSameFoot)
        {
            Debug.Log("[ShallowWaterFootRipple] 左脚间隔太短，跳过", this);
            return;
        }

        // 尝试在左脚下方生成水波。
        // 只有真正成功生成后，才记录时间。
        if (TrySpawnWaterRipple(leftFoot, leftToes))
        {
            _lastLeftRippleTime = Time.time;
        }
    }


    /// <summary>
    /// 右脚动画事件调用。
    /// </summary>
    public void SpawnRightWaterRipple()
    {
        Debug.Log("[ShallowWaterFootRipple] 收到右脚水波事件", this);
        
        // 防止右脚短时间内重复生成水波。
        if (Time.time - _lastRightRippleTime < minTimeBetweenSameFoot)
        {
            Debug.Log("[ShallowWaterFootRipple] 右脚间隔太短，跳过", this);
            return;
        }

        // 尝试在右脚下方生成水波。
        if (TrySpawnWaterRipple(rightFoot, rightToes))
        {
            _lastRightRippleTime = Time.time;
        }
    }


    /// <summary>
    /// 判断当前是否允许生成水波。
    ///
    /// 这里判断的是角色整体状态：
    /// - 有没有水面引用
    /// - 玩家是否有移动输入
    /// - Animator MoveSpeed 是否大于最小值
    /// </summary>
    private bool CanSpawnRipple()
    {
        if (waterSurface == null)
        {
            Debug.LogWarning("[ShallowWaterFootRipple] waterSurface 为空", this);
            return false;
        }

        if (playerController != null && !playerController.HasMoveInput)
        {
            Debug.LogWarning("[ShallowWaterFootRipple] 没有移动输入，跳过水波", this);
            return false;
        }

        if (animator != null && animator.GetFloat("MoveSpeed") < minAnimatorMoveSpeed)
        {
            Debug.LogWarning(
                "[ShallowWaterFootRipple] MoveSpeed 太小，MoveSpeed = " 
                + animator.GetFloat("MoveSpeed"),
                this
            );
            return false;
        }

        return true;
    }


    /// <summary>
    /// 尝试在某只脚下方生成水波。
    ///
    /// 参数：
    /// foot  = Foot 骨骼
    /// toes  = Toes 骨骼，可为空
    ///
    /// 流程：
    /// 1. 检查是否允许生成。
    /// 2. 计算脚掌中心。
    /// 3. 从脚掌中心上方向下 Raycast 检测水面。
    /// 4. 判断脚离水面是否足够近。
    /// 5. 计算水波位置、强度、半径。
    /// 6. 调用 waterSurface.AddRippleWorld()。
    /// </summary>
    private bool TrySpawnWaterRipple(Transform foot, Transform toes)
    {
        if (!CanSpawnRipple())
            return false;

        if (foot == null)
            return false;

        // 用 Foot 和 Toes 插值得到脚掌中心。
        // 这个点比单纯使用脚踝 Foot 更适合生成脚步水波。
        Vector3 footCenter = GetFootCenter(foot, toes);

        // 从脚掌中心上方开始向下发射 Ray。
        Vector3 rayOrigin = footCenter + Vector3.up * rayStartHeight;

        // 总检测距离 = 起点上抬高度 + 向下检测距离。
        float totalRayDistance = rayStartHeight + rayDistance;

        // 记录调试射线。
        _lastRayOrigin = rayOrigin;
        _lastRayEnd = rayOrigin + Vector3.down * totalRayDistance;
        _lastRayHit = false;

        
        Debug.Log("[ShallowWaterFootRipple] 开始检测水面，footCenter=" + footCenter, this);
        // 检测脚下是否有水面。
        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit waterHit,
                totalRayDistance,
                waterMask,
                queryTriggerInteraction))
        {
            if (logDebug)
                Debug.LogWarning(
                    "[ShallowWaterFootRipple] Raycast 没打到水面。waterMask=" 
                    + waterMask.value 
                    + ", rayOrigin=" + rayOrigin 
                    + ", distance=" + totalRayDistance,
                    this
                );

            return false;
        }
        
        Debug.Log(
            "[ShallowWaterFootRipple] 命中水面：" 
            + waterHit.collider.name 
            + ", hit=" + waterHit.point 
            + ", footHeightAboveWater=" + (footCenter.y - waterHit.point.y),
            this
        );

        // 记录调试命中点。
        _lastRayHit = true;
        _lastWaterHitPoint = waterHit.point;

        // 判断脚掌中心距离水面的高度。
        // 如果脚掌中心高出水面太多，说明脚还没踩到水面附近。
        float footHeightAboveWater = footCenter.y - waterHit.point.y;

        if (footHeightAboveWater > maxFootHeightAboveWater)
        {
            if (logDebug)
            {
                Debug.Log(
                    "ShallowWaterFootRipple：脚离水面太高，不生成水波。height = " + footHeightAboveWater,
                    this
                );
            }

            return false;
        }

        // 计算角色前进方向在水面上的投影。
        // 水波位置可以沿这个方向稍微前移，让波纹更靠近脚掌前方。
        Vector3 forwardOnWater = GetForwardOnWater(foot, waterHit.normal);

        // 最终水波位置：
        // 使用水面命中点，而不是脚骨骼点。
        // 因为波纹应该写在水面上。
        Vector3 ripplePosition =
            waterHit.point +
            forwardOnWater * forwardOffset;

        // 根据 Animator MoveSpeed 判断走路还是跑步。
        bool isRunning =
            animator != null &&
            animator.GetFloat("MoveSpeed") > runMoveSpeedThreshold;

        float strength = isRunning ? runStrength : walkStrength;
        int radius = isRunning ? runRadius : walkRadius;

        Debug.Log("[ShallowWaterFootRipple] 调用 AddRippleWorld，pos=" + ripplePosition, this);
        
        // 真正向水面写入扰动。
        // SurfaceWave 会把世界坐标转换成水面 UV，
        // 再写入 _InputTex，最后由 wave_equation 计算扩散。
        // waterSurface.AddRippleWorld(ripplePosition, strength, radius);

        Vector2 rippleUV = waterHit.textureCoord;
        // waterSurface.AddRippleUV(rippleUV, strength, radius);


        if (isRunning)
        {
            waterSurface.AddFootstepRippleUV(
                rippleUV,
                centerStrength: -0.45f,
                ringStrength: 0.22f,
                innerRadius: 1,
                outerRadius: 4
            );
        }
        else
        {
            waterSurface.AddFootstepRippleUV(
                rippleUV,
                centerStrength: -0.25f,
                ringStrength: 0.12f,
                innerRadius: 1,
                outerRadius: 3
            );
        }

        Debug.Log(
            "[ShallowWaterFootRipple] 使用 textureCoord 生成水波，uv=" +
            rippleUV +
            ", hit=" + waterHit.point,
            this
        );
        
        if (logDebug)
        {
            Debug.Log(
                $"ShallowWaterFootRipple：生成水波 pos={ripplePosition}, strength={strength}, radius={radius}",
                this
            );
        }

        return true;
    }

    

    /// <summary>
    /// 计算脚掌中心。
    ///
    /// 如果有 Toes 骨骼：
    /// 使用 Foot 和 Toes 插值。
    ///
    /// 如果没有 Toes：
    /// 退化为直接使用 Foot 位置。
    /// </summary>
    private Vector3 GetFootCenter(Transform foot, Transform toes)
    {
        if (foot == null)
            return transform.position;

        if (toes == null)
            return foot.position;

        return Vector3.Lerp(foot.position, toes.position, toeBlend);
    }


    /// <summary>
    /// 获取角色在水面上的前进方向。
    ///
    /// 为什么要 ProjectOnPlane？
    /// 因为角色 forward 可能带有上下方向，
    /// 而水波偏移应该沿着水面平面移动，
    /// 所以要把 forward 投影到水面法线所在的平面上。
    /// </summary>
    private Vector3 GetForwardOnWater(Transform foot, Vector3 waterNormal)
    {
        // 优先使用角色根节点 forward。
        Vector3 forward = characterRoot != null
            ? characterRoot.forward
            : transform.forward;

        // 把角色 forward 投影到水面平面上。
        forward = Vector3.ProjectOnPlane(forward, waterNormal);

        // 如果角色 forward 无效，就尝试用脚骨骼 forward。
        if (forward.sqrMagnitude < 0.0001f && foot != null)
        {
            forward = Vector3.ProjectOnPlane(foot.forward, waterNormal);
        }

        // 仍然无效时给一个默认方向，避免 Normalize 出问题。
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        return forward;
    }


    /// <summary>
    /// Scene View 调试绘制。
    ///
    /// 选中挂了这个脚本的物体时，会显示：
    /// - 最近一次脚下水面检测射线
    /// - 是否命中水面
    /// - 命中点位置
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        // 命中水面时画青色，没命中时画红色。
        Gizmos.color = _lastRayHit ? Color.cyan : Color.red;
        Gizmos.DrawLine(_lastRayOrigin, _lastRayEnd);

        // 如果命中了水面，画出命中点。
        if (_lastRayHit)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(_lastWaterHitPoint, 0.035f);
        }
    }
}

