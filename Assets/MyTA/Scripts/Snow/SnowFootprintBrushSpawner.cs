using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// RT 脚印 Brush 生成器。
///
/// 推荐使用方式：
/// 1. 挂在 Player 根物体上。
/// 2. Animation Event 调用 SpawnLeftFootprint / SpawnRightFootprint。
/// 3. 根据 Foot / Toes 骨骼插值得到脚掌中心。
/// 4. 从脚掌中心向下 Raycast。
/// 5. 在地面 hit.point 附近生成 FootprintBrush。
/// 6. FootstepCamera 拍到 Brush 后写入 CurrentBrushRT。
/// 7. FootprintRenderFeature 再把 CurrentBrushRT 累积进 AccumA。
/// </summary>
public class SnowFootprintBrushSpawner : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("脚印 Brush prefab。通常是一个 Quad，材质使用 Footprints/URP_FootprintBrush_NormalHeightSeparate。")]
    public GameObject brushPrefab;

    [Tooltip("角色根节点。一般是 Player 根物体，用于获取整体朝向。")]
    public Transform characterRoot;

    [Tooltip("角色 Animator。一般在模型子物体上，例如 Ayaka。")]
    public Animator animator;

    [Tooltip("玩家控制器。用于读取 HasMoveInput，避免停止时动画事件残留生成脚印。")]
    public ThirdPersonPlayerController playerController;

    [Header("Surface Mask")]
    [Tooltip("是否只允许在指定表面生成脚印。")]
    public bool useSurfaceMask = false;

    [Tooltip("湿地 / 可生成脚印区域判断组件。")]
    public FootprintWetlandMask wetlandMask;


    // ============================================================
    // Spawn Mode
    // ============================================================

    [Header("Spawn Mode")]
    [Tooltip("true = 按移动距离自动生成；false = 只通过动画事件生成。")]
    public bool useDistanceSpawn = false;

    [Tooltip("距离模式下，每隔多远生成一个脚印。动画事件模式下不用。")]
    public float stepDistance = 0.7f;


    // ============================================================
    // Movement Guard
    // ============================================================

    [Header("Movement Guard")]
    [Tooltip("是否要求玩家有移动输入才允许生成脚印。")]
    public bool requireMoveInput = true;

    [Tooltip("是否要求 Animator MoveSpeed 大于阈值才允许生成脚印。")]
    public bool requireAnimatorMoveSpeed = true;

    [Tooltip("Animator 中的移动速度参数名。")]
    public string moveSpeedParam = "MoveSpeed";

    [Tooltip("MoveSpeed 小于这个值时不生成脚印。")]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip("同一只脚最小生成间隔，防止动画事件重复触发。")]
    public float minTimeBetweenSameFoot = 0.15f;

    [Tooltip("游戏开始后多少秒内禁止生成脚印，避免初始化误触发。")]
    public float startBlockTime = 0.2f;

    [Tooltip("F7/F8 测试按键是否无视移动判断。调试脚印位置时建议打开。")]
    public bool debugKeysIgnoreMovementGuard = true;


    // ============================================================
    // Foot Bones
    // ============================================================

    [Header("Foot Bones")]
    [Tooltip("左脚 Foot 骨骼。为空时会尝试从 Humanoid Animator 自动获取。")]
    public Transform leftFoot;

    [Tooltip("右脚 Foot 骨骼。为空时会尝试从 Humanoid Animator 自动获取。")]
    public Transform rightFoot;

    [Tooltip("左脚 Toes 骨骼。为空时会尝试从 Humanoid Animator 自动获取。")]
    public Transform leftToes;

    [Tooltip("右脚 Toes 骨骼。为空时会尝试从 Humanoid Animator 自动获取。")]
    public Transform rightToes;

    [Tooltip("Foot 和 Toes 的插值。0=Foot，1=Toes，0.55~0.75 通常接近脚掌中心。")]
    [Range(0f, 1f)]
    public float toeBlend = 0.6f;


    // ============================================================
    // Raycast
    // ============================================================

    [Header("Raycast")]
    [Tooltip("地面 Layer。建议只包含 Ground / Terrain，避免射到角色自己。")]
    public LayerMask groundMask = ~0;

    [Tooltip("从脚掌中心向上抬多少开始 Raycast。")]
    public float rayStartHeight = 0.25f;

    [Tooltip("从脚掌中心向下检测多远。")]
    public float rayDistance = 1.0f;

    [Tooltip("Brush 沿地面法线抬起一点，避免和地面重合。")]
    public float surfaceOffset = 0.03f;


    // ============================================================
    // Placement
    // ============================================================

    [Header("Placement")]
    [Tooltip("走路时，脚印沿脚尖方向额外偏移。用于把脚踝位置修正到脚掌中心。")]
    public float walkFootForwardOffset = 0.04f;

    [Tooltip("跑步时，脚印沿脚尖方向额外偏移。")]
    public float runFootForwardOffset = 0.07f;

    [Tooltip("没有脚骨骼时，才使用这个左右偏移作为 fallback。")]
    public float footSideOffset = 0.18f;

    [Tooltip("整体脚印贴图方向修正。如果脚印横着或反了，可以填 90 / -90 / 180。")]
    public float footprintYawOffset = 0f;

    [Header("Fine Tune Local Offset")]
    [Tooltip("左脚局部偏移。X=左右，Y=前后。单位：米。")]
    public Vector2 leftLocalOffset = Vector2.zero;

    [Tooltip("右脚局部偏移。X=左右，Y=前后。单位：米。")]
    public Vector2 rightLocalOffset = Vector2.zero;

    [Header("Fine Tune Rotation")]
    [Tooltip("左脚额外旋转角度。单位：度。")]
    public float leftYawOffset = 0f;

    [Tooltip("右脚额外旋转角度。单位：度。")]
    public float rightYawOffset = 0f;


    // ============================================================
    // Brush Visual
    // ============================================================

    [Header("Brush Size")]
    [Tooltip("是否覆盖 prefab 原始缩放。")]
    public bool overrideBrushScale = true;

    [Tooltip("走路脚印尺寸。X=脚宽，Y=脚长。Quad 默认在 XY 平面。")]
    public Vector2 walkFootprintSize = new Vector2(0.22f, 0.36f);

    [Tooltip("跑步脚印尺寸。")]
    public Vector2 runFootprintSize = new Vector2(0.24f, 0.40f);

    [Header("Brush Lifetime")]
    [Tooltip("Brush 存活时间。需要至少活到 FootstepCamera 渲染一次。")]
    public float brushLife = 0.12f;


    // ============================================================
    // Textures
    // ============================================================

    [Header("Left Foot Textures")]
    public Texture leftNormalTex;
    public Texture leftHeightTex;

    [Header("Right Foot Textures")]
    public Texture rightNormalTex;
    public Texture rightHeightTex;


    [Header("Snow Brush Material Params")]
    [Tooltip("写入 Snow RT 的下陷强度。0=不压雪，1=最大压雪。")]
    [Range(0f, 1f)]
    public float sinkStrength = 1f;

    [Tooltip("圆形 Brush 边缘柔和程度。越大边缘越软。")]
    [Range(0.01f, 1f)]
    public float softness = 0.35f;


    // ============================================================
    // Layer
    // ============================================================

    [Header("Layer")]
    [Tooltip("生成出来的 Brush 会强制设置到这个 Layer。")]
    public string brushLayerName = "SnowFootprintBrush";


    // ============================================================
    // Debug Gizmos
    // ============================================================

    [Header("Debug Gizmos")]
    public bool showFootprintDebugGizmos = true;

    [Tooltip("显示左脚调试数据。")]
    public bool showLeftFootDebug = true;

    [Tooltip("显示右脚调试数据。")]
    public bool showRightFootDebug = false;

    [Tooltip("显示 Foot / Toes 骨骼点。")]
    public bool showFootBones = false;

    [Tooltip("显示 Foot 和 Toes 插值得到的脚掌中心点。")]
    public bool showFootCenter = true;

    [Tooltip("显示从脚掌中心向下的 Raycast。")]
    public bool showRaycast = true;

    [Tooltip("显示 Raycast 命中点。")]
    public bool showHitPoint = true;

    [Tooltip("显示最终 Brush 生成点。")]
    public bool showSpawnPoint = true;

    [Tooltip("显示地面法线。")]
    public bool showGroundNormal = false;

    [Tooltip("显示脚印朝向。")]
    public bool showFootForward = true;

    [Tooltip("显示 Brush 实际覆盖平面。")]
    public bool showBrushPlane = true;

    [Tooltip("显示 Brush 调试盒。类似 Decal 的投射盒，但更薄。")]
    public bool showBrushBox = true;

    [Tooltip("显示 Scene View 文字标签。")]
    public bool showDebugLabels = false;

    [Tooltip("Debug 点大小。")]
    [Range(0.005f, 0.1f)]
    public float debugPointSize = 0.015f;

    [Tooltip("地面法线显示长度。")]
    public float debugNormalLength = 0.25f;

    [Tooltip("脚印朝向线显示长度。")]
    public float debugForwardLength = 0.25f;

    [Tooltip("文字标签向上偏移高度。")]
    public float debugLabelHeight = 0.035f;

    [Tooltip("Brush 调试盒厚度。只用于 Scene 视图辅助显示。")]
    public float debugBrushBoxDepth = 0.025f;

    [Header("Debug Log")]
    public bool logSpawn = false;


    // ============================================================
    // Internal State
    // ============================================================

    private Vector3 lastStepPos;
    private bool nextLeftFoot;

    private float lastLeftFootTime = -999f;
    private float lastRightFootTime = -999f;

    private static readonly int SinkStrengthID = Shader.PropertyToID("_SinkStrength");
    private static readonly int SoftnessID = Shader.PropertyToID("_Softness");

    private enum DebugFootSide
    {
        Left,
        Right
    }

    private struct FootprintBrushDebugData
    {
        public bool valid;

        public DebugFootSide footSide;

        public Vector3 footBonePosition;
        public Vector3 toeBonePosition;
        public bool hasToe;

        public Vector3 footCenterPosition;

        public Vector3 rayOrigin;
        public Vector3 rayEnd;

        public bool rayHit;
        public Vector3 hitPoint;
        public Vector3 groundNormal;

        public Vector3 spawnPosition;
        public Quaternion spawnRotation;

        public Vector3 forwardOnSurface;
        public Vector3 rightOnSurface;

        public Vector2 brushSize;

        public float time;
    }

    private FootprintBrushDebugData lastLeftDebugData;
    private FootprintBrushDebugData lastRightDebugData;


    // ============================================================
    // Unity Events
    // ============================================================

    // [说明] Awake 只做“引用自动补齐”，避免在 Inspector 漏绑时脚印系统直接失效。
    // [说明] 这里不会生成脚印，也不会写 RT，只是在运行开始前准备角色、动画器、脚骨骼等依赖。
    private void Awake()
    {
        // [说明] characterRoot 用来代表角色整体位置和朝向；如果没手动绑定，就默认使用当前脚本所在物体。
        if (characterRoot == null)
            characterRoot = transform;

        // [说明] Animator 主要用于两件事：读取 Humanoid 脚骨骼，以及读取 MoveSpeed 判断是否真的在移动。
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // [说明] playerController 用来读取 HasMoveInput，防止角色停止时动画事件残留继续生成脚印。
        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

        // [说明] 如果角色是 Humanoid，并且 Inspector 没手动指定脚骨骼，就从 Animator 自动拿 Foot / Toes。
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
    }

    // [说明] Start 记录距离生成模式的起点位置。
    // [说明] 后续 Update 会拿当前角色位置和 lastStepPos 比较，判断是否走够 stepDistance。
    private void Start()
    {
        if (characterRoot != null)
            lastStepPos = characterRoot.position;
    }

    // [说明] Update 只负责“距离生成模式”。
    // [说明] 如果 useDistanceSpawn=false，脚印应该由 Animation Event 调用 SpawnLeftFootprint / SpawnRightFootprint 生成。
    private void Update()
    {
        // [说明] useDistanceSpawn=true 时，不依赖动画事件，而是按角色移动距离自动交替生成左右脚脚印。
        if (useDistanceSpawn)
        {
            // [说明] 距离模式至少需要角色根节点和 Brush prefab。
            // [说明] 缺任何一个都无法计算移动距离或实例化脚印 Brush。
            if (characterRoot == null || brushPrefab == null)
                return;

            // [说明] 只比较 XZ 平面距离，忽略 Y 轴高度。
            // [说明] 这样角色上下坡、地面高低变化时，不会因为高度变化误判为走了一步。
            Vector3 flatNow = new Vector3(characterRoot.position.x, 0f, characterRoot.position.z);
            Vector3 flatLast = new Vector3(lastStepPos.x, 0f, lastStepPos.z);

            // [说明] 没有走够 stepDistance 就不生成脚印。
            // [说明] 这个判断相当于一个简单的“步频模拟器”。
            if (Vector3.Distance(flatNow, flatLast) < stepDistance)
                return;

            // [说明] 走够一步后，根据 nextLeftFoot 决定生成左脚还是右脚。
            // [说明] 参数 false 表示不跳过移动保护，仍然会走 CanSpawnFootprint 和同脚冷却判断。
            if (nextLeftFoot)
                SpawnLeftFootprint(false);
            else
                SpawnRightFootprint(false);

            // [说明] 生成完成后，把本次角色位置记录为下一次距离判断的起点。
            // [说明] 同时翻转 nextLeftFoot，让下一步换另一只脚。
            lastStepPos = characterRoot.position;
            nextLeftFoot = !nextLeftFoot;
        }
        // [说明] 非距离生成模式下，Update 不做事。
        // [说明] 此时脚印入口应该来自动画事件，而不是每帧距离检测。
        else
        {
            return;
        }

        
    }


    // ============================================================
    // Animation Event API
    // ============================================================

    /// <summary>
    /// Animation Event 调用：左脚落地。
    /// </summary>
    public void SpawnLeftFootprint()
    {
        SpawnLeftFootprint(false);
    }

    /// <summary>
    /// Animation Event 调用：右脚落地。
    /// </summary>
    public void SpawnRightFootprint()
    {
        SpawnRightFootprint(false);
    }

    // [说明] 左脚内部生成入口。
    // [说明] 动画事件和距离模式最终都会走到这里，再统一调用 SpawnFootprint。
    private void SpawnLeftFootprint(bool ignoreMovementGuard)
    {
        // [说明] 不忽略移动保护时，先检查当前是否真的允许生成脚印。
        // [说明] 例如刚开局、没移动输入、MoveSpeed 太低、prefab 未绑定时都会被拦截。
        if (!ignoreMovementGuard && !CanSpawnFootprint())
            return;

        // [说明] 同一只脚有最小时间间隔，防止同一个动画落脚点连续触发多次事件。
        if (!ignoreMovementGuard && Time.time - lastLeftFootTime < minTimeBetweenSameFoot)
            return;

        // [说明] 记录左脚这次生成时间，然后把左脚骨骼、左脚脚趾、左脚贴图传给通用生成函数。
        lastLeftFootTime = Time.time;

        SpawnFootprint(true,leftFoot,leftToes,leftNormalTex,leftHeightTex);
    }

    // [说明] 右脚内部生成入口，逻辑和左脚一致，只是传入右脚骨骼和右脚贴图。
    private void SpawnRightFootprint(bool ignoreMovementGuard)
    {
        if (!ignoreMovementGuard && !CanSpawnFootprint())
            return;

        // [说明] 右脚也单独记录冷却时间，避免右脚动画事件重复生成。
        if (!ignoreMovementGuard && Time.time - lastRightFootTime < minTimeBetweenSameFoot)
            return;

        // [说明] 记录右脚这次生成时间，然后把右脚数据交给 SpawnFootprint 统一处理。
        lastRightFootTime = Time.time;

        SpawnFootprint(false,rightFoot,rightToes,rightNormalTex,rightHeightTex);

    }


    // ============================================================
    // Spawn Logic
    // ============================================================

    // [说明] 统一的脚印生成条件检查。
    // [说明] 这个函数只判断“能不能生成”，不负责计算位置，也不实例化 Brush。
    private bool CanSpawnFootprint()
    {
        // [说明] 刚进入场景的一小段时间不允许生成，避免 Animator 初始化或角色落地瞬间误触发脚印。
        if (Time.timeSinceLevelLoad < startBlockTime)
            return false;

        // [说明] 没有 Brush prefab 或角色根节点时，后面的生成逻辑没有意义，直接拒绝。
        if (brushPrefab == null || characterRoot == null)
            return false;

        // [说明] 如果要求有移动输入，则玩家没有按方向键/摇杆时不生成脚印。
        if (requireMoveInput && playerController != null && !playerController.HasMoveInput)
            return false;

        // [说明] 如果要求 Animator 速度有效，则读取 MoveSpeed 参数，过滤站立、轻微抖动、过渡动画。
        if (requireAnimatorMoveSpeed && animator != null && HasAnimatorFloat(animator, moveSpeedParam))
        {
            float moveSpeed = animator.GetFloat(moveSpeedParam);

            if (moveSpeed < minAnimatorMoveSpeed)
                return false;
        }

        return true;
    }

    // [说明] 检查 Animator 里是否存在指定的 float 参数。
    // [说明] 这样可以避免直接 GetFloat 一个不存在的参数导致报错或警告。
    private bool HasAnimatorFloat(Animator targetAnimator, string paramName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(paramName))
            return false;

        // [说明] 遍历 Animator 参数列表，只接受名字相同且类型为 Float 的参数。
        foreach (AnimatorControllerParameter p in targetAnimator.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }

    // [说明] 根据当前移动速度选择脚印前后修正值。
    // [说明] 跑步时脚掌落点通常更靠前，所以使用 runFootForwardOffset。
    private float GetCurrentFootForwardOffset()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkFootForwardOffset;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runFootForwardOffset;

        return walkFootForwardOffset;
    }

    // [说明] 根据当前移动速度选择脚印尺寸。
    // [说明] 跑步时脚印可以稍微更大，表现更重的踩踏感。
    private Vector2 GetCurrentFootprintSize()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkFootprintSize;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runFootprintSize;

        return walkFootprintSize;
    }

    // [说明] 核心生成函数。
    // [说明] 这里完成：脚掌中心计算、Raycast 贴地、朝向计算、Brush 实例化、贴图参数设置、通知 RT 管理器。
    private void SpawnFootprint(bool isLeftFoot,Transform footTransform,Transform toeTransform,Texture normalTex,Texture heightTex)
    {
        // [说明] 没有 Brush prefab 就无法生成临时投影 Quad，直接退出。
        if (brushPrefab == null)
            return;

        // [说明] 将 bool 类型的左右脚转换成 Debug 用枚举，方便后面缓存和绘制 Gizmos。
        DebugFootSide debugFootSide = isLeftFoot ? DebugFootSide.Left : DebugFootSide.Right;

        Vector3 footBonePosition;
        Vector3 toeBonePosition = Vector3.zero;
        bool hasToe = toeTransform != null;

        // [说明] 优先使用真实 Foot 骨骼位置。
        // [说明] 这样脚印会跟动画脚步位置一致，而不是简单地跟角色中心偏移。
        if (footTransform != null)
        {
            footBonePosition = footTransform.position;
        // [说明] 如果没有绑定 Foot 骨骼，则使用角色左右方向加 footSideOffset 做一个 fallback 落点。
        }
        else
        {
            Vector3 right = characterRoot.right;
            right.y = 0f;

            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            right.Normalize();

            float side = isLeftFoot ? -footSideOffset : footSideOffset;
            footBonePosition = characterRoot.position + right * side;
        }

        // [说明] footCenterPosition 是后续 Raycast 的脚掌中心。
        // [说明] 默认先用 Foot 骨骼位置，若存在 Toes 再向脚趾方向插值。
        Vector3 footCenterPosition = footBonePosition;

        // [说明] 有 Toes 骨骼时，用 Foot -> Toes 的插值点近似脚掌中心。
        // [说明] toeBlend 越大，采样点越靠近脚尖。
        if (hasToe)
        {
            toeBonePosition = toeTransform.position;
            footCenterPosition = Vector3.Lerp(
                footBonePosition,
                toeBonePosition,
                toeBlend
            );
        }

        // [说明] Raycast 从脚掌中心上方开始，向下检测地面。
        // [说明] 这样可以兼容脚骨骼略微穿地或悬空的情况。
        Vector3 rayOrigin = footCenterPosition + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;
        Vector3 rayEnd = rayOrigin + Vector3.down * totalRayDistance;

        // [说明] 当前脚印大小会根据走路/跑步状态动态选择。
        Vector2 currentBrushSize = GetCurrentFootprintSize();

        // [说明] 没有射到地面就不生成脚印。
        // [说明] 同时缓存失败数据，方便 Scene 视图里看到 Raycast 为什么没命中。
        if (!Physics.Raycast(rayOrigin, Vector3.down,out RaycastHit hit,totalRayDistance,groundMask,QueryTriggerInteraction.Ignore))
        {
            CacheBrushDebugData(
                debugFootSide,
                footBonePosition,
                toeBonePosition,
                hasToe,
                footCenterPosition,
                rayOrigin,
                rayEnd,
                false,
                Vector3.zero,
                Vector3.up,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Vector3.zero,
                currentBrushSize
            );

            if (logSpawn)
            {
                Debug.LogWarning(
                    $"[FootprintBrushSpawner] Raycast failed. foot={(isLeftFoot ? "Left" : "Right")}, " +
                    $"origin={rayOrigin}, distance={totalRayDistance}"
                );
            }

            return;
        }

        // [说明] Raycast 命中的地面法线，用来让 Brush 贴合斜坡或不平整表面。
        Vector3 normal = hit.normal;

        // [说明] 表面遮罩用于限制脚印只出现在湿地、雪地、泥地等指定区域。
        if (useSurfaceMask && wetlandMask != null)
        {
            if (!wetlandMask.CanSpawnAt(hit.point))
            {
                if (logSpawn)
                    Debug.Log("[FootprintBrushSpawner] Surface mask rejected footprint.");

                return;
            }
        }

        // [说明] 计算脚尖方向，并投影到地面切平面上。
        // [说明] 这样脚印朝向会贴着地面，而不是带有脚骨骼的上下倾斜。
        Vector3 forwardOnSurface = GetFootForwardOnSurface(footTransform,toeTransform,normal);

        // [说明] 根据脚尖方向和地面法线算出脚印的右方向，用于左右局部偏移。
        Vector3 rightOnSurface = Vector3.Cross(forwardOnSurface, normal).normalized;

        // [说明] 前向偏移负责把脚骨骼位置修正到脚掌落印位置。
        // [说明] localOffset 用来分别微调左脚和右脚，解决模型脚骨骼和贴图中心不完全一致的问题。
        float currentForwardOffset = GetCurrentFootForwardOffset();
        Vector2 localOffset = isLeftFoot ? leftLocalOffset : rightLocalOffset;

        // [说明] 最终 Brush 生成点 = 地面命中点 + 前后修正 + 左右局部修正 + 法线方向抬高。
        // [说明] surfaceOffset 可以避免 Brush 和地面 z-fighting。
        Vector3 spawnPosition =
            hit.point +
            forwardOnSurface * currentForwardOffset +
            rightOnSurface * localOffset.x +
            forwardOnSurface * localOffset.y +
            normal * surfaceOffset;

        // [说明] Quad 默认面朝自身 local +Z 或 -Z 的方向，这里用 -normal 让 Brush 面朝地面。
        // [说明] 第二个参数 forwardOnSurface 决定脚印贴图的脚尖朝向。
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        // [说明] yawOffset 用于最终角度微调。
        // [说明] footprintYawOffset 是整体修正，leftYawOffset/rightYawOffset 是左右脚单独修正。
        float yawOffset =
            footprintYawOffset +
            (isLeftFoot ? leftYawOffset : rightYawOffset);

        // [说明] 绕地面法线旋转，保证只改变贴图朝向，不破坏 Brush 贴地姿态。
        spawnRotation = Quaternion.AngleAxis(yawOffset, normal) * spawnRotation;

        // [说明] 成功命中地面后，把本次计算结果缓存起来，供 OnDrawGizmos 绘制调试信息。
        CacheBrushDebugData(
            debugFootSide,
            footBonePosition,
            toeBonePosition,
            hasToe,
            footCenterPosition,
            rayOrigin,
            rayEnd,
            true,
            hit.point,
            normal,
            spawnPosition,
            spawnRotation,
            forwardOnSurface,
            rightOnSurface,
            currentBrushSize
        );

        // [说明] 实例化临时 Brush。
        // [说明] 它不会长期留在场景里，只需要存活到 FootstepCamera 拍到它。
        GameObject brush = Instantiate(brushPrefab,spawnPosition,spawnRotation);
        

        // [说明] Brush 必须放到 FootprintBrush Layer。
        // [说明] RenderFeature 会用 LayerMask 只渲染这一层，避免把其他物体拍进 CurrentBrushRT。
        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
        {
            SetLayerRecursively(brush, brushLayer);
        }
        else
        {
            Debug.LogWarning($"[FootprintBrushSpawner] 找不到 Layer: {brushLayerName}");
        }

        // [说明] 覆盖 prefab 缩放后，脚印大小完全由 walkFootprintSize / runFootprintSize 控制。
        if (overrideBrushScale)
        {
            brush.transform.localScale = new Vector3(
                currentBrushSize.x,
                currentBrushSize.y,
                1f
            );
        }

        SetupBrushMaterial(brush);
        DisableBrushShadows(brush);

        // [说明] 通知 RT 管理器“这一帧确实生成了新 Brush”。
        // [说明] 后续 RenderFeature / RT 管理器可以据此决定是否需要累积当前 Brush。
        if (SnowFootprintRTManager.Active != null)
        {
            SnowFootprintRTManager.Active.NotifyBrushSpawned();
        }
        else
        {
            Debug.LogWarning("[SnowFootprintRTManager] SnowFootprintRTManager.Active is null.");
        }

        if (logSpawn)
        {
            Debug.Log(
                $"[SnowFootprintRTManager] Spawn {(isLeftFoot ? "Left" : "Right")} Brush. " +
                $"hit={hit.point}, spawn={spawnPosition}, normal={normal}, forward={forwardOnSurface}"
            );
        }

        // [说明] Brush 只需要短暂存在。
        // [说明] brushLife 要保证至少覆盖一次 FootstepCamera 渲染，否则可能还没拍到就被销毁。
        Destroy(brush, brushLife);
    }

    // [说明] 计算脚印在地面上的前方方向。
    // [说明] 优先使用 Foot -> Toes，其次用 Foot.forward，最后用角色整体 forward。
    private Vector3 GetFootForwardOnSurface(
        Transform footTransform,
        Transform toeTransform,
        Vector3 normal)
    {
        Vector3 forward = Vector3.zero;

        // 优先用 Foot -> Toes 方向。
        // 这个方向通常最接近脚尖方向。
        if (footTransform != null && toeTransform != null)
        {
            forward = toeTransform.position - footTransform.position;
        }

        // 如果没有 Toes，则退回脚骨骼 forward。
        if (forward.sqrMagnitude < 0.0001f && footTransform != null)
        {
            forward = footTransform.forward;
        }

        // 最后退回角色整体 forward。
        if (forward.sqrMagnitude < 0.0001f && characterRoot != null)
        {
            forward = characterRoot.forward;
        }

        // [说明] 把前方方向投影到地面平面上，去掉沿地面法线的分量。
        // [说明] 这样脚印方向始终贴着坡面。
        forward = Vector3.ProjectOnPlane(forward, normal);

        // [说明] 如果投影后方向几乎为零，就用世界前方再投影一次作为兜底。
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        }

        return forward.normalized;
    }


    // ============================================================
    // Brush Setup
    // ============================================================

    // [说明] 给 Brush 的所有 Renderer 设置材质参数。
    // [说明] 使用 MaterialPropertyBlock 可以避免实例化材质，减少运行时材质副本。
    private void SetupBrushMaterial(GameObject brush)
    {
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            // 写入 Snow/SnowCircleBrush 使用的参数。
            // _SinkStrength 控制这个 Brush 把雪压下去多少。
            // _Softness 控制圆形边缘过渡有多软。
            mpb.SetFloat(SinkStrengthID, sinkStrength);
            mpb.SetFloat(SoftnessID, softness);

            r.SetPropertyBlock(mpb);
        }
    }

    // [说明] 关闭 Brush 的阴影相关设置。
    // [说明] Brush 是给 FootstepCamera 写 RT 的工具物体，不应该影响主场景阴影。
    private void DisableBrushShadows(GameObject brush)
    {
        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // [说明] 递归设置 Layer，保证 prefab 子物体也能被 FootprintRenderFeature 的 LayerMask 捕获。
    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }


    // ============================================================
    // Debug Cache
    // ============================================================

    // [说明] 缓存一次脚印生成过程中的关键数据。
    // [说明] 这些数据不会影响运行逻辑，只服务于 Scene 视图 Gizmos 调试。
    private void CacheBrushDebugData(
        DebugFootSide footSide,
        Vector3 footBonePosition,
        Vector3 toeBonePosition,
        bool hasToe,
        Vector3 footCenterPosition,
        Vector3 rayOrigin,
        Vector3 rayEnd,
        bool rayHit,
        Vector3 hitPoint,
        Vector3 groundNormal,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 forwardOnSurface,
        Vector3 rightOnSurface,
        Vector2 brushSize)
    {
        // [说明] 把脚骨骼、Raycast、命中点、最终生成点、方向、尺寸等数据打包保存。
        FootprintBrushDebugData data = new FootprintBrushDebugData
        {
            valid = true,

            footSide = footSide,

            footBonePosition = footBonePosition,
            toeBonePosition = toeBonePosition,
            hasToe = hasToe,

            footCenterPosition = footCenterPosition,

            rayOrigin = rayOrigin,
            rayEnd = rayEnd,

            rayHit = rayHit,
            hitPoint = hitPoint,
            groundNormal = groundNormal,

            spawnPosition = spawnPosition,
            spawnRotation = spawnRotation,

            forwardOnSurface = forwardOnSurface,
            rightOnSurface = rightOnSurface,

            brushSize = brushSize,

            time = Time.time
        };

        // [说明] 左右脚分别缓存，避免右脚生成后把左脚最后一次调试信息覆盖掉。
        if (footSide == DebugFootSide.Left)
            lastLeftDebugData = data;
        else
            lastRightDebugData = data;
    }


    // ============================================================
    // Gizmos
    // ============================================================

    // [说明] Scene 视图调试入口。
    // [说明] 只负责画辅助线和辅助点，不参与脚印生成。
    private void OnDrawGizmos()
    {
        if (!showFootprintDebugGizmos)
            return;

        if (showLeftFootDebug)
            DrawBrushDebugData(lastLeftDebugData);

        if (showRightFootDebug)
            DrawBrushDebugData(lastRightDebugData);
    }

    // [说明] 根据缓存的数据绘制某一只脚的调试信息。
    // [说明] 可以逐项开关 Foot、Toes、Raycast、Hit、SpawnPoint、Forward、BrushPlane、BrushBox。
    private void DrawBrushDebugData(FootprintBrushDebugData data)
    {
        // [说明] 没有有效缓存数据时不绘制，避免 Scene 视图出现无意义的默认点。
        if (!data.valid)
            return;

        // [说明] 左右脚使用不同颜色，方便在 Scene 视图里区分当前调试的是哪只脚。
        Color footColor = data.footSide == DebugFootSide.Left
            ? new Color(0.2f, 0.7f, 1f, 1f)
            : new Color(1f, 0.45f, 0.2f, 1f);

        string footName = data.footSide == DebugFootSide.Left ? "Left" : "Right";

        if (showFootBones)
        {
            Gizmos.color = footColor;
            Gizmos.DrawSphere(data.footBonePosition, debugPointSize);
            DrawDebugLabel(data.footBonePosition, $"{footName} Foot");

            if (data.hasToe)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(data.toeBonePosition, debugPointSize);
                Gizmos.DrawLine(data.footBonePosition, data.toeBonePosition);
                DrawDebugLabel(data.toeBonePosition, $"{footName} Toes");
            }
        }

        if (showFootCenter)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(data.footCenterPosition, debugPointSize * 1.2f);
            DrawDebugLabel(data.footCenterPosition, $"{footName} Foot Center");
        }

        if (showRaycast)
        {
            Gizmos.color = data.rayHit ? Color.green : Color.red;
            Gizmos.DrawLine(data.rayOrigin, data.rayEnd);
            Gizmos.DrawWireSphere(data.rayOrigin, debugPointSize * 0.8f);
            DrawDebugLabel(data.rayOrigin, $"{footName} Ray Origin");
        }

        // [说明] Raycast 没命中时，只画到射线阶段。
        // [说明] 命中点、生成点、Brush 平面等数据都没有意义，所以直接结束。
        if (!data.rayHit)
            return;

        if (showHitPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(data.hitPoint, debugPointSize);
            DrawDebugLabel(data.hitPoint, $"{footName} Hit");
        }

        if (showGroundNormal)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                data.hitPoint,
                data.hitPoint + data.groundNormal.normalized * debugNormalLength
            );

            DrawDebugLabel(
                data.hitPoint + data.groundNormal.normalized * debugNormalLength,
                "Ground Normal"
            );
        }

        if (showSpawnPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(data.spawnPosition, debugPointSize * 1.3f);
            DrawDebugLabel(data.spawnPosition, $"{footName} Spawn Point");
        }

        if (showFootForward)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(
                data.spawnPosition,
                data.spawnPosition + data.forwardOnSurface.normalized * debugForwardLength
            );

            DrawDebugLabel(
                data.spawnPosition + data.forwardOnSurface.normalized * debugForwardLength,
                "Foot Forward"
            );
        }

        if (showBrushPlane)
            DrawBrushPlaneGizmo(data);

        if (showBrushBox)
            DrawBrushBoxGizmo(data);
    }

    // [说明] 绘制 Brush 的实际覆盖矩形。
    // [说明] 这个矩形可以帮助判断脚印贴图是否和脚掌位置、方向、大小一致。
    private void DrawBrushPlaneGizmo(FootprintBrushDebugData data)
    {
        if (data.brushSize.x <= 0f || data.brushSize.y <= 0f)
            return;

        // [说明] 修改 Gizmos.matrix 前先保存旧状态，画完后必须恢复，避免影响其他 Gizmos。
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Matrix4x4 matrix = Matrix4x4.TRS(
            data.spawnPosition,
            data.spawnRotation,
            Vector3.one
        );

        Gizmos.matrix = matrix;

        float halfWidth = data.brushSize.x * 0.5f;
        float halfLength = data.brushSize.y * 0.5f;

        Vector3 p0 = new Vector3(-halfWidth, -halfLength, 0f);
        Vector3 p1 = new Vector3( halfWidth, -halfLength, 0f);
        Vector3 p2 = new Vector3( halfWidth,  halfLength, 0f);
        Vector3 p3 = new Vector3(-halfWidth,  halfLength, 0f);

        Gizmos.color = new Color(0f, 1f, 1f, 1f);
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);

        // local +Y 是脚尖方向。
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, new Vector3(0f, halfLength, 0f));

        // local +X 是宽度方向。
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(Vector3.zero, new Vector3(halfWidth, 0f, 0f));

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    // [说明] 绘制一个很薄的 Brush 调试盒。
    // [说明] 视觉上类似 Decal 投射盒，方便观察 Brush 的位置、朝向和覆盖范围。
    private void DrawBrushBoxGizmo(FootprintBrushDebugData data)
    {
        if (data.brushSize.x <= 0f || data.brushSize.y <= 0f)
            return;

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Matrix4x4 matrix =
            Matrix4x4.TRS(data.spawnPosition, data.spawnRotation, Vector3.one)
            * Matrix4x4.Scale(new Vector3(
                data.brushSize.x,
                data.brushSize.y,
                debugBrushBoxDepth
            ));

        Gizmos.matrix = matrix;

        Gizmos.color = new Color(0f, 1f, 1f, 0.65f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    // [说明] 在 Scene 视图里绘制文字标签。
    // [说明] 只在 Unity Editor 下生效，打包后不会包含 Handles.Label。
    private void DrawDebugLabel(Vector3 position, string text)
    {
#if UNITY_EDITOR
        if (!showDebugLabels)
            return;

        Handles.Label(position + Vector3.up * debugLabelHeight, text);
#endif
    }
}