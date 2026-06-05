using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 水波渲染用的 URP RenderFeature。
///
/// 这个 RenderFeature 的职责：
///
/// 1. 只在 WaterRippleCamera 上执行。
///    因为 WaterRippleCamera 是专门用来从上往下拍摄水波 Brush 的相机。
///
/// 2. 每帧先清空 CurrentBrushRT。
///    CurrentBrushRT 是“当前帧临时水波图”，不是历史图。
///    所以它必须每帧清成默认法线 + alpha 0。
///
/// 3. 只绘制 WaterRippleBrush Layer。
///    也就是只把水波 Brush prefab 画进 CurrentBrushRT。
///
/// 4. 在需要的时候，把 CurrentBrushRT 累积进 AccumA。
///    累积结果写入 AccumB，然后通过 Swap 让 AccumB 变成新的 AccumA。
///
/// RT 数据协议：
/// RGB = 编码后的法线，默认值是 (0.5, 0.5, 1)
/// A   = 水波 mask / depression，默认值是 0
/// </summary>
[MovedFrom(false, null, null, "FootprintRenderFeature")]
public class WaterRippleRenderFeature : ScriptableRendererFeature
{
    /// <summary>
    /// 控制要绘制哪些渲染队列。
    ///
    /// Opaque：
    ///     只绘制不透明物体。
    ///
    /// Transparent：
    ///     只绘制透明物体。
    ///     你的 WaterRippleBrush shader 使用的是 Queue = Transparent，
    ///     所以一般应该选 Transparent。
    ///
    /// All：
    ///     不区分队列，全部都尝试绘制。
    /// </summary>
    public enum QueueMode
    {
        Opaque,
        Transparent,
        All
    }

    /// <summary>
    /// 在 Inspector 里暴露的配置项。
    /// </summary>
    [System.Serializable]
    public class Settings
    {
        /// <summary>
        /// Frame Debugger / Profiler 里显示的 Pass 名字。
        /// </summary>
        public string profilerTag = "WaterRipple Render Pass";

        /// <summary>
        /// RenderPass 执行时机。
        ///
        /// 建议使用 AfterRendering：
        /// - WaterRippleCamera 自己可能有普通渲染 / FinalBlit。
        /// - 我们希望这个 Pass 最后覆盖写入 CurrentBrushRT。
        ///
        /// 如果执行太早，可能会被相机后续流程覆盖。
        /// </summary>
        [Tooltip("建议 AfterRendering，确保覆盖 WaterRippleCamera 自己的 FinalBlit 结果。")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRendering;

        /// <summary>
        /// 只绘制这个 Layer 上的 Brush。
        ///
        /// 你的水波 prefab 必须放到这个 Layer。
        /// 地面、玩家、其他物体不能在这个 Layer 上，
        /// 否则它们也可能被画进 CurrentBrushRT。
        /// </summary>
        [Tooltip("只绘制 WaterRippleBrush Layer。")]
        [FormerlySerializedAs("footprintLayerMask")]
        public LayerMask waterRippleLayerMask;

        /// <summary>
        /// Brush 材质所在的渲染队列。
        ///
        /// 你的 WaterRipple/URP_WaterRippleBrush_NormalHeightSeparate
        /// 使用 Queue = Transparent，所以这里通常选 Transparent。
        /// </summary>
        [Tooltip("Brush 材质如果是 Transparent 队列，用 Transparent。")]
        public QueueMode queueMode = QueueMode.Transparent;

        /// <summary>
        /// 是否只允许 WaterRippleCamera 执行这个 Pass。
        ///
        /// 必须开启。
        /// 否则主相机也会执行这个 RenderFeature，
        /// 导致 CurrentBrushRT 被错误清空或错误写入。
        /// </summary>
        [Tooltip("是否只在 WaterRippleCamera 上执行。")]
        [FormerlySerializedAs("onlyFootstepCamera")]
        public bool onlyWaterRippleCamera = true;

        /// <summary>
        /// 是否执行 CurrentBrushRT -> AccumA 的累积。
        ///
        /// 如果关闭：
        /// - CurrentBrushRT 仍然会被清空并绘制当前 Brush。
        /// - 但不会写入历史 AccumA。
        ///
        /// 调试 CurrentBrushRT 时可以临时关闭。
        /// </summary>
        [Tooltip("是否执行 CurrentBrushRT -> AccumA 的累积。")]
        public bool accumulate = true;
    }

