using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器划水检测器。
///
/// 当前版本不再使用固定水面高度。
/// 它会从剑上的 WaveDetect 检测点上方往下 Raycast：
/// - 如果向下打到水面 Collider，并且检测点低于命中点，说明这个检测点已经进入水面。
/// - Brush 直接生成在 Raycast 命中的 hit.point 上。
/// - 检测窗口由攻击动画事件 BeginWeaponWaterRipple / EndWeaponWaterRipple 控制。
/// </summary>
public class WaterRippleWeaponEventTrail : MonoBehaviour
{
    public enum StampDirectionMode
    {
        // 用检测点这一帧的移动方向，通常最自然。
        Motion,

        // 用 WaveDetect 物体自己的 forward。
        DetectPointForward,

        // 用挂载本脚本的物体 forward。
        OwnerForward
    }

    [Header("References")]
    [Tooltip("负责真正实例化水波 Brush 的水波生成器。")]
    public WaterRippleBrushSpawner rippleSpawner;

    [Tooltip("检测点根节点。可以拖 Great_Sword，脚本会自动找下面的 WaveDetect 子物体。")]
    public Transform detectPointsRoot;

    [Tooltip("剑刃上的检测点。每个点都会向上 Raycast 检测水面。")]
    public Transform[] detectPoints;

    [Header("Animation Event Window")]
    [Tooltip("是否一启用脚本就开始检测。通常保持 false，由攻击动画事件开关。")]
    public bool activeOnEnable = false;

    [Header("Water Raycast")]
    [Tooltip("水面所在 Layer。建议只包含 Water，避免射线打到角色或场景其他物体。")]
    public LayerMask waterMask = ~0;

    [Tooltip("水面 Tag。为空时不检查 Tag，只按 Water Mask 判断。")]
    public string waterTag = "Water";

    [Tooltip("从检测点上方多高的位置开始向下检测水面。要高于水面最大波动高度。")]
    public float rayStartHeight = 2f;

    [Tooltip("检测点允许高出水面多少仍算接触。用于修正模型点和视觉剑刃的少量误差。")]
    public float enterSurfaceTolerance = 0.02f;

    [Tooltip("水面 Collider 如果是 Trigger，需要设为 Collide。")]
    public QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Movement")]
    [Tooltip("检测点一帧内移动超过这个距离才生成水波，避免武器静止泡在水里一直刷。")]
    public float minMoveDistance = 0.04f;

    [Tooltip("快速挥剑时，每隔多远补一个 Brush，防止轨迹断开。")]
    public float stampSpacing = 0.12f;

    [Tooltip("单个检测点每帧最多补几个 Brush，限制极端高速挥剑时的生成数量。")]
    public int maxStampsPerPointPerFrame = 3;

    [Tooltip("同一个检测点两次生成水波的最短间隔。")]
    public float perPointCooldown = 0.025f;

    [Header("Brush")]
    [Tooltip("武器专用 Brush prefab。为空时使用 WaterRippleBrushSpawner 上的默认 Brush。")]
    public GameObject weaponBrushPrefab;

    [Tooltip("Brush 尺寸。X 是宽度，Y 是长度。武器划水建议细长一点。")]
    public Vector2 brushSize = new Vector2(0.3f, 0.3f);

    [Tooltip("临时 Brush 存活时间，只要活到 WaterRippleCamera 拍到一帧即可。")]
    public float brushLife = 0.04f;

    [Tooltip("控制 Brush 在水面上的朝向。")]
    public StampDirectionMode directionMode = StampDirectionMode.Motion;

    [Tooltip("可选：传给 Brush 材质的法线贴图。程序化 Brush 通常不用填。")]
    public Texture normalTex;

    [Tooltip("可选：传给 Brush 材质的高度贴图。程序化 Brush 通常不用填。")]
    public Texture heightTex;

    [Header("深度强度")]
    [Tooltip("开启后，检测点越深入水面，生成的 Brush 凹凸强度越强。")]
    public bool scaleStrengthByWaterDepth = true;

    [Tooltip("检测点刚接触水面时的强度倍率。")]
    public float minDepthStrengthMultiplier = 0.25f;

    [Tooltip("检测点达到最大压入深度时的强度倍率。")]
    public float maxDepthStrengthMultiplier = 1.6f;

    [Tooltip("检测点压入水面多深时达到最大强度。")]
    public float depthForMaxStrength = 0.18f;

    [Tooltip("深度到强度的响应曲线。X 是归一化深度，Y 是强度混合比例。")]
    public AnimationCurve depthStrengthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Debug")]
    [Tooltip("如果 Detect Points 为空，自动在 Detect Points Root 下面寻找名字以 WaveDetect 开头的子物体。")]
    public bool autoFindWaveDetectChildren = true;

    [Tooltip("选中物体时绘制向下检测射线。青色表示打到水面，黄色表示没有打到。")]
    public bool drawDebugGizmos = true;

