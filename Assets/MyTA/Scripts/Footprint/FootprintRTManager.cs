using UnityEngine;

/// <summary>
/// 脚印 RT 管理器。
/// 
/// 现在的职责：
/// 1. 创建 CurrentBrushRT / AccumA / AccumB。
/// 2. 更新 FootstepCamera 的位置、正交范围。
/// 3. 计算 FootstepRect。
/// 4. 给地面材质传 AccumA。
/// 5. 给 RenderFeature 提供 RT、参数和 Swap 方法。
/// 
/// 注意：
/// 不再使用 RenderPipelineManager.beginCameraRendering / endCameraRendering。
/// 真正的渲染和累积交给 FootprintRenderFeature 做。
/// </summary>
public class FootprintRTManager : MonoBehaviour
{
    public static FootprintRTManager Active { get; private set; }

    [Header("Target")]
    public Transform target;
    public Camera footstepCamera;

    [Header("Receiver")]
    public Material receiverMaterial;
    public Transform receiverPlane;

    [Header("Materials")]
    public Material accumulateMaterial;

    [Header("RT Settings")]
    public int textureSize = 1024;
    public float radius = 8f;
    public float cameraHeight = 20f;

    [Header("Fade")]
    [Tooltip("0 = 永久保留；越大消失越快")]
    public float reduceVal = 0.001f;

    [Tooltip("越大边缘过渡越窄")]
    public float edgeSoftness = 25f;

    private RenderTexture currentBrushRT;
    private RenderTexture accumA;
    private RenderTexture accumB;

    private Vector3 lastCenter;
    private bool initialized;

    public bool Initialized => initialized;

    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture AccumA => accumA;
    public RenderTexture AccumB => accumB;

    public Material AccumulateMaterial => accumulateMaterial;
    public Camera FootstepCamera => footstepCamera;

    public Color ClearColor => NormalClearColor;

    private static readonly Color NormalClearColor = new Color(0.5f, 0.5f, 1f, 0f);

    private static readonly int LastTexID = Shader.PropertyToID("_LastTex");
    private static readonly int OffsetID = Shader.PropertyToID("_Offset");
    private static readonly int ReduceValID = Shader.PropertyToID("_ReduceVal");
    private static readonly int EdgeSoftnessID = Shader.PropertyToID("_EdgeSoftness");

    private static readonly int FootstepTexID = Shader.PropertyToID("_FootstepTex");
    private static readonly int FootstepRectID = Shader.PropertyToID("_FootstepRect");
    private static readonly int EnableFootstepID = Shader.PropertyToID("_EnableFootstep");

    private int stampVersion;
    private int consumedStampVersion;

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
        {
            ClearAllFootprints();
        }
    }

    private void Init()
    {
        if (initialized)
            return;

        if (target == null || footstepCamera == null || accumulateMaterial == null)
        {
            Debug.LogError("[FootprintRTManager] 参数没绑完整。");
            return;
        }

        currentBrushRT = CreateRT("Footprint_CurrentBrush_RT");
        accumA = CreateRT("Footprint_Accum_A");
        accumB = CreateRT("Footprint_Accum_B");

        ClearRT(currentBrushRT, NormalClearColor);
        ClearRT(accumA, NormalClearColor);
        ClearRT(accumB, NormalClearColor);

        // FootstepCamera 仍然保留，用来触发 RenderFeature，并提供相机矩阵 / Culling。
        // 但真正写入 CurrentBrushRT 的结果由 RenderFeature 覆盖。
        footstepCamera.clearFlags = CameraClearFlags.SolidColor;
        footstepCamera.backgroundColor = NormalClearColor;
        footstepCamera.targetTexture = currentBrushRT;

        footstepCamera.orthographic = true;
        footstepCamera.orthographicSize = radius;
        footstepCamera.aspect = 1f;

        lastCenter = target.position;

        initialized = true;
    }

    private RenderTexture CreateRT(string rtName)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(textureSize, textureSize);
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.colorFormat = RenderTextureFormat.ARGBHalf;
        desc.sRGB = false;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        RenderTexture rt = new RenderTexture(desc);
        rt.name = rtName;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.filterMode = FilterMode.Point;
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

        Vector3 center = target.position;

        Vector4 rect = new Vector4(
            center.x - radius,
            center.z - radius,
            center.x + radius,
            center.z + radius
        );

        receiverMaterial.SetVector(FootstepRectID, rect);
        receiverMaterial.SetTexture(FootstepTexID, accumA);
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

        receiverPlane.localScale = new Vector3(
            radius * 0.2f,
            1f,
            radius * 0.2f
        );
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

        accumulateMaterial.SetTexture(LastTexID, accumA);
        accumulateMaterial.SetVector(OffsetID, GetCurrentOffset());
        accumulateMaterial.SetFloat(ReduceValID, reduceVal);
        accumulateMaterial.SetFloat(EdgeSoftnessID, edgeSoftness);
    }

    public void SwapAccumAfterRenderFeature()
    {
        RenderTexture temp = accumA;
        accumA = accumB;
        accumB = temp;

        if (target != null)
            lastCenter = target.position;

        // 交换后立刻把最新 accumA 传给地面材质。
        if (receiverMaterial != null)
        {
            receiverMaterial.SetTexture(FootstepTexID, accumA);
            receiverMaterial.SetFloat(EnableFootstepID, 1f);
        }
    }

    public void ClearAllFootprints()
    {
        if (!initialized)
            return;

        ClearRT(currentBrushRT, NormalClearColor);
        ClearRT(accumA, NormalClearColor);
        ClearRT(accumB, NormalClearColor);

        if (target != null)
            lastCenter = target.position;
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
        {
            receiverMaterial.SetFloat(EnableFootstepID, 0f);
        }

        if (footstepCamera != null)
            footstepCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        ReleaseRT(accumA);
        ReleaseRT(accumB);

        currentBrushRT = null;
        accumA = null;
        accumB = null;

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