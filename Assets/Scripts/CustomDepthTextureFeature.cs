using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 自定义 URP Renderer Feature。
///
/// 作用：
/// 在 URP 渲染流程中插入一个自定义 Render Pass，
/// 把指定 Layer 的物体重新渲染到一张自定义深度纹理里。
///
/// 最终得到一张全局纹理：
/// _MyCustomDepthTexture
///
/// 其他 Shader 可以通过这个名字采样它。
/// </summary>
public class CustomDepthTextureFeature : ScriptableRendererFeature
{
    /// <summary>
    /// 暴露到 Inspector 面板上的配置。
    ///
    /// 只要字段是 public，并且类型可序列化，
    /// 就可以在 Universal Renderer Data 的 Renderer Feature 面板里看到。
    /// </summary>
    [Serializable]
    public class Settings
    {
        
        [Header("Cascade")]
        public int cascadeIndex = 0;
        
        /// <summary>
        /// 自定义深度纹理的全局名字。
        ///
        /// 后续 Shader 里可以通过：
        /// TEXTURE2D(_MyCustomDepthTexture);
        /// 来采样这张纹理。
        /// </summary>
        public string textureName = "";
        
        [Header("Texture Resolution")]
        [Tooltip("自定义深度纹理分辨率。值越大阴影越清晰，但性能和显存开销越高。")]
        public int shadowMapResolution = 1024;

        /// <summary>
        /// 用来渲染深度的材质。
        ///
        /// 这里通常不是物体自己的材质，
        /// 而是一个 override material。
        ///
        /// 也就是说：
        /// 场景里的物体会被重新画一遍，
        /// 但这次不使用它们原来的材质，
        /// 而是统一使用这个 depthMaterial。
        /// </summary>
        public Material depthMaterial;

        /// <summary>
        /// 哪些 Layer 的物体会被画进这张自定义深度图。
        ///
        /// ~0 表示所有 Layer。
        /// 你可以在 Inspector 里改成只渲染角色、地面、敌人等。
        /// </summary>
        [Header("Object Filter")]
        [Tooltip("只有这些 Layer 会被渲染进自定义深度图。建议只放 Cube / Sphere / 角色等投影物体。")]
        public LayerMask casterLayerMask = 0;

        [Tooltip("这些 Layer 会被强制排除。建议放 Ground / DebugPreview。")]
        public LayerMask excludeLayerMask = 0;

        /// <summary>
        /// 这个自定义 Pass 插入到 URP 渲染流程的哪个阶段。
        ///
        /// AfterRenderingPrePasses：
        /// 在 URP 的深度预处理之后执行。
        ///
        /// 常见选择：
        /// BeforeRenderingOpaques：在不透明物体前执行
        /// AfterRenderingOpaques：在不透明物体后执行
        /// BeforeRenderingPostProcessing：在后处理前执行
        /// </summary>
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;

        /// <summary>
        /// 是否把透明物体也渲染进这张深度图。
        ///
        /// false：
        /// 只渲染不透明物体，最常见、最稳定。
        ///
        /// true：
        /// 渲染全部队列，包括透明物体。
        /// 但透明物体的深度写入经常比较特殊，不一定符合预期。
        /// </summary>
        public bool renderTransparentObjects = false;
        
        
        [Header("Camera Filter")]
        [Tooltip("只对带有此 Tag 的相机生效。如果留空，则默认对所有 Game 相机生效。")]
        public string targetCameraTag = "LightCamera"; 
    }

    /// <summary>
    /// Feature 的配置数据。
    ///
    /// Unity 会把这个字段显示在 Renderer Feature 的 Inspector 面板里。
    /// </summary>
    public Settings settings = new Settings();

    /// <summary>
    /// 真正执行渲染的 Pass。
    ///
    /// Renderer Feature 本身更像是“注册器”，
    /// Render Pass 才是真正干活的地方。
    /// </summary>
    private CustomDepthPass _pass;

    /// <summary>
    /// Create 会在 Renderer Feature 被创建或重新加载时调用。
    ///
    /// 一般在这里创建自定义 Pass。
    /// 注意：
    /// 不建议在这里申请和相机尺寸相关的 RT，
    /// 因为这个时候还不知道当前相机的宽高。
    /// </summary>
    public override void Create()
    {
        _pass = new CustomDepthPass(settings);
    }

