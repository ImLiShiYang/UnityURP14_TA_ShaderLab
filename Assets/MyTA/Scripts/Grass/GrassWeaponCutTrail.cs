using UnityEngine;

public class GrassWeaponCutTrail : MonoBehaviour
{
    public enum StampDirectionMode
    {
        Motion,
        DetectPointForward,
        OwnerForward
    }

    [Header("References")]
    public GrassInteractionBrushSpawner grassBrushSpawner;
    public Transform detectPointsRoot;
    public Transform[] detectPoints;
    public PlayerAttack playerAttack;

    [Header("Attack Window")]
    public bool activeOnEnable = false;
    public float maxAttackWindowDuration = 0.8f;

    [Header("Ground Raycast")]
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 2f;
    public float rayDistance = 4f;
    public QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Ignore;
    
    [Tooltip("检测点距离地面小于这个值时才生成切草 Brush。")]
    public float maxDistanceFromGround = 0.6f;

    [Header("Stamp")]
    public GameObject cutBrushPrefab;
    public Vector2 cutBrushSize = new Vector2(0.25f, 0.9f);
    public float cutBrushLife = 0.06f;
    [Range(0f, 1f)]
    public float cutStrength = 1f;
    [Range(0.01f, 1f)]
    public float cutSoftness = 0.35f;

    [Header("Movement")]
    public float minMoveDistance = 0.05f;
    public float stampSpacing = 0.12f;
    public int maxStampsPerPointPerFrame = 4;
    public float perPointCooldown = 0.015f;

    [Header("Direction")]
    public StampDirectionMode directionMode = StampDirectionMode.Motion;

    [Header("Debug")]
    public bool autoFindDetectChildren = true;
    public string detectPointPrefix = "GrassCutDetect";
    public bool drawDebugGizmos = true;
    public bool logSpawn = false;

    private bool isDetecting;
    private bool isAttackWindowActive;
    private float attackWindowStartTime = -999f;

    private bool[] hasLastPositions;
    private Vector3[] lastPositions;
    private float[] lastSpawnTimes;
    private Vector3[] lastHitPositions;
    private bool[] hasLastHitPositions;

    private void Awake()
    {
        if (grassBrushSpawner == null)
            grassBrushSpawner = GetComponentInParent<GrassInteractionBrushSpawner>();

        if (grassBrushSpawner == null)
            grassBrushSpawner = FindObjectOfType<GrassInteractionBrushSpawner>();

        if (playerAttack == null)
            playerAttack = GetComponentInParent<PlayerAttack>();

        if (playerAttack == null)
            playerAttack = GetComponentInChildren<PlayerAttack>();

        TryAutoFindDetectPoints();
        EnsureStateArrays();
    }

    private void OnEnable()
    {
        isDetecting = activeOnEnable;
        isAttackWindowActive = false;
        ResetWeaponGrassCutSamples();
    }

    private void LateUpdate()
    {
        if (!isDetecting)
        {
            TrackAllDetectPointsOnly();
            return;
        }

        if (isAttackWindowActive && !IsAttackWindowValid())
        {
            isAttackWindowActive = false;
            isDetecting = activeOnEnable;
        }

        if (!isAttackWindowActive)
        {
            TrackAllDetectPointsOnly();
            return;
        }

        if (grassBrushSpawner == null)
            return;

        EnsureStateArrays();

        if (detectPoints == null)
            return;

        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];

            if (point == null)
                continue;

