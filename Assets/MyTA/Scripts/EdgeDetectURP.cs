using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class EdgeDetectURP : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material edgeDetectMaterial;
        [Range(0f, 1f)] public float edgesOnly = 0f;
        public Color edgeColor = Color.black;
        public Color backgroundColor = Color.white;
        [Range(0.1f, 3f)] public float sampleDistance = 1f;
        [Range(0f, 2f)] public float sensitivityDepth = 1f;
        [Range(0f, 2f)] public float sensitivityNormals = 1f;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public Settings settings = new Settings();
    private EdgeDetectPass pass;

    public override void Create()
    {
        pass = new EdgeDetectPass(settings);
        pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.edgeDetectMaterial == null)
        {
            Debug.LogWarning("EdgeDetectURP: 未分配边缘检测材质，跳过效果。");
            return;
        }
        
        // 获取当前相机
        Camera currentCamera = renderingData.cameraData.camera;
    
        // 方式1：按相机名称过滤（例如主相机叫 "Main Camera"，镜子相机叫 "MirrorCamera"）
        if (currentCamera.name == "MirrorCamera")
        {
            return; // 镜子相机不添加边缘检测 Pass
        }
        // Debug.Log($"Adding EdgeDetectPass to camera: {renderingData.cameraData.camera.name}");
        renderer.EnqueuePass(pass);
    }

    private class EdgeDetectPass : ScriptableRenderPass
    {
        private Settings settings;
        private RenderTargetIdentifier source;
        private RenderTargetHandle tempTexture;
        private string profilerTag = "EdgeDetectPass";

        public EdgeDetectPass(Settings settings)
        {
            this.settings = settings;
            renderPassEvent = settings.renderPassEvent;
            tempTexture.Init("_TempEdgeDetectTexture");
            
            // 请求法线纹理（自动也会生成深度纹理）
            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            source = renderingData.cameraData.renderer.cameraColorTarget;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            cmd.GetTemporaryRT(tempTexture.id, descriptor, FilterMode.Bilinear);

            Material mat = settings.edgeDetectMaterial;
            mat.SetFloat("_EdgeOnly", settings.edgesOnly);
            mat.SetColor("_EdgeColor", settings.edgeColor);
            mat.SetColor("_BackgroundColor", settings.backgroundColor);
            mat.SetFloat("_SampleDistance", settings.sampleDistance);
            mat.SetVector("_Sensitivity", new Vector4(settings.sensitivityNormals, settings.sensitivityDepth, 0, 0));

            Blit(cmd, source, tempTexture.id, mat, 0);
            Blit(cmd, tempTexture.id, source);

            cmd.ReleaseTemporaryRT(tempTexture.id);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}