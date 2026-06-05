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

    private RenderTexture currentBrushRT;
    private readonly RenderTexture[] waveFrames = new RenderTexture[3];

    private int waveFrameIndex;
    private Vector3 lastAlignedCenter;
    private Vector2 appliedOffsetThisFrame;
    private bool initialized;

    public bool Initialized => initialized;
    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture CurrentFrameRT => waveFrames[waveFrameIndex];
    public RenderTexture PrevFrameRT => waveFrames[(waveFrameIndex + 2) % waveFrames.Length];
    public RenderTexture PrevPrevFrameRT => waveFrames[(waveFrameIndex + 1) % waveFrames.Length];
    public Camera WaterRippleCamera => waterRippleCamera;
    public Material WaveEquationMaterial => waveEquationMaterial;
    public Color ClearColor => NormalClearColor;

    private static readonly Color NormalClearColor = new Color(0.5f, 0.5f, 1f, 0.5f);

    private static readonly int PrevTexID = Shader.PropertyToID("_PrevTex");
    private static readonly int PrevPrevTexID = Shader.PropertyToID("_PrevPrevTex");
    private static readonly int ParamID = Shader.PropertyToID("_Param");
    private static readonly int StrideID = Shader.PropertyToID("_Stride");

    private static readonly int WaterRippleTexID = Shader.PropertyToID("_WaterRippleTex");
    private static readonly int WaterRippleRectID = Shader.PropertyToID("_WaterRippleRect");
    private static readonly int EnableWaterRippleID = Shader.PropertyToID("_EnableWaterRipple");

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
        initialized = true;

        UpdateWaterRippleCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();
    }

    public void SetupWaveEquationMaterial()
    {
        if (waveEquationMaterial == null)
            return;

        appliedOffsetThisFrame = GetCurrentOffset();

        waveEquationMaterial.SetTexture(PrevTexID, PrevFrameRT);
        waveEquationMaterial.SetTexture(PrevPrevTexID, PrevPrevFrameRT);
        waveEquationMaterial.SetVector(StrideID, new Vector4(1f / textureSize, 1f / textureSize, 0f, 0f));
        waveEquationMaterial.SetVector(ParamID, new Vector4(waveFactor, waveDecay, 0f, 0f));
    }

    public void AdvanceWaveFrame()
    {
        waveFrameIndex = (waveFrameIndex + 1) % waveFrames.Length;
    }

    public void FinishWaveFrame()
    {
        if (target != null)
        {
            float diameter = radius * 2f;
            lastAlignedCenter.x -= appliedOffsetThisFrame.x * diameter;
            lastAlignedCenter.z -= appliedOffsetThisFrame.y * diameter;
            lastAlignedCenter.y = target.position.y;
        }

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
            lastAlignedCenter = target.position;

        UpdateReceiverMaterial();
    }

    public Vector2 GetCurrentOffset()
    {
        if (target == null)
            return Vector2.zero;

        float diameter = radius * 2f;
        Vector3 center = target.position;

        Vector2 uvOffset = new Vector2(
            (lastAlignedCenter.x - center.x) / diameter,
            (lastAlignedCenter.z - center.z) / diameter
        );

        float texel = 1f / textureSize;
        uvOffset.x = Mathf.Round(uvOffset.x / texel) * texel;
        uvOffset.y = Mathf.Round(uvOffset.y / texel) * texel;

        return uvOffset;
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
