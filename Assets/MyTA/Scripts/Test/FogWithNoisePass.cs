using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 自定义的 ScriptableRenderPass，用于实现基于噪声纹理的雾效。
/// 该 Pass 会在相机渲染完成后，通过全屏绘制将雾效叠加到场景颜色缓冲上。
/// </summary>
public class FogWithNoisePass : ScriptableRenderPass
{
    // 引用外部 Feature 中定义的设置参数（材质、密度、颜色、噪声纹理等）
    private readonly FogWithNoiseFeature.Settings settings;

    // 当前相机的颜色渲染目标（通常为 Camera 的 RenderTexture 或 Backbuffer）
    private RenderTargetIdentifier source;

    // 临时渲染纹理的句柄，用于在双缓冲 Blit 过程中暂存中间结果
    private RenderTargetHandle tempTextureHandle;

    /// <summary>
    /// 构造函数，接收雾效设置并初始化 Pass。
    /// </summary>
    /// <param name="settings">雾效参数配置</param>
    public FogWithNoisePass(FogWithNoiseFeature.Settings settings)
    {
        this.settings = settings;

        // 初始化临时纹理句柄，指定内部使用的名称 "_TempFogTexture"
        tempTextureHandle.Init("_TempFogTexture");

        // 关键：声明此 Pass 需要深度纹理和法线纹理。
        // URP 会在执行此 Pass 前，自动确保相机生成并提供了这些纹理。
        // 深度纹理用于重建世界坐标或计算雾的深度混合；
        // 法线纹理（可选）可用于更高级的雾效（例如基于法线方向的雾），但当前 Shader 未强制使用。
        // 注意：这里使用位或操作同时请求两种纹理。
        ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
    }

