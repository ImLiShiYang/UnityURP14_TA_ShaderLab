using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 草地交互 RenderFeature。
///
/// 第一阶段只做一件事：
/// 每次 GrassInteractionCamera 渲染时，
/// 先把 GrassInteraction_CurrentBrush_RT 清成黑色，
/// 再把 GrassInteractionBrush Layer 里的 Brush 画进去。
///
/// 这个版本暂时不做累积、不做恢复、不做草弯曲。
/// </summary>
public class GrassInteractionRenderFeature : ScriptableRendererFeature
{
    public enum QueueMode
    {
        Opaque,
        Transparent,
        All
    }

    [System.Serializable]
    public class Settings
    {
        public string profilerTag = "GrassInteraction Render Pass";
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRendering;
        public LayerMask grassInteractionLayerMask;
        public QueueMode queueMode = QueueMode.Transparent;

        [Tooltip("只允许 GrassInteractionCamera 执行这个 Pass，避免主相机也写 RT。")]
        public bool onlyGrassInteractionCamera = true;
    }

    public Settings settings = new Settings();

    private GrassInteractionPass pass;

    public override void Create()
    {
        pass = new GrassInteractionPass(settings);
        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        GrassInteractionRTManager manager = GrassInteractionRTManager.Active;

        if (manager == null || !manager.Initialized)
            return;

        if (manager.CurrentBrushRT == null)
            return;

        if (settings.onlyGrassInteractionCamera)
        {
            Camera currentCamera = renderingData.cameraData.camera;

            if (currentCamera == null || currentCamera != manager.GrassInteractionCamera)
                return;
        }

        pass.Setup(manager, settings);
        renderer.EnqueuePass(pass);
    }

    private class GrassInteractionPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly ProfilingSampler profilingSampler;

        private Settings settings;
        private GrassInteractionRTManager manager;
        private FilteringSettings filteringSettings;

        public GrassInteractionPass(Settings settings)
        {
            this.settings = settings;
            profilingSampler = new ProfilingSampler(settings.profilerTag);
        }

        public void Setup(GrassInteractionRTManager manager, Settings settings)
        {
            this.manager = manager;
            this.settings = settings;

            renderPassEvent = settings.renderPassEvent;

            RenderQueueRange queueRange = GetQueueRange(settings.queueMode);
            filteringSettings = new FilteringSettings(queueRange, settings.grassInteractionLayerMask);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (manager == null || !manager.Initialized)
                return;

            ConfigureTarget(manager.CurrentBrushRT);
            ConfigureClear(ClearFlag.Color, manager.ClearColor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (manager == null || !manager.Initialized)
                return;

            RenderTexture currentBrushRT = manager.CurrentBrushRT;
            RenderTexture accumB = manager.AccumB;
            Material accumulateMaterial = manager.AccumulateMaterial;

            if (currentBrushRT == null || accumB == null || accumulateMaterial == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(settings.profilerTag);

            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetRenderTarget(currentBrushRT);
                cmd.ClearRenderTarget(false, true, manager.ClearColor);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            SortingCriteria sortingCriteria = GetSortingCriteria(settings.queueMode);
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortingCriteria);

            context.DrawRenderers(
                renderingData.cullResults,
                ref drawingSettings,
                ref filteringSettings
            );

            manager.SetupAccumulateMaterial();
            cmd.Blit(currentBrushRT, accumB, accumulateMaterial);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            manager.SwapAccumAfterRenderFeature();

            CommandBufferPool.Release(cmd);
        }

        private static RenderQueueRange GetQueueRange(QueueMode mode)
        {
            switch (mode)
            {
                case QueueMode.Opaque:
                    return RenderQueueRange.opaque;

                case QueueMode.Transparent:
                    return RenderQueueRange.transparent;

                case QueueMode.All:
                    return RenderQueueRange.all;

                default:
                    return RenderQueueRange.transparent;
            }
        }

        private static SortingCriteria GetSortingCriteria(QueueMode mode)
        {
            switch (mode)
            {
                case QueueMode.Opaque:
                    return SortingCriteria.CommonOpaque;

                case QueueMode.Transparent:
                    return SortingCriteria.CommonTransparent;

                case QueueMode.All:
                    return SortingCriteria.None;

                default:
                    return SortingCriteria.CommonTransparent;
            }
        }
    }
}
