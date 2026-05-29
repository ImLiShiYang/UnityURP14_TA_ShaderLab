using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 雪地 Trail Brush 对象池。
///
/// 作用：
/// 1. 预创建一定数量的 Trail Brush Quad。
/// 2. 生成雪地轨迹时复用已有对象，避免频繁 Instantiate / Destroy。
/// 3. Brush 生命周期结束后 SetActive(false)，回收到池中。
/// 4. 如果池满了，可以复用最旧的 Brush。
///
/// 注意：
/// 这个对象池管理的是“写入 Snow RT 的隐藏 Brush”，不是 Decal。
/// Brush prefab 通常是一个 Quad，材质使用 Snow/SnowTrailBrush 或 Snow/SnowRoundTrailBrush。
/// </summary>
public class SnowTrailBrushPool : MonoBehaviour
{
    [Header("Pool Settings")]
    [Tooltip("Trail Brush prefab。通常是一个 Quad，材质使用 Snow/SnowTrailBrush 或 Snow/SnowRoundTrailBrush。")]
    public GameObject brushPrefab;

    [Tooltip("最大 Brush 数量。走路轨迹建议 32~128。")]
    public int maxBrushes = 64;

    [Tooltip("是否在 Awake 时预创建全部 Brush。")]
    public bool prewarmOnAwake = true;

    [Tooltip("池满时是否复用最旧的 Brush。")]
    public bool recycleOldestWhenFull = true;

    [Header("Layer")]
    [Tooltip("生成/复用出来的 Brush 会强制设置到这个 Layer。必须和 SnowFootprintRenderFeature 的 LayerMask 一致。")]
    public string brushLayerName = "SnowFootprintBrush";

    [Header("Safety")]
    [Tooltip("自动关闭 Brush 的阴影。Brush 只是 RT 画笔，不应该参与主场景阴影。")]
    public bool disableRendererShadows = true;

    [Tooltip("自动关闭 Brush 上的所有 Collider，避免角色踩到 Brush 被顶起来。")]
    public bool disableColliders = true;

    [Header("Debug")]
    public bool showDebugGUI = false;

    private readonly Queue<PooledSnowTrailBrush> available = new Queue<PooledSnowTrailBrush>();
    private readonly LinkedList<PooledSnowTrailBrush> active = new LinkedList<PooledSnowTrailBrush>();

    private static readonly int SinkStrengthID = Shader.PropertyToID("_SinkStrength");
    private static readonly int RimStrengthID = Shader.PropertyToID("_RimStrength");
    private static readonly int CenterWidthID = Shader.PropertyToID("_CenterWidth");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int OuterSoftnessID = Shader.PropertyToID("_OuterSoftness");
    private static readonly int LengthSoftnessID = Shader.PropertyToID("_LengthSoftness");

    public int ActiveCount => active.Count;
    public int AvailableCount => available.Count;
    public bool HasPrefab => brushPrefab != null;

    private void Awake()
    {
        if (prewarmOnAwake)
            Prewarm();
    }

