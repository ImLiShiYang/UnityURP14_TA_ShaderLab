using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SimpleTestPass : ScriptableRenderPass
{
    // 构造函数不再需要材质，但我们保留它以便向后兼容
    public SimpleTestPass(Material material = null)
    {
        // 此测试不需要材质
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        Debug.Log("SimpleTestPass: Execute() called");

        CommandBuffer cmd = CommandBufferPool.Get("SimpleTestPassCustom_Pass");
        
        // 获取相机的颜色目标
        var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
        
        // 直接将颜色目标清除为红色
        cmd.SetRenderTarget(cameraColorTarget);
        cmd.ClearRenderTarget(true, true, Color.red);
        
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}