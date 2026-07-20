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

        [Tooltip("是否启用时间累积。开启后会把当前帧体积云和上一帧体积云混合，用来减少闪烁和噪点。")]
        public bool enableTemporalAccumulation = true;

        [Tooltip("相机基本静止时，上一帧体积云的混合权重。数值越大越稳定，但也越容易有拖影。")]
        [Range(0f, 0.95f)]
        public float temporalBlend = 0.75f;

        [Tooltip("相机快速移动时，上一帧体积云的混合权重。建议比静止时低，避免明显残影。")]
        [Range(0f, 0.95f)]
        public float fastCameraTemporalBlend = 0.2f;

        [Tooltip("相机单帧移动距离超过这个值时，认为相机正在快速移动，并切换到快速移动混合权重。")]
        public float fastCameraPositionThreshold = 0.25f;

        [Tooltip("相机单帧旋转角度超过这个值时，认为相机正在快速旋转，并切换到快速移动混合权重。")]
        public float fastCameraAngleThreshold = 2.0f;

        [Tooltip("当前帧深度和上一帧深度差超过这个范围时，会减少或拒绝上一帧体积云，避免物体边缘残影。")]
        public float temporalDepthThreshold = 2.0f;

        [Tooltip("当前帧体积云和上一帧体积云差异超过这个范围时，会降低上一帧权重，避免云变化时产生拖影。")]
        [Range(0.01f, 2.0f)]
        public float temporalCloudChangeThreshold = 0.35f;

        [Tooltip("当体积云变化很大时，仍然保留的最小上一帧混合权重。数值越大越稳定，但残影也可能更明显。")]
        [Range(0f, 0.5f)]
        public float temporalMinBlendOnCloudChange = 0.1f;
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
        private static readonly int DownsampledCloudDepthTextureId =
            Shader.PropertyToID("_DownsampledCloudDepthTexture");

        private static readonly int VolumeCloudTextureId =
            Shader.PropertyToID("_VolumeCloudTexture");

        private static readonly int CloudHistoryTextureId =
            Shader.PropertyToID("_CloudHistoryTexture");

        private static readonly int CloudHistoryDepthTextureId =
            Shader.PropertyToID("_CloudHistoryDepthTexture");

        private static readonly int TemporalBlendFactorId =
            Shader.PropertyToID("_TemporalBlendFactor");

        private static readonly int PreviousViewProjectionMatrixId =
            Shader.PropertyToID("_PreviousViewProjectionMatrix");

        private static readonly int TemporalDepthThresholdId =
            Shader.PropertyToID("_TemporalDepthThreshold");

        private static readonly int TemporalCloudChangeThresholdId =
            Shader.PropertyToID("_TemporalCloudChangeThreshold");

        private static readonly int TemporalMinBlendOnCloudChangeId =
            Shader.PropertyToID("_TemporalMinBlendOnCloudChange");
        
        private static readonly int TemporalFrameIndexId =
            Shader.PropertyToID("_TemporalFrameIndex");

        private const int TemporalJitterSequenceLength = 16;

        private readonly Settings settings;
        private readonly ProfilingSampler profilingSampler = new ProfilingSampler("Simple Volume Cloud");

        private RTHandle cameraColorTarget;
        private RTHandle downsampledDepthTexture;
        private RTHandle volumeCloudTexture;
        private RTHandle volumeCloudBlurTexture;
        private RTHandle temporalCloudTexture;
        private RTHandle cloudHistoryTexture;
        private RTHandle cloudHistoryDepthTexture;
        private RTHandle compositeTexture;

        private int downsampleDepthPass = -1;
        private int volumeCloudRenderPass = -1;
        private int horizontalBlurPass = -1;
        private int verticalBlurPass = -1;
        private int temporalBlendPass = -1;
        private int compositePass = -1;
        private int temporalFrameIndex;

        private bool historyValid;
        private int historyCameraId = -1;
        private int historyWidth = -1;
        private int historyHeight = -1;
        private Vector3 previousCameraPosition;
        private Quaternion previousCameraRotation = Quaternion.identity;
        private Matrix4x4 previousViewProjectionMatrix = Matrix4x4.identity;

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

            temporalBlendPass = material.FindPass("VolumeCloudTemporalBlend");

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
                ref temporalCloudTexture,
                cloudDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeCloudTemporal"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref cloudHistoryTexture,
                cloudDescriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumeCloudHistory"
            );

            RenderingUtils.ReAllocateIfNeeded(
                ref cloudHistoryDepthTexture,
                depthDescriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_VolumeCloudHistoryDepth"
            );

            if (historyWidth != cloudDescriptor.width || historyHeight != cloudDescriptor.height)
            {
                historyValid = false;
                temporalFrameIndex = 0;
                
                historyWidth = cloudDescriptor.width;
                historyHeight = cloudDescriptor.height;
            }

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
                temporalCloudTexture == null ||
                cloudHistoryTexture == null ||
                cloudHistoryDepthTexture == null ||
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
                RTHandle cloudForComposite = volumeCloudTexture;

                // 1. 生成低分辨率深度图。
                // Shader 里 RayMarchCloud 会通过 _DownsampledCloudDepthTexture 读取它。
                Blitter.BlitCameraTexture(
                    cmd,
                    cameraColorTarget,
                    downsampledDepthTexture,
                    material,
                    downsampleDepthPass
                );

                cmd.SetGlobalTexture(DownsampledCloudDepthTextureId, downsampledDepthTexture);

                material.SetFloat(
                    TemporalFrameIndexId,
                    settings.enableTemporalAccumulation ? temporalFrameIndex : 0
                );

                
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

                float temporalBlendFactor = GetTemporalBlendFactor(ref renderingData);

                if (settings.enableTemporalAccumulation && temporalBlendPass >= 0)
                {
                    if (temporalBlendFactor > 0.0f)
                    {
                        material.SetFloat(TemporalBlendFactorId, temporalBlendFactor);
                        material.SetMatrix(PreviousViewProjectionMatrixId, previousViewProjectionMatrix);
                        material.SetFloat(TemporalDepthThresholdId, Mathf.Max(0.001f, settings.temporalDepthThreshold));
                        material.SetFloat(TemporalCloudChangeThresholdId, Mathf.Max(0.01f, settings.temporalCloudChangeThreshold));
                        material.SetFloat(TemporalMinBlendOnCloudChangeId, Mathf.Clamp01(settings.temporalMinBlendOnCloudChange));
                        cmd.SetGlobalTexture(CloudHistoryTextureId, cloudHistoryTexture);
                        cmd.SetGlobalTexture(CloudHistoryDepthTextureId, cloudHistoryDepthTexture);

                        Blitter.BlitCameraTexture(
                            cmd,
                            volumeCloudTexture,
                            temporalCloudTexture,
                            material,
                            temporalBlendPass
                        );

                        cloudForComposite = temporalCloudTexture;
                    }

                    Blitter.BlitCameraTexture(
                        cmd,
                        cloudForComposite,
                        cloudHistoryTexture
                    );

                    Blitter.BlitCameraTexture(
                        cmd,
                        downsampledDepthTexture,
                        cloudHistoryDepthTexture
                    );

                    UpdateHistoryCamera(ref renderingData);
                    
                    temporalFrameIndex =
                        (temporalFrameIndex + 1) % TemporalJitterSequenceLength;
                }
                else
                {
                    historyValid = false;
                    temporalFrameIndex = 0;
                }

                // 4. 这里暂时仍然设置到 _VolumeCloudTexture。
                // 因为你当前 Shader 的 CompositeFrag / DepthAwareUpsampleFog 还在读取这个名字。
                cmd.SetGlobalTexture(VolumeCloudTextureId, cloudForComposite);

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
            temporalCloudTexture?.Release();
            cloudHistoryTexture?.Release();
            cloudHistoryDepthTexture?.Release();
            compositeTexture?.Release();
        }

        // 根据相机上一帧到当前帧的移动/旋转幅度，动态决定历史帧混合权重：
        // 相机静止时多混上一帧，减少闪烁；相机快速移动时少混上一帧，避免拖影。
        private float GetTemporalBlendFactor(ref RenderingData renderingData)
        {
            if (!settings.enableTemporalAccumulation || !historyValid)
                return 0.0f;

            Camera camera = renderingData.cameraData.camera;
            if (camera == null || camera.GetInstanceID() != historyCameraId)
                return 0.0f;

            Transform cameraTransform = camera.transform;
            float positionDelta = Vector3.Distance(cameraTransform.position, previousCameraPosition);
            float angleDelta = Quaternion.Angle(cameraTransform.rotation, previousCameraRotation);

            bool cameraMovedFast =
                positionDelta > Mathf.Max(0.0f, settings.fastCameraPositionThreshold) ||
                angleDelta > Mathf.Max(0.0f, settings.fastCameraAngleThreshold);

            float blend = cameraMovedFast ? settings.fastCameraTemporalBlend : settings.temporalBlend;
            return Mathf.Clamp01(blend);
        }

        private void UpdateHistoryCamera(ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
            {
                historyValid = false;
                historyCameraId = -1;
                return;
            }

            Transform cameraTransform = camera.transform;
            historyValid = true;
            historyCameraId = camera.GetInstanceID();
            previousCameraPosition = cameraTransform.position;
            previousCameraRotation = cameraTransform.rotation;
            previousViewProjectionMatrix = GetViewProjectionMatrix(ref renderingData);
        }

        private static Matrix4x4 GetViewProjectionMatrix(ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            return cameraData.GetGPUProjectionMatrixNoJitter() * cameraData.GetViewMatrix();
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
