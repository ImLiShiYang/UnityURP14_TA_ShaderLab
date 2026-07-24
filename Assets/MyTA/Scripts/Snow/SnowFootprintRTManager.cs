using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 雪地压痕 RT 管理器。
///
/// 数据协议：
/// R = sink，下陷强度，0 表示不下陷。
/// G = rim，雪边凸起强度，第一阶段可以一直为 0。
/// B = 预留。
/// A = brush mask。
///
/// 这个类只管理 RT、FootstepCamera、RT 滚动偏移和材质参数。
/// 真正把 Brush 渲染进 CurrentBrushRT，以及 CurrentBrushRT + AccumA -> AccumB，仍然交给 RenderFeature。
/// </summary>
public class SnowFootprintRTManager : MonoBehaviour
{
    public static SnowFootprintRTManager Active { get; private set; }

    [Header("Target")]
    [Tooltip("雪地 RT 跟随的目标，一般是玩家角色。")]
    public Transform target;

    [Tooltip("从上往下拍摄 Snow Brush 的相机。")]
    public Camera footstepCamera;

    [Header("Receiver")]
    [Tooltip("接收雪地压痕 RT 的雪面材质。")]
    public Material receiverMaterial;

    [Tooltip("可选：用于调试显示 RT 覆盖范围的平面。")]
    public Transform receiverPlane;

    [Header("Materials")]
    [Tooltip("CurrentBrushRT + AccumA -> AccumB 的累积材质。")]
    public Material accumulateMaterial;

    [Header("Height Smoothing")]
    [Tooltip("可选。为空时运行时自动创建 Hidden/Snow/SnowHeightBlur 材质。")]
    public Material heightBlurMaterial;

    [Tooltip("独立平滑 RT 的采样半径（像素）。不会写回历史 RT。")]
    [Range(0f, 8f)] public float heightBlurRadius = 3f;

    [Tooltip("独立平滑 RT 的混合强度。")]
    [Range(0f, 1f)] public float heightBlurStrength = 0.8f;

    [Header("Automatic Snow Rim")]
    [Tooltip("根据下陷区域的邻域自动生成被挤到两侧的雪脊。")]
    public bool generateAutoRim = true;

    [Tooltip("下陷雪量转移到外侧雪堆的比例。0 可完全关闭；建议从 0.2 到 0.4 开始。")]
    [Range(0f, 1f)] public float autoRimTransferRatio = 0.35f;

    [Tooltip("雪脊从下陷边界向外扩展的世界空间宽度（米）。")]
    [Range(0.02f, 1.5f)] public float autoRimWidth = 0.35f;

    [Tooltip("让雪脊左右强弱略有不同，避免完全对称。")]
    [Range(0f, 1f)] public float autoRimAsymmetry = 0.35f;

    [Tooltip("雪脊不对称噪声的世界空间频率。")]
    [Min(0.01f)] public float autoRimNoiseScale = 0.8f;

    [Header("RT Settings")]
    public int textureSize = 1024;

    [Tooltip("FootstepCamera 正交范围半径。实际世界覆盖范围是 radius * 2。")]
    public float radius = 8f;

    [Tooltip("FootstepCamera 位于 target 上方的高度。")]
    public float cameraHeight = 20f;

    [Header("Fade")]
    [Tooltip("0 = 永久保留雪痕；越大消失越快。厚雪轨迹第一阶段建议保持 0。")]
    public float reduceVal = 0f;

    [Tooltip("RT 边缘渐隐强度，避免滚动窗口边缘出现硬边。")]
    public float edgeSoftness = 25f;

    private RenderTexture currentBrushRT;
    private RenderTexture accumA;
    private RenderTexture accumB;
    private RenderTexture smoothTempRT;
    private RenderTexture smoothHeightRT;
    private RenderTexture rimBlurTempRT;
    private RenderTexture finalSmoothRT;
    private bool ownsHeightBlurMaterial;

    private Vector3 lastCenter;
    private bool initialized;

    private int stampVersion;
    private int consumedStampVersion;
    private Vector2 appliedOffsetThisFrame;

    public bool Initialized => initialized;
    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture AccumA => accumA;
    public RenderTexture AccumB => accumB;
    public RenderTexture SmoothHeightRT => finalSmoothRT != null ? finalSmoothRT : smoothHeightRT;
    public Material AccumulateMaterial => accumulateMaterial;
    public Camera FootstepCamera => footstepCamera;

