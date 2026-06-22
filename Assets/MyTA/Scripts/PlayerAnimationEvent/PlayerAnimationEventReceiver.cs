using UnityEngine;

/// <summary>
/// 角色动画事件专用接收器。
///
/// 这个脚本挂在带 Animator 的模型物体上。
///
/// Animation Event 调用：
/// - SpawnLeftFootprint()
/// - SpawnRightFootprint()
/// - BeginWeaponWaterRipple()
/// - EndWeaponWaterRipple()
///
/// 这个类只负责接收动画事件并转发给各个系统：
/// 1. Decal 脚印系统
/// 2. RT Brush 脚印系统
/// 3. Snow RT Brush 雪地压痕系统
/// 4. Shallow Water Ripple 浅水脚步水波系统
/// 5. Water Ripple Brush 水波 Brush 系统
/// 6. Grass Interaction Brush 草地交互系统
/// 7. Weapon Water Ripple 武器划水系统
///
/// 这样动画里仍然只需要保留一套 Animation Event。
/// </summary>
public class PlayerAnimationEventReceiver : MonoBehaviour
{
    // ============================================================
    // Footprint Systems
    // ============================================================

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
    [Tooltip("是否把动画事件转发给 Snow RT Brush 雪地压痕系统。")]
    public bool enableSnowFootprint = true;

    [Tooltip("Snow RT Brush 雪地压痕生成器。")]
    public SnowFootprintBrushSpawner snowBrushSpawner;


    // ============================================================
    // Water Systems
    // ============================================================

    [Header("Shallow Water Ripple")]
    [Tooltip("是否把动画事件转发给浅水脚步水波系统。")]
    public bool enableWaterRipple = true;

    [Tooltip("浅水脚步水波生成器。")]
    public ShallowWaterFootRipple waterRipple;


    [Header("Water Ripple Brush")]
    [Tooltip("是否把动画事件转发给水波 Brush 生成器。")]
    public bool enableWaterRippleBrushSpawner = true;

    [Tooltip("水波 Brush 生成器。")]
    public WaterRippleBrushSpawner waterRippleBrushSpawner;


    // ============================================================
    // Grass System
    // ============================================================

    [Header("Grass Interaction Brush")]
    [Tooltip("是否把脚步动画事件转发给草地交互 Brush 生成器。")]
    public bool enableGrassInteractionBrush = true;

    [Tooltip("草地交互 Brush 生成器。")]
    public GrassInteractionBrushSpawner grassInteractionBrushSpawner;


    // ============================================================
    // Weapon Water Ripple
    // ============================================================

    [Header("Weapon Water Ripple")]
    [Tooltip("是否把武器划水动画事件转发给 WaterRippleWeaponEventTrail。")]
    public bool enableWeaponWaterRipple = true;

    [Tooltip("真正负责检测 WaveDetect 点并生成武器水波的脚本。")]
    public WaterRippleWeaponEventTrail weaponWaterRipple;


    // ============================================================
    // Debug
    // ============================================================

    [Header("Debug")]
    public bool logEvent = false;


    // ============================================================
    // Unity Events
    // ============================================================

    private void Awake()
    {
        if (decalSpawner == null)
            decalSpawner = GetComponentInParent<FootprintDecalSpawner>();

        if (brushSpawner == null)
            brushSpawner = GetComponentInParent<FootprintBrushSpawner>();

        if (snowBrushSpawner == null)
            snowBrushSpawner = GetComponentInParent<SnowFootprintBrushSpawner>();

        if (waterRipple == null)
            waterRipple = GetComponentInParent<ShallowWaterFootRipple>();

        if (waterRippleBrushSpawner == null)
            waterRippleBrushSpawner = GetComponentInParent<WaterRippleBrushSpawner>();

        if (grassInteractionBrushSpawner == null)
            grassInteractionBrushSpawner = GetComponentInParent<GrassInteractionBrushSpawner>();

        if (weaponWaterRipple == null)
            weaponWaterRipple = GetComponentInParent<WaterRippleWeaponEventTrail>();

        if (weaponWaterRipple == null)
            weaponWaterRipple = GetComponentInChildren<WaterRippleWeaponEventTrail>();
    }


    // ============================================================
    // Foot Animation Events
    // ============================================================

