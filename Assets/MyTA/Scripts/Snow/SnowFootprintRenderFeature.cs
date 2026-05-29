using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 雪地压痕用的 URP RenderFeature。
///
/// 这个 RenderFeature 只负责把 Snow Brush Layer 画进 CurrentBrushRT，
/// 然后在需要时把 CurrentBrushRT 累积到 AccumA / AccumB。
///
/// 雪地 RT 数据协议：
/// R = sink，下陷强度，0 表示没有下陷
/// G = rim，雪边凸起，第一阶段可以不用
/// B = 预留
/// A = brush mask
/// </summary>
public class SnowFootprintRenderFeature : ScriptableRendererFeature
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
        [Tooltip("Frame Debugger / Profiler 中显示的 Pass 名字。")]
        public string profilerTag = "Snow Footprint Render Pass";

        [Tooltip("建议 AfterRendering，确保覆盖 FootstepCamera 自己的 FinalBlit 结果。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRendering;

        [Tooltip("只绘制 Snow Brush / FootprintBrush Layer。")]
        public LayerMask footprintLayerMask;

        [Tooltip("Brush 材质如果是 Transparent 队列，用 Transparent。")]
        public QueueMode queueMode = QueueMode.Transparent;

        [Tooltip("是否只在 FootstepCamera 上执行。必须开启，避免主相机错误清空 RT。")]
        public bool onlyFootstepCamera = true;

        [Tooltip("是否执行 CurrentBrushRT -> AccumA 的累积。")]
        public bool accumulate = true;
    }

    public Settings settings = new Settings();

    private SnowFootprintPass pass;

    public override void Create()
    {
        pass = new SnowFootprintPass(settings);
        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 注意：这里必须使用 SnowFootprintRTManager。
        // 不能再使用旧的 FootprintRTManager，否则雪地 RT Manager 和 RenderFeature 连接不上。
        SnowFootprintRTManager manager = SnowFootprintRTManager.Active;

        if (manager == null || !manager.Initialized)
            return;

        if (manager.CurrentBrushRT == null || manager.AccumA == null || manager.AccumB == null)
            return;

        if (manager.AccumulateMaterial == null)
            return;

        if (settings.onlyFootstepCamera)
        {
            Camera currentCamera = renderingData.cameraData.camera;

            if (currentCamera == null || currentCamera != manager.FootstepCamera)
                return;
        }

        pass.Setup(manager, settings);
        renderer.EnqueuePass(pass);
    }

    private class SnowFootprintPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        private readonly ProfilingSampler profilingSampler;

        private Settings settings;
        private SnowFootprintRTManager manager;
        private FilteringSettings filteringSettings;

        public SnowFootprintPass(Settings settings)
        {
            this.settings = settings;
            profilingSampler = new ProfilingSampler(settings.profilerTag);
        }

        public void Setup(SnowFootprintRTManager manager, Settings settings)
        {
            this.manager = manager;
            this.settings = settings;

            renderPassEvent = settings.renderPassEvent;

            RenderQueueRange queueRange = GetQueueRange(settings.queueMode);
            filteringSettings = new FilteringSettings(queueRange, settings.footprintLayerMask);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (manager == null || !manager.Initialized)
                return;

            ConfigureTarget(manager.CurrentBrushRT);

            // 雪地压痕 RT 清屏色应该是黑色数据底色：
            // R = 0，没有下陷
            // G = 0，没有雪边凸起
            // B = 0，预留
            // A = 0，没有 brush mask
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
                // 1. 每帧清空 CurrentBrushRT。
                // CurrentBrushRT 只保存当前帧活着的 brush，不保存历史。
                // 历史压痕保存在 AccumA / AccumB。
                cmd.SetRenderTarget(currentBrushRT);
                cmd.ClearRenderTarget(false, true, manager.ClearColor);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // 2. 只绘制指定 Layer 上的雪地 Brush。
            SortingCriteria sortingCriteria = GetSortingCriteria(settings.queueMode);

            DrawingSettings drawingSettings = CreateDrawingSettings(
                shaderTagIds,
                ref renderingData,
                sortingCriteria
            );

            context.DrawRenderers(
                renderingData.cullResults,
                ref drawingSettings,
                ref filteringSettings
            );

            // 3. 在需要时执行累积。
            // 需要累积的情况由 SnowFootprintRTManager 判断：
            // - 有新 brush stamp
            // - RT 中心发生移动，需要滚动历史内容
            // - reduceVal > 0，需要淡出
            if (settings.accumulate && manager.ShouldAccumulateThisFrame())
            {
                manager.SetupAccumulateMaterial();

                // Blit 输入 _MainTex = CurrentBrushRT。
                // accumulateMaterial 内部还会采样 _LastTex = AccumA。
                // 输出到 AccumB。
                cmd.Blit(currentBrushRT, accumB, accumulateMaterial);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                manager.SwapAccumAfterRenderFeature();
                manager.ConsumeStamp();
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