            UpdateDetectPoint(i, point);
        }
    }

    public void BeginWeaponGrassCut()
    {
        isAttackWindowActive = true;
        isDetecting = true;
        attackWindowStartTime = Time.time;
        ResetWeaponGrassCutSamples();
    }

    public void EndWeaponGrassCut()
    {
        isAttackWindowActive = false;
        attackWindowStartTime = -999f;
        isDetecting = activeOnEnable;
        ResetWeaponGrassCutSamples();
    }

    // 动画事件里也可以用更短名字
    public void BeginGrassCut()
    {
        BeginWeaponGrassCut();
    }

    public void EndGrassCut()
    {
        EndWeaponGrassCut();
    }

    public void ResetWeaponGrassCutSamples()
    {
        EnsureStateArrays();

        if (hasLastPositions == null)
            return;

        for (int i = 0; i < hasLastPositions.Length; i++)
        {
            hasLastPositions[i] = false;
            lastSpawnTimes[i] = -999f;
            hasLastHitPositions[i] = false;
        }
    }

    private bool IsAttackWindowValid()
    {
        if (!isAttackWindowActive)
            return false;

        if (maxAttackWindowDuration <= 0f)
            return true;

        return Time.time - attackWindowStartTime <= maxAttackWindowDuration;
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
        {
            lastPositions[index] = currentPosition;
            return;
        }

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

            if (!TryGetGrassSurfaceHit(samplePosition, out RaycastHit hit))
                continue;

            bool spawned = grassBrushSpawner.SpawnGrassBrushAtSurface(
                hit.point,
                hit.normal,
                stampForward,
                cutBrushSize,
                cutBrushPrefab,
                cutBrushLife,
                cutStrength,
                cutSoftness,
                true
            );

            if (!spawned)
                continue;

            spawnedAny = true;
            lastHitPositions[index] = hit.point;
            hasLastHitPositions[index] = true;

            if (logSpawn)
            {
                Debug.Log(
                    $"[GrassWeaponCutTrail] Spawn cut brush. point={point.name}, hit={hit.point}, size={cutBrushSize}",
                    this
                );
            }
        }

        lastPositions[index] = currentPosition;

        if (spawnedAny)
            lastSpawnTimes[index] = Time.time;
    }

    private bool TryGetGrassSurfaceHit(Vector3 samplePosition, out RaycastHit hit)
    {
        Vector3 origin = samplePosition + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
        float distance = Mathf.Max(0.001f, rayStartHeight + rayDistance);

        if (!Physics.Raycast(origin, Vector3.down, out hit, distance, groundMask, queryTriggerInteraction))
            return false;

        float heightFromSurface = Vector3.Dot(samplePosition - hit.point, hit.normal);

        return Mathf.Abs(heightFromSurface) <= maxDistanceFromGround;
    }

    private Vector3 GetStampForward(Transform point, Vector3 movement)
    {
        Vector3 forward = Vector3.zero;

        switch (directionMode)
        {
            case StampDirectionMode.DetectPointForward:
                forward = point != null ? point.forward : Vector3.zero;
                break;

            case StampDirectionMode.OwnerForward:
                forward = transform.forward;
                break;

            case StampDirectionMode.Motion:
            default:
                forward = movement;
                break;
        }

        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f && point != null)
            forward = point.forward;

        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;

        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private void TrackAllDetectPointsOnly()
    {
        EnsureStateArrays();

        if (detectPoints == null)
            return;

        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];

            if (point == null)
                continue;

            lastPositions[i] = point.position;
            hasLastPositions[i] = true;
        }
    }

    private void TryAutoFindDetectPoints()
    {
        if (!autoFindDetectChildren)
            return;

        if (detectPoints != null && detectPoints.Length > 0)
            return;

        Transform root = detectPointsRoot != null ? detectPointsRoot : transform;
        System.Collections.Generic.List<Transform> found = new System.Collections.Generic.List<Transform>();

        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];

            if (child == root)
                continue;

            if (child.name.StartsWith(detectPointPrefix))
                found.Add(child);
        }

        detectPoints = found.ToArray();
    }

    private void EnsureStateArrays()
    {
        int count = detectPoints != null ? detectPoints.Length : 0;

        if (hasLastPositions != null && hasLastPositions.Length == count)
            return;

        hasLastPositions = new bool[count];
        lastPositions = new Vector3[count];
        lastSpawnTimes = new float[count];
        lastHitPositions = new Vector3[count];
        hasLastHitPositions = new bool[count];

        for (int i = 0; i < count; i++)
            lastSpawnTimes[i] = -999f;
    }

    private void OnValidate()
    {
        maxAttackWindowDuration = Mathf.Max(0f, maxAttackWindowDuration);
        rayStartHeight = Mathf.Max(0.001f, rayStartHeight);
        rayDistance = Mathf.Max(0.001f, rayDistance);
        minMoveDistance = Mathf.Max(0.001f, minMoveDistance);
        stampSpacing = Mathf.Max(0.001f, stampSpacing);
        maxStampsPerPointPerFrame = Mathf.Max(1, maxStampsPerPointPerFrame);
        perPointCooldown = Mathf.Max(0f, perPointCooldown);

        cutBrushSize.x = Mathf.Max(0.001f, cutBrushSize.x);
        cutBrushSize.y = Mathf.Max(0.001f, cutBrushSize.y);
        cutBrushLife = Mathf.Max(0.001f, cutBrushLife);
        cutStrength = Mathf.Clamp01(cutStrength);
        cutSoftness = Mathf.Clamp(cutSoftness, 0.01f, 1f);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || detectPoints == null)
            return;

        Gizmos.color = Color.red;

        for (int i = 0; i < detectPoints.Length; i++)
        {
            Transform point = detectPoints[i];

            if (point == null)
                continue;

            Gizmos.DrawSphere(point.position, 0.035f);

            Vector3 origin = point.position + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
            Vector3 end = origin + Vector3.down * Mathf.Max(0.001f, rayStartHeight + rayDistance);
            Gizmos.DrawLine(origin, end);

            if (hasLastHitPositions != null &&
                i < hasLastHitPositions.Length &&
                hasLastHitPositions[i])
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(lastHitPositions[i], 0.045f);
                Gizmos.color = Color.red;
            }
        }
    }
}