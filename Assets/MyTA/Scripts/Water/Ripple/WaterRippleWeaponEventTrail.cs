using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器 / 检测点连续划过水面时，沿运动轨迹生成水波 Brush。
///
/// 主要用途：
/// 1. 攻击动画期间，让剑身上的多个 WaveDetect 点在水面上“切水”，生成连续水波。
/// 2. 非攻击状态下，也可以让检测点以较小 Brush 生成轻微扰动。
/// 3. 根据检测点入水深度，动态调整 Brush 的强度、尺寸和生命周期。
///
/// 使用方式：
/// - 把该脚本挂到武器、角色或对应的检测点根物体上。
/// - 在 detectPointsRoot 下放置多个名字以 “WaveDetect” 开头的子物体，或手动填入 detectPoints。
/// - 攻击动画开始时通过 Animation Event 调用 BeginWeaponWaterRipple / BeginWaterCut。
/// - 攻击动画结束时通过 Animation Event 调用 EndWeaponWaterRipple / EndWaterCut。
/// - 需要配合 WaterRippleBrushSpawner 实际生成 Brush。
/// </summary>
public class WaterRippleWeaponEventTrail : MonoBehaviour
{
    /// <summary>
    /// Brush 朝向模式。
    /// 这个朝向会传给 WaterRippleBrushSpawner，用来决定生成出来的水波贴图方向。
    /// </summary>
    public enum StampDirectionMode
    {
        /// <summary>
        /// 使用检测点的运动方向作为 Brush 朝向。
        /// 适合剑身划水，因为水波方向跟随实际移动轨迹。
        /// </summary>
        Motion,

        /// <summary>
        /// 使用当前检测点自身的 forward 方向作为 Brush 朝向。
        /// 适合检测点已经手动摆好朝向的情况。
        /// </summary>
        DetectPointForward,