    /// <summary>
    /// 在相机设置阶段调用，用于获取当前相机的颜色渲染目标标识符。
    /// </summary>
    /// <param name="cmd">用于记录图形命令的 CommandBuffer</param>
    /// <param name="renderingData">当前帧的渲染数据，包含相机、剔除结果等信息</param>
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // 获取当前渲染器使用的相机颜色目标（即最终要输出到的 RenderTexture）
        source = renderingData.cameraData.renderer.cameraColorTarget;
    }

    /// <summary>
    /// 核心执行方法：在此方法中通过 CommandBuffer 绘制全屏雾效。
    /// </summary>
    /// <param name="context">渲染上下文，用于执行 CommandBuffer</param>
    /// <param name="renderingData">当前帧的渲染数据</param>
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // 创建一个新的 CommandBuffer 并命名，便于在 Profile 中识别
        CommandBuffer cmd = CommandBufferPool.Get("FogWithNoise");
        Camera camera = renderingData.cameraData.camera;

        // 计算相机的视锥体四个角的方向射线（在世界空间或相机空间，取决于 Shader 实现）
        // 这些射线用于在片元着色器中重建世界坐标或计算雾的深度衰减
        Matrix4x4 frustumCorners = GetFrustumCornersRay(camera);

        // 将所有雾效参数传递给材质
        settings.material.SetMatrix("_FrustumCornersRay", frustumCorners);
        settings.material.SetFloat("_FogDensity", settings.fogDensity);
        settings.material.SetColor("_FogColor", settings.fogColor);
        settings.material.SetFloat("_FogStart", settings.fogStart);
        settings.material.SetFloat("_FogEnd", settings.fogEnd);
        settings.material.SetTexture("_NoiseTexture", settings.noiseTexture);
        settings.material.SetFloat("_FogxSpeed", settings.fogxSpeed);
        settings.material.SetFloat("_FogySpeed", settings.fogySpeed);
        settings.material.SetFloat("_NoiseAmount", settings.noiseAmount);

        // 获取当前相机渲染目标的有效描述符（宽度、高度、格式等）
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        // 临时纹理不需要深度缓冲，因此将深度位设为 0
        desc.depthBufferBits = 0;
        // 申请一个临时渲染纹理，用于双缓冲 Blit 的第一步
        cmd.GetTemporaryRT(tempTextureHandle.id, desc);

        // 第一步：将源颜色纹理（相机原始渲染结果）通过材质中的 Shader 绘制到临时纹理上
        // 这一步实际上会执行 Shader 的 Fragment 程序，将雾效叠加到图像上
        Blit(cmd, source, tempTextureHandle.id, settings.material);

        // 第二步：将临时纹理（已叠加雾效）再绘制回相机最终的颜色目标
        Blit(cmd, tempTextureHandle.id, source);

        // 将 CommandBuffer 提交到渲染上下文执行
        context.ExecuteCommandBuffer(cmd);

        // 释放 CommandBuffer 回池，避免内存泄漏
        CommandBufferPool.Release(cmd);
    }

    /// <summary>
    /// 在相机清理阶段调用，释放本次 Pass 申请的临时渲染纹理。
    /// </summary>
    /// <param name="cmd">用于记录图形命令的 CommandBuffer</param>
    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // 释放临时纹理，以便其他 Pass 或后续帧重新使用该资源
        cmd.ReleaseTemporaryRT(tempTextureHandle.id);
    }

    /// <summary>
    /// 计算相机视锥体四个角的方向射线（世界空间，且经过远平面缩放）。
    /// 返回的 Matrix4x4 中，每一行存储一个角的方向向量，顺序为：
    /// row0: 左下角，row1: 右下角，row2: 右上角，row3: 左上角。
    /// 这种排列方式与全屏 Quad 的 UV 坐标顺序匹配，便于在 Shader 中插值。
    /// </summary>
    /// <param name="cam">场景相机</param>
    /// <returns>包含四个方向向量的矩阵</returns>
    private Matrix4x4 GetFrustumCornersRay(Camera cam)
    {
        Matrix4x4 frustumCorners = Matrix4x4.identity;
        Transform camTransform = cam.transform;

        // 获取相机的垂直视场角（度数）、近平面距离和宽高比
        float fov = cam.fieldOfView;
        float near = cam.nearClipPlane;
        float aspect = cam.aspect;

        // 计算在近平面上的半高和半宽（世界单位）
        float halfHeight = near * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        Vector3 toRight = camTransform.right * halfHeight * aspect; // 向右的水平向量
        Vector3 toTop = camTransform.up * halfHeight;               // 向上的垂直向量

        // 计算近平面四个角相对于相机位置的偏移向量（未归一化）
        Vector3 topLeft = camTransform.forward * near + toTop - toRight;
        Vector3 topRight = camTransform.forward * near + toRight + toTop;
        Vector3 bottomLeft = camTransform.forward * near - toTop - toRight;
        Vector3 bottomRight = camTransform.forward * near + toRight - toTop;

        // 计算缩放因子：使射线指向远平面（或者用于深度重建时的正确插值）
        // 这里取任意一个角（如 topLeft）的长度除以 near，得到缩放系数，
        // 使得射线长度与深度值成正比，便于在 Shader 中根据深度重建世界坐标。
        float scale = topLeft.magnitude / near;
        // 对四个方向向量进行归一化后，再乘以缩放因子，得到最终的射线方向
        topLeft.Normalize();    topLeft *= scale;
        topRight.Normalize();   topRight *= scale;
        bottomLeft.Normalize(); bottomLeft *= scale;
        bottomRight.Normalize(); bottomRight *= scale;

        // 将四个射线向量按特定顺序存入矩阵的行中
        // 顺序：左下、右下、右上、左上（与全屏 Quad 顶点 UV 顺序一致）
        frustumCorners.SetRow(0, bottomLeft);
        frustumCorners.SetRow(1, bottomRight);
        frustumCorners.SetRow(2, topRight);
        frustumCorners.SetRow(3, topLeft);

        return frustumCorners;
    }
}