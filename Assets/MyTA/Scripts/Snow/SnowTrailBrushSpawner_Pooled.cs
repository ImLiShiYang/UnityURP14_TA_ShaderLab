using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 雪地连续轨迹 Brush 生成器。
///
/// 这个版本支持对象池：
/// - 优先从 SnowTrailBrushPool 获取 Brush。
/// - 如果没有绑定对象池，可以回退到 Instantiate / Destroy。
///
/// 推荐：
/// Player 上挂 SnowTrailBrushSpawner_Pooled。
/// 场景中单独放一个 SnowTrailBrushPool，或者作为 Player 子物体。
/// </summary>
public class SnowTrailBrushSpawner_Pooled : MonoBehaviour
{
    [Header("References")]
    [Tooltip("轨迹 Brush prefab。没有对象池时会直接 Instantiate；使用对象池时也可留空。")]
    public GameObject trailBrushPrefab;

    [Tooltip("Trail Brush 对象池。建议绑定，避免移动时频繁 Instantiate / Destroy。")]
    public SnowTrailBrushPool brushPool;

    [Tooltip("是否优先使用对象池。")]
    public bool usePool = true;

    [Tooltip("角色根节点。一般是 Player 根物体。为空时默认使用当前 transform。")]
    public Transform characterRoot;

    [Tooltip("玩家控制器。用于判断是否有移动输入。可为空。")]
    public ThirdPersonPlayerController playerController;

    [Tooltip("Animator。用于读取 MoveSpeed。可为空。")]
    public Animator animator;

    [Header("Spawn Condition")]
    public bool requireMoveInput = true;
    public bool requireAnimatorMoveSpeed = false;
    public string moveSpeedParam = "MoveSpeed";
    public float minAnimatorMoveSpeed = 0.05f;
    public float startBlockTime = 0.2f;

    [Header("Trail Spawn")]
    [Tooltip("每移动多少米生成一个轨迹 Brush。值越小越连续，但更耗。")]
    public float trailStepDistance = 0.15f;

    [Tooltip("如果瞬移或移动距离太大，超过这个距离会重置轨迹。")]
    public float maxTeleportDistance = 3.0f;

    [Tooltip("是否在两次采样点之间补插值 Brush。")]
    public bool fillBetweenSteps = true;

    [Tooltip("每帧最多补多少个 Brush。")]
    public int maxBrushesPerFrame = 3;

    [Tooltip("Brush 存活时间。需要至少活到 SnowFootstepCamera 渲染一次。")]
    public float brushLife = 0.1f;

    [Header("Placement")]
    [Tooltip("地面 Layer。建议只包含隐藏 GroundCollider / Terrain，不要包含 Player / SnowFootprintBrush。")]
    public LayerMask groundMask = ~0;

    public float rayStartHeight = 1.0f;
    public float rayDistance = 3.0f;
    public float surfaceOffset = 0.04f;
    public float forwardOffset = -0.1f;
    public float sideOffset = 0f;
    public float yawOffset = 0f;

    [Header("Brush Size")]
    public bool overrideBrushScale = true;
    public Vector2 trailBrushSize = new Vector2(1.2f, 1.8f);

    [Header("Brush Material Params")]
    [Range(0f, 1f)] public float sinkStrength = 0.75f;
    [Range(0f, 1f)] public float rimStrength = 0f;
    [Range(0f, 1f)] public float centerWidth = 0.55f;
    [Range(0f, 1f)] public float edgeWidth = 0.9f;
    [Range(0.01f, 1f)] public float outerSoftness = 0.6f;
    [Range(0.01f, 1f)] public float lengthSoftness = 0.65f;

    [Header("Layer")]
    public string brushLayerName = "SnowFootprintBrush";

    [Header("Collider Safety")]
    [Tooltip("非对象池回退模式下，自动关闭 Brush Collider。对象池内部也会做同样处理。")]
    public bool disableBrushColliders = true;

    [Header("Debug")]
    public bool logSpawn = false;
    public bool showGizmos = true;
    public Color gizmoColor = new Color(0.1f, 0.8f, 1f, 0.9f);
    public float gizmoPointSize = 0.04f;

    private Vector3 lastTrailPosition;
    private Vector3 lastMoveDirection;
    private bool hasLastTrailPosition;

    private static readonly int SinkStrengthID = Shader.PropertyToID("_SinkStrength");
    private static readonly int RimStrengthID = Shader.PropertyToID("_RimStrength");
    private static readonly int CenterWidthID = Shader.PropertyToID("_CenterWidth");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int OuterSoftnessID = Shader.PropertyToID("_OuterSoftness");
    private static readonly int LengthSoftnessID = Shader.PropertyToID("_LengthSoftness");

    private void Awake()
    {
        if (characterRoot == null)
            characterRoot = transform;

        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (brushPool == null)
            brushPool = GetComponentInChildren<SnowTrailBrushPool>();

        if (brushPool == null)
            brushPool = GetComponentInParent<SnowTrailBrushPool>();
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
                Debug.Log("[SnowTrailBrushSpawner_Pooled] Teleport distance detected. Reset trail.");

            return;
        }

        if (distance < trailStepDistance)
            return;

        Vector3 moveDirection = flatCurrent - flatLast;

        if (moveDirection.sqrMagnitude < 0.0001f)
            moveDirection = GetFlatForward();
        else
            moveDirection.Normalize();

        Vector3 fromDirection = lastMoveDirection;

        if (fromDirection.sqrMagnitude < 0.0001f)
            fromDirection = moveDirection;

        fromDirection.y = 0f;
        moveDirection.y = 0f;