    /// <summary>
    /// AddRenderPasses 会在每个相机渲染时调用。
    ///
    /// 作用：
    /// 判断当前相机是否需要执行这个 Pass，
    /// 如果需要，就通过 renderer.EnqueuePass 把 Pass 加进 URP 渲染队列。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
    {
        // 如果没有指定深度材质，就不执行。
        // 因为后面 DrawRenderers 需要 overrideMaterial。
        if (settings.depthMaterial == null)
            return;

        // 这里只让 Game Camera 执行。
        //
        // 避免 SceneView、Preview Camera、反射相机等也生成这张纹理，
        // 这样可以减少干扰，也方便调试。
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;
        
        
        // ==========================================
        // 使用 Tag 过滤目标相机
        // ==========================================
        Camera currentCamera = renderingData.cameraData.camera;

        // 如果你在面板里配置了具体的 Tag，就进行检测
        if (!string.IsNullOrEmpty(settings.targetCameraTag))
        {
            // 只有当当前相机的 Tag 与目标 Tag 匹配时才继续
            // 注意：使用 CompareTag 比 currentCamera.tag == targetCameraTag 性能更好且没有 GC
            if (!currentCamera.CompareTag(settings.targetCameraTag))
            {
                return;
            }
        }

        // 每帧把最新的 Settings 传给 Pass。
        //
        // 这样你在 Inspector 修改 LayerMask、PassEvent 等配置时，
        // Pass 可以拿到最新值。
        _pass.Setup(settings);

        // 把自定义 Pass 加入 URP 的渲染流程。
        renderer.EnqueuePass(_pass);
    }

    /// <summary>
    /// 当 Renderer Feature 被销毁时调用。
    ///
    /// 这里释放 Pass 里申请的 RTHandle。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }

    /// <summary>
    /// 自定义 Render Pass。
    ///
    /// 真正的渲染流程发生在这里：
    /// 1. 创建自定义 RT
    /// 2. 设置渲染目标
    /// 3. 清屏
    /// 4. DrawRenderers 绘制指定物体
    /// 5. 把结果设置成全局纹理
    /// </summary>
    private class CustomDepthPass : ScriptableRenderPass
    {
        
        private static readonly int[] WorldToLightUVMatrixIDs =
        {
            Shader.PropertyToID("_WorldToLightUVMatrix0"),
            Shader.PropertyToID("_WorldToLightUVMatrix1"),
            Shader.PropertyToID("_WorldToLightUVMatrix2"),
            Shader.PropertyToID("_WorldToLightUVMatrix3")
        };

        private static readonly int[] WorldToLightViewMatrixIDs =
        {
            Shader.PropertyToID("_WorldToLightViewMatrix0"),
            Shader.PropertyToID("_WorldToLightViewMatrix1"),
            Shader.PropertyToID("_WorldToLightViewMatrix2"),
            Shader.PropertyToID("_WorldToLightViewMatrix3")
        };

        private static readonly int[] CustomLightDepthParamsIDs =
        {
            Shader.PropertyToID("_CustomLightDepthParams0"),
            Shader.PropertyToID("_CustomLightDepthParams1"),
            Shader.PropertyToID("_CustomLightDepthParams2"),
            Shader.PropertyToID("_CustomLightDepthParams3")
        };
        
        private static readonly int[] CustomDepthTextureIDs =
        {
            Shader.PropertyToID("_MyCustomDepthTexture0"),
            Shader.PropertyToID("_MyCustomDepthTexture1"),
            Shader.PropertyToID("_MyCustomDepthTexture2"),
            Shader.PropertyToID("_MyCustomDepthTexture3")
        };

        private static readonly string[] CustomDepthTextureNames =
        {
            "_MyCustomDepthTexture0",
            "_MyCustomDepthTexture1",
            "_MyCustomDepthTexture2",
            "_MyCustomDepthTexture3"
        };
        
