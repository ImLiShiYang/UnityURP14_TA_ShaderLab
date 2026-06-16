using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Owns the render textures and shader parameters used by the interactive water ripple system.
/// The render feature draws one-frame brush stamps, then advances the three-buffer wave equation.
/// </summary>
public class WaterRippleRTManager : MonoBehaviour
{
    public static WaterRippleRTManager Active { get; private set; }

    [Header("Target")]
    [Tooltip("The moving object that the ripple capture area follows, usually the player.")]
    public Transform target;

    [Tooltip("Top-down orthographic camera used by the render feature to capture ripple brushes.")]
    public Camera waterRippleCamera;

    [Header("Receiver")]
    [Tooltip("Material that samples _WaterRippleTex, _WaterRippleRect and _EnableWaterRipple.")]
    public Material receiverMaterial;

    [Tooltip("Optional helper plane that shows the world area covered by the ripple texture.")]
    public Transform receiverPlane;

    [Header("Materials")]
    [Tooltip("Material using Custom/URP/waterripple_wave_equation.")]
    public Material waveEquationMaterial;

    [Header("RT Settings")]
    [Min(16)]
    public int textureSize = 1024;

    [Tooltip("Half size of the square world area covered by the ripple camera.")]
    public float radius = 8f;

    public float cameraHeight = 20f;

    [Header("Wave Equation")]
    [Range(0.01f, 0.49f)]
    public float waveFactor = 0.25f;

    [Range(0.90f, 1.0f)]
    public float waveDecay = 0.995f;

    [Tooltip("waveDecay is authored as the per-step decay at this frame rate, then converted by deltaTime at runtime.")]
    public float simulationReferenceFrameRate = 60f;

    [Tooltip("How many times per second the wave equation is advanced. This keeps the ripple simulation stable across Game view layouts and FPS.")]
    public float fixedSimulationRate = 180f;

    private RenderTexture currentBrushRT;
    private readonly RenderTexture[] waveFrames = new RenderTexture[3];
    private readonly Vector3[] waveFrameCenters = new Vector3[3];

    private int waveFrameIndex;
    private Vector3 lastAlignedCenter;
    private Vector3 alignedCenterThisFrame;
    private Vector2 appliedOffsetThisFrame;
    private Vector2 prevPrevOffsetThisFrame;
    private bool initialized;
    private float simulationAccumulator;

    public bool Initialized => initialized;
    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture CurrentFrameRT => waveFrames[CurrentFrameIndex];
    public RenderTexture PrevFrameRT => waveFrames[PrevFrameIndex];
    public RenderTexture PrevPrevFrameRT => waveFrames[PrevPrevFrameIndex];
    public Camera WaterRippleCamera => waterRippleCamera;
    public Material WaveEquationMaterial => waveEquationMaterial;
    public Color ClearColor => NormalClearColor;

    private static readonly Color NormalClearColor = new Color(0.5f, 0.5f, 1f, 0.5f);

    private static readonly int PrevTexID = Shader.PropertyToID("_PrevTex");
    private static readonly int PrevPrevTexID = Shader.PropertyToID("_PrevPrevTex");
    private static readonly int PrevOffsetID = Shader.PropertyToID("_PrevOffset");
    private static readonly int PrevPrevOffsetID = Shader.PropertyToID("_PrevPrevOffset");
    private static readonly int ParamID = Shader.PropertyToID("_Param");
    private static readonly int StrideID = Shader.PropertyToID("_Stride");

    private static readonly int WaterRippleTexID = Shader.PropertyToID("_WaterRippleTex");
    private static readonly int WaterRippleRectID = Shader.PropertyToID("_WaterRippleRect");
    private static readonly int EnableWaterRippleID = Shader.PropertyToID("_EnableWaterRipple");

    private int CurrentFrameIndex => waveFrameIndex;
    private int PrevFrameIndex => (waveFrameIndex + 2) % waveFrames.Length;
    private int PrevPrevFrameIndex => (waveFrameIndex + 1) % waveFrames.Length;

#if UNITY_EDITOR
    private const string DefaultWaveEquationMaterialPath = "Assets/MyTA/Materials/Water/Wave_equation.mat";
#endif

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
        if (!initialized || target == null || waterRippleCamera == null)
            return;

        UpdateWaterRippleCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();

        if (Input.GetKeyDown(KeyCode.C))
            ClearAllWaterRipples();
    }

    public void Init()
    {
        if (initialized)
            return;

        EnsureWaveEquationMaterial();

        if (target == null || waterRippleCamera == null || waveEquationMaterial == null)
        {
            Debug.LogError("[WaterRippleRTManager] Missing target, waterRippleCamera or waveEquationMaterial.", this);
            return;
        }

        currentBrushRT = CreateRT("WaterRipple_CurrentBrush_RT");
        ClearRT(currentBrushRT, NormalClearColor);

        for (int i = 0; i < waveFrames.Length; i++)
        {
            waveFrames[i] = CreateRT("WaterRipple_WaveFrame_RT_", i);
            ClearRT(waveFrames[i], NormalClearColor);
        }

        waterRippleCamera.clearFlags = CameraClearFlags.SolidColor;
        waterRippleCamera.backgroundColor = NormalClearColor;
        waterRippleCamera.targetTexture = currentBrushRT;
        waterRippleCamera.orthographic = true;
        waterRippleCamera.orthographicSize = radius;
        waterRippleCamera.aspect = 1f;

        lastAlignedCenter = target.position;
        alignedCenterThisFrame = lastAlignedCenter;
        appliedOffsetThisFrame = Vector2.zero;
        prevPrevOffsetThisFrame = Vector2.zero;

        for (int i = 0; i < waveFrameCenters.Length; i++)
            waveFrameCenters[i] = lastAlignedCenter;

        initialized = true;
        simulationAccumulator = 0f;

        UpdateWaterRippleCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();
    }

    public bool ShouldAdvanceWaveThisRender()
    {
        float interval = 1f / Mathf.Max(1f, fixedSimulationRate);
        simulationAccumulator += Time.deltaTime;

        if (simulationAccumulator < interval)
            return false;

        simulationAccumulator = Mathf.Repeat(simulationAccumulator, interval);
        return true;
    }

    public void SetupWaveEquationMaterial()
    {
        if (waveEquationMaterial == null)
            return;

        Vector3 targetCenter = target != null ? target.position : lastAlignedCenter;
        Vector3 prevCenter = waveFrameCenters[PrevFrameIndex];
        Vector3 prevPrevCenter = waveFrameCenters[PrevPrevFrameIndex];

        appliedOffsetThisFrame = GetSnappedOffset(prevCenter, targetCenter);
        alignedCenterThisFrame = GetAlignedCenter(prevCenter, targetCenter, appliedOffsetThisFrame);
        prevPrevOffsetThisFrame = GetSnappedOffset(prevPrevCenter, alignedCenterThisFrame);

        waveEquationMaterial.SetTexture(PrevTexID, PrevFrameRT);
        waveEquationMaterial.SetTexture(PrevPrevTexID, PrevPrevFrameRT);
        waveEquationMaterial.SetVector(PrevOffsetID, new Vector4(appliedOffsetThisFrame.x, appliedOffsetThisFrame.y, 0f, 0f));
        waveEquationMaterial.SetVector(PrevPrevOffsetID, new Vector4(prevPrevOffsetThisFrame.x, prevPrevOffsetThisFrame.y, 0f, 0f));
        waveEquationMaterial.SetVector(StrideID, new Vector4(1f / textureSize, 1f / textureSize, 0f, 0f));
        float simulationDeltaTime = 1f / Mathf.Max(1f, fixedSimulationRate);
        float decaySteps = simulationDeltaTime * Mathf.Max(1f, simulationReferenceFrameRate);
        float frameRateIndependentDecay = Mathf.Pow(waveDecay, decaySteps);
        waveEquationMaterial.SetVector(ParamID, new Vector4(waveFactor, frameRateIndependentDecay, 0f, 0f));
    }

    public void AdvanceWaveFrame()
    {
        waveFrameIndex = (waveFrameIndex + 1) % waveFrames.Length;
    }

    public void FinishWaveFrame()
    {
        int latestFrameIndex = PrevFrameIndex;
        waveFrameCenters[latestFrameIndex] = alignedCenterThisFrame;
        lastAlignedCenter = alignedCenterThisFrame;

        UpdateReceiverMaterial();
    }

    public void ClearAllWaterRipples()
    {
        if (!initialized)
            return;

        ClearRT(currentBrushRT, NormalClearColor);

        for (int i = 0; i < waveFrames.Length; i++)
            ClearRT(waveFrames[i], NormalClearColor);

        if (target != null)
        {
            lastAlignedCenter = target.position;
            alignedCenterThisFrame = lastAlignedCenter;

            for (int i = 0; i < waveFrameCenters.Length; i++)
                waveFrameCenters[i] = lastAlignedCenter;
        }

        appliedOffsetThisFrame = Vector2.zero;
        prevPrevOffsetThisFrame = Vector2.zero;
        simulationAccumulator = 0f;

        UpdateReceiverMaterial();
    }

    public Vector2 GetCurrentOffset()
    {
        if (target == null)
            return Vector2.zero;

        return GetSnappedOffset(lastAlignedCenter, target.position);
    }

    private Vector2 GetSnappedOffset(Vector3 historyCenter, Vector3 targetCenter)
    {
        float diameter = radius * 2f;
        if (diameter <= 0.0001f)
            return Vector2.zero;

        Vector2 uvOffset = new Vector2(
            (historyCenter.x - targetCenter.x) / diameter,
            (historyCenter.z - targetCenter.z) / diameter
        );

        float texel = 1f / textureSize;
        uvOffset.x = Mathf.Round(uvOffset.x / texel) * texel;
        uvOffset.y = Mathf.Round(uvOffset.y / texel) * texel;

        return uvOffset;
    }

    private Vector3 GetAlignedCenter(Vector3 historyCenter, Vector3 targetCenter, Vector2 appliedOffset)
    {
        float diameter = radius * 2f;

        return new Vector3(
            historyCenter.x - appliedOffset.x * diameter,
            targetCenter.y,
            historyCenter.z - appliedOffset.y * diameter
        );
    }

    private RenderTexture CreateRT(string rtName, int index = -1)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(textureSize, textureSize)
        {
            depthBufferBits = 0,
            msaaSamples = 1,
            colorFormat = RenderTextureFormat.ARGBHalf,
            sRGB = false,
            useMipMap = false,
            autoGenerateMips = false
        };

        RenderTexture rt = new RenderTexture(desc)
        {
            name = index < 0 ? rtName : rtName + index,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        rt.Create();
        return rt;
    }

    private void UpdateWaterRippleCamera()
    {
        Vector3 center = target.position;

        waterRippleCamera.transform.position = new Vector3(
            center.x,
            center.y + cameraHeight,
            center.z
        );

        waterRippleCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        waterRippleCamera.orthographic = true;
        waterRippleCamera.orthographicSize = radius;
        waterRippleCamera.aspect = 1f;
    }

    private void UpdateReceiverMaterial()
    {
        if (receiverMaterial == null)
            return;

        Vector4 rect = new Vector4(
            lastAlignedCenter.x - radius,
            lastAlignedCenter.z - radius,
            lastAlignedCenter.x + radius,
            lastAlignedCenter.z + radius
        );

        receiverMaterial.SetVector(WaterRippleRectID, rect);
        receiverMaterial.SetFloat(EnableWaterRippleID, 1f);
        receiverMaterial.SetTexture(WaterRippleTexID, PrevFrameRT);
    }

    private void UpdateReceiverPlane()
    {
        if (receiverPlane == null || target == null)
            return;

        Vector3 center = target.position;
        receiverPlane.position = new Vector3(center.x, center.y + 0.03f, center.z);
        receiverPlane.localScale = new Vector3(radius * 0.2f, 1f, radius * 0.2f);
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
            receiverMaterial.SetFloat(EnableWaterRippleID, 0f);

        if (waterRippleCamera != null)
            waterRippleCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        currentBrushRT = null;

        for (int i = 0; i < waveFrames.Length; i++)
        {
            ReleaseRT(waveFrames[i]);
            waveFrames[i] = null;
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

    private void EnsureWaveEquationMaterial()
    {
        if (waveEquationMaterial != null)
            return;

#if UNITY_EDITOR
        waveEquationMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultWaveEquationMaterialPath);
#endif
    }
}
