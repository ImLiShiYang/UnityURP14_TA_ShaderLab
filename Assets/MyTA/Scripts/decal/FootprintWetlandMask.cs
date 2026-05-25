using UnityEngine;

/// <summary>
/// 岸边湿地区域判断。
///
/// 这个脚本只负责一件事：
/// 判断某个世界坐标 worldPosition 是否位于“岸边湿地范围”内。
///
/// 它不负责生成脚印。
/// 它也不负责渲染湿地。
///
/// FootprintDecalSpawner 只需要调用：
/// wetlandMask.CanSpawnAt(hit.point)
///
/// 如果返回 true：
///     当前脚踩的位置是湿地，可以生成脚印。
///
/// 如果返回 false：
///     当前脚踩的位置不是湿地，不生成脚印。
///
/// 这样可以让：
/// FootprintDecalSpawner 负责脚印生成逻辑，
/// FootprintWetlandMask 负责湿地区域规则，
/// 两个功能分开，代码不会越来越臃肿。
/// </summary>
public class FootprintWetlandMask : MonoBehaviour
{
    // ============================================================
    // Wetland Area Settings
    // ============================================================

    [Header("Wetland Area")]

    [Tooltip("是否启用湿地区域限制。\n关闭后，CanSpawnAt 会永远返回 true，任何地方都允许生成脚印。")]
    public bool enableMask = true;

    [Tooltip(
        "岸线的世界 Z 坐标。\n" +
        "例如水岸线固定在 Z = 41，那么这里就填 41。\n\n" +
        "这个值相当于一条横向的分界线：\n" +
        "Z = shoreZ 的地方最湿，越远离岸线越干。"
    )]
    public float shoreZ = 41f;

    [Tooltip(
        "湿地从岸线向陆地方向延伸的宽度。\n\n" +
        "例如 wetWidth = 4：\n" +
        "从 Z = shoreZ 开始，往陆地方向 4 个 Unity 单位内属于湿地过渡区域。\n" +
        "超过这个距离后，就认为是干地，不生成脚印。"
    )]
    [Min(0.0001f)]
    public float wetWidth = 4f;

    [Tooltip(
        "陆地方向。\n\n" +
        "landSide = 1：\n" +
        "    陆地在 Z < shoreZ 的一侧。\n" +
        "    水在 Z > shoreZ 的一侧。\n\n" +
        "landSide = -1：\n" +
        "    陆地在 Z > shoreZ 的一侧。\n" +
        "    水在 Z < shoreZ 的一侧。\n\n" +
        "简单理解：\n" +
        "如果你的湿地应该出现在岸线 Z 值更小的一边，用 1。\n" +
        "如果你的湿地应该出现在岸线 Z 值更大的一边，用 -1。"
    )]
    public float landSide = 1f;

    [Tooltip(
        "湿度阈值。\n\n" +
        "CalculateWetMask 会返回 0~1：\n" +
        "1 = 最湿，靠近岸线。\n" +
        "0 = 最干，远离岸线。\n\n" +
        "只有 wetMask >= minWetMaskToSpawn 时，才允许生成脚印。\n\n" +
        "值越小：脚印范围越大。\n" +
        "值越大：只有更靠近岸边的地方才有脚印。"
    )]
    [Range(0f, 1f)]
    public float minWetMaskToSpawn = 0.2f;


    // ============================================================
    // Debug Gizmos Settings
    // ============================================================

    [Header("Debug Gizmos")]

    [Tooltip("Scene View 中显示岸线和湿地区域调试框。")]
    public bool showGizmos = true;

    [Tooltip("调试区域在 X 方向显示多宽，只影响 Scene View 可视化，不影响实际逻辑。")]
    public float gizmoWidth = 40f;

    [Tooltip("调试框高度，只影响 Scene View 可视化，不影响实际逻辑。")]
    public float gizmoHeight = 0.02f;


    // ============================================================
    // Public API
    // ============================================================