        private static readonly int[] CustomDepthTextureTexelSizeIDs =
        {
            Shader.PropertyToID("_MyCustomDepthTexture0_TexelSize"),
            Shader.PropertyToID("_MyCustomDepthTexture1_TexelSize"),
            Shader.PropertyToID("_MyCustomDepthTexture2_TexelSize"),
            Shader.PropertyToID("_MyCustomDepthTexture3_TexelSize")
        };
            
        
        private static readonly int CustomLightDirectionWSID =Shader.PropertyToID("_CustomLightDirectionWS");
            
        private string _rtName;
        
        /// <summary>
        /// 保存从 Feature 传进来的配置。
        /// </summary>
        private Settings _settings;

        /// <summary>
        /// 颜色 RT。
        ///
        /// 虽然名字叫 depthColorRT，
        /// 但它本质是一张颜色纹理。
        ///
        /// 我们用 R32_SFloat 格式，
        /// 只用 R 通道来存储深度值。
        ///
        /// 例如：
        /// R = 0 表示近处
        /// R = 1 表示远处
        /// </summary>
        private RTHandle _depthColorRT;

        /// <summary>
        /// 深度缓冲 RT。
        ///
        /// 它不是给 Shader 采样用的，
        /// 而是给 GPU 做 ZTest / ZWrite 用的。
        ///
        /// 没有这个深度缓冲的话，
        /// 多个物体重叠时可能无法正确判断前后遮挡关系。
        /// </summary>
        private RTHandle _depthBufferRT;

        /// <summary>
        /// 全局纹理 ID。
        ///
        /// Shader.PropertyToID 可以把字符串转成 int，
        /// 后续 SetGlobalTexture 用 int 会更快一些。
        /// </summary>
        private int _textureId;

        /// <summary>
        /// 过滤哪些 Renderer 会被绘制。
        ///
        /// 它主要控制：
        /// 1. RenderQueue 范围：不透明 / 透明 / 全部
        /// 2. LayerMask：只画哪些 Layer
        /// </summary>
        private FilteringSettings _filteringSettings;

        /// <summary>
        /// Frame Debugger / Profiler 里显示的性能采样名字。
        ///
        /// 你在 Frame Debugger 看到的：
        /// Custom Depth Texture
        /// 就是这个名字。
        /// </summary>
        private readonly ProfilingSampler _profilingSampler =new ProfilingSampler("Custom Depth Texture");
            

        /// <summary>
        /// 要匹配的 Shader Pass 名字。
        ///
        /// DrawRenderers 不是随便画 Shader，
        /// 它会寻找物体材质 Shader 里指定 LightMode 的 Pass。
        ///
        /// 常见 URP Pass：
        /// UniversalForward：普通 URP 前向渲染 Pass
        /// UniversalForwardOnly：只走 Forward 的 Pass
        /// SRPDefaultUnlit：一些 Unlit Shader 会用
        /// DepthOnly：深度专用 Pass
        ///
        /// 这里虽然用了 overrideMaterial，
        /// 但仍然需要这些 ShaderTagId 来筛选可绘制对象。
        /// </summary>
        private readonly List<ShaderTagId> _shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("DepthOnly")
        };

        /// <summary>
        /// 构造函数。
        ///
        /// 创建 Pass 时，把 Settings 初始化进去。
        /// </summary>
        public CustomDepthPass(Settings settings)
        {
            Setup(settings);
        }
        
        private static Matrix4x4 GetTextureScaleAndBiasMatrix()
        {
            Matrix4x4 m = Matrix4x4.identity;

            m.m00 = 0.5f;

            // 固定翻转 Y
            m.m11 = -0.5f;

            m.m22 = 0.5f;

            m.m03 = 0.5f;
            m.m13 = 0.5f;
            m.m23 = 0.5f;

            return m;
        }

