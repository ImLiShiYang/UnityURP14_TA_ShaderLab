using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SimpleVolumeFogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("体积雾材质，使用 MyTA/Volumetric/SimpleRayMarchFog Shader 创建。")]
        public Material fogMaterial;

        [Tooltip("体积雾执行时机。一般放在后处理之前。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("是否影响 Scene 视图。调试时建议开启。")]
        public bool affectSceneView = true;

        [Tooltip("降采样。1 是全分辨率，2 是半分辨率。第一版建议用 1。")]
        [Range(1, 4)]
        public int downSample = 1;
    }

    public Settings settings = new Settings();

    private SimpleVolumeFogPass fogPass;

    public override void Create()
    {
        fogPass = new SimpleVolumeFogPass(settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (!ShouldRender(renderingData))
            return;

        fogPass.SetTarget(renderer.cameraColorTargetHandle);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!ShouldRender(renderingData))
            return;

        renderer.EnqueuePass(fogPass);
    }

    protected override void Dispose(bool disposing)
    {
        fogPass?.Dispose();
    }

    private bool ShouldRender(in RenderingData renderingData)
    {
        if (settings.fogMaterial == null)
            return false;

        CameraData cameraData = renderingData.cameraData;

        if (cameraData.cameraType == CameraType.Preview)
            return false;

        if (!settings.affectSceneView && cameraData.isSceneViewCamera)
            return false;

        return true;
    }

    private class SimpleVolumeFogPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Simple Volume Fog");

        private RTHandle cameraColorTarget;
        private RTHandle tempColorTexture;

        public SimpleVolumeFogPass(Settings settings)
        {
            this.settings = settings;

            // 需要 _CameraDepthTexture，用深度还原世界坐标。
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void SetTarget(RTHandle colorTarget)
        {
            cameraColorTarget = colorTarget;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;

            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            int downSample = Mathf.Max(1, settings.downSample);
            descriptor.width = Mathf.Max(1, descriptor.width / downSample);
            descriptor.height = Mathf.Max(1, descriptor.height / downSample);

            RenderingUtils.ReAllocateIfNeeded(
                ref tempColorTexture,
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_SimpleVolumeFogTemp"
            );
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (cameraColorTarget == null || tempColorTexture == null || settings.fogMaterial == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 第一次 Blit：相机颜色图 -> 临时 RT，同时执行体积雾 Shader。
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempColorTexture, settings.fogMaterial, 0);

                // 第二次 Blit：临时 RT -> 相机颜色图。
                Blitter.BlitCameraTexture(cmd, tempColorTexture, cameraColorTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            tempColorTexture?.Release();
        }
    }
}