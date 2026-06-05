using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// 水波 RT 调试绑定器。
///
/// 用途：
/// 1. 从 WaterRippleRTManager 里取出 CurrentBrushRT / AccumA / AccumB。
/// 2. 把选中的 RT 传给 WaterRippleTextureDebugUI 显示。
/// 3. 避免继续使用 Footprint / TextureDebugUI 旧名字，防止和脚印、雪地调试工具重名。
/// </summary>
[MovedFrom(false, null, null, "FootprintTextureDebugBinder")]
public class WaterRippleTextureDebugBinder : MonoBehaviour
{
    public enum Source
    {
        CurrentBrushRT,
        AccumA,
        AccumB,
        CurrentFrameRT,
        PrevFrameRT,
        PrevPrevFrameRT
    }

    [Header("Water Ripple")]
    [Tooltip("水波 RT 管理器。如果为空，会自动查找场景中的 WaterRippleRTManager。")]
    [FormerlySerializedAs("manager")]
    public WaterRippleRTManager waterRippleManager;

    [Header("Viewer")]
    [Tooltip("水波 RT 调试 UI。")]
    [FormerlySerializedAs("viewer")]
    public WaterRippleTextureDebugUI waterRippleViewer;

    [Tooltip("选择要查看的水波 RT。CurrentBrushRT = 当前帧 Brush；AccumA = 当前累积结果；AccumB = 写入缓冲。")]
    public Source source = Source.AccumA;

    private void Awake()
    {
        if (waterRippleManager == null)
            waterRippleManager = WaterRippleRTManager.Active != null
                ? WaterRippleRTManager.Active
                : FindObjectOfType<WaterRippleRTManager>();

        if (waterRippleViewer == null)
            waterRippleViewer = GetComponent<WaterRippleTextureDebugUI>();
    }

    private void LateUpdate()
    {
        if (waterRippleViewer == null)
            return;

        Texture tex = GetWaterRippleTexture();
        waterRippleViewer.SetTexture(tex);
        waterRippleViewer.SetTitle("Water Ripple / " + source);
    }

    private Texture GetWaterRippleTexture()
    {
        if (waterRippleManager == null)
            return null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                return waterRippleManager.CurrentBrushRT;

            case Source.AccumA:
                return waterRippleManager.AccumA;

            case Source.AccumB:
                return waterRippleManager.AccumB;
            
            case Source.CurrentFrameRT:
                return waterRippleManager.CurrentFrameRT;

            case Source.PrevFrameRT:
                return waterRippleManager.PrevFrameRT;
            
            case Source.PrevPrevFrameRT:
                return waterRippleManager.PrevPrevFrameRT;
            
            default:
                return null;
        }
    }
}
