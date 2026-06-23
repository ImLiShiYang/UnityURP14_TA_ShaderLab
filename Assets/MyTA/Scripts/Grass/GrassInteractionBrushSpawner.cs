using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 草地交互 Brush 生成器。
///
/// 当前版本只支持 Animation Event：
/// 左脚落地调用 SpawnLeftGrassBrush。
/// 右脚落地调用 SpawnRightGrassBrush。
///
/// 这个脚本不使用纹理，不做每帧检测，不做距离模式。
/// 它只负责在脚落地时生成一个临时 Grass Brush Quad，
/// 然后由 GrassInteractionCamera / GrassInteractionRenderFeature 拍进 GrassInteractionRT。
/// </summary>
public class GrassInteractionBrushSpawner : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("草地交互 Brush prefab。通常是一个 Quad，材质使用 Hidden/Grass/InteractionBrush。")]
    public GameObject brushPrefab;

    [Tooltip("角色根节点。一般是 Player 根物体，用于获取整体朝向。")]
    public Transform characterRoot;

    [Tooltip("角色 Animator。用于自动获取 Humanoid 脚骨骼，也可用于读取 MoveSpeed。")]
    public Animator animator;

    [Tooltip("玩家控制器。用于读取 HasMoveInput，避免停止时动画事件残留生成 Brush。")]
    public ThirdPersonPlayerController playerController;


    // ============================================================
    // Spawn Guard
    // ============================================================

    [Header("Spawn Guard")]
    [Tooltip("是否要求玩家有移动输入才允许生成草地 Brush。")]
    public bool requireMoveInput = true;

    [Tooltip("是否要求 Animator MoveSpeed 大于阈值才允许生成草地 Brush。")]
    public bool requireAnimatorMoveSpeed = true;

    [Tooltip("Animator 中的移动速度参数名。")]
    public string moveSpeedParam = "MoveSpeed";

    [Tooltip("MoveSpeed 小于这个值时不生成草地 Brush。")]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip("同一只脚最小生成间隔，防止动画事件重复触发。")]
    public float minTimeBetweenSameFoot = 0.15f;

    [Tooltip("游戏开始后多少秒内禁止生成 Brush，避免初始化误触发。")]
    public float startBlockTime = 0.2f;

    [Tooltip("是否开启 F7 / F8 测试生成。正式效果可以关闭。")]
    public bool enableDebugKeys = true;


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

    [Tooltip("没有脚骨骼时，才使用这个左右偏移作为 fallback。")]
    public float footSideOffset = 0.18f;


    // ============================================================
    // Raycast
    // ============================================================

    [Header("Raycast")]
    [Tooltip("地面 Layer。建议只包含 Ground / Terrain / GrassGround，避免射到角色自己。")]
    public LayerMask groundMask = ~0;

    [Tooltip("从脚掌中心向上抬多少开始 Raycast。")]
    public float rayStartHeight = 0.25f;

    [Tooltip("从脚掌中心向下检测多远。")]
    public float rayDistance = 1.0f;

    [Tooltip("Brush 沿地面法线抬起一点，避免和地面重合。")]
    public float surfaceOffset = 0.03f;

    [Tooltip("脚掌距离地面不超过该值时，才向草 Shader 提供有效压草中心。")]
    [Min(0f)]
    public float maxPressGroundDistance = 0.22f;


    // ============================================================
    // Surface Mask
    // ============================================================

    [Header("Surface Mask")]
    [Tooltip("是否只允许在指定区域生成草地 Brush。")]
    public bool useSurfaceMask = false;

    [Tooltip("草地 / 湿地 / 可交互区域判断组件。只要组件上有 bool CanSpawnAt(Vector3) 方法即可。")]
    public MonoBehaviour grassSurfaceMask;


    // ============================================================
    // Placement
    // ============================================================

    [Header("Placement")]
    [Tooltip("走路时，Brush 沿脚尖方向额外偏移。用于把脚踝位置修正到脚掌中心。")]
    public float walkFootForwardOffset = 0.04f;

    [Tooltip("跑步时，Brush 沿脚尖方向额外偏移。")]
    public float runFootForwardOffset = 0.07f;

    [Tooltip("左脚局部偏移。X=左右，Y=前后。单位：米。")]
    public Vector2 leftLocalOffset = Vector2.zero;

    [Tooltip("右脚局部偏移。X=左右，Y=前后。单位：米。")]
    public Vector2 rightLocalOffset = Vector2.zero;

    [Tooltip("整体 Brush 方向修正。如果 Brush 横着或反了，可以填 90 / -90 / 180。")]
    public float grassBrushYawOffset = 0f;

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

    [Tooltip("走路压草 Brush 尺寸。X=脚宽，Y=脚长。")]
    public Vector2 walkGrassBrushSize = new Vector2(0.35f, 0.55f);

    [Tooltip("跑步压草 Brush 尺寸。")]
    public Vector2 runGrassBrushSize = new Vector2(0.42f, 0.65f);

    [Header("Brush Lifetime")]
    [Tooltip("Brush 存活时间。需要至少活到 GrassInteractionCamera 渲染一次。")]
    public float brushLife = 0.35f;


    // ============================================================
    // Layer
    // ============================================================

    [Header("Layer")]
    [Tooltip("生成出来的 Brush 会强制设置到这个 Layer。")]
    public string brushLayerName = "GrassInteractionBrush";


    // ============================================================
    // Debug
    // ============================================================

    [Header("Debug Gizmos")]
    public bool showDebugGizmos = true;
    public bool showLeftFootDebug = true;
    public bool showRightFootDebug = false;
    public bool showFootCenter = true;
    public bool showRaycast = true;
    public bool showHitPoint = true;
    public bool showSpawnPoint = true;
    public bool showBrushPlane = true;
    public bool showDebugLabels = false;

    [Range(0.005f, 0.1f)]
    public float debugPointSize = 0.015f;

    public float debugForwardLength = 0.25f;
    public float debugLabelHeight = 0.035f;

    [Header("Debug Log")]
    public bool logSpawn = false;


    // ============================================================
    // Internal State
    // ============================================================

    private float lastLeftFootTime = -999f;
    private float lastRightFootTime = -999f;

    private enum DebugFootSide
    {
        Left,
        Right
    }

    private struct GrassBrushDebugData
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
    }

    private GrassBrushDebugData lastLeftDebugData;
    private GrassBrushDebugData lastRightDebugData;


    // ============================================================
    // Unity Events
    // ============================================================

    private void Awake()
    {
        if (characterRoot == null)
            characterRoot = transform;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

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

    private void Update()
    {
        if (!enableDebugKeys)
            return;

        if (Input.GetKeyDown(KeyCode.F7))
            SpawnLeftGrassBrush(true);

        if (Input.GetKeyDown(KeyCode.F8))
            SpawnRightGrassBrush(true);
    }


    // ============================================================
    // Animation Event API
    // ============================================================

    /// <summary>
    /// Animation Event 调用：左脚落地。
    /// </summary>
    public void SpawnLeftGrassBrush()
    {
        SpawnLeftGrassBrush(false);
    }

    /// <summary>
    /// Animation Event 调用：右脚落地。
    /// </summary>
    public void SpawnRightGrassBrush()
    {
        SpawnRightGrassBrush(false);
    }

    public bool TryGetFootPressCenter(bool isLeftFoot, out Vector3 centerWS)
    {
        centerWS = Vector3.zero;

        Transform footTransform = isLeftFoot ? leftFoot : rightFoot;
        Transform toeTransform = isLeftFoot ? leftToes : rightToes;

        if (footTransform == null)
            return false;

        Vector3 footCenter = footTransform.position;

        if (toeTransform != null)
            footCenter = Vector3.Lerp(footCenter, toeTransform.position, toeBlend);

        Vector3 rayOrigin = footCenter + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;

        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                totalRayDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        float heightFromSurface = Vector3.Dot(footCenter - hit.point, hit.normal);

        if (heightFromSurface > maxPressGroundDistance)
            return false;

        centerWS = hit.point;
        return true;
    }

    private void SpawnLeftGrassBrush(bool ignoreMovementGuard)
    {
        if (!ignoreMovementGuard && !CanSpawnGrassBrush())
            return;

        if (!ignoreMovementGuard && Time.time - lastLeftFootTime < minTimeBetweenSameFoot)
            return;

        lastLeftFootTime = Time.time;

        SpawnGrassBrush(
            true,
            leftFoot,
            leftToes
        );
    }

    private void SpawnRightGrassBrush(bool ignoreMovementGuard)
    {
        if (!ignoreMovementGuard && !CanSpawnGrassBrush())
            return;

        if (!ignoreMovementGuard && Time.time - lastRightFootTime < minTimeBetweenSameFoot)
            return;

        lastRightFootTime = Time.time;

        SpawnGrassBrush(
            false,
            rightFoot,
            rightToes
        );
    }


    // ============================================================
    // Spawn Guard
    // ============================================================

    private bool CanSpawnGrassBrush()
    {
        if (Time.timeSinceLevelLoad < startBlockTime)
            return false;

        if (brushPrefab == null || characterRoot == null)
            return false;

        if (requireMoveInput && playerController != null && !playerController.HasMoveInput)
            return false;

        if (requireAnimatorMoveSpeed && animator != null && HasAnimatorFloat(animator, moveSpeedParam))
        {
            float moveSpeed = animator.GetFloat(moveSpeedParam);

            if (moveSpeed < minAnimatorMoveSpeed)
                return false;
        }

        return true;
    }

    private bool HasAnimatorFloat(Animator targetAnimator, string paramName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(paramName))
            return false;

        foreach (AnimatorControllerParameter p in targetAnimator.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }


    // ============================================================
    // Spawn Logic
    // ============================================================

    private bool SpawnGrassBrush(
    bool isLeftFoot,
    Transform footTransform,
    Transform toeTransform)
    {
        // 没有刷子预制体就无法生成压草 Brush
        if (brushPrefab == null)
            return false;

        DebugFootSide debugFootSide = isLeftFoot ? DebugFootSide.Left : DebugFootSide.Right;

        Vector3 footBonePosition;
        Vector3 toeBonePosition = Vector3.zero;
        bool hasToe = toeTransform != null;

        // 优先使用传入的脚骨骼位置。
        // 如果脚骨骼为空，就根据角色中心和左右偏移估算脚的位置，作为兜底。
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

        // 默认使用脚骨骼位置作为刷子检测中心。
        // 如果有脚趾骨骼，则在脚骨骼和脚趾骨骼之间插值，让生成点更接近脚掌中心。
        Vector3 footCenterPosition = footBonePosition;

        if (hasToe)
        {
            toeBonePosition = toeTransform.position;
            footCenterPosition = Vector3.Lerp(footBonePosition, toeBonePosition, toeBlend);
        }

        // 从脚中心上方往下发射射线，用来找到真实地面位置。
        // 这样可以避免脚骨骼本身在地面上方或下方时生成点不稳定。
        Vector3 rayOrigin = footCenterPosition + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;
        Vector3 rayEnd = rayOrigin + Vector3.down * totalRayDistance;

        Vector2 currentBrushSize = GetCurrentGrassBrushSize();

        // 向下检测地面。
        // 只有命中 groundMask 指定的地面层，才会继续生成 Brush。
        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                totalRayDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
        {
            // 射线没命中时也缓存调试数据，方便在 Scene 视图中查看射线位置和失败原因。
            CacheDebugData(
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
                    $"[GrassInteractionBrushSpawner] Raycast failed. foot={(isLeftFoot ? "Left" : "Right")}, " +
                    $"origin={rayOrigin}, distance={totalRayDistance}",
                    this
                );
            }

            return false;
        }

        // 命中的地面法线。
        // 后面会用它让 Brush 贴合斜坡或地面。
        Vector3 normal = hit.normal;

        // 可选的表面遮罩检测。
        // 用来限制只有草地区域可以生成 Brush，比如石头、道路、水面上不生成压草。
        if (useSurfaceMask && grassSurfaceMask != null)
        {
            if (!CanSurfaceSpawnAt(hit.point))
            {
                if (logSpawn)
                    Debug.Log("[GrassInteractionBrushSpawner] Surface mask rejected grass brush.", this);

                return false;
            }
        }

        // 计算脚在当前地表上的前方向和右方向。
        // forwardOnSurface 用来决定 Brush 朝向。
        // rightOnSurface 用来做左右局部偏移。
        Vector3 forwardOnSurface = GetFootForwardOnSurface(footTransform, toeTransform, normal);
        Vector3 rightOnSurface = Vector3.Cross(forwardOnSurface, normal).normalized;

        float currentForwardOffset = GetCurrentFootForwardOffset();
        Vector2 localOffset = isLeftFoot ? leftLocalOffset : rightLocalOffset;

        // 计算 Brush 最终生成位置。
        // hit.point 是地面命中点。
        // currentForwardOffset 可以把 Brush 往脚前方推一点。
        // localOffset 用来分别微调左右脚的局部位置。
        // surfaceOffset 让 Brush 稍微离开地面，避免和地面 Z-Fighting。
        Vector3 spawnPosition =
            hit.point +
            forwardOnSurface * currentForwardOffset +
            rightOnSurface * localOffset.x +
            forwardOnSurface * localOffset.y +
            normal * surfaceOffset;

        // 让 Brush 贴合地面。
        // -normal 作为 forward，通常是为了让 Brush 平面朝向地面。
        // forwardOnSurface 作为 up，用来控制 Brush 在地面上的旋转朝向。
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        // 在地面法线方向上额外旋转，用来修正刷子预制体本身的朝向。
        // 左右脚也可以有各自的角度偏移。
        float yawOffset =
            grassBrushYawOffset +
            (isLeftFoot ? leftYawOffset : rightYawOffset);

        spawnRotation = Quaternion.AngleAxis(yawOffset, normal) * spawnRotation;

        // 缓存本次成功生成的调试数据。
        // 这些数据可以用于 Gizmos 可视化：脚位置、射线、命中点、Brush 方向和大小等。
        CacheDebugData(
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

        // 根据当前状态决定 Brush 大小。
        // overrideBrushScale 为 true 时使用代码计算的大小，否则使用预制体原本的缩放。
        Vector3 brushScale = overrideBrushScale
            ? new Vector3(currentBrushSize.x, currentBrushSize.y, 1f)
            : brushPrefab.transform.localScale;

        // 实例化 Brush。
        // 这个 Brush 本身不负责压弯草，而是会被交互 RT 相机 / RenderFeature 渲染到 RT 中。
        GameObject brush = Instantiate(brushPrefab, spawnPosition, spawnRotation);
        brush.transform.localScale = brushScale;

        // 设置 Brush 所在 Layer。
        // 通常交互 RT 相机会只渲染这个 Layer，从而把 Brush 写入 GrassInteractionTex。
        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
        {
            SetLayerRecursively(brush, brushLayer);
        }
        else
        {
            Debug.LogWarning($"[GrassInteractionBrushSpawner] 找不到 Layer: {brushLayerName}", this);
        }

        // Brush 只是写 RT 用的辅助物体，不应该参与正常场景阴影。
        DisableBrushShadows(brush);

        // Brush 只需要短时间存在。
        // 如果 RT 是累积式的，Brush 被销毁后压痕仍然会保留在 RT 中。
        Destroy(brush, Mathf.Max(0.001f, brushLife));

        if (logSpawn)
        {
            Debug.Log(
                $"[GrassInteractionBrushSpawner] Spawn {(isLeftFoot ? "Left" : "Right")} Grass Brush. " +
                $"hit={hit.point}, spawn={spawnPosition}, normal={normal}, forward={forwardOnSurface}",
                this
            );
        }

        return true;
    }


    // ============================================================
    // Helper
    // ============================================================

    private float GetCurrentFootForwardOffset()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkFootForwardOffset;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runFootForwardOffset;

        return walkFootForwardOffset;
    }

    private Vector2 GetCurrentGrassBrushSize()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkGrassBrushSize;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runGrassBrushSize;

        return walkGrassBrushSize;
    }

    private Vector3 GetFootForwardOnSurface(
        Transform footTransform,
        Transform toeTransform,
        Vector3 normal)
    {
        Vector3 forward = Vector3.zero;

        if (footTransform != null && toeTransform != null)
            forward = toeTransform.position - footTransform.position;

        if (forward.sqrMagnitude < 0.0001f && footTransform != null)
            forward = footTransform.forward;

        if (forward.sqrMagnitude < 0.0001f && characterRoot != null)
            forward = characterRoot.forward;

        forward = Vector3.ProjectOnPlane(forward, normal);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);

        return forward.normalized;
    }

    private bool CanSurfaceSpawnAt(Vector3 point)
    {
        if (grassSurfaceMask == null)
            return true;

        System.Type maskType = grassSurfaceMask.GetType();
        System.Reflection.MethodInfo method = maskType.GetMethod("CanSpawnAt", new[] { typeof(Vector3) });

        if (method == null || method.ReturnType != typeof(bool))
        {
            if (logSpawn)
            {
                Debug.LogWarning(
                    $"[GrassInteractionBrushSpawner] Surface mask {maskType.Name} 缺少 bool CanSpawnAt(Vector3) 方法，已默认允许生成。",
                    this
                );
            }

            return true;
        }

        return (bool)method.Invoke(grassSurfaceMask, new object[] { point });
    }

    private void DisableBrushShadows(GameObject brush)
    {
        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }


    // ============================================================
    // Debug
    // ============================================================

    private void CacheDebugData(
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
        GrassBrushDebugData data = new GrassBrushDebugData
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
            brushSize = brushSize
        };

        if (footSide == DebugFootSide.Left)
            lastLeftDebugData = data;
        else
            lastRightDebugData = data;
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos)
            return;

        if (showLeftFootDebug)
            DrawBrushDebugData(lastLeftDebugData);

        if (showRightFootDebug)
            DrawBrushDebugData(lastRightDebugData);
    }

    private void DrawBrushDebugData(GrassBrushDebugData data)
    {
        if (!data.valid)
            return;

        string footName = data.footSide == DebugFootSide.Left ? "Left" : "Right";
        Color footColor = data.footSide == DebugFootSide.Left
            ? new Color(0.2f, 0.7f, 1f, 1f)
            : new Color(1f, 0.45f, 0.2f, 1f);

        if (showFootCenter)
        {
            Gizmos.color = footColor;
            Gizmos.DrawSphere(data.footCenterPosition, debugPointSize);
            DrawDebugLabel(data.footCenterPosition, $"{footName} Foot Center");
        }

        if (showRaycast)
        {
            Gizmos.color = data.rayHit ? Color.green : Color.red;
            Gizmos.DrawLine(data.rayOrigin, data.rayEnd);
            DrawDebugLabel(data.rayOrigin, $"{footName} Ray Origin");
        }

        if (!data.rayHit)
            return;

        if (showHitPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(data.hitPoint, debugPointSize);
            DrawDebugLabel(data.hitPoint, $"{footName} Hit");
        }

        if (showSpawnPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(data.spawnPosition, debugPointSize * 1.3f);
            DrawDebugLabel(data.spawnPosition, $"{footName} Spawn");
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(
            data.spawnPosition,
            data.spawnPosition + data.forwardOnSurface.normalized * debugForwardLength
        );

        if (showBrushPlane)
            DrawBrushPlaneGizmo(data);
    }

    private void DrawBrushPlaneGizmo(GrassBrushDebugData data)
    {
        if (data.brushSize.x <= 0f || data.brushSize.y <= 0f)
            return;

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

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, new Vector3(0f, halfLength, 0f));

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    private void DrawDebugLabel(Vector3 position, string text)
    {
#if UNITY_EDITOR
        if (!showDebugLabels)
            return;

        Handles.Label(position + Vector3.up * debugLabelHeight, text);
#endif
    }
}
