using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GrassInteractionBrushPool : MonoBehaviour
{
    [Header("Pool Settings")]
    public GameObject brushPrefab;
    public int maxBrushes = 128;
    public bool prewarmOnAwake = true;
    public bool recycleOldestWhenFull = true;

    [Header("Layer")]
    public string brushLayerName = "GrassInteractionBrush";

    [Header("Safety")]
    public bool disableRendererShadows = true;
    public bool disableColliders = true;

    private readonly Queue<PooledGrassInteractionBrush> available = new Queue<PooledGrassInteractionBrush>();
    private readonly LinkedList<PooledGrassInteractionBrush> active = new LinkedList<PooledGrassInteractionBrush>();

    private static readonly int BrushStrengthID = Shader.PropertyToID("_Strength");
    private static readonly int BrushSoftnessID = Shader.PropertyToID("_Softness");

    public int ActiveCount => active.Count;
    public int AvailableCount => available.Count;
    public int CreatedCount => active.Count + available.Count;
    public bool HasPrefab => brushPrefab != null;

    private void Awake()
    {
        if (prewarmOnAwake && brushPrefab != null)
            Prewarm();
    }

    public void Prewarm()
    {
        Prewarm(maxBrushes);
    }

    public void Prewarm(int targetTotalCount)
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[GrassInteractionBrushPool] brushPrefab is null.", this);
            return;
        }

        int totalCount = available.Count + active.Count;
        int targetCount = Mathf.Clamp(targetTotalCount, 0, maxBrushes);
        int createCount = Mathf.Max(0, targetCount - totalCount);

        for (int i = 0; i < createCount; i++)
        {
            PooledGrassInteractionBrush instance = CreateNewInstance();
            ReleaseToAvailable(instance);
        }
    }

    public GameObject SpawnBrush(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float lifeTime,
        bool overrideMaterialProperties = false,
        float strength = 1f,
        float softness = 0.4f)
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[GrassInteractionBrushPool] brushPrefab is null.", this);
            return null;
        }

        PooledGrassInteractionBrush pooled = GetAvailableInstance();

        if (pooled == null || pooled.BrushObject == null)
            return null;

        GameObject brush = pooled.BrushObject;
        brush.SetActive(true);
        brush.transform.SetPositionAndRotation(position, rotation);
        brush.transform.localScale = scale;

        SetupBrushMaterial(pooled, overrideMaterialProperties, strength, softness);

        pooled.IsActiveInPool = true;
        active.AddLast(pooled);
        pooled.PlayLifetime(Mathf.Max(0.001f, lifeTime));

        return brush;
    }

    public void Release(PooledGrassInteractionBrush pooled)
    {
        if (pooled == null || !pooled.IsActiveInPool)
            return;

        pooled.IsActiveInPool = false;
        active.Remove(pooled);
        ReleaseToAvailable(pooled);
    }

    private PooledGrassInteractionBrush GetAvailableInstance()
    {
        if (available.Count > 0)
            return available.Dequeue();

        int totalCount = available.Count + active.Count;

        if (totalCount < maxBrushes)
            return CreateNewInstance();

        if (recycleOldestWhenFull && active.Count > 0)
        {
            PooledGrassInteractionBrush oldest = active.First.Value;
            active.RemoveFirst();

            oldest.StopLifetime();
            oldest.IsActiveInPool = false;

            return oldest;
        }

        return null;
    }

    private PooledGrassInteractionBrush CreateNewInstance()
    {
        GameObject go = Instantiate(brushPrefab, transform);
        go.name = "Pooled Grass Interaction Brush";

        PooledGrassInteractionBrush pooled = go.GetComponent<PooledGrassInteractionBrush>();

        if (pooled == null)
            pooled = go.AddComponent<PooledGrassInteractionBrush>();

        pooled.Initialize(this, go);

        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
            SetLayerRecursively(go, brushLayer);
        else
            Debug.LogWarning($"[GrassInteractionBrushPool] Layer not found: {brushLayerName}", this);

        PrepareBrushObject(pooled);

        return pooled;
    }

    private void ReleaseToAvailable(PooledGrassInteractionBrush pooled)
    {
        if (pooled == null)
            return;

        pooled.StopLifetime();

        if (pooled.BrushObject != null)
            pooled.BrushObject.SetActive(false);

        available.Enqueue(pooled);
    }

    private void PrepareBrushObject(PooledGrassInteractionBrush pooled)
    {
        if (pooled == null)
            return;

        if (disableRendererShadows && pooled.Renderers != null)
        {
            foreach (Renderer r in pooled.Renderers)
            {
                if (r == null)
                    continue;

                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        if (disableColliders && pooled.Colliders != null)
        {
            foreach (Collider c in pooled.Colliders)
            {
                if (c != null)
                    c.enabled = false;
            }
        }
    }

    private void SetupBrushMaterial(
        PooledGrassInteractionBrush pooled,
        bool overrideMaterialProperties,
        float strength,
        float softness)
    {
        if (pooled == null || pooled.PropertyBlock == null)
            return;

        MaterialPropertyBlock block = pooled.PropertyBlock;
        block.Clear();

        if (overrideMaterialProperties)
        {
            block.SetFloat(BrushStrengthID, Mathf.Clamp01(strength));
            block.SetFloat(BrushSoftnessID, Mathf.Clamp(softness, 0.01f, 1f));
        }

        if (pooled.Renderers == null)
            return;

        foreach (Renderer r in pooled.Renderers)
        {
            if (r != null)
                r.SetPropertyBlock(block);
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}

public class PooledGrassInteractionBrush : MonoBehaviour
{
    public GameObject BrushObject { get; private set; }
    public Renderer[] Renderers { get; private set; }
    public Collider[] Colliders { get; private set; }
    public MaterialPropertyBlock PropertyBlock { get; private set; }
    public bool IsActiveInPool { get; set; }

    private GrassInteractionBrushPool pool;
    private Coroutine lifetimeCoroutine;

    public void Initialize(GrassInteractionBrushPool ownerPool, GameObject brushObject)
    {
        pool = ownerPool;
        BrushObject = brushObject;
        Renderers = brushObject != null ? brushObject.GetComponentsInChildren<Renderer>(true) : null;
        Colliders = brushObject != null ? brushObject.GetComponentsInChildren<Collider>(true) : null;
        PropertyBlock = new MaterialPropertyBlock();
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
