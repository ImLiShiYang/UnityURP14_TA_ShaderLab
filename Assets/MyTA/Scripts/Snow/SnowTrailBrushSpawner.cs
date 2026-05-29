using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 雪地连续轨迹 Brush 生成器。
///
/// 这个脚本用于生成“宽雪沟”，不是单个脚印。
///
/// 推荐挂在 Player 根物体上。
///
/// 工作流程：
/// 1. 根据角色移动距离，每隔 trailStepDistance 生成一个 Trail Brush。
/// 2. Trail Brush 是一个 Quad，材质使用 Snow/SnowTrailBrush。
/// 3. Quad 被 SnowFootstepCamera 从上往下拍进 CurrentBrushRT。
/// 4. SnowAccumulate 把 CurrentBrushRT 累积进 AccumA。
/// 5. SnowSurface_RTDeform 读取 AccumA，让雪面形成连续沟槽。
///
/// 和脚底 SnowFootprintBrushSpawner 的区别：
/// - SnowFootprintBrushSpawner：负责脚底一个个小压痕。
/// - SnowTrailBrushSpawner：负责角色走过的大范围连续雪沟。
/// </summary>
public class SnowTrailBrushSpawner : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("轨迹 Brush prefab。通常是一个 Quad，材质使用 Snow/SnowTrailBrush。")]
    public GameObject trailBrushPrefab;

    [Tooltip("角色根节点。一般是 Player 根物体。为空时默认使用当前 transform。")]
    public Transform characterRoot;

    [Tooltip("玩家控制器。用于判断是否有移动输入。可为空。")]
    public ThirdPersonPlayerController playerController;

    [Tooltip("Animator。用于读取 MoveSpeed。可为空。")]
    public Animator animator;


    // ============================================================
    // Spawn Condition
    // ============================================================

    [Header("Spawn Condition")]
    [Tooltip("是否要求玩家有移动输入才生成轨迹。")]
    public bool requireMoveInput = true;

    [Tooltip("是否要求 Animator MoveSpeed 大于阈值才生成轨迹。")]
    public bool requireAnimatorMoveSpeed = false;

    [Tooltip("Animator 里的移动速度参数名。")]
    public string moveSpeedParam = "MoveSpeed";

    [Tooltip("MoveSpeed 小于这个值时不生成轨迹。")]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip("游戏开始后多少秒内不生成轨迹，避免初始化误触发。")]
    public float startBlockTime = 0.2f;


    [Header("Collider Safety")]
    [Tooltip("生成出来的 Trail Brush 是否自动关闭 Collider，避免角色踩到 Brush 被顶起来。")]
    public bool disableBrushColliders = true;
    
    // ============================================================
    // Trail Spawn
    // ============================================================

    [Header("Trail Spawn")]
    [Tooltip("每移动多少米生成一个轨迹 Brush。值越小，轨迹越连续，但生成更频繁。")]
    public float trailStepDistance = 0.18f;

    [Tooltip("如果瞬移或移动距离太大，超过这个距离会重置轨迹，不在中间补点。")]
    public float maxTeleportDistance = 3.0f;

    [Tooltip("是否在两次采样点之间补插值 Brush，让轨迹更连续。")]
    public bool fillBetweenSteps = true;

    [Tooltip("每帧最多补多少个 Brush，防止帧率低或瞬移时一次生成太多。")]
    public int maxBrushesPerFrame = 6;

    [Tooltip("Brush 存活时间。需要至少活到 SnowFootstepCamera 渲染一次。")]
    public float brushLife = 0.18f;


    // ============================================================
    // Placement
    // ============================================================

    [Header("Placement")]
    [Tooltip("地面 Layer。建议只包含 Ground / Terrain / SnowSurface，避免射到角色自己。")]
    public LayerMask groundMask = ~0;

    [Tooltip("从角色位置向上抬多少开始 Raycast。")]
    public float rayStartHeight = 1.0f;

    [Tooltip("从 rayOrigin 向下检测多远。")]
    public float rayDistance = 3.0f;

    [Tooltip("Brush 沿地面法线抬起一点，避免和雪面 z-fighting。")]
    public float surfaceOffset = 0.04f;

    [Tooltip("轨迹中心相对角色位置的前后偏移。正数向前，负数向后。")]
    public float forwardOffset = -0.1f;

    [Tooltip("轨迹中心相对角色位置的左右偏移。")]
    public float sideOffset = 0f;

    [Tooltip("Brush 额外旋转角度。轨迹方向不对时可填 90 / -90 / 180。")]
    public float yawOffset = 0f;


    // ============================================================
    // Brush Size
    // ============================================================

    [Header("Brush Size")]
    [Tooltip("是否覆盖 prefab 原始缩放。")]
    public bool overrideBrushScale = true;

    [Tooltip("轨迹 Brush 尺寸。X = 沟槽宽度，Y = 沟槽长度。Quad 默认在 XY 平面。")]
    public Vector2 trailBrushSize = new Vector2(0.9f, 1.2f);


    // ============================================================
    // Brush Material Params
    // ============================================================

    [Header("Brush Material Params")]
    [Tooltip("写入 R 通道的下陷强度。")]
    [Range(0f, 1f)]
    public float sinkStrength = 0.65f;

    [Tooltip("写入 G 通道的雪边强度。")]
    [Range(0f, 1f)]
    public float rimStrength = 0.25f;

    [Tooltip("沟底中心宽度。越大，中间平底越宽。")]
    [Range(0f, 1f)]
    public float centerWidth = 0.45f;

    [Tooltip("沟底到雪边的过渡宽度。越大，边缘越宽。")]
    [Range(0f, 1f)]
    public float edgeWidth = 0.78f;

    [Tooltip("雪边向外消失的柔和宽度。")]
    [Range(0.01f, 1f)]
    public float outerSoftness = 0.25f;

    [Tooltip("Brush 前后两端的柔和程度。")]
    [Range(0.01f, 1f)]
    public float lengthSoftness = 0.25f;


    // ============================================================
    // Layer
    // ============================================================

    [Header("Layer")]
    [Tooltip("生成出来的 Trail Brush 会强制设置到这个 Layer。必须和 SnowFootprintRenderFeature 的 LayerMask 一致。")]
    public string brushLayerName = "SnowFootprintBrush";


    // ============================================================
    // Debug
    // ============================================================

    [Header("Debug")]
    public bool logSpawn = false;

    public bool showGizmos = true;
    public Color gizmoColor = new Color(0.1f, 0.8f, 1f, 0.9f);
    public float gizmoPointSize = 0.04f;


    // ============================================================
    // Internal State
    // ============================================================

    private Vector3 lastTrailPosition;
    private Vector3 lastMoveDirection;
    private bool hasLastTrailPosition;

    private static readonly int SinkStrengthID = Shader.PropertyToID("_SinkStrength");
    private static readonly int RimStrengthID = Shader.PropertyToID("_RimStrength");
    private static readonly int CenterWidthID = Shader.PropertyToID("_CenterWidth");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int OuterSoftnessID = Shader.PropertyToID("_OuterSoftness");
    private static readonly int LengthSoftnessID = Shader.PropertyToID("_LengthSoftness");


    // ============================================================
    // Unity Events
    // ============================================================

    private void Awake()
    {
        if (characterRoot == null)
            characterRoot = transform;

        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        if (characterRoot != null)
        {
            lastTrailPosition = characterRoot.position;
            lastMoveDirection = GetFlatForward();
            hasLastTrailPosition = true;
        }
    }

    private void Update()
    {
        if (!CanSpawnTrail())
            return;

        Vector3 currentPosition = characterRoot.position;

        if (!hasLastTrailPosition)
        {
            lastTrailPosition = currentPosition;
            lastMoveDirection = GetFlatForward();
            hasLastTrailPosition = true;
            return;
        }

        Vector3 flatCurrent = FlattenXZ(currentPosition);
        Vector3 flatLast = FlattenXZ(lastTrailPosition);

        float distance = Vector3.Distance(flatCurrent, flatLast);

        if (distance > maxTeleportDistance)
        {
            lastTrailPosition = currentPosition;
            lastMoveDirection = GetFlatForward();

            if (logSpawn)
                Debug.Log("[SnowTrailBrushSpawner] Teleport distance detected. Reset trail.");

            return;
        }

        if (distance < trailStepDistance)
            return;

        Vector3 moveDirection = flatCurrent - flatLast;

        if (moveDirection.sqrMagnitude < 0.0001f)
            moveDirection = GetFlatForward();
        else
            moveDirection.Normalize();

        lastMoveDirection = moveDirection;

        if (fillBetweenSteps)
        {
            int count = Mathf.FloorToInt(distance / trailStepDistance);
            count = Mathf.Clamp(count, 1, maxBrushesPerFrame);

            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                Vector3 spawnCenter = Vector3.Lerp(lastTrailPosition, currentPosition, t);
                SpawnTrailBrush(spawnCenter, moveDirection);
            }
        }
        else
        {
            SpawnTrailBrush(currentPosition, moveDirection);
        }

        lastTrailPosition = currentPosition;
    }


    // ============================================================
    // Spawn Conditions
    // ============================================================

    private bool CanSpawnTrail()
    {
        if (Time.timeSinceLevelLoad < startBlockTime)
            return false;

        if (characterRoot == null || trailBrushPrefab == null)
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

    private void SpawnTrailBrush(Vector3 centerPosition, Vector3 moveDirection)
    {
        if (!TryGetGroundPoint(centerPosition, moveDirection, out Vector3 spawnPosition, out Vector3 groundNormal, out Vector3 forwardOnSurface))
            return;

        Quaternion spawnRotation = Quaternion.LookRotation(-groundNormal, forwardOnSurface);

        spawnRotation = Quaternion.AngleAxis(yawOffset, groundNormal) * spawnRotation;

        GameObject brush = Instantiate(trailBrushPrefab, spawnPosition, spawnRotation);

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
        {
            SetLayerRecursively(brush, brushLayer);
        }
        else
        {
            Debug.LogWarning($"[SnowTrailBrushSpawner] 找不到 Layer: {brushLayerName}");
        }

        if (overrideBrushScale)
        {
            brush.transform.localScale = new Vector3(
                trailBrushSize.x,
                trailBrushSize.y,
                1f
            );
        }

        SetupBrushMaterial(brush);
        DisableBrushShadows(brush);

        if (SnowFootprintRTManager.Active != null)
        {
            SnowFootprintRTManager.Active.NotifyBrushSpawned();
        }
        else
        {
            Debug.LogWarning("[SnowTrailBrushSpawner] SnowFootprintRTManager.Active is null.");
        }

        if (logSpawn)
        {
            Debug.Log(
                $"[SnowTrailBrushSpawner] Spawn Trail Brush. " +
                $"position={spawnPosition}, forward={forwardOnSurface}, normal={groundNormal}"
            );
        }

        Destroy(brush, brushLife);
    }

    private bool TryGetGroundPoint(
        Vector3 centerPosition,
        Vector3 moveDirection,
        out Vector3 spawnPosition,
        out Vector3 groundNormal,
        out Vector3 forwardOnSurface)
    {
        Vector3 flatForward = moveDirection;

        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = GetFlatForward();

        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        Vector3 flatRight = new Vector3(flatForward.z, 0f, -flatForward.x);

        Vector3 rayCenter =
            centerPosition +
            flatForward * forwardOffset +
            flatRight * sideOffset;

        Vector3 rayOrigin = rayCenter + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, totalRayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (logSpawn)
            {
                Debug.LogWarning(
                    $"[SnowTrailBrushSpawner] Raycast failed. " +
                    $"origin={rayOrigin}, distance={totalRayDistance}"
                );
            }

            spawnPosition = Vector3.zero;
            groundNormal = Vector3.up;
            forwardOnSurface = flatForward;
            return false;
        }

        groundNormal = hit.normal;

        forwardOnSurface = Vector3.ProjectOnPlane(flatForward, groundNormal);

        if (forwardOnSurface.sqrMagnitude < 0.0001f)
            forwardOnSurface = Vector3.ProjectOnPlane(GetFlatForward(), groundNormal);

        if (forwardOnSurface.sqrMagnitude < 0.0001f)
            forwardOnSurface = Vector3.forward;

        forwardOnSurface.Normalize();

        spawnPosition = hit.point + groundNormal * surfaceOffset;

        return true;
    }


    // ============================================================
    // Brush Setup
    // ============================================================

    private void SetupBrushMaterial(GameObject brush)
    {
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            mpb.SetFloat(SinkStrengthID, sinkStrength);
            mpb.SetFloat(RimStrengthID, rimStrength);
            mpb.SetFloat(CenterWidthID, centerWidth);
            mpb.SetFloat(EdgeWidthID, edgeWidth);
            mpb.SetFloat(OuterSoftnessID, outerSoftness);
            mpb.SetFloat(LengthSoftnessID, lengthSoftness);

            r.SetPropertyBlock(mpb);
        }
    }

    private void DisableBrushShadows(GameObject brush)
    {
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
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
    // Utility
    // ============================================================

    private Vector3 FlattenXZ(Vector3 v)
    {
        return new Vector3(v.x, 0f, v.z);
    }

    private Vector3 GetFlatForward()
    {
        if (characterRoot == null)
            return Vector3.forward;

        Vector3 forward = characterRoot.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return forward.normalized;
    }


    // ============================================================
    // Gizmos
    // ============================================================

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos || characterRoot == null)
            return;

        Vector3 position = characterRoot.position;
        Vector3 forward = Application.isPlaying ? lastMoveDirection : GetFlatForward();

        if (forward.sqrMagnitude < 0.0001f)
            forward = GetFlatForward();

        forward.y = 0f;
        forward.Normalize();

        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        Vector3 center =
            position +
            forward * forwardOffset +
            right * sideOffset;

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(center, gizmoPointSize);

        Gizmos.DrawLine(center, center + forward * trailBrushSize.y * 0.5f);
        Gizmos.DrawLine(center, center - forward * trailBrushSize.y * 0.5f);
        Gizmos.DrawLine(center, center + right * trailBrushSize.x * 0.5f);
        Gizmos.DrawLine(center, center - right * trailBrushSize.x * 0.5f);

        Matrix4x4 oldMatrix = Gizmos.matrix;

        Quaternion rotation = Quaternion.LookRotation(Vector3.down, forward);

        Gizmos.matrix = Matrix4x4.TRS(center, rotation, new Vector3(trailBrushSize.x, trailBrushSize.y, 0.02f));
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = oldMatrix;
    }
}