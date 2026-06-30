using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class SimpleVolumeCloudFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("体积云材质，使用 MyTA/Volumetric/SimpleVolumeCloud Shader 创建。")]
        public Material cloudMaterial;

        [Tooltip("体积云执行时机。第一版建议放在后处理之前。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Tooltip("降采样。1 是全分辨率，2 是半分辨率，4 是四分之一分辨率。")]
        [Range(1, 4)]
        public int downSample = 2;

        [Tooltip("模糊次数。第一版调试建议先用 0，看到云以后再加到 1。")]
        [Range(0, 4)]
        public int blurIterations = 0;

        [Tooltip("是否影响 Scene 视图。")]
        public bool affectSceneView = true;
    }

    public Settings settings = new Settings();

    private SimpleVolumeCloudPass cloudPass;

    public override void Create()
    {
        cloudPass = new SimpleVolumeCloudPass(settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (!ShouldRender(renderingData))
            return;

        cloudPass.SetTarget(renderer.cameraColorTargetHandle);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!ShouldRender(renderingData))
            return;

        renderer.EnqueuePass(cloudPass);
    }

    protected override void Dispose(bool disposing)
    {
        cloudPass?.Dispose();
    }

    private bool ShouldRender(in RenderingData renderingData)
    {
        if (settings.cloudMaterial == null)
            return false;

        CameraData cameraData = renderingData.cameraData;

        if (cameraData.cameraType == CameraType.Preview)
            return false;

        if (!settings.affectSceneView && cameraData.isSceneViewCamera)
            return false;

        return true;
    }

    private class SimpleVolumeCloudPass : ScriptableRenderPass
    {
        private static readonly int DownsampledFogDepthTextureId =
            Shader.PropertyToID("_DownsampledFogDepthTexture");

        private static readonly int VolumeFogTextureId =
            Shader.PropertyToID("_VolumeFogTexture");

        private readonly Settings settings;
        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Simple Volume Cloud");

        private RTHandle cameraColorTarget;
        private RTHandle downsampledDepthTexture;
        private RTHandle volumeCloudTexture;
        private RTHandle volumeCloudBlurTexture;
        private RTHandle compositeTexture;

        private int downsampleDepthPass = -1;
        private int volumeCloudRenderPass = -1;
        private int horizontalBlurPass = -1;
        private int verticalBlurPass = -1;
        private int compositePass = -1;

        public SimpleVolumeCloudPass(Settings settings)
        {
            this.settings = settings;

            // 体积云需要相机深度，用来重建射线方向和做场景遮挡。
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void SetTarget(RTHandle colorTarget)
        {
            cameraColorTarget = colorTarget;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Material material = settings.cloudMaterial;
            if (material == null)
                return;

            downsampleDepthPass = material.FindPass("DownsampleDepth");

            // 如果你已经把 Shader Pass 改名为 VolumeCloudRender，就走新名字。
            // 如果还没改，仍然可以 fallback 到 VolumeFogRender。
            volumeCloudRenderPass = FindPassWithFallback(
                material,
                "VolumeCloudRender",
                "VolumeFogRender"
            );

            horizontalBlurPass = FindPassWithFallback(
                material,
                "VolumeCloudHorizontalBlur",
                "VolumeFogHorizontalBlur"
            );

            verticalBlurPass = FindPassWithFallback(
                material,
                "VolumeCloudVerticalBlur",
                "VolumeFogVerticalBlur"
            );

            compositePass = FindPassWithFallback(
                material,
                "VolumeCloudComposite",
                "VolumeFogComposite"
            );

            RenderTextureDescriptor cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            cameraDescriptor.depthBufferBits = 0;
            cameraDescriptor.msaaSamples = 1;

            int downSample = Mathf.Max(1, settings.downSample);

            RenderTextureDescriptor cloudDescriptor = cameraDescriptor;
            cloudDescriptor.width = Mathf.Max(1, cameraDescriptor.width / downSample);
            cloudDescriptor.height = Mathf.Max(1, cameraDescriptor.height / downSample);
            cloudDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            RenderTextureDescriptor depthDescriptor = cloudDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;

            RenderingUtils.ReAllocateIfNeeded(
                ref downsampledDepthTexture,
                depthDescriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_DownsampledCloudDepth"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref volumeCloudTexture,
                cloudDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeCloud"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref volumeCloudBlurTexture,
                cloudDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeCloudBlur"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref compositeTexture,
                cameraDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeCloudComposite"
            );
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (cameraColorTarget == null ||
                downsampledDepthTexture == null ||
                volumeCloudTexture == null ||
                volumeCloudBlurTexture == null ||
                compositeTexture == null ||
                settings.cloudMaterial == null)
            {
                return;
            }

            if (downsampleDepthPass < 0 ||
                volumeCloudRenderPass < 0 ||
                compositePass < 0)
            {
                return;
            }

            int blurIterations = Mathf.Clamp(settings.blurIterations, 0, 4);

            if (blurIterations > 0 && (horizontalBlurPass < 0 || verticalBlurPass < 0))
                return;

            CommandBuffer cmd = CommandBufferPool.Get("Simple Volume Cloud");

            using (new ProfilingScope(cmd, profilingSampler))
            {
                Material material = settings.cloudMaterial;

                // 1. 生成低分辨率深度图。
                // Shader 里 RayMarchCloud 会通过 _DownsampledFogDepthTexture 读取它。
                Blitter.BlitCameraTexture(
                    cmd,
                    cameraColorTarget,
                    downsampledDepthTexture,
                    material,
                    downsampleDepthPass
                );

                cmd.SetGlobalTexture(DownsampledFogDepthTextureId, downsampledDepthTexture);

                // 2. 渲染体积云。
                // 这里输出的不是普通颜色，而是：
                // rgb = 云累积颜色
                // a   = 剩余透光率 transmittance
                Blitter.BlitCameraTexture(
                    cmd,
                    downsampledDepthTexture,
                    volumeCloudTexture,
                    material,
                    volumeCloudRenderPass
                );

                // 3. 可选模糊。
                // 第一版看效果建议 blurIterations = 0。
                // 看到云以后，可以改成 1，让边缘柔和一点。
                for (int i = 0; i < blurIterations; i++)
                {
                    Blitter.BlitCameraTexture(
                        cmd,
                        volumeCloudTexture,
                        volumeCloudBlurTexture,
                        material,
                        horizontalBlurPass
                    );

                    Blitter.BlitCameraTexture(
                        cmd,
                        volumeCloudBlurTexture,
                        volumeCloudTexture,
                        material,
                        verticalBlurPass
                    );
                }

                // 4. 这里暂时仍然设置到 _VolumeFogTexture。
                // 因为你当前 Shader 的 CompositeFrag / DepthAwareUpsampleFog 还在读取这个名字。
                cmd.SetGlobalTexture(VolumeFogTextureId, volumeCloudTexture);

                // 5. 合成到相机颜色。
                Blitter.BlitCameraTexture(
                    cmd,
                    cameraColorTarget,
                    compositeTexture,
                    material,
                    compositePass
                );

                Blitter.BlitCameraTexture(
                    cmd,
                    compositeTexture,
                    cameraColorTarget
                );
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            downsampledDepthTexture?.Release();
            volumeCloudTexture?.Release();
            volumeCloudBlurTexture?.Release();
            compositeTexture?.Release();
        }

        private static int FindPassWithFallback(Material material, string primaryName, string fallbackName)
        {
            int pass = material.FindPass(primaryName);

            if (pass >= 0)
                return pass;

            return material.FindPass(fallbackName);
        }
    }
}