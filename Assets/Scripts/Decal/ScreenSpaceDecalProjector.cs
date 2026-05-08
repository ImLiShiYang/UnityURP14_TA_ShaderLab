using UnityEngine;

/// <summary>
/// 单个屏幕空间贴花投射器。
///
/// 这个组件只负责保存贴花数据：
/// 1. 投射盒子的 Transform
/// 2. 贴花纹理
/// 3. 颜色
/// 4. 透明度
///
/// 真正绘制发生在 ScreenSpaceDecalFeature 里。
/// </summary>
[ExecuteAlways]
public class ScreenSpaceDecalProjector : MonoBehaviour
{
    /// <summary>
    /// 当前场景中的激活贴花。
    ///
    /// 先做单个 Decal，所以这里用 static 保存当前 projector。
    /// 后面做多贴花时，再改成 List。
    /// </summary>
    public static ScreenSpaceDecalProjector ActiveProjector;

    [Header("Decal Texture")]
    public Texture2D decalTexture;

    [Header("Decal Color")]
    public Color decalColor = Color.white;

    [Range(0f, 1f)]
    public float opacity = 1f;

    [Header("Fade")]
    [Range(0f, 0.5f)]
    public float edgeFade = 0.05f;

    private void OnEnable()
    {
        ActiveProjector = this;
    }

    private void OnDisable()
    {
        if (ActiveProjector == this)
        {
            ActiveProjector = null;
        }
    }

    /// <summary>
    /// 返回世界到贴花本地空间的矩阵。
    ///
    /// 如果这个物体的 Scale 是：
    /// X = 4
    /// Y = 4
    /// Z = 2
    ///
    /// 那么 Decal Box 的范围就是一个 4 x 4 x 2 的盒子。
    ///
    /// 经过 worldToLocalMatrix 之后，
    /// 盒子内部通常落在 local space 的 -0.5 到 0.5 范围内。
    /// </summary>
    public Matrix4x4 GetWorldToDecalMatrix()
    {
        return transform.worldToLocalMatrix;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 0.7f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;

        // 画一个单位立方体。
        // 真实大小由 Transform Scale 控制。
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}