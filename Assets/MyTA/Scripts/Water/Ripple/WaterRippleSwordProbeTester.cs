using UnityEngine;

/// <summary>
/// Standalone sword-water ripple tester.
/// Attach it to a copied sword object in the scene, then move/rotate the sword in Play Mode.
/// It samples points along the blade and continuously stamps the existing circular water ripple brush.
/// </summary>
public class WaterRippleSwordProbeTester : MonoBehaviour
{
    public enum ForwardMode
    {
        Motion,
        BladeAxis,
        OwnerForward
    }

    [Header("References")]
    public WaterRippleBrushSpawner rippleSpawner;
    public GameObject brushPrefabOverride;

    [Header("Blade Sampling")]
    public bool autoFitBladeFromRenderers = true;
    public Vector3 localBladeStart = new Vector3(0f, -0.5f, 0f);
    public Vector3 localBladeEnd = new Vector3(0f, 0.5f, 0f);
    [Range(2, 32)]
    public int sampleCount = 9;

    [Header("Water Raycast")]
    public LayerMask waterMask;
    public string waterTag = "";
    public float rayStartHeight = 2f;
    public float enterSurfaceTolerance = 0.08f;
    public QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Spawn")]
    public bool active = true;
    public bool continuousWhileInWater = false;
    public bool forceMaxDepthForTest = true;
    public float spawnInterval = 0.035f;
    public float minMoveDistance = 0.005f;
    public Vector2 brushSize = new Vector2(0.12f, 0.12f);
    public float brushLife = 0.12f;
    public ForwardMode forwardMode = ForwardMode.BladeAxis;

    [Header("Depth Response")]
    public float minDepthStrengthMultiplier = 1.5f;
    public float maxDepthStrengthMultiplier = 4f;
    public float depthForMaxStrength = 0.2f;
    public AnimationCurve depthStrengthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public bool scaleSizeByDepth = true;
    public float minDepthSizeMultiplier = 0.8f;
    public float maxDepthSizeMultiplier = 1.6f;

    public bool scaleLifeByDepth = true;
    public float minDepthLifeMultiplier = 1f;
    public float maxDepthLifeMultiplier = 2f;

    [Header("Debug")]
    public bool drawGizmos = true;
    public bool logSpawn = false;

    private Vector3[] lastSamplePositions;
    private bool[] hasLastSamplePositions;
    private float[] lastSpawnTimes;
    private float[] lastDepth01;
    private bool[] sampleInWater;

    private void Reset()
    {
        SetDefaultWaterMaskIfEmpty();
        FitBladeFromRenderers();
    }

    private void Awake()
    {
        if (rippleSpawner == null)
            rippleSpawner = FindObjectOfType<WaterRippleBrushSpawner>();

        SetDefaultWaterMaskIfEmpty();

        if (autoFitBladeFromRenderers)
            FitBladeFromRenderers();

        EnsureArrays();
    }

    private void OnEnable()
    {
        EnsureArrays();
        ResetSamples();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !active)
            return;

        if (rippleSpawner == null)
            rippleSpawner = FindObjectOfType<WaterRippleBrushSpawner>();

        if (rippleSpawner == null)
            return;

        EnsureArrays();

