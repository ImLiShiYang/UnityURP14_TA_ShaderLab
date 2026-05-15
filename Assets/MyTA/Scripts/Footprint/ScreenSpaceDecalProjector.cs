using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 模仿 Unity Decal Projector 的单个屏幕空间贴花投射器。
/// 本组件只负责保存贴花数据（材质、纹理、投射盒、UV、淡出等），
/// 实际绘制由 <see cref="ScreenSpaceDecalFeature"/> 中的 ScriptableRenderPass 完成。
/// </summary>
/// <remarks>
/// 坐标约定：local XY 为贴图平面，local Z 为投射深度方向。
/// </remarks>
[ExecuteAlways]
public class ScreenSpaceDecalProjector : MonoBehaviour
{
    /// <summary>
    /// 当前激活的贴花投射器（当前版本仅支持单个 decal）。
    /// </summary>
    public static ScreenSpaceDecalProjector ActiveProjector;
    
    /// <summary>
    /// 当前场景中所有启用的贴花投射器。
    /// 多 decal 版本使用 List 保存所有 projector。
    /// </summary>
    public static readonly List<ScreenSpaceDecalProjector> ActiveProjectors = new List<ScreenSpaceDecalProjector>();

    /// <summary>
    /// 模仿 Unity Decal Projector 的缩放模式。
    /// </summary>
    public enum DecalScaleMode
    {
        /// <summary>
        /// 忽略 Transform.localScale，贴花盒大小仅由 <see cref="size"/> 控制。
        /// </summary>
        ScaleInvariant,

        /// <summary>
        /// 继承 Transform 层级缩放，当前物体和父物体的 Scale 均影响最终贴花盒大小。
        /// </summary>
        InheritFromHierarchy
    }

    /// <summary>
    /// 贴花材质，通常使用自定义 Screen Space Decal shader。
    /// 为空时可在 Renderer Feature 中使用 fallback material。
    /// </summary>
    [Header("Material")]
    public Material decalMaterial;

    /// <summary>
    /// 贴花纹理（如弹孔、血迹、涂鸦等），最终传入 shader 的 _DecalTexture。
    /// </summary>
    [Header("Decal Texture")]
    public Texture2D decalTexture;
    
    [Header("Decal Normal")]
    [Tooltip("贴花法线贴图。用于给脚印制造假的凹陷 / 凸起光照效果。")]
    public Texture2D decalNormalTexture;

    [Tooltip(
        "法线效果强度。\n" +
        "0 = 不使用法线效果。\n" +
        "1 = 法线效果最强。\n\n" +
        "脚印建议 0.2 ~ 0.5，太大会像塑料凸起。"
    )]
    [Range(0f, 1f)]
    public float normalStrength = 0.35f;

    /// <summary>
    /// 贴花颜色，默认为白色（不额外改变贴图颜色）。
    /// </summary>
    [Header("Decal Color")]
    public Color decalColor = Color.white;

    /// <summary>
    /// 贴花缩放模式，默认 ScaleInvariant。
    /// </summary>
    [Header("Projector Box")]
    public DecalScaleMode scaleMode = DecalScaleMode.ScaleInvariant;

    /// <summary>
    /// 贴花投射盒尺寸（x = 宽度, y = 高度, z = 投射深度）。
    /// 渲染用的单位立方体会通过 Matrix4x4.Scale 缩放至此尺寸。
    /// </summary>
    [Tooltip("x = Width, y = Height, z = Projection Depth")]
    public Vector3 size = new Vector3(1f, 1f, 1f);

    /// <summary>
    /// 贴花盒子的中心偏移，类似 Unity Decal Projector 的 Pivot。
    /// </summary>
    [Tooltip("贴花盒子的中心偏移，类似 Unity Decal Projector 的 Pivot。")]
    public Vector3 pivot = Vector3.zero;