    /// <summary>
    /// 判断某个世界坐标是否允许生成脚印。
    ///
    /// FootprintDecalSpawner 只需要调用这个函数。
    ///
    /// 参数：
    /// worldPosition：
    ///     一般传 RaycastHit.point。
    ///     也就是脚底射线打到地面的世界坐标。
    ///
    /// 返回：
    /// true：
    ///     当前点在湿地区域内，可以生成脚印。
    ///
    /// false：
    ///     当前点不在湿地区域内，不生成脚印。
    /// </summary>
    public bool CanSpawnAt(Vector3 worldPosition)
    {
        // 如果没有启用 mask，就不做任何限制。
        // 这种情况下，任何位置都允许生成脚印。
        if (!enableMask)
            return true;

        // 计算当前位置的湿度。
        // wetMask 范围是 0~1。
        float wetMask = CalculateWetMask(worldPosition);

        // 只有湿度大于等于阈值，才允许生成脚印。
        return wetMask >= minWetMaskToSpawn;
    }

    /// <summary>
    /// 计算某个世界坐标的湿地遮罩值。
    ///
    /// 返回值范围：
    /// 1 = 最湿，通常在岸线附近。
    /// 0 = 最干，远离岸线，或者在水的一侧。
    ///
    /// 这个函数的核心逻辑是：
    /// 1. 先判断这个点是不是在陆地一侧。
    /// 2. 再计算它距离岸线有多远。
    /// 3. 离岸线越近，wetMask 越大。
    /// 4. 离岸线越远，wetMask 越小。
    /// </summary>
    public float CalculateWetMask(Vector3 worldPosition)
    {
        // ------------------------------------------------------------
        // 1. 计算当前点到岸线的“陆地方向距离”
        // ------------------------------------------------------------
        //
        // worldPosition.z：
        //     当前脚踩位置的世界 Z 坐标。
        //
        // shoreZ：
        //     岸线的世界 Z 坐标。
        //
        // landSide：
        //     用来决定哪一边是陆地。
        //
        // 假设 shoreZ = 41：
        //
        // 情况 A：landSide = 1
        //     陆地在 Z < 41。
        //
        //     如果角色站在 Z = 39：
        //         landDistance = (41 - 39) * 1 = 2
        //         是正数，说明在陆地一侧。
        //
        //     如果角色站在 Z = 43：
        //         landDistance = (41 - 43) * 1 = -2
        //         是负数，说明在水的一侧。
        //
        // 情况 B：landSide = -1
        //     陆地在 Z > 41。
        //
        //     如果角色站在 Z = 43：
        //         landDistance = (41 - 43) * -1 = 2
        //         是正数，说明在陆地一侧。
        //
        //     如果角色站在 Z = 39：
        //         landDistance = (41 - 39) * -1 = -2
        //         是负数，说明在水的一侧。
        float landDistance = (shoreZ - worldPosition.z) * landSide;

        // ------------------------------------------------------------
        // 2. 如果 landDistance < 0，说明这个点在水的一侧
        // ------------------------------------------------------------
        //
        // 水里不应该生成泥地脚印。
        // 所以直接返回 0，表示完全不湿地。
        //
        // 注意：
        // 这里的“湿地”指的是岸边陆地上的湿地，
        // 不是水面本身。
        if (landDistance < 0f)
            return 0f;

        // ------------------------------------------------------------
        // 3. 把距离转换成 0~1 的归一化值
        // ------------------------------------------------------------
        //
        // landDistance = 0：
        //     正好在岸线上。
        //     t = 0。
        //
        // landDistance = wetWidth：
        //     正好到达湿地范围边缘。
        //     t = 1。
        //
        // landDistance > wetWidth：
        //     超出湿地范围。
        //     Clamp01 后 t 仍然是 1。
        //
        // 这样后面就可以用 t 来做平滑过渡。
        float t = Mathf.Clamp01(landDistance / Mathf.Max(0.0001f, wetWidth));

        // ------------------------------------------------------------
        // 4. smoothstep 平滑过渡
        // ------------------------------------------------------------
        //
        // 公式：
        // smooth = t * t * (3 - 2 * t)
        //
        // 这是 smoothstep 的标准公式。
        //
        // 它的作用是：
        // 让 0 到 1 的变化更柔和，不是线性硬切。
        //
        // 普通线性变化：
        //     0.0, 0.1, 0.2, 0.3 ...
        //
        // smoothstep：
        //     一开始变化慢，中间变化快，结尾又变慢。
        //
        // 用在湿地边缘时，看起来会更自然。
        float smooth = t * t * (3f - 2f * t);

        // ------------------------------------------------------------
        // 5. 反转结果，得到 wetMask
        // ------------------------------------------------------------
        //
        // smooth 的含义：
        //     0 = 靠近岸线
        //     1 = 远离岸线
        //
        // 但我们想要的 wetMask 是：
        //     1 = 靠近岸线，最湿
        //     0 = 远离岸线，最干
        //
        // 所以要用 1 - smooth。
        float wetMask = 1f - smooth;

        return wetMask;
    }