        /// <summary>
        /// 更新 Pass 的配置。
        ///
        /// AddRenderPasses 每帧会调用它，
        /// 所以你在 Inspector 改设置时可以立即生效。
        /// </summary>
        public void Setup(Settings settings)
        {
            _settings = settings;

            // 设置这个 Pass 插入到 URP 管线里的时机。
            renderPassEvent = settings.renderPassEvent;

            // 把纹理名转成 int ID，后面 SetGlobalTexture 使用。
            int cascadeIndex = Mathf.Clamp(settings.cascadeIndex, 0, 3);

            if (string.IsNullOrEmpty(settings.textureName))
            {
                _textureId = CustomDepthTextureIDs[cascadeIndex];
                _rtName = CustomDepthTextureNames[cascadeIndex];
            }
            else
            {
                _textureId = Shader.PropertyToID(settings.textureName);
                _rtName = settings.textureName;
            }

            // 根据设置决定渲染队列范围。
            //
            // RenderQueueRange.opaque：
            // 只画不透明物体。
            //
            // RenderQueueRange.all：
            // 不透明和透明都画。
            RenderQueueRange queueRange = settings.renderTransparentObjects? RenderQueueRange.all: RenderQueueRange.opaque;
            

            // settings.casterLayerMask 决定允许哪些 Layer 被画进深度图
            // settings.excludeLayerMask 决定强制排除哪些 Layer
            // 先取需要渲染的 Layer
            int finalLayerMask = settings.casterLayerMask.value;

            // 再排除不想渲染的 Layer
            finalLayerMask &= ~settings.excludeLayerMask.value;

            // 如果没有选择任何 Layer，就什么都不画
            _filteringSettings = new FilteringSettings(queueRange,finalLayerMask);
                
                
            
        }

