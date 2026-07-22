using UnityEngine;

/// <summary>
/// Binds one of the water ripple render textures to WaterRippleTextureDebugUI.
/// </summary>
public class WaterRippleTextureDebugBinder : MonoBehaviour
{
    public enum Source
    {
        CurrentBrushRT,
        CurrentFrameRT,
        PrevFrameRT,
        PrevPrevFrameRT
    }

    [Header("Water Ripple")]
    public WaterRippleRTManager waterRippleManager;

    [Header("Viewer")]
    public WaterRippleTextureDebugUI waterRippleViewer;

    [Tooltip("PrevFrameRT is the newest completed wave texture and is what the receiver material samples.")]
    public Source source = Source.PrevFrameRT;

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

        waterRippleViewer.SetSourceTexture(
            WaterRippleTextureDebugUI.TextureSource.WaterRipple,
            GetWaterRippleTexture(),
            "Water Ripple / " + source);
    }

    private Texture GetWaterRippleTexture()
    {
        if (waterRippleManager == null)
            return null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                return waterRippleManager.CurrentBrushRT;

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