        if (fromDirection.sqrMagnitude < 0.0001f)
            fromDirection = Vector3.forward;

        if (moveDirection.sqrMagnitude < 0.0001f)
            moveDirection = fromDirection;

        fromDirection.Normalize();
        moveDirection.Normalize();

        if (fillBetweenSteps)
        {
            int count = Mathf.FloorToInt(distance / trailStepDistance);
            count = Mathf.Clamp(count, 1, maxBrushesPerFrame);

            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                Vector3 spawnCenter = Vector3.Lerp(lastTrailPosition, currentPosition, t);

                // 拐弯优化：每个补点的方向从上一段方向平滑过渡到当前移动方向。
                Vector3 stepDirection = Vector3.Slerp(fromDirection, moveDirection, t);

                if (stepDirection.sqrMagnitude < 0.0001f)
                    stepDirection = moveDirection;

                stepDirection.y = 0f;
                stepDirection.Normalize();

                SpawnTrailBrush(spawnCenter, stepDirection);
            }
        }
        else
        {
            SpawnTrailBrush(currentPosition, moveDirection);
        }

        lastTrailPosition = currentPosition;
        lastMoveDirection = moveDirection;
    }

    private bool CanSpawnTrail()
    {
        if (Time.timeSinceLevelLoad < startBlockTime)
            return false;

        if (characterRoot == null)
            return false;

        bool hasPool = usePool && brushPool != null && brushPool.HasPrefab;
        bool hasFallbackPrefab = trailBrushPrefab != null;

        if (!hasPool && !hasFallbackPrefab)
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

    private void SpawnTrailBrush(Vector3 centerPosition, Vector3 moveDirection)
    {
        if (!TryGetGroundPoint(centerPosition, moveDirection, out Vector3 spawnPosition, out Vector3 groundNormal, out Vector3 forwardOnSurface))
            return;

        Quaternion spawnRotation = Quaternion.LookRotation(-groundNormal, forwardOnSurface);
        spawnRotation = Quaternion.AngleAxis(yawOffset, groundNormal) * spawnRotation;

        Vector3 brushScale = overrideBrushScale
            ? new Vector3(trailBrushSize.x, trailBrushSize.y, 1f)
            : Vector3.one;

        GameObject brush = null;

        if (usePool && brushPool != null && brushPool.HasPrefab)
        {
            brush = brushPool.SpawnBrush(
                spawnPosition,
                spawnRotation,
                brushScale,
                brushLife,
                sinkStrength,
                rimStrength,
                centerWidth,
                edgeWidth,
                outerSoftness,
                lengthSoftness
            );
        }
        else
        {
            brush = Instantiate(trailBrushPrefab, spawnPosition, spawnRotation);
            SetupFallbackBrush(brush, brushScale);
            Destroy(brush, brushLife);
        }

        if (brush == null)
            return;

        if (SnowFootprintRTManager.Active != null)
            SnowFootprintRTManager.Active.NotifyBrushSpawned();
        else
            Debug.LogWarning("[SnowTrailBrushSpawner_Pooled] SnowFootprintRTManager.Active is null.");

        if (logSpawn)
        {
            Debug.Log(
                $"[SnowTrailBrushSpawner_Pooled] Spawn Trail Brush. " +
                $"position={spawnPosition}, forward={forwardOnSurface}, normal={groundNormal}, pool={(usePool && brushPool != null)}"
            );
        }
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

        if (!TryRaycastGround(rayOrigin, totalRayDistance, out RaycastHit hit))
        {
            if (logSpawn)
            {
                Debug.LogWarning(
                    $"[SnowTrailBrushSpawner_Pooled] Raycast failed. " +
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

    private bool TryRaycastGround(Vector3 rayOrigin, float totalRayDistance, out RaycastHit bestHit)
    {
        bestHit = default;

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            totalRayDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0)
            return false;

        float bestDistance = float.MaxValue;
        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            if (hit.collider == null)
                continue;

            Transform hitTransform = hit.collider.transform;

            if (characterRoot != null && hitTransform.IsChildOf(characterRoot))
                continue;

            if (brushLayer >= 0 && hit.collider.gameObject.layer == brushLayer)
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        return bestDistance < float.MaxValue;
    }

    private void SetupFallbackBrush(GameObject brush, Vector3 brushScale)
    {
        if (brush == null)
            return;

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
            SetLayerRecursively(brush, brushLayer);
        else
            Debug.LogWarning($"[SnowTrailBrushSpawner_Pooled] 找不到 Layer: {brushLayerName}");

        if (overrideBrushScale)
            brush.transform.localScale = brushScale;

        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in renderers)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

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

        if (disableBrushColliders)
        {
            Collider[] colliders = brush.GetComponentsInChildren<Collider>(true);

            foreach (Collider c in colliders)
                c.enabled = false;
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

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

        Vector3 center = position + forward * forwardOffset + right * sideOffset;

        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(center, gizmoPointSize);

        Gizmos.DrawLine(center, center + forward * trailBrushSize.y * 0.5f);
        Gizmos.DrawLine(center, center - forward * trailBrushSize.y * 0.5f);
        Gizmos.DrawLine(center, center + right * trailBrushSize.x * 0.5f);
        Gizmos.DrawLine(center, center - right * trailBrushSize.x * 0.5f);

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Quaternion rotation = Quaternion.LookRotation(Vector3.down, forward);

        Gizmos.matrix = Matrix4x4.TRS(
            center,
            rotation,
            new Vector3(trailBrushSize.x, trailBrushSize.y, 0.02f)
        );

        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = oldMatrix;
    }
}