        /// <summary>
        /// OnCameraSetup 会在 Execute 之前调用。
        ///
        /// 适合在这里：
        /// 1. 根据当前相机分辨率创建 RT
        /// 2. 设置当前 Pass 的 Render Target
        /// 3. 设置 Clear 行为
        ///
        /// URP 14 推荐在这里配置 RTHandle。
        /// </summary>
        public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData renderingData)
        {
            // 当前相机的 RenderTexture 描述。
            //
            // 包含宽高、MSAA、HDR、颜色格式等信息。
            // 我们基于它来创建同分辨率的自定义纹理。
            RenderTextureDescriptor cameraDesc =renderingData.cameraData.cameraTargetDescriptor;
                

            // ================================
            // 1. 创建颜色 RT，用来保存自定义深度值
            // ================================

            RenderTextureDescriptor colorDesc = cameraDesc;
            
            int resolution = Mathf.Max(16, _settings.shadowMapResolution);

            colorDesc.width = resolution;
            colorDesc.height = resolution;

            // 颜色 RT 自己不需要内置 depth buffer。
            // 因为我们会单独创建 _depthBufferRT。
            colorDesc.depthBufferBits = 0;

            // 自定义深度图通常不需要 MSAA。
            // 用 1 可以避免采样和 Resolve 的额外复杂度。
            colorDesc.msaaSamples = 1;

            // R32_SFloat 表示：
            // 单通道，32 位浮点。
            //
            // 这张纹理只有 R 通道。
            // Frame Debugger 预览时会把 R 当成灰度显示。
            colorDesc.graphicsFormat = GraphicsFormat.R32_SFloat;
            
            colorDesc.useMipMap = false;
            colorDesc.autoGenerateMips = false;

            // 创建或复用 RTHandle。
            //
            // ReAllocateIfNeeded 的好处：
            // 如果分辨率、格式没变，就复用旧 RT；
            // 如果相机尺寸变了，就自动重新分配。
            RenderingUtils.ReAllocateIfNeeded(ref _depthColorRT,colorDesc, FilterMode.Point,TextureWrapMode.Clamp,name: _rtName);

            // ================================
            // 2. 创建真正的深度缓冲
            // ================================

            RenderTextureDescriptor depthDesc = cameraDesc;
            
            depthDesc.width = resolution;
            depthDesc.height = resolution;
            
            depthDesc.useMipMap = false;
            depthDesc.autoGenerateMips = false;

            // 同样不使用 MSAA。
            depthDesc.msaaSamples = 1;

            // 这张 RT 不需要颜色格式。
            // 它只作为 depth buffer 使用。
            depthDesc.graphicsFormat = GraphicsFormat.None;

            // 设置深度格式为 32 位浮点深度。
            depthDesc.depthStencilFormat = GraphicsFormat.D32_SFloat;

            // 深度位数。
            depthDesc.depthBufferBits = 32;

            RenderingUtils.ReAllocateIfNeeded(ref _depthBufferRT,depthDesc,FilterMode.Point,TextureWrapMode.Clamp,name: _rtName + "_DepthBuffer" );
            
            // ================================
            // 3. 设置当前 Pass 的渲染目标
            // ================================

            // 第一个参数：颜色输出目标
            // 第二个参数：深度缓冲目标
            //
            // 之后 Execute 里的 DrawRenderers 就会画到这两个 RT 上。
            ConfigureTarget(_depthColorRT, _depthBufferRT);

            // ================================
            // 4. 设置清屏
            // ================================

            // ClearFlag.All 表示同时清颜色和深度。
            //
            // 注意：
            // 你的颜色纹理是 R32_SFloat，只有 R 通道。
            // 所以这里的颜色最终只看 red 分量。
            //
            // new Color(0.25f, 0, 0, 1)
            // 实际写入的是 R = 0.25。
            // Frame Debugger 会显示成 25% 灰度，而不是红色。
            ConfigureClear(ClearFlag.All, Color.white);
        }

        /// <summary>
        /// Execute 是这个 Pass 真正执行渲染的地方。
        ///
        /// 主要流程：
        /// 1. 创建 CommandBuffer
        /// 2. 设置 DrawingSettings
        /// 3. 使用 context.DrawRenderers 绘制物体
        /// 4. 把结果 RT 设置为全局纹理
        /// </summary>
        public override void Execute(ScriptableRenderContext context,ref RenderingData renderingData)
        {
            // 没有深度材质就不执行。
            if (_settings.depthMaterial == null)
                return;

            // 从 CommandBufferPool 里拿一个 CommandBuffer。
            //
            // 不要直接 new CommandBuffer，
            // 用 Pool 可以减少 GC 和内存分配。
            CommandBuffer cmd = CommandBufferPool.Get();

            // ProfilingScope 用来在 Frame Debugger / Profiler 中标记范围。
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // 先执行前面积累在 cmd 里的命令。
                //
                // 注意：
                // CommandBuffer 里的命令不是立刻执行的，
                // 需要 context.ExecuteCommandBuffer 才会提交给渲染上下文。
                context.ExecuteCommandBuffer(cmd);

                // 执行完后清空 cmd，
                // 避免后面重复执行旧命令。
                cmd.Clear();

                // 排序方式。
                //
                // 对不透明物体，通常使用 cameraData.defaultOpaqueSortFlags。
                // 它一般会按前后顺序优化渲染。
                SortingCriteria sortingCriteria =renderingData.cameraData.defaultOpaqueSortFlags;
                    

                // 创建绘制设置。
                //
                // 第一个 ShaderTagId 是主 Pass 名。
                // 后面可以继续 SetShaderPassName 添加更多可匹配的 Pass。
                DrawingSettings drawingSettings = CreateDrawingSettings(
                    _shaderTagIds[0],
                    ref renderingData,
                    sortingCriteria
                );

                // 添加额外的 Shader Pass 名。
                //
                // 这样不同类型的 URP Shader 都有机会被 DrawRenderers 匹配到。
                for (int i = 1; i < _shaderTagIds.Count; i++)
                {
                    drawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
                }

                // 用自定义 depthMaterial 替换原物体材质。
                //
                // 这一步非常关键：
                // 场景中的 Plane、Sphere 等物体不会用自己的原材质绘制，
                // 而是统一用这个 depthMaterial 绘制。
                drawingSettings.overrideMaterial = _settings.depthMaterial;

                // 使用 overrideMaterial 的第 0 个 Pass。
                //
                // 如果你的 depthMaterial Shader 里有多个 Pass，
                // 可以通过这个索引选择用哪个 Pass。
                drawingSettings.overrideMaterialPassIndex = 0;

                // 不需要每个物体的额外数据。
                //
                // 例如 lightmap、reflection probe、light probe 等。
                // 深度图一般不需要这些。
                drawingSettings.perObjectData = PerObjectData.None;

                // 真正绘制物体。
                //
                // renderingData.cullResults：
                // 当前相机裁剪后的可见物体集合。
                //
                // drawingSettings：
                // 怎么画，用哪个 ShaderTag、哪个材质、怎么排序。
                //
                // _filteringSettings：
                // 画哪些 RenderQueue、哪些 Layer。
                context.DrawRenderers(renderingData.cullResults,ref drawingSettings,ref _filteringSettings);
                
                Camera lightCamera = renderingData.cameraData.camera;

                Matrix4x4 viewMatrix = lightCamera.worldToCameraMatrix;

                // 这里 true 更适合 RenderTexture / ShadowMap 类型目标
                Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(
                    lightCamera.projectionMatrix,
                    true
                );

                Matrix4x4 textureScaleAndBias = GetTextureScaleAndBiasMatrix();

                Matrix4x4 worldToLightUVMatrix =
                    textureScaleAndBias * projMatrix * viewMatrix;

                float nearPlane = lightCamera.nearClipPlane;
                float farPlane = lightCamera.farClipPlane;

                int cascadeIndex = Mathf.Clamp(_settings.cascadeIndex, 0, 3);

                cmd.SetGlobalMatrix(WorldToLightUVMatrixIDs[cascadeIndex], worldToLightUVMatrix);
                cmd.SetGlobalMatrix(WorldToLightViewMatrixIDs[cascadeIndex], viewMatrix);
                cmd.SetGlobalVector(CustomLightDepthParamsIDs[cascadeIndex], new Vector4(nearPlane, farPlane, 1.0f / Mathf.Max(0.0001f, farPlane - nearPlane), 0.0f));
                
                
                
                // cmd.SetGlobalMatrix(WorldToLightUVMatrixID, worldToLightUVMatrix);
                // cmd.SetGlobalMatrix(WorldToLightViewMatrixID, viewMatrix);
                // cmd.SetGlobalVector(CustomLightDepthParamsID,new Vector4(nearPlane,farPlane,1.0f / Mathf.Max(0.0001f, farPlane - nearPlane),0.0f ));
                
                
                Vector3 lightDirWS = lightCamera.transform.forward;

                cmd.SetGlobalVector(CustomLightDirectionWSID,new Vector4(lightDirWS.x,lightDirWS.y,lightDirWS.z,0.0f ) );

                
                int resolution = Mathf.Max(16, _settings.shadowMapResolution);

                cmd.SetGlobalVector(
                    CustomDepthTextureTexelSizeIDs[cascadeIndex],
                    new Vector4(
                        1.0f / resolution,
                        1.0f / resolution,
                        resolution,
                        resolution
                    )
                );
                
                // 把这张 RT 设置成全局纹理。
                //
                // 后续所有 Shader 都可以用 _settings.textureName 采样它。
                //
                // 例如 textureName = "_MyCustomDepthTexture"，
                // Shader 里就可以声明：
                //
                // TEXTURE2D(_MyCustomDepthTexture);
                // SAMPLER(sampler_MyCustomDepthTexture);
                cmd.SetGlobalTexture(_textureId, _depthColorRT);
            }

            // 提交 CommandBuffer。
            //
            // 注意：
            // DrawRenderers 是通过 context 直接记录的，
            // SetGlobalTexture 是记录在 cmd 里的。
            // 所以最后还需要 ExecuteCommandBuffer。
            context.ExecuteCommandBuffer(cmd);

            // 用完后归还给 Pool。
            CommandBufferPool.Release(cmd);
        }

        /// <summary>
        /// 每个相机渲染结束后的清理。
        ///
        /// 注意：
        /// RTHandle 不要在这里每帧 Release。
        ///
        /// 因为这张 RT 每帧都要复用，
        /// 如果每帧释放再创建，会造成额外开销。
        /// </summary>
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // 这里暂时不做事情。
        }

        /// <summary>
        /// 真正释放 RTHandle。
        ///
        /// 当 Renderer Feature 被销毁、关闭或重新加载时，
        /// 会通过 Feature.Dispose 调用到这里。
        /// </summary>
        public void Dispose()
        {
            _depthColorRT?.Release();
            _depthBufferRT?.Release();
        }
    }
}