    /// <summary>
    /// Animation Event 调用：左脚落地。
    ///
    /// 注意：
    /// 动画事件里可以继续使用原来的 SpawnLeftFootprint。
    /// 不需要额外再加一套 SpawnLeftGrassBrush 事件。
    /// </summary>
    public void SpawnLeftFootprint()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] SpawnLeftFootprint", this);

        if (enableDecalFootprint && decalSpawner != null)
            decalSpawner.SpawnLeftFootprint();

        if (enableRTFootprint && brushSpawner != null)
            brushSpawner.SpawnLeftFootprint();

        if (enableSnowFootprint && snowBrushSpawner != null)
            snowBrushSpawner.SpawnLeftFootprint();

        if (enableWaterRipple && waterRipple != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Left Shallow Water Ripple", this);

            waterRipple.SpawnLeftWaterRipple();
        }

        if (enableWaterRippleBrushSpawner && waterRippleBrushSpawner != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Left Water Ripple Brush", this);

            waterRippleBrushSpawner.SpawnLeftWaterRipple();
        }

        if (enableGrassInteractionBrush && grassInteractionBrushSpawner != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Left Grass Interaction Brush", this);

            grassInteractionBrushSpawner.SpawnLeftGrassBrush();
        }
    }

    /// <summary>
    /// Animation Event 调用：右脚落地。
    ///
    /// 注意：
    /// 动画事件里可以继续使用原来的 SpawnRightFootprint。
    /// 不需要额外再加一套 SpawnRightGrassBrush 事件。
    /// </summary>
    public void SpawnRightFootprint()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] SpawnRightFootprint", this);

        if (enableDecalFootprint && decalSpawner != null)
            decalSpawner.SpawnRightFootprint();

        if (enableRTFootprint && brushSpawner != null)
            brushSpawner.SpawnRightFootprint();

        if (enableSnowFootprint && snowBrushSpawner != null)
            snowBrushSpawner.SpawnRightFootprint();

        if (enableWaterRipple && waterRipple != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Right Shallow Water Ripple", this);

            waterRipple.SpawnRightWaterRipple();
        }

        if (enableWaterRippleBrushSpawner && waterRippleBrushSpawner != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Right Water Ripple Brush", this);

            waterRippleBrushSpawner.SpawnRightWaterRipple();
        }

        if (enableGrassInteractionBrush && grassInteractionBrushSpawner != null)
        {
            if (logEvent)
                Debug.Log("[PlayerAnimationEventReceiver] Call Right Grass Interaction Brush", this);

            grassInteractionBrushSpawner.SpawnRightGrassBrush();
        }
    }


    // ============================================================
    // Weapon Animation Events
    // ============================================================

    /// <summary>
    /// Animation Event 调用：攻击动作进入划水时间段。
    /// </summary>
    public void BeginWeaponWaterRipple()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] BeginWeaponWaterRipple", this);

        if (enableWeaponWaterRipple && weaponWaterRipple != null)
            weaponWaterRipple.BeginWeaponWaterRipple();
    }

    /// <summary>
    /// Animation Event 调用：攻击动作离开划水时间段。
    /// </summary>
    public void EndWeaponWaterRipple()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] EndWeaponWaterRipple", this);

        if (enableWeaponWaterRipple && weaponWaterRipple != null)
            weaponWaterRipple.EndWeaponWaterRipple();
    }

    /// <summary>
    /// 短别名。动画事件下拉里如果觉得 BeginWeaponWaterRipple 太长，可以用这个。
    /// </summary>
    public void BeginWaterCut()
    {
        BeginWeaponWaterRipple();
    }

    /// <summary>
    /// 短别名。动画事件下拉里如果觉得 EndWeaponWaterRipple 太长，可以用这个。
    /// </summary>
    public void EndWaterCut()
    {
        EndWeaponWaterRipple();
    }


    // ============================================================
    // Grass Animation Event Aliases
    // ============================================================

    /// <summary>
    /// 可选短别名：左脚草地交互。
    ///
    /// 如果以后你想让某些动画只触发草地 Brush，
    /// 可以直接在 Animation Event 里调用这个函数。
    /// </summary>
    public void SpawnLeftGrassBrush()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] SpawnLeftGrassBrush", this);

        if (enableGrassInteractionBrush && grassInteractionBrushSpawner != null)
            grassInteractionBrushSpawner.SpawnLeftGrassBrush();
    }

    /// <summary>
    /// 可选短别名：右脚草地交互。
    ///
    /// 如果以后你想让某些动画只触发草地 Brush，
    /// 可以直接在 Animation Event 里调用这个函数。
    /// </summary>
    public void SpawnRightGrassBrush()
    {
        if (logEvent)
            Debug.Log("[PlayerAnimationEventReceiver] SpawnRightGrassBrush", this);

        if (enableGrassInteractionBrush && grassInteractionBrushSpawner != null)
            grassInteractionBrushSpawner.SpawnRightGrassBrush();
    }
}