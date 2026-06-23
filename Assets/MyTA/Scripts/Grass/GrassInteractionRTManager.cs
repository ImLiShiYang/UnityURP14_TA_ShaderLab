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

    [Header("Two Feet Radial Press")]
    [Tooltip("脚骨骼与接地检测的数据源。为空时会从 target 自动查找。")]
    public GrassInteractionBrushSpawner footPressSource;

    [Min(0.01f)]
    public float leftPressRadius = 0.45f;

    [Min(0.01f)]
    public float rightPressRadius = 0.45f;

    [Header("Accumulation")]
    [Tooltip("用于合并当前 Brush、实时双脚和历史压草强度的 Shader。为空时使用 Shader.Find。")]
    public Shader accumulateShader;

    [Tooltip("脚离开后，草从完全压弯恢复到直立所需的秒数。0 表示永久保留。")]
    [Min(0f)]
    public float recoveryTime = 3f;

    [Tooltip("历史 RT 接近边缘时的淡出锐度。")]
    [Min(0.01f)]
    public float edgeSoftness = 25f;

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
    private RenderTexture accumA;
    private RenderTexture accumB;
    private Material accumulateMaterial;
    private Vector3 lastCenter;
    private Vector2 appliedOffsetThisFrame;
    private Vector3 leftPressCenterWS;
    private Vector3 rightPressCenterWS;
    private bool leftPressActive;
    private bool rightPressActive;
    private bool initialized;

    public bool Initialized => initialized;
    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture AccumA => accumA;
    public RenderTexture AccumB => accumB;
    public Material AccumulateMaterial => accumulateMaterial;
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

    private static readonly int PressCenter0WSID =
        Shader.PropertyToID("_PressCenter0WS");

    private static readonly int PressCenter1WSID =
        Shader.PropertyToID("_PressCenter1WS");

    private static readonly int EnablePressCenter0ID =
        Shader.PropertyToID("_EnablePressCenter0");

    private static readonly int EnablePressCenter1ID =
        Shader.PropertyToID("_EnablePressCenter1");

    private static readonly int PressRadius0ID =
        Shader.PropertyToID("_PressRadius0");

    private static readonly int PressRadius1ID =
        Shader.PropertyToID("_PressRadius1");

    private static readonly int LastTexID =
        Shader.PropertyToID("_LastTex");

    private static readonly int OffsetID =
        Shader.PropertyToID("_Offset");

    private static readonly int DecayAmountID =
        Shader.PropertyToID("_DecayAmount");

    private static readonly int EdgeSoftnessID =
        Shader.PropertyToID("_EdgeSoftness");

    private static readonly int InteractionRectID =
        Shader.PropertyToID("_InteractionRect");

    private static readonly int RadialMaskPowerID =
        Shader.PropertyToID("_RadialMaskPower");

    private static readonly int EnableRadialPressID =
        Shader.PropertyToID("_EnableRadialPress");

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

        if (footPressSource == null)
        {
            footPressSource = target.GetComponent<GrassInteractionBrushSpawner>();

            if (footPressSource == null)
                footPressSource = target.GetComponentInChildren<GrassInteractionBrushSpawner>();
        }

        Shader resolvedAccumulateShader = accumulateShader != null
            ? accumulateShader
            : Shader.Find("Hidden/Grass/InteractionAccumulate");

        if (resolvedAccumulateShader == null)
        {
            Debug.LogError("[GrassInteractionRTManager] Missing grass accumulate shader.", this);
            return;
        }

        accumulateMaterial = new Material(resolvedAccumulateShader)
        {
            name = "GrassInteraction_Accumulate_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };

        currentBrushRT = CreateRT("GrassInteraction_CurrentBrush_RT");
        accumA = CreateRT("GrassInteraction_Accum_A");
        accumB = CreateRT("GrassInteraction_Accum_B");
        ClearRT(currentBrushRT, ClearColor);
        ClearRT(accumA, ClearColor);
        ClearRT(accumB, ClearColor);

        lastCenter = target.position;

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
        ClearRT(accumA, ClearColor);
        ClearRT(accumB, ClearColor);

        if (target != null)
            lastCenter = target.position;

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
        // 没有草材质时，不需要给草物体设置高度轴参数
        if (grassMaterial == null)
            return;

        // 查找场景中所有 MeshRenderer，用来找到使用 grassMaterial 的草物体
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        foreach (MeshRenderer renderer in renderers)
        {
            // 只处理使用当前草材质的 Renderer，避免影响其他物体
            if (!UsesGrassMaterial(renderer))
                continue;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

            // 没有 Mesh 的物体无法判断本地高度范围
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            // 获取 Mesh 的本地空间包围盒。
            // 注意：这里用的是 sharedMesh.bounds，所以判断的是模型本地空间，不受物体旋转影响。
            Bounds bounds = meshFilter.sharedMesh.bounds;

            // 默认认为草沿本地 Y 轴生长
            Vector3 axis = Vector3.up;
            float minHeight = bounds.min.y;
            float maxHeight = bounds.max.y;

            // 自动判断 Mesh 哪个本地轴最长。
            // 最长的轴通常就是草片从根部到草尖的方向。
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

            // 使用 MaterialPropertyBlock 给当前 Renderer 单独设置高度轴和高度范围。
            // 这样即使多个草物体共用同一个材质，也可以拥有各自正确的 Mesh 高度参数。
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
        Vector3 center = lastCenter;

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
            grassMaterial.SetTexture(GrassInteractionTexID, accumA);
            grassMaterial.SetVector(GrassInteractionRectID, rect);
            grassMaterial.SetFloat(EnableGrassInteractionID, 1f);
            grassMaterial.SetVector(GrassBendDirWSID, new Vector4(bendDir.x, bendDir.y, bendDir.z, 0f));
        }

        UpdateFootPressMaterial();
    }

    private void UpdateFootPressMaterial()
    {
        leftPressCenterWS = Vector3.zero;
        rightPressCenterWS = Vector3.zero;

        leftPressActive = footPressSource != null &&
                          footPressSource.TryGetFootPressCenter(true, out leftPressCenterWS);

        rightPressActive = footPressSource != null &&
                           footPressSource.TryGetFootPressCenter(false, out rightPressCenterWS);

        if (grassMaterial == null)
            return;

        grassMaterial.SetVector(
            PressCenter0WSID,
            new Vector4(leftPressCenterWS.x, leftPressCenterWS.y, leftPressCenterWS.z, 0f)
        );

        grassMaterial.SetVector(
            PressCenter1WSID,
            new Vector4(rightPressCenterWS.x, rightPressCenterWS.y, rightPressCenterWS.z, 0f)
        );

        grassMaterial.SetFloat(EnablePressCenter0ID, leftPressActive ? 1f : 0f);
        grassMaterial.SetFloat(EnablePressCenter1ID, rightPressActive ? 1f : 0f);
        grassMaterial.SetFloat(PressRadius0ID, Mathf.Max(0.01f, leftPressRadius));
        grassMaterial.SetFloat(PressRadius1ID, Mathf.Max(0.01f, rightPressRadius));
    }

    public void SetupAccumulateMaterial()
    {
        // 累积材质或跟随目标不存在时，无法更新历史压草 RT
        if (accumulateMaterial == null || target == null)
            return;

        // 计算当前帧交互区域相对上一帧移动了多少 UV。
        // 因为顶部相机跟着角色走，如果不把历史 RT 做偏移，
        // 走过的压草痕迹就会像贴在相机上一样滑动。
        appliedOffsetThisFrame = GetCurrentOffset();

        // 计算本帧恢复量。
        // recoveryTime 表示从完全压弯恢复到直立需要的时间。
        // recoveryTime = 3 时，每秒大约恢复 1/3。
        // recoveryTime = 0 时，decayAmount = 0，表示历史压痕不自动恢复。
        float decayAmount = recoveryTime > 0.0001f
            ? Time.deltaTime / recoveryTime
            : 0f;

        // 根据本帧实际应用的 RT 偏移，计算累积 Shader 当前对应的世界空间范围。
        // 这个 rect 会传给累积 Shader，用于把脚的世界坐标映射到 RT UV。
        float diameter = radius * 2f;
        Vector3 center = lastCenter;
        center.x -= appliedOffsetThisFrame.x * diameter;
        center.z -= appliedOffsetThisFrame.y * diameter;

        Vector4 rect = new Vector4(
            center.x - radius,
            center.z - radius,
            center.x + radius,
            center.z + radius
        );

        // 从草材质读取径向压草衰减参数。
        // 这样累积 Shader 和草 Shader 使用同一套压草半径衰减曲线。
        // 如果材质上没有这个参数，就使用默认值。
        float radialMaskPower = grassMaterial != null && grassMaterial.HasProperty(RadialMaskPowerID)
            ? grassMaterial.GetFloat(RadialMaskPowerID)
            : 1.2f;

        // 从草材质读取是否启用实时脚部径向压草。
        // 这样可以在草材质面板里统一控制脚部压草开关。
        float enableRadialPress = grassMaterial != null && grassMaterial.HasProperty(EnableRadialPressID)
            ? grassMaterial.GetFloat(EnableRadialPressID)
            : 1f;

        // 上一帧的历史压草 RT。
        // 累积 Shader 会在它的基础上叠加当前 Brush、脚部实时压草和恢复衰减。
        accumulateMaterial.SetTexture(LastTexID, accumA);

        // 历史 RT 的 UV 偏移。
        // 用来抵消顶部相机跟随角色移动造成的 RT 内容偏移。
        accumulateMaterial.SetVector(OffsetID, appliedOffsetThisFrame);

        // 本帧恢复量，累积 Shader 会用它让历史压痕逐渐变淡。
        accumulateMaterial.SetFloat(DecayAmountID, decayAmount);

        // 交互区域靠近边缘时的淡出强度。
        // 防止历史压痕在 RT 边缘出现硬切。
        accumulateMaterial.SetFloat(EdgeSoftnessID, edgeSoftness);

        // 当前累积 RT 对应的世界空间 XZ 范围。
        // xy = minX, minZ
        // zw = maxX, maxZ
        accumulateMaterial.SetVector(InteractionRectID, rect);

        // 左脚压草中心，世界空间。
        accumulateMaterial.SetVector(
            PressCenter0WSID,
            new Vector4(leftPressCenterWS.x, leftPressCenterWS.y, leftPressCenterWS.z, 0f)
        );

        // 右脚压草中心，世界空间。
        accumulateMaterial.SetVector(
            PressCenter1WSID,
            new Vector4(rightPressCenterWS.x, rightPressCenterWS.y, rightPressCenterWS.z, 0f)
        );

        // 左右脚是否当前有效。
        // 如果某只脚没有检测到可用位置，就不参与实时径向压草。
        accumulateMaterial.SetFloat(EnablePressCenter0ID, leftPressActive ? 1f : 0f);
        accumulateMaterial.SetFloat(EnablePressCenter1ID, rightPressActive ? 1f : 0f);

        // 左右脚压草半径。
        // 最小限制到 0.01，避免半径过小导致除零或异常。
        accumulateMaterial.SetFloat(PressRadius0ID, Mathf.Max(0.01f, leftPressRadius));
        accumulateMaterial.SetFloat(PressRadius1ID, Mathf.Max(0.01f, rightPressRadius));

        // 径向压草的衰减曲线。
        // 数值越大，压草影响越集中在脚附近。
        accumulateMaterial.SetFloat(RadialMaskPowerID, radialMaskPower);

        // 是否启用脚部实时径向压草。
        accumulateMaterial.SetFloat(EnableRadialPressID, enableRadialPress);
    }

    public void SwapAccumAfterRenderFeature()
    {
        RenderTexture temp = accumA;
        accumA = accumB;
        accumB = temp;

        if (target != null)
        {
            float diameter = radius * 2f;
            lastCenter.x -= appliedOffsetThisFrame.x * diameter;
            lastCenter.z -= appliedOffsetThisFrame.y * diameter;
            lastCenter.y = target.position.y;
        }

        if (grassMaterial != null)
        {
            Vector4 rect = new Vector4(
                lastCenter.x - radius,
                lastCenter.z - radius,
                lastCenter.x + radius,
                lastCenter.z + radius
            );

            grassMaterial.SetTexture(GrassInteractionTexID, accumA);
            grassMaterial.SetVector(GrassInteractionRectID, rect);
        }
    }

    private Vector2 GetCurrentOffset()
    {
        if (target == null || textureSize <= 0)
            return Vector2.zero;

        Vector3 center = target.position;
        float diameter = Mathf.Max(radius * 2f, 0.0001f);

        Vector2 uvOffset = new Vector2(
            (lastCenter.x - center.x) / diameter,
            (lastCenter.z - center.z) / diameter
        );

        float texel = 1f / textureSize;
        uvOffset.x = Mathf.Round(uvOffset.x / texel) * texel;
        uvOffset.y = Mathf.Round(uvOffset.y / texel) * texel;

        return uvOffset;
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

        if (grassMaterial != null)
        {
            grassMaterial.SetFloat(EnableGrassInteractionID, 0f);
            grassMaterial.SetFloat(EnablePressCenter0ID, 0f);
            grassMaterial.SetFloat(EnablePressCenter1ID, 0f);
        }

        if (grassInteractionCamera != null)
            grassInteractionCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        ReleaseRT(accumA);
        ReleaseRT(accumB);
        currentBrushRT = null;
        accumA = null;
        accumB = null;

        if (accumulateMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(accumulateMaterial);
            else
                DestroyImmediate(accumulateMaterial);

            accumulateMaterial = null;
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
