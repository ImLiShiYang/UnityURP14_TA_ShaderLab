using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 屏幕空间贴花 Renderer Feature。
///
/// 这一版使用体积盒绘制：
/// 不再画全屏三角形，而是绘制 decal projector 的 cube 体积盒。
/// Shader 内仍然通过深度重建世界坐标，再判断像素是否落在 decal 盒子内部。
///
/// URP14 注意：
/// 不要在 AddRenderPasses 中访问 renderer.cameraColorTargetHandle。
/// 应该在 SetupRenderPasses 中访问。
/// </summary>
public class ScreenSpaceDecalFeature : ScriptableRendererFeature
{
    /// <summary>
    /// Renderer Feature 的可配置参数。
    /// 这个类会显示在 URP Renderer Data 的 Inspector 面板中。
    /// </summary>
    [System.Serializable]
    public class Settings
    {
        /// <summary>
        /// 备用贴花材质。
        /// 如果 Projector 上没有设置 decalMaterial，就使用这里的材质。
        /// </summary>
        [Header("Fallback Material")]
        [Tooltip("如果 Projector 上没有设置 decalMaterial，就使用这里的材质。")]
        public Material decalMaterial;

        /// <summary>
        /// 控制这个 Render Pass 在 URP 渲染流程中的执行时机。
        /// 默认 AfterRenderingOpaques，表示在不透明物体渲染完成后执行。
        /// </summary>
        [Header("Render Timing")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        /// <summary>
        /// 是否在 Unity Scene View 中显示贴花。
        /// </summary>
        [Header("Scene View")]
        public bool showInSceneView = true;
    }

    /// <summary>
    /// 当前 Renderer Feature 的设置。
    /// public 字段会显示在 Inspector 中。
    /// </summary>
    public Settings settings = new Settings();

    // 真正执行绘制逻辑的 ScriptableRenderPass。
    private ScreenSpaceDecalPass _pass;

    /// <summary>
    /// Unity / URP 在创建 Renderer Feature 时调用。
    /// 通常在这里创建自定义 ScriptableRenderPass。
    /// </summary>
    public override void Create()
    {
        _pass = new ScreenSpaceDecalPass(settings);
    }

    /// <summary>
    /// URP14 推荐在这里设置 Render Pass 需要的相机渲染目标。
    /// 此时 renderer.cameraColorTargetHandle 已经可用。
    /// </summary>
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (_pass == null)
            return;

        if (!ShouldRender(renderingData))
            return;

        _pass.SetTarget(renderer.cameraColorTargetHandle);
    }

