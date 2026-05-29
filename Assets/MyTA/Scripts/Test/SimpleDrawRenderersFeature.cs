using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SimpleDrawRenderersFeature : ScriptableRendererFeature
{
    public Material overrideMaterial;
    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    public string globalTextureName = "_MyCustomDepthTexture";

    class CustomRenderPass : ScriptableRenderPass
    {
        Material materialToUse;
        RTHandle rtHandle;
        string textureName;   // 存储纹理名称

        // 在构造函数中接收纹理名称
        public CustomRenderPass(Material mat, RenderPassEvent evt, string texName)
        {
            materialToUse = mat;
            renderPassEvent = evt;
            textureName = texName;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (materialToUse == null) return;

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 24;          // 开启深度
            descriptor.msaaSamples = 1;               // 关闭 MSAA
            descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB;

            RenderingUtils.ReAllocateIfNeeded(ref rtHandle, descriptor,
                FilterMode.Point, TextureWrapMode.Clamp, name: "CustomRT");

            // 使用 ConfigureTarget 并绑定深度（框架会自动处理绑定与清除状态）
            ConfigureTarget(rtHandle, rtHandle);
            ConfigureClear(ClearFlag.All, Color.black);   // 黑色背景，深度清为 1.0
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (materialToUse == null || rtHandle == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("DrawObjects");

            // 绘制设置
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSettings = CreateDrawingSettings(
                new ShaderTagId("UniversalForward"),
                ref renderingData,
                sortingCriteria
            );
            drawingSettings.overrideMaterial = materialToUse;
            drawingSettings.overrideMaterialPassIndex = 0;

            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);

            // 传统 API，直接绘制物体
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);

            // 将结果暴露为全局纹理
            cmd.SetGlobalTexture(textureName, rtHandle);  // 使用已保存的 textureName
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            rtHandle?.Release();
        }
    }

    CustomRenderPass scriptablePass;

    public override void Create()
    {
        // 将 globalTextureName 也传入 Pass
        scriptablePass = new CustomRenderPass(overrideMaterial, renderPassEvent, globalTextureName);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (overrideMaterial == null) return;
        renderer.EnqueuePass(scriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) scriptablePass?.Dispose();
    }
}