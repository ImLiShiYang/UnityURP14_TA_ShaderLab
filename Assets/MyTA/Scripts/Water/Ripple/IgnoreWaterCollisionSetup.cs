using UnityEngine;

public class IgnoreWaterCollisionSetup : MonoBehaviour
{
    private void Awake()
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        int customWaterLayer = LayerMask.NameToLayer("CustomWater");
        int waterBrushLayer = LayerMask.NameToLayer("WaterRippleBrush");

        if (playerLayer >= 0 && customWaterLayer >= 0)
        {
            Physics.IgnoreLayerCollision(playerLayer, customWaterLayer, true);
        }

        if (playerLayer >= 0 && waterBrushLayer >= 0)
        {
            Physics.IgnoreLayerCollision(playerLayer, waterBrushLayer, true);
        }
    }
}