    /// <summary>
    /// Inspector 配置。
    /// </summary>
    public Settings settings = new Settings();

    /// <summary>
    /// 真正执行渲染逻辑的 ScriptableRenderPass。
    /// RenderFeature 只是负责创建和提交 Pass。
    /// </summary>
    private WaterRipplePass pass;

    /// <summary>
    /// URP 创建 RenderFeature 时调用。
    ///
    /// 在这里创建一次 WaterRipplePass，
    /// 后面每帧只需要把它 Enqueue 到 Renderer 里。
    /// </summary>
    public override void Create()
    {
        pass = new WaterRipplePass(settings);
        pass.renderPassEvent = settings.renderPassEvent;
    }

    /// <summary>
    /// 每个相机渲染前，URP 会调用这里，询问是否要添加自定义 Pass。
    ///
    /// 这里要做很多保护判断：
    /// 1. WaterRippleRTManager 是否存在。
    /// 2. 三张 RT 是否已经创建。
    /// 3. 累积材质是否存在。
    /// 4. 当前相机是不是 WaterRippleCamera。
    ///
    /// 全部满足后，才把 WaterRipplePass 加入渲染队列。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // WaterRippleRTManager.Active 是当前场景里的水波 RT 管理器。
        // RenderFeature 通过它拿 CurrentBrushRT / AccumA / AccumB / 材质等数据。
        WaterRippleRTManager manager = WaterRippleRTManager.Active;

        // 没有管理器，或者管理器还没初始化，直接不执行。
        if (manager == null || !manager.Initialized)
            return;

        // CurrentBrushRT 是当前输入；三张 Frame RT 是波动方程的历史缓冲。
        if (manager.CurrentBrushRT == null ||
            manager.CurrentFrameRT == null ||
            manager.PrevFrameRT == null ||
            manager.PrevPrevFrameRT == null)
            return;

        // 当前路径使用波动方程材质推进三缓冲。
        if (manager.WaveEquationMaterial == null)
            return;

        // 如果只允许 WaterRippleCamera 执行，就检查当前相机。
        if (settings.onlyWaterRippleCamera)
        {
            Camera currentCamera = renderingData.cameraData.camera;

            // 当前相机不是 WaterRippleCamera，就跳过。
            // 这样可以避免主相机误执行这个 Pass。
            if (currentCamera == null || currentCamera != manager.WaterRippleCamera)
                return;
        }

        // 把当前 manager 和 settings 传给 pass。
        pass.Setup(manager, settings);

        // 把 pass 加入 URP 当前相机的渲染流程。
        renderer.EnqueuePass(pass);
    }

    /// <summary>
    /// 真正执行水波 RT 渲染和累积的 Pass。
    /// </summary>
    private class WaterRipplePass : ScriptableRenderPass
    {
        /// <summary>
        /// 要绘制的 Shader Pass 名称。
        ///
        /// URP 绘制 Renderer 时，会根据这些 ShaderTagId 找对应 Pass。
        ///
        /// UniversalForward / UniversalForwardOnly：
        ///     常见 URP Lit/Unlit shader 的 Forward Pass。
        ///
        /// SRPDefaultUnlit：
        ///     一些简单 Unlit shader 会使用这个 tag。
        ///
        /// 你的 WaterRippleBrush shader 如果没有写 LightMode，
        /// 通常会落到 SRPDefaultUnlit。
        /// </summary>
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };

        /// <summary>
        /// 用于 Profiler / Frame Debugger 标记这个 Pass。
        /// </summary>
        private readonly ProfilingSampler profilingSampler;

        /// <summary>
        /// 当前 Pass 使用的配置。
        /// </summary>
        private Settings settings;

        /// <summary>
        /// 当前水波系统的 RT 管理器。
        /// </summary>
        private WaterRippleRTManager manager;

        /// <summary>
        /// 过滤要绘制的 Renderer。
        ///
        /// 包含两个核心条件：
        /// 1. RenderQueueRange：Opaque / Transparent / All
        /// 2. LayerMask：只绘制 WaterRippleBrush Layer
        /// </summary>
        private FilteringSettings filteringSettings;

