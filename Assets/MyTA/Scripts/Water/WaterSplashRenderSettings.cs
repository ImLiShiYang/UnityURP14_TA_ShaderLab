using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Publishes whether the active URP quality tier provides Camera Opaque Texture.
/// The splash shader keeps a tinted fallback when refraction is unavailable.
/// </summary>
public static class WaterSplashRenderSettings
{
    private static readonly int OpaqueTextureAvailableId =
        Shader.PropertyToID("_WaterSplashOpaqueTextureAvailable");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyPipelineCapabilities()
    {
        UniversalRenderPipelineAsset pipelineAsset =
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        float available = pipelineAsset != null && pipelineAsset.supportsCameraOpaqueTexture
            ? 1f
            : 0f;

        Shader.SetGlobalFloat(OpaqueTextureAvailableId, available);
    }
}
