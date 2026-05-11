using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 脚印 Decal 对象池。
///
/// 作用：
/// 1. 预创建一定数量的脚印 Decal。
/// 2. 生成脚印时复用已有对象，而不是反复 Instantiate。
/// 3. 脚印淡出结束后 SetActive(false)，回收到池中。
/// 4. 如果所有脚印都在使用，则复用最旧的那个脚印。
/// </summary>
public class FootprintDecalPool : MonoBehaviour
{
    [Header("Pool Settings")]
    // 脚印 Decal prefab，需要挂有 ScreenSpaceDecalProjector
    public ScreenSpaceDecalProjector footprintPrefab;

    // 最大脚印数量
    public int maxFootprints = 64;

    // 是否在 Awake 时预创建全部脚印
    public bool prewarmOnAwake = true;

    // 如果池满了，是否复用最旧的脚印
    public bool recycleOldestWhenFull = true;

    [Header("Debug")]
    // 是否在 Game 视图左上角显示对象池调试信息
    public bool showDebugGUI = true;

    // 未使用的脚印队列
    private readonly Queue<PooledFootprintDecal> _available = new Queue<PooledFootprintDecal>();

    // 正在显示或淡出的脚印列表
    // 越靠前表示越旧
    private readonly LinkedList<PooledFootprintDecal> _active = new LinkedList<PooledFootprintDecal>();

    /// <summary>
    /// 当前正在使用的脚印数量。
    /// </summary>
    public int ActiveCount
    {
        get { return _active.Count; }
    }

    /// <summary>
    /// 当前空闲可复用的脚印数量。
    /// </summary>
    public int AvailableCount
    {
        get { return _available.Count; }
    }

    /// <summary>
    /// 初始化对象池。
    /// </summary>
    private void Awake()
    {
        if (prewarmOnAwake)
        {
            Prewarm();
        }
    }

    /// <summary>
    /// 预创建脚印对象。
    /// </summary>
    private void Prewarm()
    {
        if (footprintPrefab == null)
        {
            Debug.LogWarning("FootprintDecalPool: footprintPrefab is null.");
            return;
        }

        for (int i = 0; i < maxFootprints; i++)
        {
            PooledFootprintDecal instance = CreateNewInstance();
            ReleaseToAvailable(instance);
        }
    }

    /// <summary>
    /// 创建一个新的池化脚印实例。
    /// 
    /// 这个方法只负责“创建对象”和“初始化对象”，
    /// 不负责显示脚印，也不负责设置脚印位置、贴图、大小等运行时数据。
    /// </summary>
    private PooledFootprintDecal CreateNewInstance()
    {
        // 根据脚印 prefab 实例化一个新的 ScreenSpaceDecalProjector。
        //
        // 参数 1：footprintPrefab
        // 要复制的脚印 prefab。
        //
        // 参数 2：transform
        // 把新创建出来的脚印对象放到当前对象池物体下面，
        // 这样 Hierarchy 会更整洁：
        //
        // FootprintDecalPool
        // ├── Pooled Footprint Decal
        // ├── Pooled Footprint Decal
        // └── Pooled Footprint Decal
        ScreenSpaceDecalProjector projector = Instantiate(footprintPrefab, transform);

        // 给实例化出来的对象改一个名字。
        //
        // 这只是为了在 Hierarchy 里更容易看懂，
        // 不影响脚印功能。
        projector.name = "Pooled Footprint Decal";

        // 尝试从这个脚印对象身上获取 PooledFootprintDecal 组件。
        //
        // PooledFootprintDecal 是我们自己写的脚印生命周期组件，
        // 负责控制：
        // 1. 脚印显示多久
        // 2. 脚印如何淡出
        // 3. 淡出结束后如何回收到对象池
        PooledFootprintDecal pooled = projector.GetComponent<PooledFootprintDecal>();

        // 如果 prefab 上没有提前挂 PooledFootprintDecal，
        // 就在运行时自动添加一个。
        //
        // 这样 prefab 只需要有 ScreenSpaceDecalProjector 也能正常工作。
        if (pooled == null)
        {
            pooled = projector.gameObject.AddComponent<PooledFootprintDecal>();
        }

        // 初始化池化脚印对象。
        //
        // this：
        // 当前 FootprintDecalPool，也就是这个脚印属于哪个对象池。
        // 后面脚印淡出结束时，会通过这个 pool 把自己还回去。
        //
        // projector：
        // 当前脚印对象上的 ScreenSpaceDecalProjector。
        // PooledFootprintDecal 需要通过它修改 opacity，
        // 以及在生命周期结束时通知对象池回收。
        pooled.Initialize(this, projector);

        // 返回这个已经创建并初始化好的池化脚印对象。
        //
        // 后续 Prewarm() 会把它放入可用队列；
        // SpawnFootprint() 会从队列里取出来使用。
        return pooled;
    }

    /// <summary>
    /// 生成一个脚印。
    ///
    /// 注意：
    /// 这里不是 Instantiate，而是从池里拿一个对象出来复用。
    /// </summary>
    public ScreenSpaceDecalProjector SpawnFootprint(Vector3 position, Quaternion rotation,Texture2D texture,Vector3 size,Vector3 pivot,float visibleTime,float fadeTime)
    {
        if (footprintPrefab == null)
        {
            Debug.LogWarning("FootprintDecalPool: footprintPrefab is null.");
            return null;
        }

        PooledFootprintDecal pooled = GetAvailableInstance();

        if (pooled == null)
            return null;

        ScreenSpaceDecalProjector projector = pooled.Projector;

        if (projector == null)
            return null;

        // 如果对象之前是禁用状态，先激活。
        // ScreenSpaceDecalProjector.OnEnable 会把它加入 ActiveProjectors，
        // 这样 Render Feature 才会绘制它。
        projector.gameObject.SetActive(true);

        // 设置位置和旋转
        projector.transform.SetPositionAndRotation(position, rotation);

        // 设置本次脚印的数据
        projector.decalTexture = texture;
        projector.size = size;
        projector.pivot = pivot;
        projector.opacity = 1f;

        // 记录为正在使用
        pooled.IsActiveInPool = true;
        _active.AddLast(pooled);

        // 开始生命周期：完整显示 → 淡出 → 回收到池
        pooled.PlayLifetime(visibleTime, fadeTime);

        return projector;
    }

