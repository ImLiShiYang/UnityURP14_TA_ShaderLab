using UnityEngine;

/// <summary>
/// 草地交互 RT 管理器。
///
/// 第一阶段只负责三件事：
/// 1. 创建 GrassInteraction_CurrentBrush_RT。
/// 2. 控制顶部正交相机跟随 target。
/// 3. 把当前 RT 和世界空间范围传给后续草 Shader。
///
/// 注意：
/// 这个版本暂时不做草弯曲、不做恢复、不做 GPU Instancing。
/// 目标只是先确认 GrassInteractionRT 里能看到 Brush。
/// </summary>
public class GrassInteractionRTManager : MonoBehaviour
{
    public static GrassInteractionRTManager Active { get; private set; }

    [Header("Target")]
    [Tooltip("交互区域跟随的目标，一般是 Player 根物体。")]
    public Transform target;

    [Tooltip("从上往下拍 Brush 的正交相机。")]
    public Camera grassInteractionCamera;

    [Header("Grass Material")]
    public Material grassMaterial;

    [Tooltip("可选：显示交互区域大小的辅助平面。第一步可以先不填。")]
    public Transform debugReceiverPlane;

    [Header("RT Settings")]
    [Min(16)]
    public int textureSize = 512;

    [Tooltip("交互相机覆盖范围的一半。radius=8 表示覆盖 16m x 16m。")]
    public float radius = 8f;

    [Tooltip("交互相机离角色多高。")]
    public float cameraHeight = 20f;

    private RenderTexture currentBrushRT;
    private bool initialized;

    public bool Initialized => initialized;
    public RenderTexture CurrentBrushRT => currentBrushRT;
    public Camera GrassInteractionCamera => grassInteractionCamera;
    public Color ClearColor => Color.black;

    private static readonly int GrassInteractionTexID =
        Shader.PropertyToID("_GrassInteractionTex");

    private static readonly int GrassInteractionRectID =
        Shader.PropertyToID("_GrassInteractionRect");

    private static readonly int EnableGrassInteractionID =
        Shader.PropertyToID("_EnableGrassInteraction");
    
    private static readonly int GrassBendDirWSID =
        Shader.PropertyToID("_GrassBendDirWS");

    private static readonly int GrassHeightAxisOSID =
        Shader.PropertyToID("_GrassHeightAxisOS");

    private static readonly int GrassHeightMinOSID =
        Shader.PropertyToID("_GrassHeightMinOS");

    private static readonly int GrassHeightMaxOSID =
        Shader.PropertyToID("_GrassHeightMaxOS");

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
        if (!initialized || target == null || grassInteractionCamera == null)
            return;

        UpdateGrassInteractionCamera();
        UpdateDebugReceiverMaterial();
        UpdateDebugReceiverPlane();

        if (Input.GetKeyDown(KeyCode.C))
            ClearGrassInteractionRT();
    }

    public void Init()
    {
        if (initialized)
            return;

        if (target == null || grassInteractionCamera == null)
        {
            Debug.LogError("[GrassInteractionRTManager] Missing target or grassInteractionCamera.", this);
            return;
        }

        currentBrushRT = CreateRT("GrassInteraction_CurrentBrush_RT");
        ClearRT(currentBrushRT, ClearColor);

        grassInteractionCamera.clearFlags = CameraClearFlags.SolidColor;
        grassInteractionCamera.backgroundColor = ClearColor;
        grassInteractionCamera.targetTexture = currentBrushRT;
        grassInteractionCamera.orthographic = true;
        grassInteractionCamera.orthographicSize = radius;
        grassInteractionCamera.aspect = 1f;

        initialized = true;

        ApplyGrassMeshHeightBounds();

        UpdateGrassInteractionCamera();
        UpdateDebugReceiverMaterial();
        UpdateDebugReceiverPlane();
    }

    public void ClearGrassInteractionRT()
    {
        if (!initialized)
            return;

        ClearRT(currentBrushRT, ClearColor);
        UpdateDebugReceiverMaterial();
    }

    private RenderTexture CreateRT(string rtName)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(textureSize, textureSize)
        {
            depthBufferBits = 0,
            msaaSamples = 1,
            colorFormat = RenderTextureFormat.ARGB32,
            sRGB = false,
            useMipMap = false,
            autoGenerateMips = false
        };

        RenderTexture rt = new RenderTexture(desc)
        {
            name = rtName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        rt.Create();
        return rt;
    }

    private void UpdateGrassInteractionCamera()
    {
        Vector3 center = target.position;

        grassInteractionCamera.transform.position = new Vector3(
            center.x,
            center.y + cameraHeight,
            center.z
        );

        // 和你的 WaterRippleRTManager 一样，从上往下拍。
        grassInteractionCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        grassInteractionCamera.orthographic = true;
        grassInteractionCamera.orthographicSize = radius;
        grassInteractionCamera.aspect = 1f;
    }

    private void ApplyGrassMeshHeightBounds()
    {
        if (grassMaterial == null)
            return;

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        foreach (MeshRenderer renderer in renderers)
        {
            if (!UsesGrassMaterial(renderer))
                continue;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            Bounds bounds = meshFilter.sharedMesh.bounds;
            Vector3 axis = Vector3.up;
            float minHeight = bounds.min.y;
            float maxHeight = bounds.max.y;

            if (bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z)
            {
                axis = Vector3.right;
                minHeight = bounds.min.x;
                maxHeight = bounds.max.x;
            }
            else if (bounds.size.z >= bounds.size.y)
            {
                axis = Vector3.forward;
                minHeight = bounds.min.z;
                maxHeight = bounds.max.z;
            }

            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(GrassHeightAxisOSID, axis);
            propertyBlock.SetFloat(GrassHeightMinOSID, minHeight);
            propertyBlock.SetFloat(GrassHeightMaxOSID, maxHeight);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private bool UsesGrassMaterial(Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;

        foreach (Material material in materials)
        {
            if (material == grassMaterial)
                return true;
        }

        return false;
    }

    private void UpdateDebugReceiverMaterial()
    {
        Vector3 center = target.position;

        Vector4 rect = new Vector4(
            center.x - radius,
            center.z - radius,
            center.x + radius,
            center.z + radius
        );
        
        
        Vector3 bendDir = target.forward;
        bendDir.y = 0f;

        if (bendDir.sqrMagnitude < 0.0001f)
            bendDir = Vector3.forward;

        bendDir.Normalize();


        // 可选：如果你做了一个调试材质，也同步给它。
        if (grassMaterial != null)
        {
            grassMaterial.SetTexture(GrassInteractionTexID, currentBrushRT);
            grassMaterial.SetVector(GrassInteractionRectID, rect);
            grassMaterial.SetFloat(EnableGrassInteractionID, 1f);
            grassMaterial.SetVector(GrassBendDirWSID, new Vector4(bendDir.x, bendDir.y, bendDir.z, 0f));
        }
    }

    private void UpdateDebugReceiverPlane()
    {
        if (debugReceiverPlane == null || target == null)
            return;

        Vector3 center = target.position;

        debugReceiverPlane.position = new Vector3(
            center.x,
            center.y + 0.03f,
            center.z
        );

        // Unity Plane 默认 10m，所以这里和你水波的 receiverPlane 缩放逻辑一致。
        debugReceiverPlane.localScale = new Vector3(radius * 0.2f, 1f, radius * 0.2f);
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
        Shader.SetGlobalFloat(EnableGrassInteractionID, 0f);

        if (grassInteractionCamera != null)
            grassInteractionCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        currentBrushRT = null;

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
