using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 屏幕空间贴花 Renderer Feature。
///
/// URP14 注意点：
/// 不要在 AddRenderPasses 里访问 renderer.cameraColorTargetHandle。
///
/// 正确流程：
/// 1. AddRenderPasses 只负责 enqueue pass
/// 2. SetupRenderPasses 里再把 cameraColorTargetHandle 传给 Pass
/// </summary>
public class ScreenSpaceDecalFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Material")]
        public Material decalMaterial;

        [Header("Render Timing")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public Settings settings = new Settings();

    private ScreenSpaceDecalPass _pass;

    public override void Create()
    {
        _pass = new ScreenSpaceDecalPass(settings);
    }

    /// <summary>
    /// URP14 推荐在这里访问 renderer.cameraColorTargetHandle。
    ///
    /// 注意：
    /// AddRenderPasses 阶段 camera target 可能还没创建，
    /// 所以不能在那里访问。
    /// </summary>
    public override void SetupRenderPasses(ScriptableRenderer renderer,in RenderingData renderingData)
    {
        if (settings.decalMaterial == null)
            return;

        if (ScreenSpaceDecalProjector.ActiveProjector == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        _pass.SetTarget(renderer.cameraColorTargetHandle);
    }

    /// <summary>
    /// AddRenderPasses 只负责把 Pass 加入渲染队列。
    /// 不要在这里访问 cameraColorTargetHandle。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        if (settings.decalMaterial == null)
            return;

        if (ScreenSpaceDecalProjector.ActiveProjector == null)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        renderer.EnqueuePass(_pass);
    }

    private class ScreenSpaceDecalPass : ScriptableRenderPass
    {
        private static readonly int DecalWorldToLocalID = Shader.PropertyToID("_DecalWorldToLocal");
           

        private static readonly int DecalTextureID =Shader.PropertyToID("_DecalTexture");
            

        private static readonly int DecalColorID =Shader.PropertyToID("_DecalColor");
            

        private static readonly int DecalParamsID =Shader.PropertyToID("_DecalParams");
            

        private readonly Settings _settings;
        private RTHandle _cameraColorTarget;

        public ScreenSpaceDecalPass(Settings settings)
        {
            _settings = settings;
            renderPassEvent = settings.renderPassEvent;

            // 这个 Pass 需要采样 _CameraDepthTexture。
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void SetTarget(RTHandle cameraColorTarget)
        {
            _cameraColorTarget = cameraColorTarget;
            renderPassEvent = _settings.renderPassEvent;
        }

        public override void Execute( ScriptableRenderContext context,ref RenderingData renderingData)
        {
            if (_cameraColorTarget == null)
                return;

            ScreenSpaceDecalProjector projector =ScreenSpaceDecalProjector.ActiveProjector;
                

            if (projector == null)
                return;

            if (_settings.decalMaterial == null)
                return;

            CommandBuffer cmd =CommandBufferPool.Get("Screen Space Decal");
                

            Material mat = _settings.decalMaterial;

            mat.SetMatrix(DecalWorldToLocalID,projector.GetWorldToDecalMatrix());
            

            if (projector.decalTexture != null)
            {
                mat.SetTexture(DecalTextureID, projector.decalTexture);
            }

            mat.SetColor(DecalColorID, projector.decalColor);

            mat.SetVector(DecalParamsID,new Vector4(projector.opacity,projector.edgeFade,0f,0f));
            

            CoreUtils.SetRenderTarget(cmd, _cameraColorTarget);

            // cmd.DrawMesh(RenderingUtils.fullscreenMesh,Matrix4x4.identity,mat,0,0);
            cmd.DrawProcedural(Matrix4x4.identity,mat,0,MeshTopology.Triangles,3,1);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}