    // 雪地高度 RT 不能用法线默认色 (0.5, 0.5, 1, 0)。
    // 因为 R 会被雪面 Shader 当成下陷强度。
    public Color ClearColor => SnowClearColor;
    private static readonly Color SnowClearColor = new Color(0f, 0f, 0f, 0f);

    private static readonly int LastTexID = Shader.PropertyToID("_LastTex");
    private static readonly int OffsetID = Shader.PropertyToID("_Offset");
    private static readonly int ReduceValID = Shader.PropertyToID("_ReduceVal");
    private static readonly int EdgeSoftnessID = Shader.PropertyToID("_EdgeSoftness");

    private static readonly int FootstepTexID = Shader.PropertyToID("_FootstepTex");
    private static readonly int FootstepRectID = Shader.PropertyToID("_FootstepRect");
    private static readonly int EnableFootstepID = Shader.PropertyToID("_EnableFootstep");
    private static readonly int SmoothFootstepTexID = Shader.PropertyToID("_SmoothFootstepTex");
    private static readonly int BlurRadiusID = Shader.PropertyToID("_BlurRadius");
    private static readonly int BlurStrengthID = Shader.PropertyToID("_BlurStrength");
    private static readonly int AutoRimStrengthID = Shader.PropertyToID("_AutoRimStrength");
    private static readonly int AutoRimRadiusID = Shader.PropertyToID("_AutoRimRadius");
    private static readonly int AutoRimAsymmetryID = Shader.PropertyToID("_AutoRimAsymmetry");
    private static readonly int AutoRimNoiseScaleID = Shader.PropertyToID("_AutoRimNoiseScale");
    private static readonly int FootstepWorldRectID = Shader.PropertyToID("_FootstepWorldRect");
    private static readonly int RawDepressionTexID = Shader.PropertyToID("_RawDepressionTex");
    private static readonly int BaseSmoothedTexID = Shader.PropertyToID("_BaseSmoothedTex");

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;