    [Tooltip("生成水波时输出日志。调试完成后建议关闭。")]
    public bool logSpawn = false;

    private bool isDetecting;
    private bool[] hasLastPositions;
    private Vector3[] lastPositions;
    private float[] lastSpawnTimes;

    private void Awake()
    {
        if (rippleSpawner == null)
            rippleSpawner = GetComponentInParent<WaterRippleBrushSpawner>();

        if (rippleSpawner == null)
            rippleSpawner = FindObjectOfType<WaterRippleBrushSpawner>();

        TryAutoFindDetectPoints();
        EnsureStateArrays();
    }

    private void OnEnable()
    {
        isDetecting = activeOnEnable;
        ResetWeaponWaterRippleSamples();
    }

    private void LateUpdate()
    {
        EnsureStateArrays();

        if (detectPoints == null)
            return;

        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];
            if (point == null)
                continue;

            if (isDetecting && rippleSpawner != null)
            {
                UpdateDetectPoint(i, point);
            }
            else
            {
                TrackDetectPointOnly(i, point);
            }
        }
    }

    public void BeginWeaponWaterRipple()
    {
        isDetecting = true;
    }

    public void EndWeaponWaterRipple()
    {
        isDetecting = false;
        ResetWeaponWaterRippleSamples();
    }

    public void BeginWaterCut()
    {
        BeginWeaponWaterRipple();
    }

    public void EndWaterCut()
    {
        EndWeaponWaterRipple();
    }

    public void ResetWeaponWaterRippleSamples()
    {
        EnsureStateArrays();

        if (hasLastPositions == null)
            return;

        for (int i = 0; i < hasLastPositions.Length; i++)
        {
            hasLastPositions[i] = false;
            lastSpawnTimes[i] = -999f;
        }
    }

    private void UpdateDetectPoint(int index, Transform point)
    {
        Vector3 previousPosition = lastPositions[index];
        Vector3 currentPosition = point.position;

        if (!hasLastPositions[index])
        {
            lastPositions[index] = currentPosition;
            hasLastPositions[index] = true;
            return;
        }

        Vector3 movement = currentPosition - previousPosition;
        float moveDistance = movement.magnitude;

        if (moveDistance < minMoveDistance)
            return;

        if (Time.time - lastSpawnTimes[index] < perPointCooldown)
            return;

        Vector3 stampForward = GetStampForward(point, movement);
        int stampCount = Mathf.Clamp(
            Mathf.CeilToInt(moveDistance / Mathf.Max(0.001f, stampSpacing)),
            1,
            Mathf.Max(1, maxStampsPerPointPerFrame)
        );

        bool spawnedAny = false;

        for (int i = 1; i <= stampCount; i++)
        {
            float t = i / (float)stampCount;
            Vector3 samplePosition = Vector3.Lerp(previousPosition, currentPosition, t);

            // 核心判断：从检测点上方向下打到水面，并且采样点已经低于命中水面。
            if (!TryGetWaterSurfaceHit(samplePosition, out RaycastHit waterHit))
                continue;

            Vector3 waterNormal = GetUsableWaterNormal(waterHit.normal);
            float contactDepth;
            float strengthMultiplier = GetDepthStrengthMultiplier(
                samplePosition,
                waterHit,
                waterNormal,
                out contactDepth
            );

            bool spawned = rippleSpawner.SpawnWaterRippleBrushAtSurface(
                waterHit.point,
                waterNormal,
                stampForward,
                brushSize,
                weaponBrushPrefab,
                normalTex,
                heightTex,
                brushLife,
                strengthMultiplier: strengthMultiplier
            );

            if (spawned)
            {
                spawnedAny = true;

                if (logSpawn)
                {
                    Debug.Log(
                        $"[WaterRippleWeaponEventTrail] 生成武器水波：检测点={point.name}，位置={waterHit.point}，压入深度={contactDepth}，强度倍率={strengthMultiplier}",
                        this
                    );
                }
            }
        }

        lastPositions[index] = currentPosition;

        if (spawnedAny)
            lastSpawnTimes[index] = Time.time;
    }

    private bool TryGetWaterSurfaceHit(Vector3 samplePosition, out RaycastHit hit)
    {
        Vector3 origin = samplePosition + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
        float distance = Mathf.Max(0.001f, rayStartHeight * 2f);

        if (!Physics.Raycast(origin, Vector3.down, out hit, distance, waterMask, queryTriggerInteraction))
        {
            if (logSpawn)
                Debug.Log("[WaterRippleWeaponEventTrail] 射线没有打到水面。", this);

            return false;
        }

        if (logSpawn)
        {
            Debug.Log(
                "[WaterRippleWeaponEventTrail] 射线命中水面：" + hit.collider.name +
                "，Tag=" + hit.collider.tag +
                "，Layer=" + LayerMask.LayerToName(hit.collider.gameObject.layer) +
                "，水面Y=" + hit.point.y +
                "，检测点Y=" + samplePosition.y +
                "，高度差=" + (samplePosition.y - hit.point.y),
                this
            );
        }
        
        if (!string.IsNullOrEmpty(waterTag) && hit.collider.tag != waterTag)
            return false;

        // 向下射线只负责找到水面；这里再判断检测点是否真的进入水面。
        return samplePosition.y <= hit.point.y + enterSurfaceTolerance;
    }

    private float GetDepthStrengthMultiplier(
        Vector3 samplePosition,
        RaycastHit waterHit,
        Vector3 waterNormal,
        out float contactDepth)
    {
        if (!scaleStrengthByWaterDepth)
        {
            contactDepth = 0f;
            return 1f;
        }

        // heightFromSurface 大于 0 表示检测点还在 Collider 命中点上方。
        // enterSurfaceTolerance 是视觉接触带：检测点刚进入这个范围时强度较弱，
        // 继续往水里压入时，强度会逐渐变强。
        float heightFromSurface = Vector3.Dot(samplePosition - waterHit.point, waterNormal);
        contactDepth = Mathf.Max(0f, enterSurfaceTolerance - heightFromSurface);    

        float normalizedDepth = Mathf.Clamp01(contactDepth / Mathf.Max(0.001f, depthForMaxStrength));

        if (depthStrengthCurve != null)
            normalizedDepth = Mathf.Clamp01(depthStrengthCurve.Evaluate(normalizedDepth));

        return Mathf.Lerp(minDepthStrengthMultiplier, maxDepthStrengthMultiplier, normalizedDepth);
    }

    private Vector3 GetUsableWaterNormal(Vector3 hitNormal)
    {
        Vector3 normal = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : Vector3.up;

        // 有些 Mesh 的法线可能朝下。
        // WaterRippleBrushSpawner 需要一个朝上的表面法线，所以这里统一翻到上半球。
        if (Vector3.Dot(normal, Vector3.up) < 0f)
            normal = -normal;

        return normal;
    }

    private void TrackDetectPointOnly(int index, Transform point)
    {
        lastPositions[index] = point.position;
        hasLastPositions[index] = true;
    }

    private Vector3 GetStampForward(Transform point, Vector3 movement)
    {
        switch (directionMode)
        {
            case StampDirectionMode.DetectPointForward:
                return point.forward;

            case StampDirectionMode.OwnerForward:
                return transform.forward;

            default:
                if (movement.sqrMagnitude > 0.0001f)
                    return movement.normalized;

                return transform.forward;
        }
    }

    private void EnsureStateArrays()
    {
        int count = detectPoints != null ? detectPoints.Length : 0;

        if (hasLastPositions != null && hasLastPositions.Length == count)
            return;

        hasLastPositions = new bool[count];
        lastPositions = new Vector3[count];
        lastSpawnTimes = new float[count];

        for (int i = 0; i < count; i++)
            lastSpawnTimes[i] = -999f;
    }

    private void TryAutoFindDetectPoints()
    {
        if (!autoFindWaveDetectChildren)
            return;

        if (detectPoints != null && detectPoints.Length > 0)
            return;

        Transform root = detectPointsRoot != null ? detectPointsRoot : transform;
        List<Transform> points = new List<Transform>();

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == root)
                continue;

            if (child.name.StartsWith("WaveDetect"))
                points.Add(child);
        }

        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        detectPoints = points.ToArray();
    }

    private void OnValidate()
    {
        rayStartHeight = Mathf.Max(0.001f, rayStartHeight);
        enterSurfaceTolerance = Mathf.Max(0f, enterSurfaceTolerance);
        minMoveDistance = Mathf.Max(0.001f, minMoveDistance);
        stampSpacing = Mathf.Max(0.001f, stampSpacing);
        maxStampsPerPointPerFrame = Mathf.Max(1, maxStampsPerPointPerFrame);
        perPointCooldown = Mathf.Max(0f, perPointCooldown);
        brushSize.x = Mathf.Max(0.001f, brushSize.x);
        brushSize.y = Mathf.Max(0.001f, brushSize.y);
        brushLife = Mathf.Max(0.001f, brushLife);
        minDepthStrengthMultiplier = Mathf.Max(0f, minDepthStrengthMultiplier);
        maxDepthStrengthMultiplier = Mathf.Max(minDepthStrengthMultiplier, maxDepthStrengthMultiplier);
        depthForMaxStrength = Mathf.Max(0.001f, depthForMaxStrength);
    }

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
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(origin, hit.point);
                Gizmos.DrawWireSphere(hit.point, 0.04f);
            }
            else
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(origin, end);
            }

            Gizmos.DrawWireSphere(point.position, 0.03f);
        }
    }
}