    /// <summary>
    /// 从池里取一个可用脚印。
    /// </summary>
    private PooledFootprintDecal GetAvailableInstance()
    {
        // 1. 优先使用空闲对象
        if (_available.Count > 0)
        {
            return _available.Dequeue();
        }

        // 2. 计算当前对象池里总共有多少个对象
        int totalCount = _available.Count + _active.Count;

        // 3. 如果还没达到最大数量，就创建新对象
        bool hasReachedMaxCount = totalCount >= maxFootprints;

        if (!hasReachedMaxCount)
        {
            return CreateNewInstance();
        }

        // 4. 到这里说明已经达到最大数量，不能再创建新对象
        // 如果允许复用最旧脚印，并且确实存在正在使用的脚印，就复用它
        if (recycleOldestWhenFull && _active.Count > 0)
        {
            PooledFootprintDecal oldest = _active.First.Value;

            _active.RemoveFirst();

            oldest.StopLifetime();
            oldest.IsActiveInPool = false;

            return oldest;
        }

        // 5. 如果不允许复用旧脚印，就返回 null，表示这次不生成脚印
        return null;
    }

    /// <summary>
    /// 回收脚印。
    /// </summary>
    public void Release(PooledFootprintDecal pooled)
    {
        if (pooled == null)
            return;

        if (!pooled.IsActiveInPool)
            return;

        pooled.IsActiveInPool = false;

        // 从正在使用列表中移除
        _active.Remove(pooled);

        ReleaseToAvailable(pooled);
    }

    /// <summary>
    /// 把脚印放回可用队列。
    /// </summary>
    private void ReleaseToAvailable(PooledFootprintDecal pooled)
    {
        if (pooled == null)
            return;

        pooled.StopLifetime();

        ScreenSpaceDecalProjector projector = pooled.Projector;

        if (projector != null)
        {
            projector.opacity = 0f;

            // 禁用对象。
            // ScreenSpaceDecalProjector.OnDisable 会从 ActiveProjectors 移除，
            // 所以禁用后的脚印不会继续被绘制。
            projector.gameObject.SetActive(false);
        }

        _available.Enqueue(pooled);
    }

    /// <summary>
    /// Game 视图调试信息。
    /// </summary>
    private void OnGUI()
    {
        if (!showDebugGUI)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 100), GUI.skin.box);

        GUILayout.Label($"Footprints Active: {ActiveCount}");
        GUILayout.Label($"Footprints Available: {AvailableCount}");
        GUILayout.Label($"Footprints Max: {maxFootprints}");

        GUILayout.EndArea();
    }
}

/// <summary>
/// 单个池化脚印实例。
///
/// 这个组件挂在每个脚印对象上，
/// 只负责管理当前脚印自己的生命周期协程。
/// </summary>
public class PooledFootprintDecal : MonoBehaviour
{
    /// <summary>
    /// 当前脚印对象上的 ScreenSpaceDecalProjector。
    /// </summary>
    public ScreenSpaceDecalProjector Projector { get; private set; }

    /// <summary>
    /// 当前脚印是否被对象池认为是正在使用状态。
    /// </summary>
    public bool IsActiveInPool { get; set; }

    private FootprintDecalPool _pool;
    private Coroutine _lifetimeCoroutine;

    /// <summary>
    /// 初始化池化脚印。
    /// </summary>
    public void Initialize(FootprintDecalPool pool, ScreenSpaceDecalProjector projector)
    {
        _pool = pool;
        Projector = projector;
    }

    /// <summary>
    /// 开始脚印生命周期。
    /// </summary>
    public void PlayLifetime(float visibleTime, float fadeTime)
    {
        StopLifetime();

        _lifetimeCoroutine = StartCoroutine(LifetimeCoroutine(visibleTime, fadeTime));
    }

    /// <summary>
    /// 停止当前生命周期协程。
    /// 复用旧脚印时需要先停止旧协程。
    /// </summary>
    public void StopLifetime()
    {
        if (_lifetimeCoroutine != null)
        {
            StopCoroutine(_lifetimeCoroutine);
            _lifetimeCoroutine = null;
        }
    }

    /// <summary>
    /// 脚印生命周期：
    /// 先完整显示，再逐渐淡出，最后回收到对象池。
    /// </summary>
    private IEnumerator LifetimeCoroutine(float visibleTime, float fadeTime)
    {
        if (Projector == null)
            yield break;

        Projector.opacity = 1f;

        if (visibleTime > 0f)
        {
            yield return new WaitForSeconds(visibleTime);
        }

        float startOpacity = Projector.opacity;
        float timer = 0f;

        if (fadeTime <= 0f)
        {
            Projector.opacity = 0f;
        }
        else
        {
            while (timer < fadeTime)
            {
                if (Projector == null)
                    yield break;

                timer += Time.deltaTime;

                float t = Mathf.Clamp01(timer / fadeTime);
                Projector.opacity = Mathf.Lerp(startOpacity, 0f, t);

                yield return null;
            }

            Projector.opacity = 0f;
        }

        _lifetimeCoroutine = null;

        if (_pool != null)
        {
            _pool.Release(this);
        }
    }
}