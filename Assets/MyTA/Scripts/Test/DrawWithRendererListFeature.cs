using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DrawWithRendererListFeature : ScriptableRendererFeature
{
    [Header("Configuration")]
    public Material overrideMaterial;                       // 用于覆写的材质
    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    public string globalTextureName = "_MyCustomDepthTexture"; // 全局纹理名称

    [Header("Camera Filter")]
    [Tooltip("通过标签过滤相机，例如设置为 'LightCamera'")]
    public string cameraTag = "LightCamera"; // 新增：用于过滤的标签

    class CustomRenderPass : ScriptableRenderPass
    {
        private Material materialToUse;
        private RTHandle depthTextureHandle;
        private string textureName;
        private string filterTag;
        
        public CustomRenderPass(Material mat, RenderPassEvent evt, string globalTexName, string tag)
        {
            materialToUse = mat;
            renderPassEvent = evt;
            textureName = globalTexName;
            this.filterTag = tag;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
            if (materialToUse == null) return;
        
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            
            Camera currentCam = renderingData.cameraData.camera;
            float farClip = currentCam.farClipPlane;
            
            descriptor.depthBufferBits = 24;
            descriptor.msaaSamples = 1;
            // descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
            descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB;
        
            RenderingUtils.ReAllocateIfNeeded(ref depthTextureHandle, descriptor,FilterMode.Point, TextureWrapMode.Clamp, name: textureName);
                
        
            // ConfigureTarget(depthTextureHandle);
            //    第二个参数是深度缓冲区，这里用颜色纹理内置的深度缓冲区
            // ConfigureTarget(depthTextureHandle, depthTextureHandle);
            
            // ConfigureClear(ClearFlag.Color, Color.white);
            // ConfigureClear(ClearFlag.All, new Color(currentCam.farClipPlane, 0, 0, 1));
            // ConfigureClear(ClearFlag.All, new Color(1f, 0, 0, 1));
            // ConfigureClear(ClearFlag.All, Color.white);
            
            // cmd.ClearRenderTarget(true, true, new Color(0.0f, 0.0f, 0.0f, 1.0f), 0.0f);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        
            if (materialToUse == null) 
                return;
            if (depthTextureHandle == null) 
                return;
            
        
            CommandBuffer cmd = CommandBufferPool.Get("DrawDepthToTexture");
            
            // 设置渲染目标 (再次确认)
            cmd.SetRenderTarget(depthTextureHandle, depthTextureHandle);
            
            Camera currentCam = renderingData.cameraData.camera;
            materialToUse.SetFloat("_CameraFarPlane", currentCam.farClipPlane);
            float farClip = currentCam.farClipPlane;
            
            // cmd.SetRenderTarget(depthTextureHandle, depthTextureHandle);
            cmd.ClearRenderTarget(true, true, new Color(1,1,1,1),1);
        
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            var drawingSettings = CreateDrawingSettings(new ShaderTagId("UniversalForward"), ref renderingData, sortingCriteria);
        
            drawingSettings.overrideMaterial = materialToUse;
            drawingSettings.overrideMaterialPassIndex = 0;
        
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
        
            var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
            var rendererList = context.CreateRendererList(ref rendererListParams);
            
            cmd.DrawRendererList(rendererList);
            cmd.SetGlobalTexture(textureName, depthTextureHandle);
        
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        // public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        // {
        //     if (materialToUse == null || depthTextureHandle == null) return;
        //
        //     CommandBuffer cmd = CommandBufferPool.Get("TestRenderTarget");
        //
        //     // 设置渲染目标并清除为黑色
        //     cmd.SetRenderTarget(depthTextureHandle, depthTextureHandle);
        //     cmd.ClearRenderTarget(true, true, Color.black, 0.0f);
        //
        //     // 使用内置材质绘制全屏四边形（确保能看到东西）
        //     cmd.DrawProcedural(Matrix4x4.identity, materialToUse, 0, MeshTopology.Triangles, 3, 1);
        //
        //     context.ExecuteCommandBuffer(cmd);
        //     CommandBufferPool.Release(cmd);
        // }

        public void Dispose()
        {
            depthTextureHandle?.Release();
        }
    }

    private CustomRenderPass scriptablePass;

    public override void Create()
    {
        // ★ 将 targetCamera 传递给 Pass
        scriptablePass = new CustomRenderPass(overrideMaterial, renderPassEvent, globalTextureName,cameraTag);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (overrideMaterial == null)
        {
            Debug.LogWarning("DrawWithRendererListFeature: 材质丢失，渲染Pass不会执行。");
            return;
        }
        
        // Camera currentCamera = renderingData.cameraData.camera;
        // // 标签过滤逻辑
        // if (!string.IsNullOrEmpty(cameraTag) && !currentCamera.CompareTag(cameraTag))
        //     return;


        renderer.EnqueuePass(scriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            scriptablePass?.Dispose();
        }
    }
}