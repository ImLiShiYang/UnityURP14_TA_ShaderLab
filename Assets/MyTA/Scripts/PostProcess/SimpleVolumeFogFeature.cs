using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

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
        
        [Tooltip("体积雾模糊次数。1-2 通常够用。")]
        [Range(0, 4)]
        public int blurIterations = 1;
    }

    public Settings settings = new Settings();

    private SimpleVolumeCloudPass  cloudPass;

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
        if (settings.fogMaterial == null)
            return false;

        CameraData cameraData = renderingData.cameraData;

        if (cameraData.cameraType == CameraType.Preview)
            return false;

        if (!settings.affectSceneView && cameraData.isSceneViewCamera)
            return false;

        return true;
    }

    private class SimpleVolumeCloudPass  : ScriptableRenderPass
    {
        private static readonly int DownsampledFogDepthTextureId = Shader.PropertyToID("_DownsampledFogDepthTexture");
        private static readonly int VolumeFogTextureId = Shader.PropertyToID("_VolumeFogTexture");
        
        private const int MaxVolumetricAdditionalLights = 32;

        private static readonly int VolumetricAdditionalLightCountId = Shader.PropertyToID("_VolumetricAdditionalLightCount");
        private static readonly int VolumetricAdditionalAnisotropyId = Shader.PropertyToID("_VolumetricAdditionalAnisotropy");
        private static readonly int VolumetricAdditionalScatteringId = Shader.PropertyToID("_VolumetricAdditionalScattering");
        private static readonly int VolumetricAdditionalRadiusId = Shader.PropertyToID("_VolumetricAdditionalRadius");

        private static readonly float[] AdditionalAnisotropy = new float[MaxVolumetricAdditionalLights];
        private static readonly float[] AdditionalScattering = new float[MaxVolumetricAdditionalLights];
        private static readonly float[] AdditionalRadius = new float[MaxVolumetricAdditionalLights];

        private readonly Settings settings;
        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Simple Volume Fog V2");

        private RTHandle cameraColorTarget;
        private RTHandle downsampledDepthTexture;
        private RTHandle volumeFogTexture;
        private RTHandle volumeFogBlurTexture;
        private RTHandle compositeTexture;

        private int downsampleDepthPass = -1;
        private int volumeFogRenderPass = -1;
        private int horizontalBlurPass = -1;
        private int verticalBlurPass = -1;
        private int compositePass = -1;

        public SimpleVolumeCloudPass(Settings settings)
        {
            this.settings = settings;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void SetTarget(RTHandle colorTarget)
        {
            cameraColorTarget = colorTarget;
        }
        
        private static void UpdateAdditionalLightParameters(Material material, ref RenderingData renderingData)
        {
            System.Array.Clear(AdditionalAnisotropy, 0, AdditionalAnisotropy.Length);
            System.Array.Clear(AdditionalScattering, 0, AdditionalScattering.Length);
            System.Array.Clear(AdditionalRadius, 0, AdditionalRadius.Length);

            int mainLightIndex = renderingData.lightData.mainLightIndex;
            int additionalLightIndex = 0;

            var visibleLights = renderingData.lightData.visibleLights;

            for (int i = 0; i < visibleLights.Length && additionalLightIndex < MaxVolumetricAdditionalLights; i++)
            {
                if (i == mainLightIndex)
                    continue;

                Light unityLight = visibleLights[i].light;

                if (unityLight != null &&
                    unityLight.TryGetComponent(out VolumetricAdditionalLight volumetricLight) &&
                    volumetricLight.enabled &&
                    volumetricLight.gameObject.activeInHierarchy)
                {
                    AdditionalAnisotropy[additionalLightIndex] = volumetricLight.anisotropy;
                    AdditionalScattering[additionalLightIndex] = volumetricLight.scattering;
                    AdditionalRadius[additionalLightIndex] = volumetricLight.radius;
                }

                additionalLightIndex++;
            }

            material.SetInt(VolumetricAdditionalLightCountId, additionalLightIndex);
            material.SetFloatArray(VolumetricAdditionalAnisotropyId, AdditionalAnisotropy);
            material.SetFloatArray(VolumetricAdditionalScatteringId, AdditionalScattering);
            material.SetFloatArray(VolumetricAdditionalRadiusId, AdditionalRadius);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Material material = settings.fogMaterial;
            if (material == null)
                return;

            downsampleDepthPass = material.FindPass("DownsampleDepth");
            volumeFogRenderPass = material.FindPass("VolumeFogRender");
            horizontalBlurPass = material.FindPass("VolumeFogHorizontalBlur");
            verticalBlurPass = material.FindPass("VolumeFogVerticalBlur");
            compositePass = material.FindPass("VolumeFogComposite");

            RenderTextureDescriptor cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            cameraDescriptor.depthBufferBits = 0;
            cameraDescriptor.msaaSamples = 1;

            int downSample = Mathf.Max(1, settings.downSample);

            RenderTextureDescriptor halfDescriptor = cameraDescriptor;
            halfDescriptor.width = Mathf.Max(1, cameraDescriptor.width / downSample);
            halfDescriptor.height = Mathf.Max(1, cameraDescriptor.height / downSample);

            RenderTextureDescriptor depthDescriptor = halfDescriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;

            RenderingUtils.ReAllocateIfNeeded(
                ref downsampledDepthTexture,
                depthDescriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_DownsampledFogDepth"
            );

            RenderTextureDescriptor fogDescriptor = halfDescriptor;
            fogDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

            RenderingUtils.ReAllocateIfNeeded(
                ref volumeFogTexture,
                fogDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeFog"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref volumeFogBlurTexture,
                fogDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeFogBlur"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref compositeTexture,
                cameraDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeFogComposite"
            );
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (cameraColorTarget == null ||
                downsampledDepthTexture == null ||
                volumeFogTexture == null ||
                volumeFogBlurTexture == null ||
                compositeTexture == null ||
                settings.fogMaterial == null)
            {
                return;
            }

            if (downsampleDepthPass < 0 ||
                volumeFogRenderPass < 0 ||
                horizontalBlurPass < 0 ||
                verticalBlurPass < 0 ||
                compositePass < 0)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 取出体积雾材质，后面的 Blit 都会使用这个材质里的不同 Pass。
                Material material = settings.fogMaterial;

                // 第一步：生成低分辨率深度纹理。
                // 这里通过 downsampleDepthPass 把当前相机画面/深度信息处理到 downsampledDepthTexture。
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, downsampledDepthTexture, material, downsampleDepthPass);

                // 把低分辨率深度图设置为全局纹理，方便后续 Shader Pass 使用。
                cmd.SetGlobalTexture(DownsampledFogDepthTextureId, downsampledDepthTexture);
    
                // 更新额外灯光参数，比如附加点光、聚光灯等，用于体积雾光照计算。
                UpdateAdditionalLightParameters(material, ref renderingData);

                // 第二步：根据低分辨率深度图计算体积雾结果，输出到 volumeFogTexture。
                Blitter.BlitCameraTexture(cmd, downsampledDepthTexture, volumeFogTexture, material, volumeFogRenderPass);

                // 第三步：对体积雾纹理做模糊，让体积雾更柔和，减少低分辨率带来的锯齿和噪点。
                int blurIterations = Mathf.Clamp(settings.blurIterations, 0, 4);
                for (int i = 0; i < blurIterations; i++)
                {
                    // 横向模糊：volumeFogTexture -> volumeFogBlurTexture
                    Blitter.BlitCameraTexture(cmd, volumeFogTexture, volumeFogBlurTexture, material, horizontalBlurPass);

                    // 纵向模糊：volumeFogBlurTexture -> volumeFogTexture
                    Blitter.BlitCameraTexture(cmd, volumeFogBlurTexture, volumeFogTexture, material, verticalBlurPass);
                }

                // 把最终体积雾纹理设置为全局纹理，合成 Pass 会读取它。
                cmd.SetGlobalTexture(VolumeFogTextureId, volumeFogTexture);

                // 第四步：把体积雾叠加到原始相机画面上，输出到临时合成纹理。
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, compositeTexture, material, compositePass);

                // 第五步：把合成后的结果写回相机颜色目标，最终显示到屏幕。
                Blitter.BlitCameraTexture(cmd, compositeTexture, cameraColorTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            downsampledDepthTexture?.Release();
            volumeFogTexture?.Release();
            volumeFogBlurTexture?.Release();
            compositeTexture?.Release();
        }
    }
}
