using UnityEngine;

/// <summary>
/// 动画事件接收器。
///
/// 这个脚本挂在带 Animator 的模型物体上。
///
/// Animation Event 调用：
/// - SpawnLeftFootprint()
/// - SpawnRightFootprint()
///
/// 然后这个脚本可以同时转发给：
/// 1. Decal 脚印系统
/// 2. RT Brush 脚印系统
///
/// 这样你不需要给 Decal 和 RT 分别写两套 Animation Event。
/// </summary>
public class FootstepEventReceiver : MonoBehaviour
{
    [Header("Decal Footprint")]
    [Tooltip("是否把动画事件转发给 Decal 脚印系统。")]
    public bool enableDecalFootprint = true;

    [Tooltip("旧的 Decal 脚印生成器。")]
    public FootprintDecalSpawner decalSpawner;


    [Header("RT Brush Footprint")]
    [Tooltip("是否把动画事件转发给 RT Brush 脚印系统。")]
    public bool enableRTFootprint = true;

    [Tooltip("新的 RT Brush 脚印生成器。")]
    public FootprintBrushSpawner brushSpawner;


    [Header("Debug")]
    public bool logEvent = false;


    private void Awake()
    {
        // 自动从父物体查找 Decal 生成器。
        if (decalSpawner == null)
        {
            decalSpawner = GetComponentInParent<FootprintDecalSpawner>();
        }

        // 自动从父物体查找 RT Brush 生成器。
        if (brushSpawner == null)
        {
            brushSpawner = GetComponentInParent<FootprintBrushSpawner>();
        }
    }

    /// <summary>
    /// 动画事件调用：左脚落地。
    /// </summary>
    public void SpawnLeftFootprint()
    {
        if (logEvent)
        {
            Debug.Log("[FootstepEventReceiver] SpawnLeftFootprint");
        }

        if (enableDecalFootprint && decalSpawner != null)
        {
            decalSpawner.SpawnLeftFootprint();
        }

        if (enableRTFootprint && brushSpawner != null)
        {
            brushSpawner.SpawnLeftFootprint();
        }
    }

    /// <summary>
    /// 动画事件调用：右脚落地。
    /// </summary>
    public void SpawnRightFootprint()
    {
        if (logEvent)
        {
            Debug.Log("[FootstepEventReceiver] SpawnRightFootprint");
        }

        if (enableDecalFootprint && decalSpawner != null)
        {
            decalSpawner.SpawnRightFootprint();
        }

        if (enableRTFootprint && brushSpawner != null)
        {
            brushSpawner.SpawnRightFootprint();
        }
    }
}