    /// <summary>
    /// UV 平铺次数（x = U方向, y = V方向）。
    /// </summary>
    [Header("UV")]
    public Vector2 tiling = Vector2.one;

    /// <summary>
    /// UV 偏移（x = U方向, y = V方向）。
    /// </summary>
    public Vector2 offset = Vector2.zero;

    
    /// <summary>
    /// 贴花整体透明度（0 = 全透明, 1 = 完全不透明）。
    /// 通常传入 shader 的 _DecalParams.x。
    /// </summary>
    [Header("Fade")]
    [Range(0f, 1f)]
    public float opacity = 1f;

    /// <summary>
    /// 贴花边缘淡出距离。值越大边缘越柔和。
    /// </summary>
    [Range(0f, 0.5f)]
    public float edgeFade = 0.02f;

    /// <summary>
    /// 最大绘制距离，摄像机距离超过此值后不再绘制。
    /// </summary>
    [Header("Distance Fade")]
    public float drawDistance = 1000f;

    /// <summary>
    /// 距离淡出开始比例。
    /// 例如 drawDistance=100、startFade=0.9 时：
    /// 0~90米完整显示，90~100米逐渐淡出，超过100米不显示。
    /// </summary>
    [Range(0f, 1f)]
    public float startFade = 0.9f;

    
    /// <summary>
    /// 角度淡出范围（x = 开始淡出角度，y = 完全淡出角度）。
    /// 比较接收表面 world normal 与 decal backward direction 的夹角。
    /// </summary>
    /// [Header("Angle Fade")]
    [Tooltip("单位：角度。x = 开始淡出角度，y = 完全淡出角度。测试时可以设为 0, 180。")]
    public Vector2 angleFade = new Vector2(0f, 180f);

    /// <summary>
    /// 组件启用时，将自己注册为当前激活的投射器。
    /// </summary>
    private void OnEnable()
    {
        // ActiveProjector = this;
        
        // 组件启用时，把自己加入全局 decal 列表。
        // Contains 用来避免重复添加。
        if (!ActiveProjectors.Contains(this))
        {
            ActiveProjectors.Add(this);
        }
    }

    /// <summary>
    /// 组件禁用时，清除当前激活的投射器引用。
    /// </summary>
    private void OnDisable()
    {
        // if (ActiveProjector == this)
        // {
        //     ActiveProjector = null;
        // }
        
        // 组件禁用或销毁时，从全局 decal 列表中移除自己。
        ActiveProjectors.Remove(this);
    }

    /// <summary>
    /// 编辑器校验：确保 size 不为零（避免除零），限制距离和角度参数的合法范围。
    /// </summary>
    private void OnValidate()
    {
        // 避免除零：后续会计算 1/size.x、1/size.y、1/size.z
        size.x = Mathf.Max(0.0001f, size.x);
        size.y = Mathf.Max(0.0001f, size.y);
        size.z = Mathf.Max(0.0001f, size.z);

        drawDistance = Mathf.Max(0.0001f, drawDistance);

        // 把开始淡出角度限制在 0 到 180 度之间。
        // Clamp 的作用是：小于 0 时取 0，大于 180 时取 180，中间值保持不变。
        angleFade.x = Mathf.Clamp(angleFade.x, 0f, 180f);
        // 结束角度必须大于开始角度，加 0.001f 避免 smoothstep 边界重合导致计算不稳定
        angleFade.y = Mathf.Clamp(angleFade.y, angleFade.x + 0.001f, 180f);
    }