        for (int i = 0; i < sampleCount; i++)
            UpdateSample(i);
    }

    [ContextMenu("Fit Blade From Renderers")]
    public void FitBladeFromRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return;

        bool hasBounds = false;
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            Bounds worldBounds = r.bounds;
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 localCorner = transform.InverseTransformPoint(worldCorner);

                        if (!hasBounds)
                        {
                            localBounds = new Bounds(localCorner, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localCorner);
                        }
                    }
                }
            }
        }

        if (!hasBounds)
            return;

        Vector3 size = localBounds.size;
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;
        Vector3 c = localBounds.center;

        if (size.x >= size.y && size.x >= size.z)
        {
            localBladeStart = new Vector3(min.x, c.y, c.z);
            localBladeEnd = new Vector3(max.x, c.y, c.z);
        }
        else if (size.y >= size.x && size.y >= size.z)
        {
            localBladeStart = new Vector3(c.x, min.y, c.z);
            localBladeEnd = new Vector3(c.x, max.y, c.z);
        }
        else
        {
            localBladeStart = new Vector3(c.x, c.y, min.z);
            localBladeEnd = new Vector3(c.x, c.y, max.z);
        }
    }

    [ContextMenu("Reset Samples")]
    public void ResetSamples()
    {
        EnsureArrays();

        for (int i = 0; i < sampleCount; i++)
        {
            hasLastSamplePositions[i] = false;
            lastSpawnTimes[i] = -999f;
            lastDepth01[i] = 0f;
            sampleInWater[i] = false;
        }
    }

    private void UpdateSample(int index)
    {
        Vector3 samplePosition = GetSamplePosition(index);
        Vector3 previousPosition = lastSamplePositions[index];
        Vector3 movement = samplePosition - previousPosition;
        float moveDistance = hasLastSamplePositions[index] ? movement.magnitude : 0f;

        lastSamplePositions[index] = samplePosition;
        hasLastSamplePositions[index] = true;

        if (Time.time - lastSpawnTimes[index] < spawnInterval)
            return;

        if (moveDistance < minMoveDistance)
            return;

        if (!TryGetWaterSurfaceHit(samplePosition, out RaycastHit waterHit, out float contactDepth))
        {
            sampleInWater[index] = false;
            return;
        }

        sampleInWater[index] = true;

        Vector3 waterNormal = GetUsableWaterNormal(waterHit.normal);
        float depth01 = forceMaxDepthForTest
            ? 1f
            : Mathf.Clamp01(contactDepth / Mathf.Max(0.001f, depthForMaxStrength));

        if (!forceMaxDepthForTest && depthStrengthCurve != null)
            depth01 = Mathf.Clamp01(depthStrengthCurve.Evaluate(depth01));

        float strengthMultiplier = Mathf.Lerp(minDepthStrengthMultiplier, maxDepthStrengthMultiplier, depth01);
        float sizeMultiplier = scaleSizeByDepth ? Mathf.Lerp(minDepthSizeMultiplier, maxDepthSizeMultiplier, depth01) : 1f;
        float lifeMultiplier = scaleLifeByDepth ? Mathf.Lerp(minDepthLifeMultiplier, maxDepthLifeMultiplier, depth01) : 1f;

        Vector3 forward = GetBrushForward(movement, waterNormal);
        bool spawned = rippleSpawner.SpawnWaterRippleBrushAtSurface(
            waterHit.point,
            waterNormal,
            forward,
            brushSize * sizeMultiplier,
            brushPrefabOverride,
            null,
            null,
            brushLife * lifeMultiplier,
            strengthMultiplier: strengthMultiplier
        );

        if (!spawned)
            return;

        lastSpawnTimes[index] = Time.time;
        lastDepth01[index] = depth01;

        if (logSpawn)
        {
            Debug.Log(
                $"[WaterRippleSwordProbeTester] point={index}, depth={contactDepth:F3}, depth01={depth01:F2}, strength={strengthMultiplier:F2}, size={sizeMultiplier:F2}, life={lifeMultiplier:F2}",
                this
            );
        }
    }

    private Vector3 GetSamplePosition(int index)
    {
        float t = sampleCount <= 1 ? 0.5f : index / (float)(sampleCount - 1);
        return transform.TransformPoint(Vector3.Lerp(localBladeStart, localBladeEnd, t));
    }

    private bool TryGetWaterSurfaceHit(Vector3 samplePosition, out RaycastHit hit, out float contactDepth)
    {
        Vector3 origin = samplePosition + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
        float distance = Mathf.Max(0.001f, rayStartHeight * 2f);

        if (!Physics.Raycast(origin, Vector3.down, out hit, distance, waterMask, queryTriggerInteraction))
        {
            contactDepth = 0f;
            return false;
        }

        if (!string.IsNullOrEmpty(waterTag) && hit.collider.tag != waterTag)
        {
            contactDepth = 0f;
            return false;
        }

        Vector3 normal = GetUsableWaterNormal(hit.normal);
        float heightFromSurface = Vector3.Dot(samplePosition - hit.point, normal);
        contactDepth = Mathf.Max(0f, -heightFromSurface);

        return heightFromSurface <= 0f;
    }

    private Vector3 GetBrushForward(Vector3 movement, Vector3 waterNormal)
    {
        Vector3 forward;

        switch (forwardMode)
        {
            case ForwardMode.Motion:
                forward = movement.sqrMagnitude > 0.000001f ? movement.normalized : transform.forward;
                break;

            case ForwardMode.OwnerForward:
                forward = transform.forward;
                break;

            default:
                forward = (transform.TransformPoint(localBladeEnd) - transform.TransformPoint(localBladeStart)).normalized;
                break;
        }

        forward = Vector3.ProjectOnPlane(forward, waterNormal);

        if (forward.sqrMagnitude < 0.000001f)
            forward = Vector3.ProjectOnPlane(transform.forward, waterNormal);

        if (forward.sqrMagnitude < 0.000001f)
            forward = Vector3.forward;

        return forward.normalized;
    }

    private Vector3 GetUsableWaterNormal(Vector3 hitNormal)
    {
        Vector3 normal = hitNormal.sqrMagnitude > 0.0001f ? hitNormal.normalized : Vector3.up;

        if (Vector3.Dot(normal, Vector3.up) < 0f)
            normal = -normal;

        return normal;
    }

    private void SetDefaultWaterMaskIfEmpty()
    {
        if (waterMask.value != 0)
            return;

        int customWaterLayer = LayerMask.NameToLayer("CustomWater");
        if (customWaterLayer >= 0)
        {
            waterMask = 1 << customWaterLayer;
            return;
        }

        int waterLayer = LayerMask.NameToLayer("Water");
        waterMask = waterLayer >= 0 ? 1 << waterLayer : ~0;
    }

    private void EnsureArrays()
    {
        sampleCount = Mathf.Clamp(sampleCount, 2, 32);

        if (lastSamplePositions != null && lastSamplePositions.Length == sampleCount)
            return;

        lastSamplePositions = new Vector3[sampleCount];
        hasLastSamplePositions = new bool[sampleCount];
        lastSpawnTimes = new float[sampleCount];
        lastDepth01 = new float[sampleCount];
        sampleInWater = new bool[sampleCount];

        for (int i = 0; i < sampleCount; i++)
            lastSpawnTimes[i] = -999f;
    }

    private void OnValidate()
    {
        sampleCount = Mathf.Clamp(sampleCount, 2, 32);
        rayStartHeight = Mathf.Max(0.001f, rayStartHeight);
        enterSurfaceTolerance = Mathf.Max(0f, enterSurfaceTolerance);
        spawnInterval = Mathf.Max(0.001f, spawnInterval);
        minMoveDistance = Mathf.Max(0f, minMoveDistance);
        brushSize.x = Mathf.Max(0.001f, brushSize.x);
        brushSize.y = Mathf.Max(0.001f, brushSize.y);
        brushLife = Mathf.Max(0.001f, brushLife);
        minDepthStrengthMultiplier = Mathf.Max(0f, minDepthStrengthMultiplier);
        maxDepthStrengthMultiplier = Mathf.Max(minDepthStrengthMultiplier, maxDepthStrengthMultiplier);
        depthForMaxStrength = Mathf.Max(0.001f, depthForMaxStrength);
        minDepthSizeMultiplier = Mathf.Max(0f, minDepthSizeMultiplier);
        maxDepthSizeMultiplier = Mathf.Max(minDepthSizeMultiplier, maxDepthSizeMultiplier);
        minDepthLifeMultiplier = Mathf.Max(0f, minDepthLifeMultiplier);
        maxDepthLifeMultiplier = Mathf.Max(minDepthLifeMultiplier, maxDepthLifeMultiplier);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        int count = Mathf.Clamp(sampleCount, 2, 32);
        Vector3 previous = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Vector3 samplePosition = GetSamplePosition(i);
            bool inWater = sampleInWater != null && i < sampleInWater.Length && sampleInWater[i];
            float depth01 = lastDepth01 != null && i < lastDepth01.Length ? lastDepth01[i] : 0f;

            Gizmos.color = inWater ? Color.Lerp(Color.cyan, Color.red, depth01) : Color.yellow;
            Gizmos.DrawWireSphere(samplePosition, Mathf.Lerp(0.015f, 0.06f, depth01));

            Vector3 origin = samplePosition + Vector3.up * Mathf.Max(0.001f, rayStartHeight);
            Gizmos.DrawLine(origin, samplePosition);

            if (i > 0)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(previous, samplePosition);
            }

            previous = samplePosition;
        }
    }
}