    public void Prewarm()
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[SnowTrailBrushPool] brushPrefab is null.");
            return;
        }

        int totalCount = available.Count + active.Count;
        int createCount = Mathf.Max(0, maxBrushes - totalCount);

        for (int i = 0; i < createCount; i++)
        {
            PooledSnowTrailBrush instance = CreateNewInstance();
            ReleaseToAvailable(instance);
        }
    }

    private PooledSnowTrailBrush CreateNewInstance()
    {
        GameObject go = Instantiate(brushPrefab, transform);
        go.name = "Pooled Snow Trail Brush";

        PooledSnowTrailBrush pooled = go.GetComponent<PooledSnowTrailBrush>();

        if (pooled == null)
            pooled = go.AddComponent<PooledSnowTrailBrush>();

        pooled.Initialize(this, go);

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
            SetLayerRecursively(go, brushLayer);
        else
            Debug.LogWarning($"[SnowTrailBrushPool] 找不到 Layer: {brushLayerName}");

        PrepareBrushObject(go);

        return pooled;
    }

    /// <summary>
    /// 从对象池中取出一个 Trail Brush 并激活。
    /// </summary>
    public GameObject SpawnBrush(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float lifeTime,
        float sinkStrength,
        float rimStrength,
        float centerWidth,
        float edgeWidth,
        float outerSoftness,
        float lengthSoftness)
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[SnowTrailBrushPool] brushPrefab is null.");
            return null;
        }

        PooledSnowTrailBrush pooled = GetAvailableInstance();

        if (pooled == null || pooled.BrushObject == null)
            return null;

        GameObject brush = pooled.BrushObject;

        brush.SetActive(true);
        brush.transform.SetPositionAndRotation(position, rotation);
        brush.transform.localScale = scale;

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
            SetLayerRecursively(brush, brushLayer);

        PrepareBrushObject(brush);
        SetupBrushMaterial(brush, sinkStrength, rimStrength, centerWidth, edgeWidth, outerSoftness, lengthSoftness);

        pooled.IsActiveInPool = true;
        active.AddLast(pooled);
        pooled.PlayLifetime(lifeTime);

        return brush;
    }

    private PooledSnowTrailBrush GetAvailableInstance()
    {
        if (available.Count > 0)
            return available.Dequeue();

        int totalCount = available.Count + active.Count;
        bool reachedMax = totalCount >= maxBrushes;

        if (!reachedMax)
            return CreateNewInstance();

        if (recycleOldestWhenFull && active.Count > 0)
        {
            PooledSnowTrailBrush oldest = active.First.Value;
            active.RemoveFirst();

            oldest.StopLifetime();
            oldest.IsActiveInPool = false;

            return oldest;
        }

        return null;
    }

    public void Release(PooledSnowTrailBrush pooled)
    {
        if (pooled == null)
            return;

        if (!pooled.IsActiveInPool)
            return;

        pooled.IsActiveInPool = false;
        active.Remove(pooled);
        ReleaseToAvailable(pooled);
    }

    private void ReleaseToAvailable(PooledSnowTrailBrush pooled)
    {
        if (pooled == null)
            return;

        pooled.StopLifetime();

        if (pooled.BrushObject != null)
            pooled.BrushObject.SetActive(false);

        available.Enqueue(pooled);
    }

    private void PrepareBrushObject(GameObject brush)
    {
        if (brush == null)
            return;

        if (disableRendererShadows)
        {
            Renderer[] renderers = brush.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        if (disableColliders)
        {
            Collider[] colliders = brush.GetComponentsInChildren<Collider>(true);

            foreach (Collider c in colliders)
            {
                c.enabled = false;
            }
        }
    }

    private void SetupBrushMaterial(
        GameObject brush,
        float sinkStrength,
        float rimStrength,
        float centerWidth,
        float edgeWidth,
        float outerSoftness,
        float lengthSoftness)
    {
        Renderer[] renderers = brush.GetComponentsInChildren<Renderer>(true);

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

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void OnGUI()
    {
        if (!showDebugGUI)
            return;

        GUILayout.BeginArea(new Rect(10, 120, 280, 100), GUI.skin.box);
        GUILayout.Label($"Snow Trail Brush Active: {ActiveCount}");
        GUILayout.Label($"Snow Trail Brush Available: {AvailableCount}");
        GUILayout.Label($"Snow Trail Brush Max: {maxBrushes}");
        GUILayout.EndArea();
    }
}

/// <summary>
/// 单个池化 Snow Trail Brush 实例。
/// 只负责生命周期，到时间后通知 SnowTrailBrushPool 回收。
/// </summary>
public class PooledSnowTrailBrush : MonoBehaviour
{
    public GameObject BrushObject { get; private set; }
    public bool IsActiveInPool { get; set; }

    private SnowTrailBrushPool pool;
    private Coroutine lifetimeCoroutine;

    public void Initialize(SnowTrailBrushPool ownerPool, GameObject brushObject)
    {
        pool = ownerPool;
        BrushObject = brushObject;
    }

    public void PlayLifetime(float lifeTime)
    {
        StopLifetime();
        lifetimeCoroutine = StartCoroutine(LifetimeCoroutine(lifeTime));
    }

    public void StopLifetime()
    {
        if (lifetimeCoroutine != null)
        {
            StopCoroutine(lifetimeCoroutine);
            lifetimeCoroutine = null;
        }
    }

    private IEnumerator LifetimeCoroutine(float lifeTime)
    {
        if (lifeTime > 0f)
            yield return new WaitForSeconds(lifeTime);
        else
            yield return null;

        lifetimeCoroutine = null;

        if (pool != null)
            pool.Release(this);
    }
}
