using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP render feature that captures water ripple brush stamps and advances the wave equation.
/// </summary>
public class WaterRippleRenderFeature : ScriptableRendererFeature
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
        public string profilerTag = "WaterRipple Render Pass";
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRendering;
        public LayerMask waterRippleLayerMask;
        public QueueMode queueMode = QueueMode.Transparent;
        public bool onlyWaterRippleCamera = true;
        public bool updateWave = true;
    }

    public Settings settings = new Settings();

    private WaterRipplePass pass;

    public override void Create()
    {
        pass = new WaterRipplePass(settings);
        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        WaterRippleRTManager manager = WaterRippleRTManager.Active;

        if (manager == null || !manager.Initialized)
            return;

        if (manager.CurrentBrushRT == null ||
            manager.CurrentFrameRT == null ||
            manager.PrevFrameRT == null ||
            manager.PrevPrevFrameRT == null ||
            manager.WaveEquationMaterial == null)
            return;

        if (settings.onlyWaterRippleCamera)
        {
            Camera currentCamera = renderingData.cameraData.camera;

            if (currentCamera == null || currentCamera != manager.WaterRippleCamera)
                return;
        }

        pass.Setup(manager, settings);
        renderer.EnqueuePass(pass);
    }

    private class WaterRipplePass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly ProfilingSampler profilingSampler;

        private Settings settings;
        private WaterRippleRTManager manager;
        private FilteringSettings filteringSettings;

        public WaterRipplePass(Settings settings)
        {
            this.settings = settings;
            profilingSampler = new ProfilingSampler(settings.profilerTag);
        }

        public void Setup(WaterRippleRTManager manager, Settings settings)
        {
            this.manager = manager;
            this.settings = settings;

            renderPassEvent = settings.renderPassEvent;

            RenderQueueRange queueRange = GetQueueRange(settings.queueMode);
            filteringSettings = new FilteringSettings(queueRange, settings.waterRippleLayerMask);
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
            RenderTexture currentFrameRT = manager.CurrentFrameRT;
            Material waveEquationMaterial = manager.WaveEquationMaterial;

            if (currentBrushRT == null || currentFrameRT == null || waveEquationMaterial == null)
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
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);

            if (settings.updateWave)
            {
                manager.SetupWaveEquationMaterial();
                cmd.Blit(currentBrushRT, currentFrameRT, waveEquationMaterial);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                manager.AdvanceWaveFrame();
                manager.FinishWaveFrame();
            }

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