    /// <summary>
    /// 计算 world -> normalized decal local 矩阵。
    /// shader 用此矩阵将 worldPos 转换到归一化局部空间（-0.5 ~ 0.5），超出范围则 discard。
    /// </summary>
    /// <returns>世界空间到归一化贴花局部空间的变换矩阵。</returns>
    public Matrix4x4 GetWorldToDecalMatrix()
    {
        // 避免除零
        Vector3 safeSize = new Vector3(Mathf.Max(0.0001f, size.x), Mathf.Max(0.0001f, size.y), Mathf.Max(0.0001f, size.z));

        Matrix4x4 worldToLocal;

        if (scaleMode == DecalScaleMode.InheritFromHierarchy)
        {
            worldToLocal = transform.worldToLocalMatrix;
        }
        else
        {
            // Matrix4x4.TRS(position, rotation, scale) 会创建一个 local -> world 矩阵。
            //
            // 这里使用：
            // transform.position：当前 Projector 的世界位置。
            // transform.rotation：当前 Projector 的世界旋转。
            // Vector3.one：缩放固定为 (1, 1, 1)，也就是故意忽略 Transform Scale。
            //
            // Matrix4x4.TRS(...) 本身得到的是 local -> world。
            // 后面的 .inverse 表示取逆矩阵。
            // local -> world 的逆矩阵就是 world -> local。
            //
            // 所以这一行的最终结果是：
            // 得到一个只考虑位置和旋转、不考虑缩放的 world -> local 矩阵。
            //
            // 这样做是为了实现 ScaleInvariant 模式：
            // Transform 的 Scale 不影响 decal 盒大小，decal 的真实尺寸只由 size 字段控制。
            worldToLocal = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
        }

        // 绘图时用 +pivot，反向转换时用 -pivot
        Matrix4x4 pivotMatrix = Matrix4x4.Translate(-pivot);
        // 1/size 将真实尺寸归一化到单位盒范围
        Matrix4x4 normalizeMatrix = Matrix4x4.Scale(new Vector3(1f / safeSize.x, 1f / safeSize.y, 1f / safeSize.z));

        // 矩阵作用顺序（右到左）：worldToLocal → pivotMatrix → normalizeMatrix
        return normalizeMatrix * pivotMatrix * worldToLocal;
    }

    /// <summary>
    /// 计算 decal 体积盒的 local -> world 矩阵。
    /// 将 -0.5~0.5 的单位立方体变换为真实位置、旋转、尺寸的投射盒，供 cmd.DrawMesh 使用。
    /// </summary>
    /// <returns>贴花投射盒的局部空间到世界空间的变换矩阵。</returns>
    public Matrix4x4 GetDecalLocalToWorldMatrix()
    {
        Matrix4x4 baseMatrix;

        if (scaleMode == DecalScaleMode.InheritFromHierarchy)
        {
            baseMatrix = transform.localToWorldMatrix;
        }
        else
        {
            baseMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }

        // 矩阵作用顺序（右到左）：Scale(size) → Translate(pivot) → baseMatrix
        return baseMatrix * Matrix4x4.Translate(pivot) * Matrix4x4.Scale(size);
    }

    /// <summary>
    /// Decal 的 backward world direction（local +Z 为投射方向，backward 为其反向）。
    /// </summary>
    public Vector3 DecalBackwardWS
    {
        get { return -transform.forward.normalized; }
    }

    /// <summary>
    /// 在 Scene View 中绘制贴花投射盒线框和投射方向线。
    /// </summary>
    
    /*
    private void OnDrawGizmos()
    {
        // 绘制贴花盒线框
        Gizmos.color = new Color(0f, 0.7f, 1f, 0.25f);
        Gizmos.matrix = GetDecalLocalToWorldMatrix();
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        // 单独构造 baseMatrix 画方向线，避免被 size 缩放影响
        Matrix4x4 baseMatrix;

        if (scaleMode == DecalScaleMode.InheritFromHierarchy)
        {
            baseMatrix = transform.localToWorldMatrix;
        }
        else
        {
            baseMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        }

        Gizmos.matrix = baseMatrix;
        Gizmos.color = new Color(0f, 0.9f, 1f, 1f);

        // Vector3.forward 即 local +Z，表示投射方向
        Gizmos.DrawLine(pivot, pivot + Vector3.forward * Mathf.Max(0.25f, size.z * 0.5f));
    }
    */
}