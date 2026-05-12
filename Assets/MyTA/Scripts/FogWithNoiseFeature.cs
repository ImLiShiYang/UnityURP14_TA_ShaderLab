using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 自定义的ScriptableRendererFeature，用于在URP渲染管线中添加基于噪声的雾效
public class FogWithNoiseFeature : ScriptableRendererFeature
{
    // 可序列化的设置类，用于在Inspector面板中调整雾效参数
    [System.Serializable]
    public class Settings
    {
        [Tooltip("用于渲染雾效的材质")]
        public Material material = null;

        [Range(0.0f, 3.0f)]
        [Tooltip("雾的整体密度，值越大雾越浓")]
        public float fogDensity = 1.0f;

        [Tooltip("雾的颜色")]
        public Color fogColor = Color.white;

        [Tooltip("用于生成噪声图案的纹理，通常使用噪声图（如Perlin噪声）")]
        public Texture noiseTexture = null;

        [Range(-0.5f, 0.5f)]
        [Tooltip("噪声纹理在X轴方向上的滚动速度，负值表示反向移动")]
        public float fogxSpeed = 0.1f;

        [Range(-0.5f, 0.5f)]
        [Tooltip("噪声纹理在Y轴方向上的滚动速度，负值表示反向移动")]
        public float fogySpeed = 0.1f;

        [Range(0.0f, 3.0f)]
        [Tooltip("噪声对雾效强度的影响程度，0表示无影响，值越大噪声越明显")]
        public float noiseAmount = 1.0f;

        [Tooltip("雾效起始距离（在世界空间或相机空间，取决于具体Shader实现），从该距离开始雾逐渐变浓")]
        public float fogStart = 0.0f;

        [Tooltip("雾效结束距离，到达该距离时雾浓度达到最大")]
        public float fogEnd = 2.0f;

        [Tooltip("此渲染特性在渲染管线中的执行时机，默认为在不透明物体渲染完成之后、透明物体渲染完成之后")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    // 公开的设置实例，可在Unity编辑器中进行配置
    public Settings settings = new Settings();

    // 自定义的渲染Pass实例，实际执行雾效绘制逻辑
    private FogWithNoisePass fogPass;

    // 当ScriptableRendererFeature被创建时调用，用于初始化资源
    public override void Create()
    {
        // 实例化自定义的Pass，并传入设置参数
        fogPass = new FogWithNoisePass(settings);
        // 将Pass的执行顺序设置为Settings中指定的渲染事件
        fogPass.renderPassEvent = settings.renderPassEvent;
    }

    // 每帧渲染时调用，用于向渲染器添加需要执行的Pass
    // renderer: 当前使用的ScriptableRenderer
    // renderingData: 当前帧的渲染数据（包含相机、剔除结果等）
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 仅当材质和噪声纹理均有效时才添加Pass，避免出现空引用错误
        if (settings.material == null && settings.noiseTexture == null)
            return;
        
        // 获取当前相机
        Camera currentCamera = renderingData.cameraData.camera;
    
        // 方式1：按相机名称过滤（例如主相机叫 "Main Camera"，镜子相机叫 "MirrorCamera"）
        if (currentCamera.name == "MirrorCamera")
        {
            return; // 镜子相机不添加边缘检测 Pass
        }
        // Debug.Log($"Adding EdgeDetectPass to camera: {renderingData.cameraData.camera.name}");
        renderer.EnqueuePass(fogPass);
    }
}