        ReleaseRTs();
    }

    private void Start()
    {
        Init();
    }

    private void Update()
    {
        if (!initialized || target == null || footstepCamera == null)
            return;

        UpdateFootstepCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();

        if (Input.GetKeyDown(KeyCode.C))
            ClearAllFootprints();
    }

    private void Init()
    {
        if (initialized)
            return;

        if (target == null || footstepCamera == null || accumulateMaterial == null)
        {
            Debug.LogError("[SnowFootprintRTManager] 参数没绑完整。");
            return;
        }

        currentBrushRT = CreateRT("Snow_CurrentBrush_RT");
        accumA = CreateRT("Snow_Accum_A");
        accumB = CreateRT("Snow_Accum_B");
        smoothTempRT = CreateRT("Snow_Smooth_Temp_RT");
        smoothHeightRT = CreateRT("Snow_Smooth_Height_RT");
        rimBlurTempRT = CreateRT("Snow_Rim_Blur_Temp_RT", RenderTextureFormat.RHalf);
        finalSmoothRT = smoothHeightRT;

        ClearRT(currentBrushRT, SnowClearColor);
        ClearRT(accumA, SnowClearColor);
        ClearRT(accumB, SnowClearColor);
        ClearRT(smoothTempRT, SnowClearColor);
        ClearRT(smoothHeightRT, SnowClearColor);
        ClearRT(rimBlurTempRT, SnowClearColor);

        EnsureHeightBlurMaterial();

        stampVersion = 0;
        consumedStampVersion = 0;
        appliedOffsetThisFrame = Vector2.zero;

        footstepCamera.clearFlags = CameraClearFlags.SolidColor;
        footstepCamera.backgroundColor = SnowClearColor;
        footstepCamera.targetTexture = currentBrushRT;
        footstepCamera.orthographic = true;
        footstepCamera.orthographicSize = radius;
        footstepCamera.aspect = 1f;

        lastCenter = target.position;

        UpdateFootstepCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();

        initialized = true;
    }

    private RenderTexture CreateRT(
        string rtName,
        RenderTextureFormat format = RenderTextureFormat.ARGBHalf)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(textureSize, textureSize)
        {
            depthBufferBits = 0,
            msaaSamples = 1,
            colorFormat = format,
            sRGB = false,
            useMipMap = false,
            autoGenerateMips = false
        };

        RenderTexture rt = new RenderTexture(desc);
        rt.name = rtName;
        rt.wrapMode = TextureWrapMode.Clamp;

        // 雪面位移和法线重建都要采样这张数据图。
        // Bilinear 会让压痕边缘更平滑。后续如果发现滚动窗口导致历史变糊，再改 Point 测试。
        rt.filterMode = FilterMode.Bilinear;

        rt.Create();
        return rt;
    }

    private void UpdateFootstepCamera()
    {
        Vector3 center = target.position;

        footstepCamera.transform.position = new Vector3(
            center.x,
            center.y + cameraHeight,
            center.z
        );

        footstepCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        footstepCamera.orthographic = true;
        footstepCamera.orthographicSize = radius;
        footstepCamera.aspect = 1f;
    }

    private void UpdateReceiverMaterial()
    {
        if (receiverMaterial == null)
            return;

        Vector3 center = lastCenter;

        Vector4 rect = new Vector4(
            center.x - radius,
            center.z - radius,
            center.x + radius,
            center.z + radius
        );

        receiverMaterial.SetVector(FootstepRectID, rect);
        receiverMaterial.SetTexture(FootstepTexID, accumA);
        receiverMaterial.SetTexture(
            SmoothFootstepTexID,
            finalSmoothRT != null ? finalSmoothRT : (smoothHeightRT != null ? smoothHeightRT : accumA));
        receiverMaterial.SetFloat(EnableFootstepID, 1f);
    }

    private void UpdateReceiverPlane()
    {
        if (receiverPlane == null || target == null)
            return;

        Vector3 center = target.position;

        receiverPlane.position = new Vector3(
            center.x,
            center.y + 0.03f,
            center.z
        );

        // Unity 默认 Plane 是 10x10，所以这里用 radius * 0.2f。
        receiverPlane.localScale = new Vector3(
            radius * 0.2f,
            1f,
            radius * 0.2f
        );
    }

    public void NotifyBrushSpawned()
    {
        stampVersion++;
    }

    public bool HasNewStamp()
    {
        return stampVersion != consumedStampVersion;
    }

    public void ConsumeStamp()
    {
        consumedStampVersion = stampVersion;
    }

    public bool ShouldAccumulateThisFrame()
    {
        Vector2 offset = GetCurrentOffset();

        bool hasOffset = offset.sqrMagnitude > 0.00000001f;
        bool hasFade = reduceVal > 0f;

        return HasNewStamp() || hasOffset || hasFade;
    }

    public Vector2 GetCurrentOffset()
    {
        if (target == null)
            return Vector2.zero;

        Vector3 center = target.position;
        float diameter = radius * 2f;

        Vector2 uvOffset = new Vector2(
            (lastCenter.x - center.x) / diameter,
            (lastCenter.z - center.z) / diameter
        );

        float texel = 1f / textureSize;

        uvOffset.x = Mathf.Round(uvOffset.x / texel) * texel;
        uvOffset.y = Mathf.Round(uvOffset.y / texel) * texel;

        return uvOffset;
    }

    public void SetupAccumulateMaterial()
    {
        if (accumulateMaterial == null)
            return;

        appliedOffsetThisFrame = GetCurrentOffset();

        accumulateMaterial.SetTexture(LastTexID, accumA);
        accumulateMaterial.SetVector(OffsetID, appliedOffsetThisFrame);
        accumulateMaterial.SetFloat(ReduceValID, reduceVal);
        accumulateMaterial.SetFloat(EdgeSoftnessID, edgeSoftness);
    }

    public void SwapAccumAfterRenderFeature()
    {
        RenderTexture temp = accumA;
        accumA = accumB;
        accumB = temp;

        if (target != null)
        {
            float diameter = radius * 2f;

            // GetCurrentOffset 做过 texel 对齐。
            // 因此 lastCenter 也必须按实际应用的 offset 更新，不能直接等于 target.position。
            lastCenter.x -= appliedOffsetThisFrame.x * diameter;
            lastCenter.z -= appliedOffsetThisFrame.y * diameter;
            lastCenter.y = target.position.y;
        }

        ConsumeStamp();
        UpdateReceiverMaterial();
    }

    public void BlurAccumulatedHeight(CommandBuffer cmd)
    {
        if (cmd == null || accumA == null || smoothHeightRT == null)
            return;

        EnsureHeightBlurMaterial();

        if (heightBlurMaterial == null || smoothTempRT == null)
        {
            cmd.Blit(accumA, smoothHeightRT);
            finalSmoothRT = smoothHeightRT;
            return;
        }

        heightBlurMaterial.SetFloat(BlurRadiusID, heightBlurRadius);
        heightBlurMaterial.SetFloat(BlurStrengthID, heightBlurStrength);
        heightBlurMaterial.SetFloat(
            AutoRimStrengthID,
            generateAutoRim ? autoRimTransferRatio : 0f);
        float worldDiameter = Mathf.Max(radius * 2f, 0.001f);
        float rimRadiusPixels = autoRimWidth * Mathf.Max(textureSize, 1) / worldDiameter;
        heightBlurMaterial.SetFloat(AutoRimRadiusID, Mathf.Clamp(rimRadiusPixels, 1f, 256f));
        heightBlurMaterial.SetFloat(AutoRimAsymmetryID, autoRimAsymmetry);
        heightBlurMaterial.SetFloat(AutoRimNoiseScaleID, autoRimNoiseScale);
        heightBlurMaterial.SetVector(
            FootstepWorldRectID,
            new Vector4(
                lastCenter.x - radius,
                lastCenter.z - radius,
                lastCenter.x + radius,
                lastCenter.z + radius));

        // Base displacement smoothing. This remains independent from the
        // accumulated history so repeated updates do not keep widening tracks.
        cmd.Blit(accumA, smoothTempRT, heightBlurMaterial, 0);
        cmd.Blit(smoothTempRT, smoothHeightRT, heightBlurMaterial, 1);

        if (!generateAutoRim ||
            autoRimTransferRatio <= 0.001f ||
            rimBlurTempRT == null)
        {
            finalSmoothRT = smoothHeightRT;
            return;
        }

        // Positive Gaussian residual:
        // mound = max(GaussianBlur(depression) - depression, 0).
        // A single-channel RHalf target keeps the extra 4096 RT affordable.
        cmd.Blit(accumA, rimBlurTempRT, heightBlurMaterial, 2);
        heightBlurMaterial.SetTexture(RawDepressionTexID, accumA);
        heightBlurMaterial.SetTexture(BaseSmoothedTexID, smoothHeightRT);
        cmd.Blit(rimBlurTempRT, smoothTempRT, heightBlurMaterial, 3);
        finalSmoothRT = smoothTempRT;
    }

    public void RefreshReceiverMaterial()
    {
        UpdateReceiverMaterial();
    }

    private void EnsureHeightBlurMaterial()
    {
        if (heightBlurMaterial != null)
            return;

        Shader blurShader = Shader.Find("Hidden/Snow/SnowHeightBlur");
        if (blurShader == null)
            return;

        heightBlurMaterial = new Material(blurShader)
        {
            name = "Runtime Snow Height Blur"
        };
        ownsHeightBlurMaterial = true;
    }

    public void ClearAllFootprints()
    {
        if (!initialized)
            return;

        ClearRT(currentBrushRT, SnowClearColor);
        ClearRT(accumA, SnowClearColor);
        ClearRT(accumB, SnowClearColor);
        ClearRT(smoothTempRT, SnowClearColor);
        ClearRT(smoothHeightRT, SnowClearColor);
        ClearRT(rimBlurTempRT, SnowClearColor);
        finalSmoothRT = smoothHeightRT;

        if (target != null)
            lastCenter = target.position;

        consumedStampVersion = stampVersion;
        appliedOffsetThisFrame = Vector2.zero;

        UpdateReceiverMaterial();
    }

    private void ClearRT(RenderTexture rt, Color clearColor)
    {
        if (rt == null)
            return;

        RenderTexture old = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = old;
    }

    private void ReleaseRTs()
    {
        if (receiverMaterial != null)
            receiverMaterial.SetFloat(EnableFootstepID, 0f);

        if (footstepCamera != null)
            footstepCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        ReleaseRT(accumA);
        ReleaseRT(accumB);
        ReleaseRT(smoothTempRT);
        ReleaseRT(smoothHeightRT);
        ReleaseRT(rimBlurTempRT);

        currentBrushRT = null;
        accumA = null;
        accumB = null;
        smoothTempRT = null;
        smoothHeightRT = null;
        rimBlurTempRT = null;
        finalSmoothRT = null;

        if (ownsHeightBlurMaterial && heightBlurMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(heightBlurMaterial);
            else
                DestroyImmediate(heightBlurMaterial);

            heightBlurMaterial = null;
            ownsHeightBlurMaterial = false;
        }

        initialized = false;
    }

    private void ReleaseRT(RenderTexture rt)
    {
        if (rt == null)
            return;

        rt.Release();

        if (Application.isPlaying)
            Destroy(rt);
        else
            DestroyImmediate(rt);
    }
}