        /// <summary>
        /// 使用当前脚本所在物体的 forward 方向作为 Brush 朝向。
        /// 适合希望所有检测点统一跟随角色 / 武器根节点朝向的情况。
        /// </summary>
        OwnerForward
    }

    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("实际负责创建水波 Brush 的生成器。为空时会自动从父级或场景中查找。")]
    public WaterRippleBrushSpawner rippleSpawner;

    [Tooltip("检测点根节点。为空时默认从当前物体下面自动查找 WaveDetect 开头的子物体。")]
    public Transform detectPointsRoot;

    [Tooltip("用于检测是否进入水面的点。通常沿剑身摆多个 WaveDetect 点，以扩大刷子的覆盖范围。")]
    public Transform[] detectPoints;
    public PlayerAttack playerAttack;


    // ============================================================
    // Detection Window
    // ============================================================

    [Header("Detection Window")]
    [Tooltip("脚本启用后是否默认开始检测。true = 非攻击状态也检测；false = 只在动画事件打开窗口后检测。")]
    public bool activeOnEnable = true;
    public float maxAttackWindowDuration = 0.8f;
    public float attackStateValidationDelay = 0.05f;


    // ============================================================
    // Water Raycast
    // ============================================================

    [Header("Water Raycast")]
    [Tooltip("水面所在 Layer。建议只勾选水面 Layer，避免射线误命中角色或地面。")]
    public LayerMask waterMask = ~0;

    [Tooltip("可选：水面的 Tag。为空时不检查 Tag；填写后只有命中对应 Tag 才认为是水面。")]
    public string waterTag = "";

    [Tooltip("从检测点上方多高的位置开始向下发射射线。")]
    public float rayStartHeight = 2f;

    [Tooltip("检测点距离水面多近时也算入水。数值越大，越容易在刚接触水面时生成水波。")]
    public float enterSurfaceTolerance = 0.02f;

    [Tooltip("Positive values make near-surface detect points count as underwater sooner; negative values require deeper submersion.")]
    public float surfaceDetectionOffset = 0f;

    [Tooltip("射线是否命中 Trigger。水面 Collider 如果是 Trigger，一般需要保持 Collide。")]
    public QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;


    // ============================================================
    // Movement
    // ============================================================

    [Header("Movement")]
    [Tooltip("攻击窗口内，检测点至少移动这么远才生成水波。数值越小，水波越密。")]
    public float minMoveDistance = 0.04f;

    [Tooltip("非攻击状态下，检测点至少移动这么远才生成水波。通常比攻击更小，用来产生轻微连续扰动。")]
    public float nonAttackMinMoveDistance = 0.015f;

    [Tooltip("沿检测点移动轨迹每隔多少距离补一个 Brush。数值越小，剑身划水越连续，但开销也越高。")]
    public float stampSpacing = 0.12f;

    [Tooltip("每个检测点每帧最多补多少个 Brush，防止高速运动时一帧生成太多对象。")]
    public int maxStampsPerPointPerFrame = 3;

    [Tooltip("同一个检测点生成 Brush 的最小时间间隔，防止过度密集生成。")]
    public float perPointCooldown = 0.025f;


    // ============================================================
    // Attack Brush
    // ============================================================

    [Header("Attack Brush")]
    [Tooltip("攻击 / 切水时使用的 Brush prefab。通常尺寸和强度更大。")]
    public GameObject weaponBrushPrefab;

    [Tooltip("攻击 Brush 基础尺寸。最终尺寸还会乘以深度倍率。")]
    public Vector2 brushSize = new Vector2(0.3f, 0.3f);

    [Tooltip("攻击 Brush 生命周期。需要至少存活到 WaterRippleCamera 渲染一次。")]
    public float brushLife = 0.04f;


    // ============================================================
    // Non Attack Brush
    // ============================================================

    [Header("Non Attack Brush")]
    [Tooltip("非攻击状态是否使用单独的 idle 参数。关闭后非攻击也使用攻击 Brush 的尺寸和生命周期。")]
    public bool useIdleBrushSettings = true;

    [Tooltip("非攻击状态使用的 Brush prefab。为空时会回退使用 weaponBrushPrefab。")]
    public GameObject idleBrushPrefab;

    [Tooltip("非攻击状态 Brush 基础尺寸。通常比攻击 Brush 小。")]
    public Vector2 idleBrushSize = new Vector2(0.08f, 0.08f);

    [Tooltip("非攻击状态的强度倍率。这里会覆盖深度强度倍率，用于让待机 / 普通移动水波更弱。")]
    [Range(0f, 1f)]
    public float idleStrengthMultiplier = 0.55f;

    [Tooltip("非攻击状态下，深度对尺寸的影响比例。0 = 不受深度影响；1 = 完全使用深度尺寸倍率。")]
    [Range(0f, 1f)]
    public float idleSizeDepthMultiplier = 0.2f;

    [Tooltip("非攻击状态 Brush 生命周期。")]
    public float idleBrushLife = 0.06f;


    // ============================================================
    // Brush Direction
    // ============================================================

    [Header("Brush Direction")]
    [Tooltip("Brush 方向来源。剑身划水通常建议用 Motion。")]
    public StampDirectionMode directionMode = StampDirectionMode.Motion;

    [Tooltip("覆盖 Brush 材质使用的法线贴图。为空时使用 prefab / 材质默认贴图。")]
    public Texture normalTex;

    [Tooltip("覆盖 Brush 材质使用的高度贴图。为空时使用 prefab / 材质默认贴图。")]
    public Texture heightTex;


    // ============================================================
    // Depth Response
    // ============================================================

    [Header("Depth Response")]
    [Tooltip("是否根据入水深度缩放水波强度。")]
    public bool scaleStrengthByWaterDepth = true;

    [Tooltip("刚接触水面时的最小强度倍率。")]
    public float minDepthStrengthMultiplier = 0.25f;

    [Tooltip("达到 depthForMaxStrength 深度时的最大强度倍率。")]
    public float maxDepthStrengthMultiplier = 1.8f;

    [Tooltip("入水深度达到这个值时，depth01 视为 1。")]
    public float depthForMaxStrength = 0.8f;

    [Tooltip("深度到强度的曲线。横轴是归一化深度 depth01，纵轴是插值比例。")]
    public AnimationCurve depthStrengthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("是否根据入水深度缩放水波尺寸。")]
    public bool scaleSizeByWaterDepth = true;

    [Tooltip("刚接触水面时的最小尺寸倍率。")]
    public float minDepthSizeMultiplier = 0.6f;

    [Tooltip("达到最大深度时的最大尺寸倍率。")]
    public float maxDepthSizeMultiplier = 2.6f;

    [Tooltip("深度到尺寸的曲线。")]
    public AnimationCurve depthSizeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("是否根据入水深度缩放 Brush 生命周期。")]
    public bool scaleLifeByWaterDepth = true;

    [Tooltip("刚接触水面时的最小生命周期倍率。")]
    public float minDepthLifeMultiplier = 0.7f;

    [Tooltip("达到最大深度时的最大生命周期倍率。")]
    public float maxDepthLifeMultiplier = 2f;

    [Tooltip("深度到生命周期的曲线。")]
    public AnimationCurve depthLifeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);


    // ============================================================
    // Debug
    // ============================================================

    [Header("Debug")]
    [Tooltip("是否自动查找名字以 WaveDetect 开头的子物体作为检测点。")]
    public bool autoFindWaveDetectChildren = true;

    [Tooltip("选中物体时是否绘制射线、命中点和检测点 Gizmos。")]
    public bool drawDebugGizmos = true;

    [Tooltip("是否在生成 Brush 时打印日志。调试时可开，正常运行建议关闭。")]
    public bool logSpawn = false;


    // ============================================================
    // Runtime State
    // ============================================================

    // 当前是否进行水面检测。
    // 攻击动画事件打开后一定为 true；非攻击时取决于 activeOnEnable。
    private bool isDetecting;

    // 当前是否处于攻击 / 切水窗口。
    // 用来决定使用 weaponBrushPrefab 还是 idleBrushPrefab，以及使用哪套移动阈值。
    private bool isAttackWindowActive;
    private float attackWindowStartTime = -999f;

    // 每个检测点是否已经记录过上一帧位置。
    // 第一次记录位置时不生成 Brush，避免从默认值跳到当前位置产生异常长轨迹。
    private bool[] hasLastPositions;

    // 每个检测点上一帧的位置。
    private Vector3[] lastPositions;

    // 每个检测点上一次成功生成 Brush 的时间。
    private float[] lastSpawnTimes;

    // 每个检测点上一次命中水面的归一化深度，主要用于 Gizmos 调试显示。
    private float[] lastDepth01;

    // 每个检测点上一次命中的水面位置，预留给调试 / 后续扩展使用。
    private Vector3[] lastHitPositions;

    // 每个检测点是否有有效的上一次水面命中位置。
    private bool[] hasLastHitPositions;


    /// <summary>
    /// 初始化引用、自动查找检测点，并根据检测点数量创建运行时状态数组。
    /// </summary>
    private void Awake()
    {
        // 优先从父级查找 spawner，适合该脚本挂在武器或角色子物体上的情况。
        if (rippleSpawner == null)
            rippleSpawner = GetComponentInParent<WaterRippleBrushSpawner>();

        // 父级找不到时，再从场景中找一个，避免忘记手动绑定。
        if (rippleSpawner == null)
            rippleSpawner = FindObjectOfType<WaterRippleBrushSpawner>();

        if (playerAttack == null)
            playerAttack = GetComponentInParent<PlayerAttack>();

        if (playerAttack == null)
            playerAttack = GetComponentInChildren<PlayerAttack>();

        TryAutoFindDetectPoints();
        EnsureStateArrays();
    }

    /// <summary>
    /// 物体启用时重置检测状态和历史采样，避免上一次禁用前的位置影响本次轨迹。
    /// </summary>
    private void OnEnable()
    {
        isAttackWindowActive = false;
        isDetecting = activeOnEnable;
        ResetWeaponWaterRippleSamples();
    }

    /// <summary>
    /// 使用 LateUpdate 是为了尽量在动画更新、骨骼更新之后再读取检测点位置。
    /// 这样剑身上的 WaveDetect 点会更接近当前帧最终位置。
    /// </summary>
    private void LateUpdate()
    {
        EnsureStateArrays();

        if (detectPoints == null)
            return;

        if (isAttackWindowActive && !IsAttackBrushActive())
        {
            isAttackWindowActive = false;
            isDetecting = activeOnEnable;
        }

        // 逐个处理剑身 / 武器 / 身体上的检测点。
        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];
            if (point == null)
                continue;

            if (isDetecting && rippleSpawner != null)
                UpdateDetectPoint(i, point);
            else
                TrackDetectPointOnly(i, point);
        }
    }

    /// <summary>
    /// 动画事件：开始武器切水窗口。
    /// 攻击动画进入有效划水帧时调用。
    /// </summary>
    public void BeginWeaponWaterRipple()
    {
        isDetecting = true;
        isAttackWindowActive = true;
        attackWindowStartTime = Time.time;
    }

    /// <summary>
    /// 动画事件：结束武器切水窗口。
    /// 攻击动画离开有效划水帧时调用。
    /// </summary>
    public void EndWeaponWaterRipple()
    {
        isAttackWindowActive = false;
        attackWindowStartTime = -999f;
        isDetecting = activeOnEnable;

        // 结束窗口时清空历史采样，避免下一次攻击从旧位置拉出一条长水波。
        ResetWeaponWaterRippleSamples();
    }

    /// <summary>
    /// BeginWeaponWaterRipple 的别名。
    /// 保留这个方法名，方便动画事件里使用更短的名字 BeginWaterCut。
    /// </summary>
    public void BeginWaterCut()
    {
        BeginWeaponWaterRipple();
    }

    /// <summary>
    /// EndWeaponWaterRipple 的别名。
    /// </summary>
    public void EndWaterCut()
    {
        EndWeaponWaterRipple();
    }

    /// <summary>
    /// 重置所有检测点的历史位置、冷却时间和调试深度。
    /// 常用于动画窗口开始 / 结束、物体启用等时机。
    /// </summary>
    public void ResetWeaponWaterRippleSamples()
    {
        EnsureStateArrays();

        if (hasLastPositions == null)
            return;

        for (int i = 0; i < hasLastPositions.Length; i++)
        {
            hasLastPositions[i] = false;
            lastSpawnTimes[i] = -999f;
            hasLastHitPositions[i] = false;
            lastDepth01[i] = 0f;
        }
    }

    /// <summary>
    /// 判断当前是否仍然处于“攻击 Brush 有效窗口”。
    ///
    /// 注意：
    /// 这个函数只负责判断攻击 Brush 是否还有效，
    /// 它本身不会修改 isAttackWindowActive 或 isDetecting。
    ///
    /// 主要作用是做一个超时保护：
    /// 如果 BeginWeaponWaterRipple() 被调用了，
    /// 但动画事件里的 EndWeaponWaterRipple() 没有被正确调用，
    /// 那么超过 maxAttackWindowDuration 后，这里会返回 false，
    /// 避免武器一直保持攻击 Brush 状态。
    /// </summary>
    /// <returns>
    /// true  = 当前仍然可以使用攻击 Brush；
    /// false = 当前不应该再使用攻击 Brush。
    /// </returns>
    private bool IsAttackBrushActive()
    {
        // 如果攻击窗口本身没有开启，
        // 说明当前不是攻击 / 切水阶段，直接返回 false。
        if (!isAttackWindowActive)
            return false;

        // 计算从攻击窗口开始到当前帧，已经经过了多少秒。
        // attackWindowStartTime 在 BeginWeaponWaterRipple() 中记录。
        float elapsed = Time.time - attackWindowStartTime;

        // maxAttackWindowDuration 是攻击窗口的最大持续时间。
        // > 0 表示启用超时保护；
        // <= 0 可以理解为不使用这个超时限制。
        //
        // 如果攻击窗口已经超过最大持续时间，
        // 就认为攻击 Brush 已经失效。
        if (maxAttackWindowDuration > 0f && elapsed > maxAttackWindowDuration)
            return false;

        // 攻击窗口已开启，并且还没有超过最大持续时间，
        // 当前帧仍然允许使用攻击 Brush。
        return true;
    }

    
    /// <summary>
    /// 更新单个检测点：
    /// 1. 计算本帧移动距离。
    /// 2. 判断是否超过最小移动阈值和冷却时间。
    /// 3. 沿上一帧位置到当前帧位置插值采样。
    /// 4. 对每个采样点向下检测水面。
    /// 5. 根据入水深度生成对应强度 / 尺寸 / 生命周期的 Brush。
    /// </summary>
    private void UpdateDetectPoint(int index, Transform point)
    {
        Vector3 previousPosition = lastPositions[index];
        Vector3 currentPosition = point.position;

        // 第一次只有当前位置，没有上一帧位置，先记录，不生成水波。
        if (!hasLastPositions[index])
        {
            lastPositions[index] = currentPosition;
            hasLastPositions[index] = true;
            return;
        }

        Vector3 movement = currentPosition - previousPosition;
        float moveDistance = movement.magnitude;

        // 攻击状态和非攻击状态使用不同移动阈值。
        // 攻击阈值一般可以稍大，避免小抖动；非攻击阈值可以更小，保持轻微扰动连续。
        bool useAttackBrushForThisPoint = IsAttackBrushActive();
        float requiredMoveDistance = useAttackBrushForThisPoint ? minMoveDistance : nonAttackMinMoveDistance;

        // 移动太小就不生成，防止检测点静止时因为骨骼微抖不断刷 Brush。
        if (moveDistance < requiredMoveDistance)
        {
            return;
        }

        // 同一个检测点冷却未结束时，只更新位置，不生成 Brush。
        if (Time.time - lastSpawnTimes[index] < perPointCooldown)
        {
            lastPositions[index] = currentPosition;
            return;
        }

        // 计算 Brush 朝向。默认使用检测点运动方向。
        Vector3 stampForward = GetStampForward(point, movement);

        // 根据本帧移动距离决定沿路径补几个 Brush。
        // 移动越快，stampCount 越多，可以减少“断线”。
        int stampCount = useAttackBrushForThisPoint? Mathf.Clamp(Mathf.CeilToInt(moveDistance / Mathf.Max(0.001f, stampSpacing)),1,
                Mathf.Max(1, maxStampsPerPointPerFrame)): 1;
            
        bool spawnedAny = false;

        // 沿上一帧到当前帧的运动线段均匀采样。
        // 这样高速挥剑时，不会只在当前帧终点生成一个点，而是形成连续划痕。
        for (int i = 1; i <= stampCount; i++)
        {
            float t = i / (float)stampCount;
            Vector3 samplePosition = useAttackBrushForThisPoint
                ? Vector3.Lerp(previousPosition, currentPosition, t)
                : currentPosition;

            // 从采样点上方向下检测水面。没有命中水面就跳过。
            if (!TryGetWaterSurfaceHit(samplePosition, out RaycastHit waterHit, !useAttackBrushForThisPoint))
                continue;

            Vector3 waterNormal = GetUsableWaterNormal(waterHit.normal);

            // 根据检测点相对水面的高度，计算入水深度。
            // depth01 = 0 表示刚接触水面；depth01 = 1 表示达到或超过 depthForMaxStrength。
            float contactDepth;
            float depth01 = GetNormalizedDepth(samplePosition, waterHit.point, waterNormal, out contactDepth);

            // 当前是否使用攻击 Brush。
            bool useAttackBrush = useAttackBrushForThisPoint;

            // 深度影响强度：越深通常水波越强。
            float strengthMultiplier = GetDepthMultiplier(
                scaleStrengthByWaterDepth,
                minDepthStrengthMultiplier,
                maxDepthStrengthMultiplier,
                depthStrengthCurve,
                depth01
            );

            // 深度影响尺寸：越深通常影响范围越大。
            float sizeMultiplier = GetDepthMultiplier(
                scaleSizeByWaterDepth,
                minDepthSizeMultiplier,
                maxDepthSizeMultiplier,
                depthSizeCurve,
                depth01
            );

            // 深度影响生命周期：越深 Brush 可以存在稍久，让 RT 有更明显输入。
            float lifeMultiplier = GetDepthMultiplier(
                scaleLifeByWaterDepth,
                minDepthLifeMultiplier,
                maxDepthLifeMultiplier,
                depthLifeCurve,
                depth01
            );

            // 根据当前窗口选择攻击 / 非攻击 prefab。
            // idleBrushPrefab 没填时，回退到 weaponBrushPrefab，避免无法生成。
            GameObject brushPrefab = useAttackBrush ? weaponBrushPrefab : idleBrushPrefab;
            if (brushPrefab == null)
                brushPrefab = weaponBrushPrefab;

            // 根据当前窗口选择基础尺寸和生命周期。
            Vector2 baseBrushSize = useAttackBrush || !useIdleBrushSettings ? brushSize : idleBrushSize;
            float baseBrushLife = useAttackBrush || !useIdleBrushSettings ? brushLife : idleBrushLife;

            // 非攻击状态下使用更弱、更小的扰动，避免待机 / 普通移动水波过强。
            if (!useAttackBrush && useIdleBrushSettings)
            {
                strengthMultiplier = idleStrengthMultiplier;
                sizeMultiplier = Mathf.Lerp(1f, sizeMultiplier, idleSizeDepthMultiplier);
                lifeMultiplier = 1f;
            }
            


            // 交给 WaterRippleBrushSpawner 在水面命中点生成 Brush。
            // 参数中包含：命中点、水面法线、Brush 朝向、尺寸、prefab、贴图、生命周期、强度倍率。
            bool spawned = rippleSpawner.SpawnWaterRippleBrushAtSurface(
                waterHit.point,
                waterNormal,
                stampForward,
                baseBrushSize * sizeMultiplier,
                brushPrefab,
                normalTex,
                heightTex,
                baseBrushLife * lifeMultiplier,
                strengthMultiplier: strengthMultiplier
            );

            if (!spawned)
                continue;

            spawnedAny = true;
            
            string prefabName = brushPrefab != null ? brushPrefab.name : "NULL";

            Debug.Log(
                $"[WeaponTrail Spawn] " +
                $"frame={Time.frameCount}, " +
                $"trailObj={name}, " +
                $"trailID={GetInstanceID()}, " +
                $"point={point.name}, " +
                $"branch={(useAttackBrush ? "ATTACK" : "IDLE")}, " +
                $"prefab={prefabName}, " +
                $"isDetecting={isDetecting}, " +
                $"isAttackWindowActive={isAttackWindowActive}, " +
                $"attackBrushActive={IsAttackBrushActive()}, " +
                $"moveDistance={moveDistance:F4}, " +
                $"required={requiredMoveDistance:F4}, " +
                $"stampCount={stampCount}, " +
                $"size={baseBrushSize * sizeMultiplier}, " +
                $"strength={strengthMultiplier:F2}",
                this
            );

            // 记录本次命中的深度和位置，用于 Gizmos 和后续调试。
            lastDepth01[index] = depth01;
            lastHitPositions[index] = waterHit.point;
            hasLastHitPositions[index] = true;

            if (logSpawn)
            {
                Debug.Log(
                    $"[WaterRippleWeaponEventTrail] Spawn. attack={useAttackBrush}, point={point.name}, depth={contactDepth:F3}, strength={strengthMultiplier:F2}",
                    this
                );
            }
        }

        // 本帧处理完成后，更新上一帧位置。
        lastPositions[index] = currentPosition;

        // 只有真的生成过 Brush，才刷新冷却时间。
        if (spawnedAny)
            lastSpawnTimes[index] = Time.time;
    }

    /// <summary>
    /// 从采样点上方向下发射射线，检测该点是否接触 / 进入水面。
    /// </summary>
    /// <param name="samplePosition">检测点当前采样位置。</param>
    /// <param name="hit">命中的水面信息。</param>
    /// <returns>true = 命中水面且检测点已经接触或低于水面。</returns>
    private bool TryGetWaterSurfaceHit(Vector3 samplePosition, out RaycastHit hit, bool requireUnderwater = false)
    {
        // 从采样点上方开始向下射线，避免检测点已经在水面下导致射线起点错过水面。
        Vector3 origin = samplePosition + Vector3.up * Mathf.Max(0.001f, rayStartHeight);

        // 射线长度为上下各 rayStartHeight，覆盖从上方到采样点下方的范围。
        float distance = Mathf.Max(0.001f, rayStartHeight * 2f);

        if (!Physics.Raycast(origin, Vector3.down, out hit, distance, waterMask, queryTriggerInteraction))
            return false;

        // 如果指定了 waterTag，则额外检查 Tag，避免同 Layer 中其他物体被当成水面。
        if (!string.IsNullOrEmpty(waterTag) && hit.collider.tag != waterTag)
            return false;

        Vector3 waterNormal = GetUsableWaterNormal(hit.normal);

        // samplePosition 相对水面的有符号高度：
        // > 0 表示在水面法线方向上方；
        // = 0 表示刚好在水面；
        // < 0 表示已经进入水面下方。
        float heightFromSurface = Vector3.Dot(samplePosition - hit.point, waterNormal);

        float tolerance = requireUnderwater
            ? surfaceDetectionOffset
            : enterSurfaceTolerance + surfaceDetectionOffset;

        return heightFromSurface <= tolerance;
    }

    /// <summary>
    /// 计算检测点入水深度，并归一化成 0~1。
    /// </summary>
    /// <param name="samplePosition">检测点采样位置。</param>
    /// <param name="waterSurfacePosition">射线命中的水面点。</param>
    /// <param name="waterNormal">可用的水面法线。</param>
    /// <param name="contactDepth">输出真实入水深度，单位米。</param>
    /// <returns>归一化深度。0 = 刚接触；1 = 达到 depthForMaxStrength 或更深。</returns>
    private float GetNormalizedDepth(Vector3 samplePosition, Vector3 waterSurfacePosition, Vector3 waterNormal, out float contactDepth)
    {
        // 点到水面的有符号距离。
        float heightFromSurface = Vector3.Dot(samplePosition - waterSurfacePosition, waterNormal);

        // 在水面下方时 heightFromSurface 为负，取反得到入水深度。
        contactDepth = Mathf.Max(0f, -heightFromSurface);

        // 将真实深度归一化，供曲线和插值使用。
        return Mathf.Clamp01(contactDepth / Mathf.Max(0.001f, depthForMaxStrength));
    }

    /// <summary>
    /// 根据深度计算倍率。
    /// 可用于强度、尺寸、生命周期三类参数。
    /// </summary>
    private float GetDepthMultiplier(
        bool enabled,
        float minMultiplier,
        float maxMultiplier,
        AnimationCurve curve,
        float depth01)
    {
        if (!enabled)
            return 1f;

        float t = Mathf.Clamp01(depth01);

        // 曲线用于控制深度响应的非线性变化。
        // 例如让刚入水变化慢一些，深水时变化更快。
        if (curve != null)
            t = Mathf.Clamp01(curve.Evaluate(t));

        return Mathf.Lerp(minMultiplier, maxMultiplier, t);
    }

    /// <summary>
    /// 获取可靠的水面法线。
    /// 如果命中法线异常，就回退到 Vector3.up；
    /// 如果法线朝下，则翻转成朝上。
    /// </summary>
    private Vector3 GetUsableWaterNormal(Vector3 hitNormal)
    {
        Vector3 normal = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : Vector3.up;

        if (Vector3.Dot(normal, Vector3.up) < 0f)
            normal = -normal;

        return normal;
    }

    /// <summary>
    /// 不生成水波时，只跟踪检测点位置。
    /// 这样下一次重新开启检测时，不会从很久以前的位置拉出异常轨迹。
    /// </summary>
    private void TrackDetectPointOnly(int index, Transform point)
    {
        lastPositions[index] = point.position;
        hasLastPositions[index] = true;
    }

    /// <summary>
    /// 根据 directionMode 获取 Brush 朝向。
    /// </summary>
    private Vector3 GetStampForward(Transform point, Vector3 movement)
    {
        switch (directionMode)
        {
            case StampDirectionMode.DetectPointForward:
                return point.forward;

            case StampDirectionMode.OwnerForward:
                return transform.forward;

            default:
                // 默认使用运动方向。运动方向太小时回退到当前物体 forward。
                if (movement.sqrMagnitude > 0.0001f)
                    return movement.normalized;

                return transform.forward;
        }
    }

    /// <summary>
    /// 确保所有运行时数组和 detectPoints 数量一致。
    /// 如果检测点数量变化，就重新分配数组。
    /// </summary>
    private void EnsureStateArrays()
    {
        int count = detectPoints != null ? detectPoints.Length : 0;

        // 数量没变就不用重新分配。
        if (hasLastPositions != null && hasLastPositions.Length == count)
            return;

        hasLastPositions = new bool[count];
        lastPositions = new Vector3[count];
        lastSpawnTimes = new float[count];
        lastDepth01 = new float[count];
        lastHitPositions = new Vector3[count];
        hasLastHitPositions = new bool[count];

        // 初始化为很早的时间，保证第一次满足条件时不会被冷却挡住。
        for (int i = 0; i < count; i++)
            lastSpawnTimes[i] = -999f;
    }

    /// <summary>
    /// 自动从 detectPointsRoot 或当前物体的子物体中，查找名字以 WaveDetect 开头的检测点。
    /// </summary>
    private void TryAutoFindDetectPoints()
    {
        if (!autoFindWaveDetectChildren)
            return;

        // 如果已经手动填了检测点，就不自动覆盖。
        if (detectPoints != null && detectPoints.Length > 0)
            return;

        Transform root = detectPointsRoot != null ? detectPointsRoot : transform;
        List<Transform> points = new List<Transform>();

        // true 表示包括 inactive 子物体。
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == root)
                continue;

            if (child.name.StartsWith("WaveDetect"))
                points.Add(child);
        }

        // 按名字排序，保证数组顺序稳定。
        // 例如 WaveDetect_01、WaveDetect_02、WaveDetect_03。
        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        detectPoints = points.ToArray();
    }

    /// <summary>
    /// Inspector 中参数变化时执行。
    /// 这里主要做参数范围保护，避免填入明显非法值。
    /// </summary>
    private void OnValidate()
    {
        // 下面这些原本可以用于强制限制数值范围。
        // 当前保持注释状态，说明你暂时不想在 OnValidate 中强制改这些参数。
        // rayStartHeight = Mathf.Max(0.001f, rayStartHeight);
        // enterSurfaceTolerance = Mathf.Max(0f, enterSurfaceTolerance);

        // 当前 surfaceDetectionOffset 在运行逻辑里暂未使用，只做 Inspector 输入范围限制。
        surfaceDetectionOffset = Mathf.Clamp(surfaceDetectionOffset, -1f, 1f);
        maxAttackWindowDuration = Mathf.Max(0f, maxAttackWindowDuration);
        attackStateValidationDelay = Mathf.Max(0f, attackStateValidationDelay);

        // minMoveDistance = Mathf.Max(0.001f, minMoveDistance);
        // nonAttackMinMoveDistance = Mathf.Max(0.001f, nonAttackMinMoveDistance);
        // stampSpacing = Mathf.Max(0.001f, stampSpacing);
        // maxStampsPerPointPerFrame = Mathf.Max(1, maxStampsPerPointPerFrame);
        // perPointCooldown = Mathf.Max(0f, perPointCooldown);

        // brushSize.x = Mathf.Max(0.001f, brushSize.x);
        // brushSize.y = Mathf.Max(0.001f, brushSize.y);
        // brushLife = Mathf.Max(0.001f, brushLife);

        // idleBrushSize.x = Mathf.Max(0.001f, idleBrushSize.x);
        // idleBrushSize.y = Mathf.Max(0.001f, idleBrushSize.y);
        // idleBrushLife = Mathf.Max(0.001f, idleBrushLife);

        // 保证非攻击状态的强度和深度尺寸混合比例始终在 0~1。
        idleStrengthMultiplier = Mathf.Clamp01(idleStrengthMultiplier);
        idleSizeDepthMultiplier = Mathf.Clamp01(idleSizeDepthMultiplier);

        // minDepthStrengthMultiplier = Mathf.Max(0f, minDepthStrengthMultiplier);
        // maxDepthStrengthMultiplier = Mathf.Max(minDepthStrengthMultiplier, maxDepthStrengthMultiplier);
        // depthForMaxStrength = Mathf.Max(0.001f, depthForMaxStrength);
        // minDepthSizeMultiplier = Mathf.Max(0f, minDepthSizeMultiplier);
        // maxDepthSizeMultiplier = Mathf.Max(minDepthSizeMultiplier, maxDepthSizeMultiplier);
        // minDepthLifeMultiplier = Mathf.Max(0f, minDepthLifeMultiplier);
        // maxDepthLifeMultiplier = Mathf.Max(minDepthLifeMultiplier, maxDepthLifeMultiplier);
    }

    /// <summary>
    /// 选中物体时绘制调试信息。
    /// 青色到红色表示入水深度从浅到深。
    /// 黄色表示该检测点当前没有检测到水面。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || detectPoints == null)
            return;

        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];
            if (point == null)
                continue;

            Vector3 origin = point.position + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
            Vector3 end = origin + Vector3.down * Mathf.Max(0.001f, rayStartHeight * 2f);

            if (TryGetWaterSurfaceHit(point.position, out RaycastHit hit))
            {
                // 用上一次记录的 depth01 控制颜色和球体大小。
                float depth01 = lastDepth01 != null && i < lastDepth01.Length ? lastDepth01[i] : 0f;

                // 越浅越接近青色，越深越接近红色。
                Gizmos.color = Color.Lerp(Color.cyan, Color.red, depth01);

                // 射线起点到水面命中点。
                Gizmos.DrawLine(origin, hit.point);

                // 命中点显示一个球，深度越大球越大。
                Gizmos.DrawWireSphere(hit.point, Mathf.Lerp(0.03f, 0.12f, depth01));
            }
            else
            {
                // 没检测到水面时，画黄色完整射线，方便检查 LayerMask / Tag / Raycast 高度。
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(origin, end);
            }

            // 检测点自身位置。
            Gizmos.DrawWireSphere(point.position, 0.03f);
        }
    }
}
