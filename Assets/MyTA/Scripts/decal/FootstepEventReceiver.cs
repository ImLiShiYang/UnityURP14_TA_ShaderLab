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

    [Header("波纹")]
    [Tooltip("是否把动画事件转发给浅水脚步水波系统。")]
    public bool enableWaterRippleBrushSpawner = true;
    
    [Tooltip("波纹。")]
    public WaterRippleBrushSpawner waterRippleBrushSpawner;

    [Header("Weapon Water Ripple")]
    [Tooltip("是否把武器划水动画事件转发给 WaterRippleWeaponEventTrail。")]
    public bool enableWeaponWaterRipple = true;

    [Tooltip("真正负责检测 WaveDetect 点并生成武器水波的脚本。")]
    public WaterRippleWeaponEventTrail weaponWaterRipple;
    
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
        
        if(waterRippleBrushSpawner==null)
        {
            waterRippleBrushSpawner=GetComponentInParent<WaterRippleBrushSpawner>();
        }

        if (weaponWaterRipple == null)
        {
            // WaterRippleWeaponEventTrail 可以挂在角色父物体、Animator 物体或子物体上。
            // 这里尽量自动查找，减少 Inspector 漏绑导致动画事件只接到但没有实际效果。
            weaponWaterRipple = GetComponentInParent<WaterRippleWeaponEventTrail>();
        }

        if (weaponWaterRipple == null)
        {
            weaponWaterRipple = GetComponentInChildren<WaterRippleWeaponEventTrail>();
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

        if (enableWaterRippleBrushSpawner && waterRippleBrushSpawner != null)
        {
            waterRippleBrushSpawner.SpawnLeftWaterRipple();
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

        if (enableWaterRippleBrushSpawner && waterRippleBrushSpawner != null)
        {
            waterRippleBrushSpawner.SpawnRightWaterRipple();
        }
        
    }

    public void BeginWeaponWaterRipple()
    {
        // 动画事件入口：攻击动作进入划水时间段。
        // 这个类只负责接收事件并转发，真正检测点的位置判断在 WaterRippleWeaponEventTrail。
        if (logEvent)
        {
            Debug.Log("[FootstepEventReceiver] BeginWeaponWaterRipple", this);
        }

        if (enableWeaponWaterRipple && weaponWaterRipple != null)
        {
            weaponWaterRipple.BeginWeaponWaterRipple();
        }
    }

    public void EndWeaponWaterRipple()
    {
        // 动画事件入口：攻击动作离开划水时间段。
        if (logEvent)
        {
            Debug.Log("[FootstepEventReceiver] EndWeaponWaterRipple", this);
        }

        if (enableWeaponWaterRipple && weaponWaterRipple != null)
        {
            weaponWaterRipple.EndWeaponWaterRipple();
        }
    }

    public void BeginWaterCut()
    {
        // 短别名。动画事件下拉里如果觉得 BeginWeaponWaterRipple 太长，可以用这个。
        BeginWeaponWaterRipple();
    }

    public void EndWaterCut()
    {
        // 短别名。动画事件下拉里如果觉得 EndWeaponWaterRipple 太长，可以用这个。
        EndWeaponWaterRipple();
    }
}
