using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// RT 水波 Brush 生成器。
///
/// 这个类本身不直接计算波动方程，也不直接写 RenderTexture。
/// 它的职责是把“脚步 / 每帧脚下划水 / 武器轨迹”等交互事件，转换成场景里的临时 WaterRippleBrush 物体。
/// WaterRippleCamera 拍到这些 Brush 后写入 CurrentBrushRT，之后再由 WaterRippleRenderFeature / 水波模拟流程推进波动方程。
///
/// 当前支持的输入来源：
/// 1. Animation Event：走路 / 跑步动画在左脚、右脚落地帧调用 SpawnLeftWaterRipple / SpawnRightWaterRipple。
/// 2. 距离模式：useDistanceSpawn=true 时，根据角色 XZ 位移距离自动交替生成左右脚水波。
/// 3. 每帧脚下水波：enableEveryFrameFootRipple=true 时，脚贴近水面且发生位移就持续生成短生命周期 Brush。
/// 4. 外部系统入口：武器、水面划痕等系统可调用 SpawnWaterRippleBrushAtSurface，在指定水面点生成 Brush。
///
/// 推荐挂载位置：Player 根物体。
/// 常用依赖：Brush Prefab、WaterRippleBrushPool、Animator、ThirdPersonPlayerController、左右脚 Foot / Toes 骨骼。
/// </summary>
public class WaterRippleBrushSpawner : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("水波 Brush prefab。通常是一个 Quad，材质使用 WaterRipple/URP_WaterRippleBrush_NormalHeightSeparate。")]
    public GameObject brushPrefab;

    [Tooltip("水波 Brush 对象池。建议绑定，避免每帧水波频繁 Instantiate / Destroy。")]
    public WaterRippleBrushPool brushPool;

    [Tooltip("是否优先使用对象池生成水波 Brush。关闭后会使用 Instantiate / Destroy fallback。")]
    public bool usePooling = true;

    [Tooltip("没有找到对象池时，运行时自动创建一个子对象池。")]
    public bool autoCreatePool = true;

    [Tooltip("为额外 Brush prefab 自动创建对象池时，预热多少个 Brush。主要给武器等 override prefab 使用。")]
    public int autoCreatedExtraPoolPrewarmCount = 32;

    [Tooltip("角色根节点。一般是 Player 根物体，用于获取整体朝向。")]
    public Transform characterRoot;

    [Tooltip("角色 Animator。一般在模型子物体上，例如 Ayaka。")]
    public Animator animator;

    [Tooltip("玩家控制器。用于读取 HasMoveInput，避免停止时动画事件残留生成水波。")]
    public ThirdPersonPlayerController playerController;

    [Tooltip("攻击状态组件。用于判断攻击动画期间是否允许每帧脚下水波无视移动输入 / MoveSpeed 限制。")]
    public PlayerAttack playerAttack;

    [Header("Surface Mask")]
    [Tooltip("是否只允许在指定表面生成水波。")]
    public bool useSurfaceMask = false;

    [Tooltip("湿地 / 可生成水波区域判断组件。只要组件上有 bool CanSpawnAt(Vector3) 方法即可。")]
    public MonoBehaviour wetlandMask;


    // ============================================================
    // Spawn Mode
    // ============================================================

    [Header("移动距离模式")]
    [Tooltip("true = 按移动距离自动生成；false = 只通过动画事件生成。")]
    public bool useDistanceSpawn = false;

    [Tooltip("距离模式下，每隔多远生成一个水波。动画事件模式下不用。")]
    public float stepDistance = 0.7f;

    [Tooltip("是否要求玩家有移动输入才允许生成水波。")]
    public bool requireMoveInput = true;

    [Tooltip("是否要求 Animator MoveSpeed 大于阈值才允许生成水波。")]
    public bool requireAnimatorMoveSpeed = true;

    [Tooltip("Animator 中的移动速度参数名。")]
    public string moveSpeedParam = "MoveSpeed";

    [Tooltip("MoveSpeed 小于这个值时不生成水波。")]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip("同一只脚最小生成间隔，防止动画事件重复触发。")]
    public float minTimeBetweenSameFoot = 0.15f;

    [Tooltip("游戏开始后多少秒内禁止生成水波，避免初始化误触发。")]
    public float startBlockTime = 0.2f;

    [Tooltip("F7/F8 测试按键是否无视移动判断。调试水波位置时建议打开。")]
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

    [Header("Leg Splash Follow")]
    public Transform waterSurface;

    public Transform leftKnee;
    public Transform rightKnee;

    public ParticleSystem leftLegSplashParticle;
    public ParticleSystem rightLegSplashParticle;

    public float legSplashHeightOffset = 0.02f;
    public float legSplashForwardOffset = 0.03f;

    [Header("Foot Water Splash")]
    [Tooltip("One-shot water splash prefab. Local +Z must point along movement and local +Y along the water normal.")]
    public GameObject footWaterSplashPrefab;

    [Tooltip("Enables the one-shot splash emitted by left/right foot animation events.")]
    public bool enableFootWaterSplash = true;

    [Tooltip("Uniform splash scale while barely moving.")]
    [Min(0.01f)]
    public float minFootWaterSplashScale = 0.32f;

    [Tooltip("Uniform splash scale at or above Foot Splash Max Speed.")]
    [Min(0.01f)]
    public float maxFootWaterSplashScale = 0.58f;

    [Tooltip("Horizontal character speed, in metres per second, that maps to the maximum splash scale.")]
    [Min(0.01f)]
    public float footWaterSplashMaxSpeed = 5.5f;

    [Tooltip("Small lift along the water normal to avoid transparent sorting and z-fighting with the water surface.")]
    [Min(0f)]
    public float footWaterSplashSurfaceOffset = 0.015f;

    [Tooltip("Failsafe lifetime for spawned instances. The prefab also destroys itself when its root particle stops.")]
    [Min(0.1f)]
    public float footWaterSplashDestroyDelay = 2f;
    
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
    [Tooltip("走路时，水波沿脚尖方向额外偏移。用于把脚踝位置修正到脚掌中心。")]
    public float walkFootForwardOffset = 0.04f;

    [Tooltip("跑步时，水波沿脚尖方向额外偏移。")]
    public float runFootForwardOffset = 0.07f;

    [Tooltip("没有脚骨骼时，才使用这个左右偏移作为 fallback。")]
    public float footSideOffset = 0.18f;

    [Tooltip("整体水波贴图方向修正。如果水波横着或反了，可以填 90 / -90 / 180。")]
    public float waterRippleYawOffset = 0f;

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

    [Tooltip("走路水波尺寸。X=脚宽，Y=脚长。Quad 默认在 XY 平面。")]
    public Vector2 walkWaterRippleSize = new Vector2(0.22f, 0.36f);

    [Tooltip("跑步水波尺寸。")]
    public Vector2 runWaterRippleSize = new Vector2(0.24f, 0.40f);

    [Header("Brush Lifetime")]
    [Tooltip("Brush 存活时间。需要至少活到 WaterRippleCamera 渲染一次。")]
    public float brushLife = 0.12f;

    [Header("每帧脚下水波")]
    [Tooltip("开启后，脚贴近水面且脚掌采样点发生位移时，每帧在脚下生成一个短生命周期水波输入点。适合表现脚在水里连续划开的效果。")]
    public bool enableEveryFrameFootRipple = true;

    [Tooltip("开启每帧水波后，是否屏蔽原来的动画事件 / 距离模式落脚水波，避免两套输入同时写入造成水波过强。")]
    public bool blockStepRippleWhenEveryFrameEnabled = true;
    

    [Tooltip("每帧水波输入点的生命周期。一定要比普通 brushLife 短，否则会重复注入太多帧。")]
    public float everyFrameBrushLife = 0.035f;

    [Tooltip("同一只脚距离上一次成功生成点的移动距离小于这个值时，不生成新的每帧水波，避免站立抖动时疯狂写入。")]
    public float everyFrameMinMoveDistance = 0.025f;

    [Tooltip("脚掌中心距离命中表面超过这个高度时，认为脚已经离开水面，不生成每帧水波。")]
    public float maxEveryFrameFootHeightFromSurface = 0.16f;

    [Tooltip("攻击动画处于激活状态时，是否允许每帧脚下水波无视移动输入 / MoveSpeed 限制。用于角色攻击时站在水里也能继续产生扰动。")]
    public bool allowEveryFrameFootRippleDuringAttack = true;


    // ============================================================
    // Textures
    // ============================================================

    [Header("Left Foot Textures")]
    [Tooltip("左脚 Brush 使用的法线纹理。为空时使用 Brush 材质自己的默认纹理。")]
    public Texture leftNormalTex;

    [Tooltip("左脚 Brush 使用的高度纹理。通常用来表达水面向下凹陷 / 向上水边的形状。")]
    public Texture leftHeightTex;

    [Header("Right Foot Textures")]
    [Tooltip("右脚 Brush 使用的法线纹理。为空时使用 Brush 材质自己的默认纹理。")]
    public Texture rightNormalTex;

    [Tooltip("右脚 Brush 使用的高度纹理。通常用来表达水面向下凹陷 / 向上水边的形状。")]
    public Texture rightHeightTex;

    
    
    // ============================================================
    // Brush Material Params
    // ============================================================

    [Header("Brush Material Params")]
    [Tooltip("法线强度。")]
    public float normalStrength = 1f;

    [Tooltip("高度强度。路线 B Signed Height 一般先用 1。")]
    public float heightStrength = 1f;

    [Tooltip("路线 B Signed Height 通常为 0。如果凹凸方向反了再改成 1。")]
    public float invertHeight = 0f;


    // ============================================================
    // Layer
    // ============================================================

    [Header("Layer")]
    [Tooltip("生成出来的 Brush 会强制设置到这个 Layer。")]
    public string brushLayerName = "WaterRippleBrush";


    // ============================================================
    // Debug Gizmos
    // ============================================================

    [Header("Debug Gizmos")]
    public bool showWaterRippleDebugGizmos = true;

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

    [Tooltip("显示水波朝向。")]
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

    [Tooltip("水波朝向线显示长度。")]
    public float debugForwardLength = 0.25f;

    [Tooltip("文字标签向上偏移高度。")]
    public float debugLabelHeight = 0.035f;

    [Tooltip("Brush 调试盒厚度。只用于 Scene 视图辅助显示。")]
    public float debugBrushBoxDepth = 0.025f;

    [Header("Debug Log")]
    [Tooltip("是否打印水波生成 / 拦截日志。调试 Raycast、SurfaceMask、攻击放行、武器 Brush 时可以打开。")]
    public bool logSpawn = false;


    // ============================================================
    // Internal State
    // ============================================================

    // [说明] 距离生成模式使用：记录上一次生成脚步水波时的角色位置，以及下一次应该生成左脚还是右脚。
    private Vector3 lastStepPos;
    private bool nextLeftFoot;

    // [说明] 动画事件 / 距离模式使用：左右脚各自的生成冷却，防止同一只脚连续重复触发。
    private float lastLeftFootTime = -999f;
    private float lastRightFootTime = -999f;

    // Used for true horizontal movement speed so splash size is continuous rather than walk/run only.
    private CharacterController characterController;

    // [说明] 每帧脚下水波使用：记录上一帧成功生成点，用移动距离过滤站立抖动。
    private bool hasLastLeftEveryFramePoint;
    private bool hasLastRightEveryFramePoint;
    private Vector3 lastLeftEveryFramePoint;
    private Vector3 lastRightEveryFramePoint;

    // [说明] Brush shader 属性 ID 缓存，避免运行时反复用字符串查找属性。
    private static readonly int NormalTexID = Shader.PropertyToID("_NormalTex");
    private static readonly int HeightTexID = Shader.PropertyToID("_HeightTex");
    private static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
    private static readonly int HeightStrengthID = Shader.PropertyToID("_HeightStrength");
    private static readonly int InvertHeightID = Shader.PropertyToID("_InvertHeight");

    // [说明] 按 Brush prefab 缓存对象池。脚步和武器可以使用不同 prefab，因此需要多 prefab 对象池映射。
    private readonly Dictionary<GameObject, WaterRippleBrushPool> brushPoolsByPrefab = new Dictionary<GameObject, WaterRippleBrushPool>();

    private enum DebugFootSide
    {
        Left,
        Right
    }

    private struct WaterRippleBrushDebugData
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

    private WaterRippleBrushDebugData lastLeftDebugData;
    private WaterRippleBrushDebugData lastRightDebugData;


    // ============================================================
    // Unity Events
    // ============================================================

    // [说明] Awake 只做“引用自动补齐”，避免在 Inspector 漏绑时水波系统直接失效。
    // [说明] 这里不会生成水波，也不会写 RT，只是在运行开始前准备角色、动画器、脚骨骼等依赖。
    private void Awake()
    {
        // [说明] characterRoot 用来代表角色整体位置和朝向；如果没手动绑定，就默认使用当前脚本所在物体。
        if (characterRoot == null)
            characterRoot = transform;

        // [说明] Animator 主要用于两件事：读取 Humanoid 脚骨骼，以及读取 MoveSpeed 判断是否真的在移动。
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // [说明] playerController 用来读取 HasMoveInput，防止角色停止时动画事件残留继续生成水波。
        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

        characterController = GetComponentInParent<CharacterController>();

        // [说明] playerAttack 用来判断攻击窗口。攻击期间如果 allowEveryFrameFootRippleDuringAttack=true，
        // [说明] 每帧脚下水波可以不受 HasMoveInput / MoveSpeed 的限制。
        if (playerAttack == null)
            playerAttack = GetComponentInParent<PlayerAttack>();

        if (playerAttack == null)
            playerAttack = GetComponentInChildren<PlayerAttack>();

        // [说明] 开启对象池时，优先使用手动绑定的 WaterRippleBrushPool；没绑定则尝试在场景中自动查找。
        if (usePooling && brushPool == null)
        {
#if UNITY_2023_1_OR_NEWER
            brushPool = FindFirstObjectByType<WaterRippleBrushPool>();
#else
            brushPool = FindObjectOfType<WaterRippleBrushPool>();
#endif
        }

        // [说明] 如果场景里没有对象池，并且允许自动创建，就在当前物体下创建一个默认脚步 Brush 对象池。
        if (usePooling && brushPool == null && autoCreatePool && brushPrefab != null)
        {
            GameObject poolObject = new GameObject("WaterRippleBrushPool");
            poolObject.transform.SetParent(transform, false);

            brushPool = poolObject.AddComponent<WaterRippleBrushPool>();
            brushPool.brushPrefab = brushPrefab;
            brushPool.brushLayerName = brushLayerName;
        }

        // [说明] 对象池没有指定 prefab 时，默认使用本脚本的 brushPrefab。
        if (brushPool != null && brushPool.brushPrefab == null)
        {
            brushPool.brushPrefab = brushPrefab;
        }

        // [说明] 注册默认对象池。后续 SpawnBrushObject 会按 prefab 从 brushPoolsByPrefab 中取对应对象池。
        if (usePooling && brushPool != null && brushPool.HasPrefab)
        {
            brushPool.brushLayerName = brushLayerName;
            RegisterBrushPool(brushPool.brushPrefab, brushPool);

            if (brushPool.prewarmOnAwake && brushPool.CreatedCount == 0)
                brushPool.Prewarm();
        }

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
        
        if(leftLegSplashParticle!=null)
            leftLegSplashParticle=Instantiate(leftLegSplashParticle);
        if(rightLegSplashParticle!=null)
            rightLegSplashParticle=Instantiate(rightLegSplashParticle);
    }

    // [说明] Update 同时支持两种模式：
    // [说明] 1. 原来的距离生成模式：走够一步生成一次水波。
    // [说明] 2. 新增的每帧脚下水波：脚贴近水面且移动时，每帧生成一个短生命周期输入点。
    private void Update()
    {
        // [说明] useDistanceSpawn=true 时，不依赖动画事件，而是按角色移动距离自动交替生成左右脚水波。
        // [说明] 注意：开启每帧水波并且 blockStepRippleWhenEveryFrameEnabled=true 时，下面的 SpawnLeft/Right 会被内部拦截，避免叠加。
        if (useDistanceSpawn)
        {
            if (characterRoot != null && HasBrushSource())
            {
                // [说明] 只比较 XZ 平面距离，忽略 Y 轴高度。
                Vector3 flatNow = new Vector3(characterRoot.position.x, 0f, characterRoot.position.z);
                Vector3 flatLast = new Vector3(lastStepPos.x, 0f, lastStepPos.z);

                if (Vector3.Distance(flatNow, flatLast) >= stepDistance)
                {
                    if (nextLeftFoot)
                        SpawnLeftWaterRipple(false);
                    else
                        SpawnRightWaterRipple(false);

                    lastStepPos = characterRoot.position;
                    nextLeftFoot = !nextLeftFoot;
                }
            }
        }

        // [说明] 新增：每帧检测左右脚。
        // [说明] 只有脚正在移动、贴近水面、且通过移动保护时，才会实际生成 Brush。
        if (enableEveryFrameFootRipple)
        {
            UpdateEveryFrameFootRipple(true, leftFoot, leftToes, leftNormalTex, leftHeightTex);
            UpdateEveryFrameFootRipple(false, rightFoot, rightToes, rightNormalTex, rightHeightTex);
            
            // UpdateLegSplash(leftKnee, leftFoot, leftLegSplashParticle);
            // UpdateLegSplash(rightKnee, rightFoot, rightLegSplashParticle);
        }
    }

    private void UpdateLegSplash(Transform knee, Transform foot, ParticleSystem particle)
    {
        if (knee == null || foot == null || particle == null)
            return;

        Vector3 rayOrigin = foot.position + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit waterHit, totalRayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (particle.isPlaying)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            return;
        }

        float waterY = waterHit.point.y + legSplashHeightOffset;

        Debug.DrawLine(rayOrigin, waterHit.point, Color.cyan, 1f);

        float minY = Mathf.Min(knee.position.y, foot.position.y);
        float maxY = Mathf.Max(knee.position.y, foot.position.y);

        if (waterY < minY || waterY > maxY)
        {
            if (particle.isPlaying)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            return;
        }

        float t = Mathf.InverseLerp(foot.position.y, knee.position.y, waterY);
        Vector3 splashPos = Vector3.Lerp(foot.position, knee.position, t);

        Vector3 forward = characterRoot != null ? characterRoot.forward : transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
            splashPos += forward * legSplashForwardOffset;
        }

        splashPos.y = waterY;

        particle.transform.position = splashPos;
        particle.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        Debug.DrawRay(splashPos, Vector3.up * 0.5f, Color.red, 0.1f);

        if (!CanSpawnWaterRipple(allowAttackMotion: allowEveryFrameFootRippleDuringAttack))
        {
            if (particle.isPlaying)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            return;
        }

        if (!particle.isPlaying)
            particle.Play();
    }

    // ============================================================
    // Animation Event API
    // ============================================================

    /// <summary>
    /// Animation Event 调用：左脚落地。
    /// </summary>
    public void SpawnLeftWaterRipple()
    {
        if (!useDistanceSpawn)
            SpawnLeftWaterRipple(false);
    }

    /// <summary>
    /// Animation Event 调用：右脚落地。
    /// </summary>
    public void SpawnRightWaterRipple()
    {
        if (!useDistanceSpawn)
            SpawnRightWaterRipple(false);
    }

    // [说明] 左脚内部生成入口。
    // [说明] 动画事件和距离模式最终都会走到这里，再统一调用 SpawnWaterRipple。
    private void SpawnLeftWaterRipple(bool ignoreMovementGuard)
    {
        // [说明] 开启每帧水波时，默认屏蔽原来的落脚/距离水波，避免两套输入叠在一起。
        // [说明] ignoreMovementGuard=true 一般来自调试按键，仍然允许手动测试。
        bool suppressStepBrush =
            !ignoreMovementGuard &&
            enableEveryFrameFootRipple &&
            blockStepRippleWhenEveryFrameEnabled;

        // [说明] 不忽略移动保护时，先检查当前是否真的允许生成水波。
        // [说明] 例如刚开局、没移动输入、MoveSpeed 太低、prefab 未绑定时都会被拦截。
        if (!ignoreMovementGuard && !CanSpawnWaterRipple())
            return;

        // [说明] 同一只脚有最小时间间隔，防止同一个动画落脚点连续触发多次事件。
        if (!ignoreMovementGuard && Time.time - lastLeftFootTime < minTimeBetweenSameFoot)
            return;

        // [说明] 记录左脚这次生成时间，然后把左脚骨骼、左脚脚趾、左脚贴图传给通用生成函数。
        bool spawned = SpawnWaterRipple(
            true,
            leftFoot,
            leftToes,
            leftNormalTex,
            leftHeightTex,
            rejectIfFootTooHighFromSurface: true,
            spawnBrush: !suppressStepBrush,
            spawnFootSplashParticle: true
        );

        if (spawned)
            lastLeftFootTime = Time.time;
    }

    // [说明] 右脚内部生成入口，逻辑和左脚一致，只是传入右脚骨骼和右脚贴图。
    private void SpawnRightWaterRipple(bool ignoreMovementGuard)
    {
        // [说明] 开启每帧水波时，默认屏蔽原来的落脚/距离水波，避免两套输入叠在一起。
        bool suppressStepBrush =
            !ignoreMovementGuard &&
            enableEveryFrameFootRipple &&
            blockStepRippleWhenEveryFrameEnabled;

        if (!ignoreMovementGuard && !CanSpawnWaterRipple())
            return;

        // [说明] 右脚也单独记录冷却时间，避免右脚动画事件重复生成。
        if (!ignoreMovementGuard && Time.time - lastRightFootTime < minTimeBetweenSameFoot)
            return;

        // [说明] 记录右脚这次生成时间，然后把右脚数据交给 SpawnWaterRipple 统一处理。
        bool spawned = SpawnWaterRipple(
            false,
            rightFoot,
            rightToes,
            rightNormalTex,
            rightHeightTex,
            rejectIfFootTooHighFromSurface: true,
            spawnBrush: !suppressStepBrush,
            spawnFootSplashParticle: true
        );

        if (spawned)
            lastRightFootTime = Time.time;

    }

    

    // [说明] 每帧脚下水波入口。
    // [说明] 这个函数不会一次性补很多插值点，而是“当前帧只生成当前脚位置的一个输入点”。
    private void UpdateEveryFrameFootRipple(bool isLeftFoot,Transform footTransform,Transform toeTransform,Texture normalTex,Texture heightTex)
    {
        if (!CanSpawnWaterRipple(allowAttackMotion: allowEveryFrameFootRippleDuringAttack))
        {
            ClearEveryFrameFootRippleState(isLeftFoot);
            return;
        }

        Vector3 footSamplePosition = GetEveryFrameFootSamplePosition(isLeftFoot, footTransform, toeTransform);

        if (isLeftFoot)
        {
            if (hasLastLeftEveryFramePoint)
            {
                float moveDistance = Vector3.Distance(lastLeftEveryFramePoint, footSamplePosition);

                if (moveDistance < everyFrameMinMoveDistance)
                    return;
            }

            bool spawned = SpawnWaterRipple(
                true,
                footTransform,
                toeTransform,
                normalTex,
                heightTex,
                everyFrameBrushLife,
                true
            );

            if (spawned)
            {
                lastLeftEveryFramePoint = footSamplePosition;
                hasLastLeftEveryFramePoint = true;
            }
            else
            {
                ClearEveryFrameFootRippleState(true);
            }
        }
        else
        {
            if (hasLastRightEveryFramePoint)
            {
                float moveDistance = Vector3.Distance(lastRightEveryFramePoint, footSamplePosition);

                if (moveDistance < everyFrameMinMoveDistance)
                    return;
            }

            bool spawned = SpawnWaterRipple(
                false,
                footTransform,
                toeTransform,
                normalTex,
                heightTex,
                everyFrameBrushLife,
                true
            );

            if (spawned)
            {
                lastRightEveryFramePoint = footSamplePosition;
                hasLastRightEveryFramePoint = true;
            }
            else
            {
                ClearEveryFrameFootRippleState(false);
            }
        }
    }

    // [说明] 取得每帧水波用的脚掌采样点。
    // [说明] 这里和 SpawnWaterRipple 内部的脚掌中心计算保持一致，方便用它做“是否移动足够距离”的判断。
    private Vector3 GetEveryFrameFootSamplePosition(bool isLeftFoot,Transform footTransform,Transform toeTransform)
    {
        Vector3 footBonePosition;

        if (footTransform != null)
        {
            footBonePosition = footTransform.position;
        }
        else
        {
            Vector3 right = characterRoot != null ? characterRoot.right : Vector3.right;
            right.y = 0f;

            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            right.Normalize();

            float side = isLeftFoot ? -footSideOffset : footSideOffset;
            Vector3 rootPosition = characterRoot != null ? characterRoot.position : transform.position;
            footBonePosition = rootPosition + right * side;
        }

        if (toeTransform != null)
            return Vector3.Lerp(footBonePosition, toeTransform.position, toeBlend);

        return footBonePosition;
    }

    // [说明] 脚离开水面、Raycast 失败、移动条件不满足时，清空上一帧状态。
    // [说明] 这样下一次重新接触水面时，会先记录位置，不会和旧位置硬连。
    private void ClearEveryFrameFootRippleState(bool isLeftFoot)
    {
        if (isLeftFoot)
        {
            hasLastLeftEveryFramePoint = false;
        }
        else
        {
            hasLastRightEveryFramePoint = false;
        }
    }


    // ============================================================
    // Spawn Logic
    // ============================================================

    // [说明] 统一的水波生成条件检查。
    // [说明] 这个函数只判断“能不能生成”，不负责计算位置，也不实例化 Brush。
    // [说明] allowAttackMotion=true 时，攻击激活期间可以跳过移动输入 / MoveSpeed 过滤，避免站桩攻击时脚下水波被误拦截。
    private bool CanSpawnWaterRipple(bool allowAttackMotion = false)
    {
        // [说明] 刚进入场景的一小段时间不允许生成，避免 Animator 初始化或角色落地瞬间误触发水波。
        if (Time.timeSinceLevelLoad < startBlockTime)
            return false;

        // [说明] 没有 Brush prefab 或角色根节点时，后面的生成逻辑没有意义，直接拒绝。
        if (!HasBrushSource() || characterRoot == null)
            return false;

        // [说明] 攻击窗口放行：只影响移动输入 / MoveSpeed 过滤，不会跳过 prefab、角色根节点、开局屏蔽等基础检查。
        bool ignoreMovementGuardsForAttack = allowAttackMotion
            && playerAttack != null
            && playerAttack.IsAttackActive;

        // [说明] 如果要求有移动输入，则玩家没有按方向键 / 摇杆时不生成水波；攻击放行时会跳过这个限制。
        if (!ignoreMovementGuardsForAttack && requireMoveInput && playerController != null && !playerController.HasMoveInput)
            return false;

        // [说明] 如果要求 Animator 速度有效，则读取 MoveSpeed 参数，过滤站立、轻微抖动、过渡动画。
        if (!ignoreMovementGuardsForAttack && requireAnimatorMoveSpeed && animator != null && HasAnimatorFloat(animator, moveSpeedParam))
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

    // [说明] 根据当前移动速度选择水波前后修正值。
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

    // [说明] 根据当前移动速度选择水波尺寸。
    // [说明] 跑步时水波可以稍微更大，表现更重的踩踏感。
    private Vector2 GetCurrentWaterRippleSize()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkWaterRippleSize;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runWaterRippleSize;

        return walkWaterRippleSize;
    }

    // [说明] 核心生成函数。
    // [说明] 这里完成：脚掌中心计算、Raycast 贴地、朝向计算、Brush 实例化、贴图参数设置、通知 RT 管理器。
    // [说明] 返回 true 表示本次确实生成了 Brush；返回 false 表示被移动保护、Raycast、高度、SurfaceMask 等条件拦截。
    private bool SpawnWaterRipple(
        bool isLeftFoot,
        Transform footTransform,
        Transform toeTransform,
        Texture normalTex,
        Texture heightTex,
        float? overrideBrushLife = null,
        bool rejectIfFootTooHighFromSurface = false,
        bool spawnBrush = true,
        bool spawnFootSplashParticle = false)
    {
        GameObject sourceBrushPrefab = GetDefaultBrushPrefab();

        // [说明] 没有 Brush prefab 就无法生成临时投影 Quad，直接退出。
        if (spawnBrush && sourceBrushPrefab == null)
            return false;

        // [说明] 将 bool 类型的左右脚转换成 Debug 用枚举，方便后面缓存和绘制 Gizmos。
        DebugFootSide debugFootSide = isLeftFoot ? DebugFootSide.Left : DebugFootSide.Right;

        Vector3 footBonePosition;
        Vector3 toeBonePosition = Vector3.zero;
        bool hasToe = toeTransform != null;

        // [说明] 优先使用真实 Foot 骨骼位置。
        // [说明] 这样水波会跟动画脚步位置一致，而不是简单地跟角色中心偏移。
        if (footTransform != null)
        {
            footBonePosition = footTransform.position;
        }
        // [说明] 如果没有绑定 Foot 骨骼，则使用角色左右方向加 footSideOffset 做一个 fallback 落点。
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
            footCenterPosition = Vector3.Lerp(footBonePosition,toeBonePosition,toeBlend);
            
        }

        // [说明] Raycast 从脚掌中心上方开始，向下检测地面。
        // [说明] 这样可以兼容脚骨骼略微穿地或悬空的情况。
        Vector3 rayOrigin = footCenterPosition + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;
        Vector3 rayEnd = rayOrigin + Vector3.down * totalRayDistance;

        // [说明] 当前脚步 Brush 尺寸根据 MoveSpeed 在 walk/run 尺寸之间选择。
        // [说明] 每帧脚下水波当前复用同一套尺寸，但会使用更短的 overrideBrushLife 来避免持续重复注入。
        Vector2 currentBrushSize = GetCurrentWaterRippleSize();

        // [说明] 没有射到地面就不生成水波。
        // [说明] 同时缓存失败数据，方便 Scene 视图里看到 Raycast 为什么没命中。
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, totalRayDistance, groundMask, QueryTriggerInteraction.Ignore))
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
                    $"[WaterRippleBrushSpawner] Raycast failed. foot={(isLeftFoot ? "Left" : "Right")}, " +
                    $"origin={rayOrigin}, distance={totalRayDistance}"
                );
            }

            return false;
        }

        // [说明] Raycast 命中的地面法线，用来让 Brush 贴合斜坡或不平整表面。
        Vector3 normal = hit.normal;

        // [说明] 每帧划水模式下，脚离表面太高就认为已经离开水面。
        // [说明] 否则脚在空中摆动时，也可能因为 Raycast 命中地面而持续生成水波。
        if (rejectIfFootTooHighFromSurface)
        {
            float footHeightFromSurface = Vector3.Dot(footCenterPosition - hit.point, normal);

            if (footHeightFromSurface > maxEveryFrameFootHeightFromSurface)
            {
                if (logSpawn)
                {
                    Debug.Log(
                        $"[WaterRippleBrushSpawner] Every frame ripple rejected. foot={(isLeftFoot ? "Left" : "Right")}, " +
                        $"heightFromSurface={footHeightFromSurface}"
                    );
                }

                return false;
            }
        }

        // [说明] 表面遮罩用于限制水波只出现在水面、湿地、雪地、泥地等指定区域。
        if (useSurfaceMask && wetlandMask != null)
        {
            if (!CanSurfaceSpawnAt(hit.point))
            {
                if (logSpawn)
                    Debug.Log("[WaterRippleBrushSpawner] Surface mask rejected waterRipple.");

                return false;
            }
        }

        // [说明] 计算脚尖方向，并投影到地面切平面上。
        // [说明] 这样水波朝向会贴着地面，而不是带有脚骨骼的上下倾斜。
        Vector3 forwardOnSurface = GetFootForwardOnSurface(footTransform, toeTransform, normal);

        // [说明] 根据脚尖方向和地面法线算出水波的右方向，用于左右局部偏移。
        Vector3 rightOnSurface = Vector3.Cross(forwardOnSurface, normal).normalized;

        // [说明] 前向偏移负责把脚骨骼位置修正到脚掌落印位置。
        // [说明] localOffset 用来分别微调左脚和右脚，解决模型脚骨骼和贴图中心不完全一致的问题。
        float currentForwardOffset = GetCurrentFootForwardOffset();
        Vector2 localOffset = isLeftFoot ? leftLocalOffset : rightLocalOffset;

        // [说明] 最终 Brush 生成点 = 地面命中点 + 前后修正 + 左右局部修正 + 法线方向抬高。
        // [说明] surfaceOffset 可以避免 Brush 和地面 z-fighting。
        Vector3 spawnPosition = hit.point;
            
                               // forwardOnSurface * currentForwardOffset +
                               // rightOnSurface * localOffset.x +
                               // forwardOnSurface * localOffset.y +
                               // normal * surfaceOffset;

        // [说明] Quad 默认面朝自身 local +Z 或 -Z 的方向，这里用 -normal 让 Brush 面朝地面。
        // [说明] 第二个参数 forwardOnSurface 决定水波贴图的脚尖朝向。
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        // [说明] yawOffset 用于最终角度微调。
        // [说明] waterRippleYawOffset 是整体修正，leftYawOffset/rightYawOffset 是左右脚单独修正。
        float yawOffset =
            waterRippleYawOffset +
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

        bool brushSpawned = false;
        if (spawnBrush)
        {
            float life = overrideBrushLife.HasValue ? overrideBrushLife.Value : brushLife;
            Vector3 brushScale = overrideBrushScale
                ? new Vector3(currentBrushSize.x, currentBrushSize.y, 1f)
                : sourceBrushPrefab.transform.localScale;

            GameObject brush = SpawnBrushObject(
                sourceBrushPrefab,
                spawnPosition,
                spawnRotation,
                brushScale,
                life,
                normalTex,
                heightTex,
                1f
            );

            brushSpawned = brush != null;
        }

        bool splashSpawned = spawnFootSplashParticle &&
            SpawnFootWaterSplash(spawnPosition, normal, forwardOnSurface);

        if (!brushSpawned && !splashSpawned)
            return false;
        
        if (logSpawn)
        {
            Debug.Log(
                $"[WaterRippleBrushSpawner] Spawn {(isLeftFoot ? "Left" : "Right")} water response. " +
                $"hit={hit.point}, spawn={spawnPosition}, normal={normal}, forward={forwardOnSurface}"
            );
        }

        return true;
    }

    private bool SpawnFootWaterSplash(Vector3 surfacePosition, Vector3 surfaceNormal, Vector3 forwardOnSurface)
    {
        if (!enableFootWaterSplash || footWaterSplashPrefab == null)
            return false;

        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : Vector3.up;

        Vector3 forward = Vector3.ProjectOnPlane(forwardOnSurface, normal);
        if (forward.sqrMagnitude < 0.0001f && characterRoot != null)
            forward = Vector3.ProjectOnPlane(characterRoot.forward, normal);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);

        forward.Normalize();

        float speed01 = GetFootWaterSplashSpeed01();
        float scale = Mathf.Lerp(
            Mathf.Max(0.01f, minFootWaterSplashScale),
            Mathf.Max(0.01f, maxFootWaterSplashScale),
            speed01
        );

        Vector3 position = surfacePosition + normal * Mathf.Max(0f, footWaterSplashSurfaceOffset);
        Quaternion rotation = Quaternion.LookRotation(forward, normal);
        GameObject instance = Instantiate(footWaterSplashPrefab, position, rotation);
        instance.transform.localScale = footWaterSplashPrefab.transform.localScale * scale;
        Destroy(instance, Mathf.Max(0.1f, footWaterSplashDestroyDelay));
        return true;
    }

    private float GetFootWaterSplashSpeed01()
    {
        if (characterController != null)
        {
            Vector3 velocity = characterController.velocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            return Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, footWaterSplashMaxSpeed));
        }

        if (animator != null && HasAnimatorFloat(animator, moveSpeedParam))
            return Mathf.Clamp01(animator.GetFloat(moveSpeedParam));

        return playerController != null && playerController.HasMoveInput ? 0.5f : 0f;
    }

    // [说明] 表面遮罩兼容旧 / 新命名，只要求目标组件提供 bool CanSpawnAt(Vector3) 方法。
    private bool CanSurfaceSpawnAt(Vector3 point)
    {
        if (wetlandMask == null)
            return true;

        System.Type maskType = wetlandMask.GetType();
        System.Reflection.MethodInfo method = maskType.GetMethod("CanSpawnAt", new[] { typeof(Vector3) });

        if (method == null || method.ReturnType != typeof(bool))
        {
            if (logSpawn)
                Debug.LogWarning($"[WaterRippleBrushSpawner] Surface mask {maskType.Name} 缺少 bool CanSpawnAt(Vector3) 方法，已默认允许生成。");

            return true;
        }

        return (bool)method.Invoke(wetlandMask, new object[] { point });
    }


    // [说明] 计算水波在地面上的前方方向。
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
        // [说明] 这样水波方向始终贴着坡面。
        forward = Vector3.ProjectOnPlane(forward, normal);

        // [说明] 如果投影后方向几乎为零，就用世界前方再投影一次作为兜底。
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        }

        return forward.normalized;
    }

    /// <summary>
    /// 给武器 / 外部系统使用的水波 Brush 生成入口。
    ///
    /// 这个入口不做脚步骨骼计算，也不做向下 Raycast。
    /// 调用方需要提前算好水面点、表面法线、Brush 朝向和 Brush 尺寸。
    /// 典型调用方是 WaterRippleWeaponEventTrail：它沿剑身检测水面接触点，然后把每个接触点传进这里生成划水 Brush。
    /// </summary>
    public bool SpawnWaterRippleBrushAtSurface(
        Vector3 surfacePosition,
        Vector3 surfaceNormal,
        Vector3 forward,
        Vector2 brushSize,
        GameObject brushPrefabOverride = null,
        Texture normalTex = null,
        Texture heightTex = null,
        float? overrideBrushLife = null,
        bool checkSurfaceMask = true,
        float strengthMultiplier = 1f)
    {
        // [说明] 武器可以使用自己的 Brush prefab；不填时复用脚步水波的默认 brushPrefab。
        GameObject sourceBrushPrefab = brushPrefabOverride != null ? brushPrefabOverride : brushPrefab;

        if (sourceBrushPrefab == null)
            return false;

        // [说明] 默认水面法线朝上，避免外部传入零向量导致 Quaternion 计算异常。
        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;

        // [说明] 复用原本脚步水波的表面遮罩逻辑：如果区域不允许生成水波，就直接跳过。
        if (checkSurfaceMask && useSurfaceMask && wetlandMask != null && !CanSurfaceSpawnAt(surfacePosition))
            return false;

        // [说明] Brush 的长边方向要贴在水面切平面上，不能带有上下倾斜分量。
        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(forward, normal);

        if (forwardOnSurface.sqrMagnitude < 0.0001f && characterRoot != null)
            forwardOnSurface = Vector3.ProjectOnPlane(characterRoot.forward, normal);

        if (forwardOnSurface.sqrMagnitude < 0.0001f)
            forwardOnSurface = Vector3.ProjectOnPlane(Vector3.forward, normal);

        forwardOnSurface.Normalize();

        Vector3 spawnPosition = surfacePosition + normal * surfaceOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);
        spawnRotation = Quaternion.AngleAxis(waterRippleYawOffset, normal) * spawnRotation;

        float life = overrideBrushLife.HasValue ? overrideBrushLife.Value : brushLife;
        Vector3 brushScale = overrideBrushScale
            ? new Vector3(Mathf.Max(0.001f, brushSize.x), Mathf.Max(0.001f, brushSize.y), 1f)
            : sourceBrushPrefab.transform.localScale;

        // [说明] strengthMultiplier 用来让武器划水、重击等输入临时放大 / 缩小法线和高度强度。
        GameObject brush = SpawnBrushObject(
            sourceBrushPrefab,
            spawnPosition,
            spawnRotation,
            brushScale,
            life,
            normalTex,
            heightTex,
            strengthMultiplier
        );

        if (brush == null)
            return false;

        if (logSpawn)
        {
            Debug.Log(
                $"[WaterRippleBrushSpawner] 生成武器 Brush。位置={spawnPosition}，法线={normal}，朝向={forwardOnSurface}，强度倍率={strengthMultiplier}",
                this
            );
        }

        return true;
    }


    // ============================================================
    // Brush Setup
    // ============================================================

    // [说明] 检查当前是否有可用 Brush 来源。
    // [说明] 可以直接使用 brushPrefab，也可以使用对象池里的默认 prefab。
    private bool HasBrushSource()
    {
        return brushPrefab != null || (usePooling && brushPool != null && brushPool.HasPrefab);
    }

    // [说明] 获取默认脚步 Brush prefab。
    // [说明] 优先使用本脚本绑定的 brushPrefab；没绑定时退回对象池 prefab。
    private GameObject GetDefaultBrushPrefab()
    {
        if (brushPrefab != null)
            return brushPrefab;

        if (usePooling && brushPool != null && brushPool.HasPrefab)
            return brushPool.brushPrefab;

        return null;
    }

    // [说明] 把某个 Brush prefab 和对应对象池登记到字典里。
    // [说明] 这样脚步 Brush、武器 Brush 等不同 prefab 可以各自复用自己的池。
    private void RegisterBrushPool(GameObject sourceBrushPrefab, WaterRippleBrushPool pool)
    {
        if (sourceBrushPrefab == null || pool == null)
            return;

        brushPoolsByPrefab[sourceBrushPrefab] = pool;
    }

    // [说明] 根据本次要生成的 prefab 查找对象池。
    // [说明] 找不到时，如果允许 autoCreatePool，会为这个 override prefab 创建一个额外对象池。
    private WaterRippleBrushPool GetPoolForPrefab(GameObject sourceBrushPrefab)
    {
        if (!usePooling || sourceBrushPrefab == null)
            return null;

        if (brushPoolsByPrefab.TryGetValue(sourceBrushPrefab, out WaterRippleBrushPool cachedPool) && cachedPool != null)
            return cachedPool;

        if (brushPool != null)
        {
            if (brushPool.brushPrefab == null)
            {
                brushPool.brushPrefab = sourceBrushPrefab;

                if (brushPool.prewarmOnAwake && brushPool.CreatedCount == 0)
                    brushPool.Prewarm();
            }

            if (brushPool.brushPrefab == sourceBrushPrefab)
            {
                RegisterBrushPool(sourceBrushPrefab, brushPool);
                return brushPool;
            }
        }

        if (!autoCreatePool)
            return null;

        WaterRippleBrushPool extraPool = CreatePoolForPrefab(sourceBrushPrefab);
        RegisterBrushPool(sourceBrushPrefab, extraPool);

        return extraPool;
    }

    // [说明] 为额外 Brush prefab 创建对象池。
    // [说明] 主要用于武器 / 外部系统传入 brushPrefabOverride，而它和脚步默认 prefab 不同的情况。
    private WaterRippleBrushPool CreatePoolForPrefab(GameObject sourceBrushPrefab)
    {
        GameObject poolObject = new GameObject($"WaterRippleBrushPool_{sourceBrushPrefab.name}");
        poolObject.transform.SetParent(transform, false);

        WaterRippleBrushPool extraPool = poolObject.AddComponent<WaterRippleBrushPool>();
        WaterRippleBrushPool templatePool = brushPool;

        extraPool.brushPrefab = sourceBrushPrefab;
        extraPool.brushLayerName = brushLayerName;

        if (templatePool != null)
        {
            extraPool.maxBrushes = templatePool.maxBrushes;
            extraPool.prewarmOnAwake = templatePool.prewarmOnAwake;
            extraPool.recycleOldestWhenFull = templatePool.recycleOldestWhenFull;
            extraPool.disableRendererShadows = templatePool.disableRendererShadows;
            extraPool.disableColliders = templatePool.disableColliders;
            extraPool.showDebugGUI = templatePool.showDebugGUI;
            extraPool.debugGUISize = templatePool.debugGUISize;
            extraPool.debugGUIFontSize = templatePool.debugGUIFontSize;
            extraPool.debugGUIBackgroundColor = templatePool.debugGUIBackgroundColor;
            extraPool.debugGUIBorderColor = templatePool.debugGUIBorderColor;
            extraPool.debugGUITitleColor = templatePool.debugGUITitleColor;
            extraPool.debugGUITextColor = templatePool.debugGUITextColor;
            extraPool.debugGUIShadowColor = templatePool.debugGUIShadowColor;
            extraPool.debugGUIRefreshInterval = templatePool.debugGUIRefreshInterval;
            extraPool.debugGUIPosition = templatePool.debugGUIPosition + new Vector2(0f, brushPoolsByPrefab.Count * (templatePool.debugGUISize.y + 8f));
        }

        int prewarmCount = Mathf.Clamp(autoCreatedExtraPoolPrewarmCount, 0, extraPool.maxBrushes);

        if (extraPool.prewarmOnAwake && prewarmCount > 0)
            extraPool.Prewarm(prewarmCount);

        return extraPool;
    }

    // [说明] 真正生成 Brush 物体的统一入口。
    // [说明] 优先走对象池；没有对象池时 fallback 到 Instantiate + Destroy。
    private GameObject SpawnBrushObject(
        GameObject sourceBrushPrefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float life,
        Texture normalTex,
        Texture heightTex,
        float strengthMultiplier)
    {
        float safeLife = Mathf.Max(0.001f, life);

        WaterRippleBrushPool pool = GetPoolForPrefab(sourceBrushPrefab);

        // [说明] 有对象池时，交给 WaterRippleBrushPool 处理激活、参数设置和延迟回收。
        if (pool != null)
        {
            return pool.SpawnBrush(
                position,
                rotation,
                scale,
                safeLife,
                normalTex,
                heightTex,
                normalStrength,
                heightStrength,
                invertHeight,
                strengthMultiplier
            );
        }

        // [说明] 没有对象池时，直接实例化临时 Brush，并在 safeLife 后销毁。
        GameObject brush = Instantiate(sourceBrushPrefab, position, rotation);
        brush.transform.localScale = scale;

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
        {
            SetLayerRecursively(brush, brushLayer);
        }
        else
        {
            Debug.LogWarning($"[WaterRippleBrushSpawner] 找不到 Layer: {brushLayerName}", this);
        }
        
        // Debug.Log("strengthMultiplier: " + strengthMultiplier);
        SetupBrushMaterial(brush, normalTex, heightTex, strengthMultiplier);
        DisableBrushShadows(brush);

        Destroy(brush, safeLife);

        return brush;
    }

    // [说明] 给 Brush 的所有 Renderer 设置材质参数。
    // [说明] 使用 MaterialPropertyBlock 可以避免实例化材质，减少运行时材质副本。
    private void SetupBrushMaterial(GameObject brush, Texture normalTex, Texture heightTex, float strengthMultiplier = 1f)
    {
        // [说明] strengthMultiplier 只影响这一次生成出来的临时 Brush。
        // [说明] 脚步水波不传这个参数，会使用默认值 1；武器划水可以传更大的倍率来增强输入。
        float safeStrengthMultiplier = Mathf.Max(0f, strengthMultiplier);

        // [说明] prefab 可能包含多个 Renderer，因此这里递归获取所有子 Renderer。
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            // [说明] 先读取已有 PropertyBlock，再追加水波纹理和强度参数，避免覆盖其他外部设置。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            // [说明] NormalTex 写入 Brush shader 的 _NormalTex，用来输出水波法线到 CurrentBrushRT。
            if (normalTex != null)
                mpb.SetTexture(NormalTexID, normalTex);

            // [说明] HeightTex 写入 Brush shader 的 _HeightTex，用来输出水波凹陷/泥边高度信息。
            if (heightTex != null)
                mpb.SetTexture(HeightTexID, heightTex);

            // [说明] 这些参数控制 Brush shader 对法线和高度的解释。
            // [说明] 如果水波凹凸方向反了，优先检查 invertHeight。
            mpb.SetFloat(NormalStrengthID, normalStrength * safeStrengthMultiplier);
            mpb.SetFloat(HeightStrengthID, heightStrength * safeStrengthMultiplier);
            // Debug.Log("高度："+heightStrength * safeStrengthMultiplier);
            mpb.SetFloat(InvertHeightID, invertHeight);

            r.SetPropertyBlock(mpb);
        }
    }

    // [说明] 关闭 Brush 的阴影相关设置。
    // [说明] Brush 是给 WaterRippleCamera 写 RT 的工具物体，不应该影响主场景阴影。
    private void DisableBrushShadows(GameObject brush)
    {
        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // [说明] 递归设置 Layer，保证 prefab 子物体也能被 WaterRippleRenderFeature 的 LayerMask 捕获。
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

    // [说明] 缓存一次水波生成过程中的关键数据。
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
        WaterRippleBrushDebugData data = new WaterRippleBrushDebugData
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
    // [说明] 只负责画辅助线和辅助点，不参与水波生成。
    private void OnDrawGizmos()
    {
        if (!showWaterRippleDebugGizmos)
            return;

        if (showLeftFootDebug)
            DrawBrushDebugData(lastLeftDebugData);

        if (showRightFootDebug)
            DrawBrushDebugData(lastRightDebugData);
    }

    // [说明] 根据缓存的数据绘制某一只脚的调试信息。
    // [说明] 可以逐项开关 Foot、Toes、Raycast、Hit、SpawnPoint、Forward、BrushPlane、BrushBox。
    private void DrawBrushDebugData(WaterRippleBrushDebugData data)
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
    // [说明] 这个矩形可以帮助判断水波贴图是否和脚掌位置、方向、大小一致。
    private void DrawBrushPlaneGizmo(WaterRippleBrushDebugData data)
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
    private void DrawBrushBoxGizmo(WaterRippleBrushDebugData data)
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
