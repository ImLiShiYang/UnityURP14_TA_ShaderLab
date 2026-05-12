// SimpleTestFeature.cs
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class SimpleTestFeature : ScriptableRendererFeature
{
    public Material testMaterial;
    private SimpleTestPass testPass;

    // SimpleTestFeature.cs 中，Create 方法改为：
    public override void Create()
    {
        Debug.Log("SimpleTestFeature: Create() called");
        testPass = new SimpleTestPass(); // 不再传递材质
        testPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

// AddRenderPasses 方法中去掉材质检查
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Debug.Log("SimpleTestFeature: AddRenderPasses() called");
        renderer.EnqueuePass(testPass);
    }
}