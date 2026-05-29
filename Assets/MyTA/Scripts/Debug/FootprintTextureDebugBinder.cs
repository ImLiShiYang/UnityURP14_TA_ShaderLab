using UnityEngine;

public class FootprintTextureDebugBinder : MonoBehaviour
{
    public enum DebugTarget
    {
        OldFootprint,
        SnowFootprint
    }

    public enum Source
    {
        CurrentBrushRT,
        AccumA,
        AccumB
    }

    [Header("Target")]
    [Tooltip("OldFootprint = 旧脚印 RT；SnowFootprint = 新雪地 RT。")]
    public DebugTarget debugTarget = DebugTarget.OldFootprint;

    [Header("Old Footprint")]
    public FootprintRTManager manager;

    [Header("Snow Footprint")]
    public SnowFootprintRTManager snowManager;

    [Header("Viewer")]
    public TextureDebugUI viewer;
    public Source source = Source.AccumA;

    private void Awake()
    {
        if (manager == null)
            manager = FindObjectOfType<FootprintRTManager>();

        if (snowManager == null)
            snowManager = FindObjectOfType<SnowFootprintRTManager>();
    }

    private void LateUpdate()
    {
        if (viewer == null)
            return;

        Texture tex = null;
        string titlePrefix = "Footprint";

        switch (debugTarget)
        {
            case DebugTarget.OldFootprint:
                tex = GetOldFootprintTexture();
                titlePrefix = "Footprint";
                break;

            case DebugTarget.SnowFootprint:
                tex = GetSnowFootprintTexture();
                titlePrefix = "Snow Footprint";
                break;
        }

        viewer.SetTexture(tex);
        viewer.SetTitle(titlePrefix + " / " + source);
    }

    private Texture GetOldFootprintTexture()
    {
        if (manager == null)
            return null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                return manager.CurrentBrushRT;

            case Source.AccumA:
                return manager.AccumA;

            case Source.AccumB:
                return manager.AccumB;

            default:
                return null;
        }
    }

    private Texture GetSnowFootprintTexture()
    {
        if (snowManager == null)
            return null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                return snowManager.CurrentBrushRT;

            case Source.AccumA:
                return snowManager.AccumA;

            case Source.AccumB:
                return snowManager.AccumB;

            default:
                return null;
        }
    }
}
