using UnityEngine;

/// <summary>
/// 把草地交互 RT 绑定到 WaterRippleTextureDebugUI 上显示。
///
/// 当前草地 RT 协议：
/// 黑色 = 没有草地交互
/// 白色 = 草被踩压 / Brush 写入区域
///
/// 注意：
/// 第一版草地系统只有 CurrentBrushRT，
/// 后面如果加入 AccumRT / 恢复衰减，再扩展 Source 枚举。
/// </summary>
public class GrassInteractionTextureDebugBinder : MonoBehaviour
{
    public enum Source
    {
        CurrentBrushRT,
        AccumulatedRT
    }

    [Header("Grass Interaction")]
    public GrassInteractionRTManager grassInteractionManager;

    [Header("Viewer")]
    [Tooltip("这里先复用 WaterRippleTextureDebugUI，不需要重新写 UI。")]
    public WaterRippleTextureDebugUI viewer;

    [Tooltip("当前显示的草地交互 RT。")]
    public Source source = Source.CurrentBrushRT;

    private void Awake()
    {
        if (grassInteractionManager == null)
        {
            grassInteractionManager = GrassInteractionRTManager.Active != null
                ? GrassInteractionRTManager.Active
                : FindObjectOfType<GrassInteractionRTManager>();
        }

        if (viewer == null)
            viewer = GetComponent<WaterRippleTextureDebugUI>();
    }

    private void LateUpdate()
    {
        if (viewer == null)
            return;

        Texture texture = GetGrassInteractionTexture();

        viewer.SetTexture(texture);
        viewer.SetTitle("Grass Interaction / " + source);
    }

    private Texture GetGrassInteractionTexture()
    {
        if (grassInteractionManager == null)
            return null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                return grassInteractionManager.CurrentBrushRT;

            case Source.AccumulatedRT:
                return grassInteractionManager.AccumA;

            default:
                return null;
        }
    }
}