    // ============================================================
    // Editor Validation
    // ============================================================

    /// <summary>
    /// OnValidate 会在 Inspector 中修改参数时自动调用。
    ///
    /// 这里用来保证参数不会出现明显错误：
    /// 1. wetWidth 不能为 0，否则计算时会除以 0。
    /// 2. landSide 不应该是 0，只允许接近 1 或 -1。
    /// </summary>
    private void OnValidate()
    {
        // 湿地宽度不能为 0。
        wetWidth = Mathf.Max(0.0001f, wetWidth);

        // landSide 只保留方向意义。
        // 大于等于 0 就当成 1。
        // 小于 0 就当成 -1。
        //
        // 这样可以避免用户在 Inspector 里填 0、0.3、2 之类的值导致逻辑混乱。
        landSide = landSide >= 0f ? 1f : -1f;

        // Gizmo 显示尺寸也限制一下，避免误填 0。
        gizmoWidth = Mathf.Max(0.1f, gizmoWidth);
        gizmoHeight = Mathf.Max(0.001f, gizmoHeight);
    }


    // ============================================================
    // Gizmos
    // ============================================================

    /// <summary>
    /// 只在选中这个物体时绘制 Gizmos。
    ///
    /// 它不会影响游戏运行。
    /// 它只是帮助你在 Scene View 中看清楚：
    /// 1. 岸线在哪里。
    /// 2. 湿地区域大概覆盖到哪里。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!showGizmos)
            return;

        // ------------------------------------------------------------
        // 1. 绘制岸线
        // ------------------------------------------------------------
        //
        // 当前逻辑里，岸线是一条 Z = shoreZ 的直线。
        //
        // X 方向画一条长线。
        // Z 固定为 shoreZ。
        // Y 使用当前物体的 Y。
        //
        // 所以你可以把这个脚本挂到一个空物体上，
        // 然后通过移动空物体的 Y 来控制 Gizmo 线显示高度。
        Gizmos.color = Color.cyan;

        Vector3 center = new Vector3(transform.position.x, transform.position.y, shoreZ);

        Vector3 left = center + Vector3.left * (gizmoWidth * 0.5f);
        Vector3 right = center + Vector3.right * (gizmoWidth * 0.5f);

        // Gizmos.DrawLine(left, right);

        // ------------------------------------------------------------
        // 2. 绘制湿地区域范围
        // ------------------------------------------------------------
        //
        // 湿地从 shoreZ 往陆地方向延伸 wetWidth。
        //
        // 如果 landSide = 1：
        //     陆地在 Z < shoreZ。
        //     wetEndZ = shoreZ - wetWidth。
        //
        // 如果 landSide = -1：
        //     陆地在 Z > shoreZ。
        //     wetEndZ = shoreZ + wetWidth。
        //
        // 公式统一写成：
        //     wetEndZ = shoreZ - wetWidth * landSide
        //
        // landSide = 1:
        //     wetEndZ = shoreZ - wetWidth
        //
        // landSide = -1:
        //     wetEndZ = shoreZ + wetWidth
        float wetEndZ = shoreZ - wetWidth * landSide;

        // 湿地区域中心点在 shoreZ 和 wetEndZ 的中间。
        Vector3 wetCenter = new Vector3(
            transform.position.x,
            transform.position.y,
            (shoreZ + wetEndZ) * 0.5f
        );

        // 湿地区域大小：
        // X = gizmoWidth，只是为了看得清楚。
        // Y = gizmoHeight，让它像一层很薄的区域。
        // Z = wetWidth，表示湿地实际延伸宽度。
        Vector3 wetSize = new Vector3(
            gizmoWidth,
            gizmoHeight,
            Mathf.Abs(wetWidth)
        );

        // 用半透明青色画湿地区域盒子。
        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawWireCube(wetCenter, wetSize);
    }
}