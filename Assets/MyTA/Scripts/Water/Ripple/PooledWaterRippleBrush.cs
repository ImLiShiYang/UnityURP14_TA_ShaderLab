using UnityEngine;

/// <summary>
/// 单个“水波 Brush”对象池实例的生命周期组件。
///
/// 这个脚本通常挂在池中生成出来的 Brush 预制体实例上。
/// 它不负责生成水波，也不负责材质参数配置；这些都由 <see cref="WaterRippleBrushPool"/> 统一处理。
/// 本类只负责缓存常用组件，并在生命周期结束时把自己归还给对象池。
/// </summary>
public class PooledWaterRippleBrush : MonoBehaviour
{
    /// <summary>
    /// 当前池对象对应的 Brush 根物体。
    /// 一般就是由对象池 Instantiate 出来的那个 Quad / Brush Prefab 实例。
    /// </summary>
    public GameObject BrushObject { get; private set; }

    /// <summary>
    /// Brush 物体及其子物体上的所有 Renderer。
    /// 对象池会通过这些 Renderer 设置 MaterialPropertyBlock，让同一个材质实例可以复用不同的贴图和强度参数。
    /// </summary>
    public Renderer[] Renderers { get; private set; }

    /// <summary>
    /// Brush 物体及其子物体上的所有 Collider。
    /// 水波 Brush 只是用来写入 RT 的渲染输入，一般不应该参与物理或射线检测，所以对象池里通常会禁用它们。
    /// </summary>
    public Collider[] Colliders { get; private set; }

    /// <summary>
    /// 当前 Brush 专用的材质属性块。
    /// 使用 MaterialPropertyBlock 可以避免运行时频繁实例化材质，同时让每个 Brush 拥有独立的贴图和强度参数。
    /// </summary>
    public MaterialPropertyBlock PropertyBlock { get; private set; }

    /// <summary>
    /// 当前 Brush 是否正处于对象池的 active 链表中。
    /// 用这个标记避免同一个对象被重复 Release，导致池状态错乱。
    /// </summary>
    public bool IsActiveInPool { get; set; }

    // 这个 Brush 所属的对象池。生命周期结束后需要通过它归还自己。
    private WaterRippleBrushPool pool;

    // 这个 Brush 应该被回收的时间点，单位是 Time.time。
    private float releaseTime;

    // 是否正在计时。false 时 Update 会被关闭，避免无意义的每帧开销。
    private bool hasReleaseTime;

    /// <summary>
    /// 初始化池对象，并缓存后续会反复使用的组件引用。
    ///
    /// 注意：这个方法只应该由 <see cref="WaterRippleBrushPool"/> 创建实例时调用。
    /// </summary>
    /// <param name="ownerPool">创建并管理该 Brush 的对象池。</param>
    /// <param name="brushObject">池中实际复用的 Brush 根物体。</param>
    public void Initialize(WaterRippleBrushPool ownerPool, GameObject brushObject)
    {
        pool = ownerPool;
        BrushObject = brushObject;

        // 缓存 Renderer / Collider，之后 Spawn 和 Prepare 时不用反复 GetComponentsInChildren。
        // true 表示即使子物体当前是 inactive，也能被找到。
        Renderers = brushObject.GetComponentsInChildren<Renderer>(true);
        Colliders = brushObject.GetComponentsInChildren<Collider>(true);

        // 每个池对象保留一个独立 MPB，后续只 Clear / Set，不重复 new。
        PropertyBlock = new MaterialPropertyBlock();

        // 默认不启用 Update。只有 PlayLifetime 后才开始计时。
        enabled = false;
    }

    /// <summary>
    /// 开始一次生命周期计时。
    /// 到达 lifeTime 后，Update 会自动把该 Brush 归还给对象池。
    /// </summary>
    /// <param name="lifeTime">本次 Brush 保持激活的时间，单位秒。</param>
    public void PlayLifetime(float lifeTime)
    {
        // 先清理旧计时，避免同一个对象被复用时残留上一次的状态。
        StopLifetime();

        // 最小给 0.001 秒，避免 lifeTime 为 0 时立刻进入异常边界情况。
        releaseTime = Time.time + Mathf.Max(0.001f, lifeTime);
        hasReleaseTime = true;

        // 只有生命周期进行中才启用 Update，减少空闲对象的性能消耗。
        enabled = true;
    }

    /// <summary>
    /// 停止生命周期计时。
    /// 通常在对象被回收、被强制复用，或者进入 available 队列前调用。
    /// </summary>
    public void StopLifetime()
    {
        hasReleaseTime = false;
        enabled = false;
    }

    /// <summary>
    /// 生命周期倒计时。
    /// 时间到后通知对象池 Release 自己。
    /// </summary>
    private void Update()
    {
        // 没有计时，或者还没到回收时间，就继续等待。
        if (!hasReleaseTime || Time.time < releaseTime)
            return;

        // 先关闭自身计时，避免 Release 过程中出现重复触发。
        hasReleaseTime = false;
        enabled = false;

        // 生命周期结束后归还对象池。
        if (pool != null)
            pool.Release(this);
    }
}