    /// <summary>
    /// Unity / URP 在这里收集需要执行的 Render Pass。
    /// 这个函数只负责 EnqueuePass，不应该在这里访问 cameraColorTargetHandle。
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null)
            return;

        if (!ShouldRender(renderingData))
            return;

        renderer.EnqueuePass(_pass);
    }

    /// <summary>
    /// 判断当前相机、Projector、材质状态是否允许绘制 decal。
    /// </summary>
    private bool ShouldRender(RenderingData renderingData)
    {
        // 没有任何启用的 projector，就不需要执行 decal pass。
        if (ScreenSpaceDecalProjector.ActiveProjectors.Count == 0)
            return false;

        // 至少要有一个 projector 有可用材质。
        // 优先使用 projector 自己的材质；如果没有，就使用 Renderer Feature 的 fallback 材质。
        bool hasValidProjector = false;

        for (int i = 0; i < ScreenSpaceDecalProjector.ActiveProjectors.Count; i++)
        {
            ScreenSpaceDecalProjector projector = ScreenSpaceDecalProjector.ActiveProjectors[i];

            if (projector == null)
                continue;

            Material mat = projector.decalMaterial != null ? projector.decalMaterial : settings.decalMaterial;

            if (mat != null)
            {
                hasValidProjector = true;
                break;
            }
        }

        if (!hasValidProjector)
            return false;

        CameraType cameraType = renderingData.cameraData.cameraType;

        if (cameraType == CameraType.Game)
            return true;

        if (settings.showInSceneView && cameraType == CameraType.SceneView)
            return true;

        return false;
    }

    /// <summary>
    /// Renderer Feature 被禁用或销毁时调用。
    /// 用于释放 pass 内部创建的资源。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }

    /// <summary>
    /// 真正执行屏幕空间 decal 绘制的 Render Pass。
    /// </summary>
    private class ScreenSpaceDecalPass : ScriptableRenderPass
    {
        // Shader.PropertyToID 会把 shader 属性名转换成 int ID。
        // 用 ID 设置材质参数比每次使用字符串更高效。
        private static readonly int DecalWorldToLocalID = Shader.PropertyToID("_DecalWorldToLocal");
        private static readonly int DecalTextureID = Shader.PropertyToID("_DecalTexture");
        private static readonly int DecalColorID = Shader.PropertyToID("_DecalColor");
        private static readonly int DecalParamsID = Shader.PropertyToID("_DecalParams");
        private static readonly int DecalTilingOffsetID = Shader.PropertyToID("_DecalTilingOffset");
        private static readonly int DecalBackwardWSID = Shader.PropertyToID("_DecalBackwardWS");
        private static readonly int DecalDistanceFadeID = Shader.PropertyToID("_DecalDistanceFade");

        // Renderer Feature 中的设置引用。
        private readonly Settings _settings;

        // 体积盒 mesh。
        // 这是一个 -0.5 到 0.5 的单位 cube。
        private readonly Mesh _cubeMesh;

        // 当前相机颜色目标。
        // decal 最终会混合到这个 RTHandle 指向的颜色 buffer 上。
        private RTHandle _cameraColorTarget;
        
        // MaterialPropertyBlock 用来给每一次 DrawMesh 单独传参数。
        // 多 decal 时，每个 decal 的矩阵、贴图、颜色、透明度都可能不同。
        // 如果直接 mat.SetXXX，多个 decal 共用同一个材质时容易互相覆盖。
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

        /// <summary>
        /// 创建屏幕空间 decal pass。
        /// </summary>
        public ScreenSpaceDecalPass(Settings settings)
        {
            _settings = settings;
            renderPassEvent = settings.renderPassEvent;

            // 告诉 URP：这个 pass 需要 _CameraDepthTexture。
            // Shader 会采样深度图来重建当前像素的 world position。
            ConfigureInput(ScriptableRenderPassInput.Depth);

            _cubeMesh = CreateCubeMesh();
        }

        /// <summary>
        /// 设置当前 pass 要写入的相机颜色目标。
        /// </summary>
        public void SetTarget(RTHandle cameraColorTarget)
        {
            _cameraColorTarget = cameraColorTarget;
            renderPassEvent = _settings.renderPassEvent;
        }

        /// <summary>
        /// 释放 pass 内部创建的资源。
        /// </summary>
        public void Dispose()
        {
            if (_cubeMesh != null)
            {
                CoreUtils.Destroy(_cubeMesh);
            }
        }

        /// <summary>
        /// URP 执行到这个 Render Pass 时调用。
        /// 这里设置 shader 参数，并绘制 decal 体积盒。
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_cameraColorTarget == null)
                return;

            if (ScreenSpaceDecalProjector.ActiveProjectors.Count == 0)
                return;

            Camera camera = renderingData.cameraData.camera;

            CommandBuffer cmd = CommandBufferPool.Get("Screen Space Decal Volume Box");

            // 所有 decal 都画到当前相机颜色目标上。
            CoreUtils.SetRenderTarget(cmd, _cameraColorTarget);

            // 遍历当前场景中所有启用的 decal projector。
            for (int i = 0; i < ScreenSpaceDecalProjector.ActiveProjectors.Count; i++)
            {
                ScreenSpaceDecalProjector projector = ScreenSpaceDecalProjector.ActiveProjectors[i];

                if (projector == null)
                    continue;

                // 优先使用 projector 自己的材质。
                // 如果 projector 没有材质，就使用 Renderer Feature 的 fallback 材质。
                Material mat = projector.decalMaterial != null ? projector.decalMaterial : _settings.decalMaterial;

                if (mat == null)
                    continue;

                float distanceFade = 1f;

                // 计算距离淡出。
                if (projector.drawDistance > 0f && camera != null)
                {
                    float distance = Vector3.Distance(camera.transform.position, projector.transform.position);

                    // 超出最大绘制距离，跳过这个 decal。
                    if (distance > projector.drawDistance)
                        continue;

                    float fadeStartDistance = projector.drawDistance * projector.startFade;
                    float fadeRange = Mathf.Max(0.0001f, projector.drawDistance - fadeStartDistance);

                    distanceFade = 1f - Mathf.Clamp01((distance - fadeStartDistance) / fadeRange);
                }

                float angleStart = Mathf.Clamp(projector.angleFade.x, 0f, 180f);
                float angleEnd = Mathf.Clamp(projector.angleFade.y, angleStart + 0.001f, 180f);

                // Shader 中用 dot(normal, direction) 得到 cos(angle)，
                // 所以这里提前把角度转换成 cos 值传给 shader。
                float cosStart = Mathf.Cos(angleStart * Mathf.Deg2Rad);
                float cosEnd = Mathf.Cos(angleEnd * Mathf.Deg2Rad);

                Vector3 decalBackwardWS = projector.DecalBackwardWS;

                // 每画一个 decal，都清空并重新设置一次 PropertyBlock。
                _propertyBlock.Clear();

                _propertyBlock.SetMatrix(DecalWorldToLocalID, projector.GetWorldToDecalMatrix());

                if (projector.decalTexture != null)
                {
                    _propertyBlock.SetTexture(DecalTextureID, projector.decalTexture);
                }

                _propertyBlock.SetColor(DecalColorID, projector.decalColor);

                // _DecalParams:
                // x = opacity
                // y = edgeFade
                // z = cosEnd
                // w = cosStart
                _propertyBlock.SetVector(DecalParamsID, new Vector4(projector.opacity, projector.edgeFade, cosEnd, cosStart));

                // _DecalTilingOffset:
                // xy = tiling
                // zw = offset
                _propertyBlock.SetVector(DecalTilingOffsetID, new Vector4(projector.tiling.x, projector.tiling.y, projector.offset.x, projector.offset.y));

                // decal backward world direction。
                _propertyBlock.SetVector(DecalBackwardWSID, new Vector4(decalBackwardWS.x, decalBackwardWS.y, decalBackwardWS.z, 0f));

                // 距离淡出参数。
                _propertyBlock.SetVector(DecalDistanceFadeID, new Vector4(distanceFade, 0f, 0f, 0f));

                // 绘制当前 decal 的体积盒。
                //
                // 注意最后一个参数 _propertyBlock：
                // 它保证每个 decal 都有自己独立的 shader 参数。
                cmd.DrawMesh(_cubeMesh, projector.GetDecalLocalToWorldMatrix(), mat, 0, 0, _propertyBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <summary>
        /// 创建一个 -0.5 到 0.5 的单位立方体 mesh。
        /// 后续绘制时会通过矩阵把它变换成真实 decal 体积盒。
        /// </summary>
        private static Mesh CreateCubeMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Screen Space Decal Volume Cube";

            const float h = 0.5f;

            // 这里每个面单独使用 4 个顶点。
            // 虽然 cube 实际只有 8 个角点，但拆成 24 个顶点更适合每个面拥有独立法线。
            Vector3[] vertices =
            {
                // +Z
                new Vector3(-h, -h,  h),
                new Vector3(-h,  h,  h),
                new Vector3( h,  h,  h),
                new Vector3( h, -h,  h),

                // -Z
                new Vector3( h, -h, -h),
                new Vector3( h,  h, -h),
                new Vector3(-h,  h, -h),
                new Vector3(-h, -h, -h),

                // +X
                new Vector3( h, -h,  h),
                new Vector3( h,  h,  h),
                new Vector3( h,  h, -h),
                new Vector3( h, -h, -h),

                // -X
                new Vector3(-h, -h, -h),
                new Vector3(-h,  h, -h),
                new Vector3(-h,  h,  h),
                new Vector3(-h, -h,  h),

                // +Y
                new Vector3(-h,  h,  h),
                new Vector3(-h,  h, -h),
                new Vector3( h,  h, -h),
                new Vector3( h,  h,  h),

                // -Y
                new Vector3(-h, -h, -h),
                new Vector3(-h, -h,  h),
                new Vector3( h, -h,  h),
                new Vector3( h, -h, -h),
            };

            // Unity Mesh 的 triangles 是 int 数组。
            // 每 3 个 int 组成一个三角形。
            // 每个 cube 面由两个三角形组成，所以一个面 6 个索引，6 个面共 36 个索引。
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;

            // 重新计算法线。
            // 当前 decal shader 不一定使用 mesh normal，但给 mesh 补全 normal 是更规范的做法。
            mesh.RecalculateNormals();

            // 重新计算包围盒。
            // Unity 会用 bounds 做裁剪和可见性判断。
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}