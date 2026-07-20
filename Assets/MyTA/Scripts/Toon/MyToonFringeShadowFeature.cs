using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MyToonFringeShadowFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Name shown in Frame Debugger and Profiler.")]
        public string featureName = "MyToonFringeShadow";

        [Tooltip("Render before opaque objects so the face shader can sample the fringe shadow texture.")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingOpaques;

        [Tooltip("Only render fringe shadow casters on this layer.")]
        public LayerMask hairLayer;

        [Range(1000, 5000)] public int queueMin = 2000;
        [Range(1000, 5000)] public int queueMax = 3000;

        [Range(1, 4)]
        public int downSample = 1;

        public bool affectSceneView = true;
    }

    public Settings settings = new Settings();

    class FringeShadowPass : ScriptableRenderPass
    {
        const string FringeShadowTexName = "_MyToonFringeShadowTex";
        static readonly int FringeShadowTexId = Shader.PropertyToID(FringeShadowTexName);

        readonly Settings settings;
        readonly string passName;
        readonly ProfilingSampler fringeProfilingSampler;
        readonly ShaderTagId shaderTagId = new ShaderTagId("MyToonFringeShadow");

        FilteringSettings filteringSettings;
        RTHandle fringeShadowRT;
        RTHandle fringeShadowDepthRT;

        public FringeShadowPass(Settings settings)
        {
            this.settings = settings;

            passName = string.IsNullOrWhiteSpace(settings.featureName)
                ? "MyToonFringeShadow"
                : settings.featureName;

            fringeProfilingSampler = new ProfilingSampler(passName);

            var queue = new RenderQueueRange
            {
                lowerBound = Mathf.Min(settings.queueMin, settings.queueMax),
                upperBound = Mathf.Max(settings.queueMin, settings.queueMax)
            };

            filteringSettings = new FilteringSettings(queue, settings.hairLayer);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;

            if (!settings.affectSceneView && cameraData.isSceneViewCamera)
                return;

            var colorDesc = cameraData.cameraTargetDescriptor;
            colorDesc.msaaSamples = 1;
            colorDesc.depthBufferBits = 0;
            colorDesc.colorFormat = RenderTextureFormat.ARGBHalf;
            colorDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            colorDesc.depthStencilFormat = GraphicsFormat.None;

            colorDesc.width = Mathf.Max(1, colorDesc.width / settings.downSample);
            colorDesc.height = Mathf.Max(1, colorDesc.height / settings.downSample);

            RenderingUtils.ReAllocateIfNeeded(
                ref fringeShadowRT,
                colorDesc,
                // 颜色通道存的是原始深度，不能与黑色清屏值做双线性混合。
                // Point 采样可避免头发轮廓在相机远近变化时产生错误深度。
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: FringeShadowTexName
            );

            var depthDesc = colorDesc;
            depthDesc.graphicsFormat = GraphicsFormat.None;
            depthDesc.depthStencilFormat = GraphicsFormat.D32_SFloat;
            depthDesc.depthBufferBits = 32;

            RenderingUtils.ReAllocateIfNeeded(
                ref fringeShadowDepthRT,
                depthDesc,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_MyToonFringeShadowDepth"
            );

            ConfigureTarget(fringeShadowRT, fringeShadowDepthRT);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;

            if (!settings.affectSceneView && cameraData.isSceneViewCamera)
                return;

            if (fringeShadowRT == null)
                return;

            var cmd = CommandBufferPool.Get(passName);

            using (new ProfilingScope(cmd, fringeProfilingSampler))
            {
                cmd.SetGlobalTexture(FringeShadowTexId, fringeShadowRT);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var drawingSettings = CreateDrawingSettings(
                    shaderTagId,
                    ref renderingData,
                    renderingData.cameraData.defaultOpaqueSortFlags
                );

                context.DrawRenderers(
                    renderingData.cullResults,
                    ref drawingSettings,
                    ref filteringSettings
                );

                cmd.SetGlobalTexture(FringeShadowTexId, fringeShadowRT);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            fringeShadowRT?.Release();
            fringeShadowDepthRT?.Release();
        }
    }

    FringeShadowPass pass;

    public override void Create()
    {
        pass = new FringeShadowPass(settings);
        pass.renderPassEvent = settings.passEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
    }
}
