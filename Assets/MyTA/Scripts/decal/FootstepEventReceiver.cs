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
/// 3. Snow RT Brush 雪地压痕系统
/// 4. Shallow Water Ripple 浅水脚步水波系统
///
/// 这样动画里仍然只需要保留一套 Animation Event。
/// </summary>
public class FootstepEventReceiver : MonoBehaviour
{
    [Header("Decal Footprint")]
    [Tooltip("是否把动画事件转发给旧 Decal 脚印系统。")]
    public bool enableDecalFootprint = true;

    [Tooltip("旧的 Decal 脚印生成器。")]
    public FootprintDecalSpawner decalSpawner;


    [Header("Old RT Brush Footprint")]
    [Tooltip("是否把动画事件转发给旧 RT Brush 脚印系统。")]
    public bool enableRTFootprint = true;

    [Tooltip("旧的 RT Brush 脚印生成器。")]
    public FootprintBrushSpawner brushSpawner;


    [Header("Snow RT Brush Footprint")]
    [Tooltip("是否把动画事件转发给新 Snow RT Brush 雪地压痕系统。")]
    public bool enableSnowFootprint = true;

    [Tooltip("新的 Snow RT Brush 雪地压痕生成器。")]
    public SnowFootprintBrushSpawner snowBrushSpawner;


    [Header("Shallow Water Ripple")]
    [Tooltip("是否把动画事件转发给浅水脚步水波系统。")]
    public bool enableWaterRipple = true;

    [Tooltip("浅水脚步水波生成器。")]
    public ShallowWaterFootRipple waterRipple;


    [Header("Debug")]
    public bool logEvent = false;


    private void Awake()
    {
        if (decalSpawner == null)
        {
            decalSpawner = GetComponentInParent<FootprintDecalSpawner>();
        }

        if (brushSpawner == null)
        {
            brushSpawner = GetComponentInParent<FootprintBrushSpawner>();
        }

        if (snowBrushSpawner == null)
        {
            snowBrushSpawner = GetComponentInParent<SnowFootprintBrushSpawner>();
        }

        if (waterRipple == null)
        {
            waterRipple = GetComponentInParent<ShallowWaterFootRipple>();
        }
    }

    /// <summary>
    /// Animation Event 调用：左脚落地。
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

        if (enableSnowFootprint && snowBrushSpawner != null)
        {
            snowBrushSpawner.SpawnLeftFootprint();
        }
        
        if (enableWaterRipple && waterRipple != null)
        {
            Debug.Log("[FootstepEventReceiver] Call Left Water Ripple", this);
            waterRipple.SpawnLeftWaterRipple();
        }
    }

    /// <summary>
    /// Animation Event 调用：右脚落地。
    /// </summary>
    public void SpawnRightFootprint()
    {
        if (logEvent)
        {
            Debug.Log("[FootstepEventReceiver] SpawnLeftFootprint");
        }

        if (enableDecalFootprint && decalSpawner != null)
        {
            decalSpawner.SpawnRightFootprint();
        }

        if (enableRTFootprint && brushSpawner != null)
        {
            brushSpawner.SpawnRightFootprint();
        }

        if (enableSnowFootprint && snowBrushSpawner != null)
        {
            snowBrushSpawner.SpawnRightFootprint();
        }
        
        if (enableWaterRipple && waterRipple != null)
        {
            Debug.Log("[FootstepEventReceiver] Call Right Water Ripple", this);
            waterRipple.SpawnRightWaterRipple();
        }
    }
}