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
public class FootprintBrushSpawner : MonoBehaviour
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
    public string brushLayerName = "FootprintBrush";


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

    private static readonly int NormalTexID = Shader.PropertyToID("_NormalTex");
    private static readonly int HeightTexID = Shader.PropertyToID("_HeightTex");
    private static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
    private static readonly int HeightStrengthID = Shader.PropertyToID("_HeightStrength");
    private static readonly int InvertHeightID = Shader.PropertyToID("_InvertHeight");

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

    private void Start()
    {
        if (characterRoot != null)
            lastStepPos = characterRoot.position;
    }

    private void Update()
    {
        if (!useDistanceSpawn)
            return;

        if (characterRoot == null || brushPrefab == null)
            return;

        Vector3 flatNow = new Vector3(characterRoot.position.x, 0f, characterRoot.position.z);
        Vector3 flatLast = new Vector3(lastStepPos.x, 0f, lastStepPos.z);

        if (Vector3.Distance(flatNow, flatLast) < stepDistance)
            return;

        if (nextLeftFoot)
            SpawnLeftFootprint(false);
        else
            SpawnRightFootprint(false);

        lastStepPos = characterRoot.position;
        nextLeftFoot = !nextLeftFoot;
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

    private void SpawnLeftFootprint(bool ignoreMovementGuard)
    {
        if (!ignoreMovementGuard && !CanSpawnFootprint())
            return;

        if (!ignoreMovementGuard && Time.time - lastLeftFootTime < minTimeBetweenSameFoot)
            return;

        lastLeftFootTime = Time.time;

        SpawnFootprint(
            true,
            leftFoot,
            leftToes,
            leftNormalTex,
            leftHeightTex
        );
    }

    private void SpawnRightFootprint(bool ignoreMovementGuard)
    {
        if (!ignoreMovementGuard && !CanSpawnFootprint())
            return;

        if (!ignoreMovementGuard && Time.time - lastRightFootTime < minTimeBetweenSameFoot)
            return;

        lastRightFootTime = Time.time;

        SpawnFootprint(
            false,
            rightFoot,
            rightToes,
            rightNormalTex,
            rightHeightTex
        );
    }


    // ============================================================
    // Spawn Logic
    // ============================================================

    private bool CanSpawnFootprint()
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

    private float GetCurrentFootForwardOffset()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkFootForwardOffset;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runFootForwardOffset;

        return walkFootForwardOffset;
    }

    private Vector2 GetCurrentFootprintSize()
    {
        if (animator == null || !HasAnimatorFloat(animator, moveSpeedParam))
            return walkFootprintSize;

        float moveSpeed = animator.GetFloat(moveSpeedParam);

        if (moveSpeed > 0.75f)
            return runFootprintSize;

        return walkFootprintSize;
    }

    private void SpawnFootprint(
        bool isLeftFoot,
        Transform footTransform,
        Transform toeTransform,
        Texture normalTex,
        Texture heightTex)
    {
        if (brushPrefab == null)
            return;

        DebugFootSide debugFootSide = isLeftFoot ? DebugFootSide.Left : DebugFootSide.Right;

        Vector3 footBonePosition;
        Vector3 toeBonePosition = Vector3.zero;
        bool hasToe = toeTransform != null;

        if (footTransform != null)
        {
            footBonePosition = footTransform.position;
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

        Vector3 footCenterPosition = footBonePosition;

        if (hasToe)
        {
            toeBonePosition = toeTransform.position;
            footCenterPosition = Vector3.Lerp(
                footBonePosition,
                toeBonePosition,
                toeBlend
            );
        }

        Vector3 rayOrigin = footCenterPosition + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;
        Vector3 rayEnd = rayOrigin + Vector3.down * totalRayDistance;

        Vector2 currentBrushSize = GetCurrentFootprintSize();

        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                totalRayDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
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

        Vector3 normal = hit.normal;

        if (useSurfaceMask && wetlandMask != null)
        {
            if (!wetlandMask.CanSpawnAt(hit.point))
            {
                if (logSpawn)
                    Debug.Log("[FootprintBrushSpawner] Surface mask rejected footprint.");

                return;
            }
        }

        Vector3 forwardOnSurface = GetFootForwardOnSurface(
            footTransform,
            toeTransform,
            normal
        );

        Vector3 rightOnSurface = Vector3.Cross(forwardOnSurface, normal).normalized;

        float currentForwardOffset = GetCurrentFootForwardOffset();
        Vector2 localOffset = isLeftFoot ? leftLocalOffset : rightLocalOffset;

        Vector3 spawnPosition =
            hit.point +
            forwardOnSurface * currentForwardOffset +
            rightOnSurface * localOffset.x +
            forwardOnSurface * localOffset.y +
            normal * surfaceOffset;

        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        float yawOffset =
            footprintYawOffset +
            (isLeftFoot ? leftYawOffset : rightYawOffset);

        spawnRotation = Quaternion.AngleAxis(yawOffset, normal) * spawnRotation;

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

        GameObject brush = Instantiate(
            brushPrefab,
            spawnPosition,
            spawnRotation
        );

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
        {
            SetLayerRecursively(brush, brushLayer);
        }
        else
        {
            Debug.LogWarning($"[FootprintBrushSpawner] 找不到 Layer: {brushLayerName}");
        }

        if (overrideBrushScale)
        {
            brush.transform.localScale = new Vector3(
                currentBrushSize.x,
                currentBrushSize.y,
                1f
            );
        }

        SetupBrushMaterial(brush, normalTex, heightTex);
        DisableBrushShadows(brush);

        if (FootprintRTManager.Active != null)
        {
            FootprintRTManager.Active.NotifyBrushSpawned();
        }
        else
        {
            Debug.LogWarning("[FootprintBrushSpawner] FootprintRTManager.Active is null.");
        }

        if (logSpawn)
        {
            Debug.Log(
                $"[FootprintBrushSpawner] Spawn {(isLeftFoot ? "Left" : "Right")} Brush. " +
                $"hit={hit.point}, spawn={spawnPosition}, normal={normal}, forward={forwardOnSurface}"
            );
        }

        Destroy(brush, brushLife);
    }

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

        forward = Vector3.ProjectOnPlane(forward, normal);

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        }

        return forward.normalized;
    }


    // ============================================================
    // Brush Setup
    // ============================================================

    private void SetupBrushMaterial(GameObject brush, Texture normalTex, Texture heightTex)
    {
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            if (normalTex != null)
                mpb.SetTexture(NormalTexID, normalTex);

            if (heightTex != null)
                mpb.SetTexture(HeightTexID, heightTex);

            mpb.SetFloat(NormalStrengthID, normalStrength);
            mpb.SetFloat(HeightStrengthID, heightStrength);
            mpb.SetFloat(InvertHeightID, invertHeight);

            r.SetPropertyBlock(mpb);
        }
    }

    private void DisableBrushShadows(GameObject brush)
    {
        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
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
    // Debug Cache
    // ============================================================

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

        if (footSide == DebugFootSide.Left)
            lastLeftDebugData = data;
        else
            lastRightDebugData = data;
    }


    // ============================================================
    // Gizmos
    // ============================================================

    private void OnDrawGizmos()
    {
        if (!showFootprintDebugGizmos)
            return;

        if (showLeftFootDebug)
            DrawBrushDebugData(lastLeftDebugData);

        if (showRightFootDebug)
            DrawBrushDebugData(lastRightDebugData);
    }

    private void DrawBrushDebugData(FootprintBrushDebugData data)
    {
        if (!data.valid)
            return;

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

    private void DrawBrushPlaneGizmo(FootprintBrushDebugData data)
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

    private void DrawDebugLabel(Vector3 position, string text)
    {
#if UNITY_EDITOR
        if (!showDebugLabels)
            return;

        Handles.Label(position + Vector3.up * debugLabelHeight, text);
#endif
    }
}