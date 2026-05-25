using UnityEngine;

public class FootprintTextureDebugBinder : MonoBehaviour
{
    public enum Source
    {
        CurrentBrushRT,
        AccumA,
        AccumB
    }

    public FootprintRTManager manager;
    public TextureDebugUI viewer;
    public Source source = Source.AccumA;

    private void LateUpdate()
    {
        if (manager == null || viewer == null)
            return;

        Texture tex = null;

        switch (source)
        {
            case Source.CurrentBrushRT:
                tex = manager.CurrentBrushRT;
                break;

            case Source.AccumA:
                tex = manager.AccumA;
                break;

            case Source.AccumB:
                tex = manager.AccumB;
                break;
        }

        viewer.SetTexture(tex);
        viewer.SetTitle("Footprint / " + source);
    }
}