        /// <summary>
        /// 构造函数。
        /// </summary>
        public WaterRipplePass(Settings settings)
        {
            this.settings = settings;
            profilingSampler = new ProfilingSampler(settings.profilerTag);
        }

        /// <summary>
        /// 每次 AddRenderPasses 时都会调用 Setup，
        /// 把最新的 manager / settings 传进来。
        /// </summary>
        public void Setup(WaterRippleRTManager manager, Settings settings)
        {
            this.manager = manager;
            this.settings = settings;

            // 保证 Pass 执行时机和 Inspector 配置一致。
            renderPassEvent = settings.renderPassEvent;

            // 根据队列模式决定绘制 Opaque / Transparent / All。
            RenderQueueRange queueRange = GetQueueRange(settings.queueMode);

            // 只绘制指定 Layer 上的 Brush。
            filteringSettings = new FilteringSettings(queueRange, settings.waterRippleLayerMask);
        }

        /// <summary>
        /// URP 在执行 Pass 前调用。
        ///
        /// 这里声明：
        /// 1. 当前 Pass 的渲染目标是 CurrentBrushRT。
        /// 2. 进入这个 Pass 时要清成 manager.ClearColor。
        ///
        /// manager.ClearColor 应该是：
        /// RGB = (0.5, 0.5, 1)
        /// A   = 0
        /// </summary>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (manager == null || !manager.Initialized)
                return;

            // 把这个 Pass 的颜色输出目标设为 CurrentBrushRT。
            ConfigureTarget(manager.CurrentBrushRT);

            // 声明清屏颜色。
            // 注意：这里是 URP 的 Pass 配置清屏。
            // 下面 Execute 里又手动 Clear 一次，是为了调试和稳定性。
            ConfigureClear(ClearFlag.Color, manager.ClearColor);
        }

        /// <summary>
        /// Pass 的实际执行逻辑。
        ///
        /// 执行顺序：
        ///
        /// 1. 清空 CurrentBrushRT。
        /// 2. 绘制 WaterRippleBrush Layer 到 CurrentBrushRT。
        /// 3. 如果需要累积，把 CurrentBrushRT 和 AccumA 合并到 AccumB。
        /// 4. 交换 AccumA / AccumB。
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (manager == null || !manager.Initialized)
                return;

            RenderTexture currentBrushRT = manager.CurrentBrushRT;
            
            Material waveEquationMaterial = manager.WaveEquationMaterial;

            RenderTexture currentFrameRT=manager.CurrentFrameRT;
            
            
            // 缺少关键资源时直接退出，避免把错误结果写进三缓冲。
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

            // 创建绘制设置。
            // shaderTagIds 决定哪些 Shader Pass 可以被绘制。
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds,ref renderingData,sortingCriteria);

            context.DrawRenderers(renderingData.cullResults,ref drawingSettings,ref filteringSettings);


            if (settings.accumulate)
            {
                manager.setWaterRippleEquationMaterial();
                
                cmd.Blit(currentBrushRT, currentFrameRT, waveEquationMaterial);

                // 提交累积 Blit 命令。
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                // 告诉 manager：
                // 当前这次新水波 stamp 已经被消费。
                //
                // 如果人物站着不动，没有新 stamp，
                // 下一帧 ShouldAccumulateThisFrame() 就会返回 false。
                manager.ConsumeStamp();
                
                manager.AdvanceWaveFrame();
                manager.waterRippleAfterRenderFeature();
            }

            // 用完 CommandBuffer 后必须释放回池子。
            CommandBufferPool.Release(cmd);
        }

        /// <summary>
        /// 根据 QueueMode 返回 RenderQueueRange。
        ///
        /// 这个范围会影响 DrawRenderers 能绘制哪些 Renderer。
        /// </summary>
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

        /// <summary>
        /// 根据 QueueMode 返回排序方式。
        ///
        /// Opaque：
        ///     通常从前到后排序，减少 overdraw。
        ///
        /// Transparent：
        ///     通常从后到前排序，保证透明混合顺序。
        ///
        /// All：
        ///     不指定排序。
        /// </summary>
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
