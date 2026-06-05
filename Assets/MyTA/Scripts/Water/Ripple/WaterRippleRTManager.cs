using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 水波 RT 管理器。
///
/// 这个类不直接负责“画水波”，真正的绘制发生在 WaterRippleRenderFeature 里。
/// 这个类主要负责：
///
/// 1. 创建和维护三张 RenderTexture：
///    - CurrentBrushRT：当前帧临时水波图
///    - AccumA：当前正在使用的历史累积图
///    - AccumB：下一帧写入用的临时累积图
///
/// 2. 控制 WaterRippleCamera：
///    - 让它始终跟随角色
///    - 用正交相机从上往下看
///    - 为 RenderFeature 提供相机矩阵和 culling 结果
///
/// 3. 给地面材质传递水波数据：
///    - _WaterRippleTex：累积后的水波 RT，也就是 AccumA
///    - _WaterRippleRect：RT 对应的世界空间范围
///    - _EnableWaterRipple：是否启用水波
///
/// 4. 给累积 Shader 传递参数：
///    - _LastTex：上一帧累积结果
///    - _Offset：角色移动导致的 RT 空间偏移
///    - _ReduceVal：水波淡出速度
///    - _EdgeSoftness：RT 边缘渐隐范围
///
/// 5. 控制“是否需要累积这一帧”：
///    - 有新水波生成时，需要累积
///    - RT 中心发生移动时，需要累积
///    - reduceVal > 0 时，需要持续淡出
///
/// 注意：
/// 当前设计中，不再使用 RenderPipelineManager.beginCameraRendering / endCameraRendering。
/// 真正的 CurrentBrushRT 渲染和 CurrentBrushRT -> AccumA 累积，都交给 WaterRippleRenderFeature 做。
/// </summary>
[MovedFrom(false, null, null, "FootprintRTManager")]
public class WaterRippleRTManager : MonoBehaviour
{
    /// <summary>
    /// 当前场景里活动的 WaterRippleRTManager。
    /// WaterRippleRenderFeature 和 WaterRippleBrushSpawner 会通过它访问 RT 和通知新水波。
    /// </summary>
    public static WaterRippleRTManager Active { get; private set; }

    [Header("Target")]
    [Tooltip("水波系统跟随的目标，一般是玩家角色。")]
    public Transform target;

    [Tooltip("从上往下拍摄 WaterRippleBrush 的相机。这个相机主要用于触发 RenderFeature。")]
    [FormerlySerializedAs("footstepCamera")]
    public Camera waterRippleCamera;

    [Header("Receiver")]
    [Tooltip("接收水波 RT 的地面材质，通常使用 WaterRipple/InteractiveWaterRippleGround。")]
    public Material receiverMaterial;

    [Tooltip("可选：用于显示/同步水波影响范围的接收平面。")]
    public Transform receiverPlane;

    [Header("Materials")]
    [Tooltip("CurrentBrushRT + AccumA -> AccumB 的累积材质。Shader 一般是 Hidden/WaterRipple/WaterRippleAccumulate。")]
    public Material accumulateMaterial;
    
    [Tooltip("水波的波动方程材质")]
    public Material waveEquationMaterial;
    

    [Header("RT Settings")]
    [Tooltip("水波 RT 分辨率。越大越清晰，但成本越高。")]
    public int textureSize = 1024;

    [Tooltip("WaterRippleCamera 正交范围半径。实际覆盖世界范围是 radius * 2。")]
    public float radius = 8f;

    [Tooltip("WaterRippleCamera 位于 target 上方的高度。")]
    public float cameraHeight = 20f;

    [Header("Fade")]
    [Tooltip("水波每次累积时减少的 alpha/mask 值。0 = 永久保留；越大消失越快。")]
    public float reduceVal = 0.001f;

    [Tooltip("越大，RT 边缘渐隐区域越窄；越小，越早从边缘开始淡出。")]
    public float edgeSoftness = 25f;

    /// <summary>
    /// 当前帧临时水波 RT。
    ///
    /// 它只应该保存“这一帧还活着的 brush”。
    /// 它不是历史图，所以应该每帧清空成默认法线色。
    ///
    /// 协议：
    /// RGB = encoded normal，默认是 (0.5, 0.5, 1)
    /// A   = 当前帧水波 mask，默认是 0
    /// </summary>
    private RenderTexture currentBrushRT;

    /// <summary>
    /// 当前正在使用的历史累积 RT。
    ///
    /// 地面材质实际采样的是这张图。
    ///
    /// 协议：
    /// RGB = 累积后的 encoded normal
    /// A   = 累积后的水波 mask / depression
    /// </summary>
    private RenderTexture accumA;

    /// <summary>
    /// 累积过程中的写入目标。
    ///
    /// 每次累积时：
    /// CurrentBrushRT + AccumA -> AccumB
    ///
    /// 写完后交换：
    /// AccumA <-> AccumB
    /// </summary>
    private RenderTexture accumB;

    /// <summary>
    /// 上一次完成累积时，WaterRippleCamera 对应的中心点。
    ///
    /// 用它和当前 target.position 计算 _Offset，
    /// 让历史水波在 RT 中跟随玩家移动而滚动。
    /// </summary>
    private Vector3 lastCenter;

    /// <summary>
    /// 是否已经完成初始化，避免重复创建 RT。
    /// </summary>
    private bool initialized;

    public bool Initialized => initialized;

    public RenderTexture CurrentBrushRT => currentBrushRT;
    public RenderTexture AccumA => accumA;
    public RenderTexture AccumB => accumB;

    public Material AccumulateMaterial => accumulateMaterial;
    public Camera WaterRippleCamera => waterRippleCamera;
    public Material WaveEquationMaterial => waveEquationMaterial;
    

    [System.Obsolete("Use WaterRippleCamera instead. 保留它是为了兼容旧代码引用。")]
    public Camera FootstepCamera => waterRippleCamera;

    private RenderTexture[] m_renderTextures = new RenderTexture[3];
    public int m_textureIdx = 0;
    public int renderTexturesLength => m_renderTextures.Length;
    
    
    /// <summary>
    /// 给 RenderFeature 使用的清屏色。
    ///
    /// 非常重要：
    /// RGB = (0.5, 0.5, 1) 表示默认切线空间法线，也就是没有扰动。
    /// A   = 0 表示没有水波。
    ///
    /// 不能把 alpha 设成 1。
    /// 因为地面 shader 会把 alpha 当作水波 mask。
    /// </summary>
    public Color ClearColor => NormalClearColor;

    private static readonly Color NormalClearColor = new Color(0.5f, 0.5f, 1f, 0.5f);

    /// <summary>
    /// 累积 shader 参数 ID。
    /// 提前缓存 PropertyToID，避免每帧用字符串查找。
    /// </summary>
    private static readonly int LastTexID = Shader.PropertyToID("_LastTex");
    private static readonly int OffsetID = Shader.PropertyToID("_Offset");
    private static readonly int ReduceValID = Shader.PropertyToID("_ReduceVal");
    private static readonly int EdgeSoftnessID = Shader.PropertyToID("_EdgeSoftness");
    
    private static readonly int PrevTexID = Shader.PropertyToID("_PrevTex");
    private static readonly int PrevPrevTexID = Shader.PropertyToID("_PrevPrevTex");
    private static readonly int ParamID = Shader.PropertyToID("_Param");
    private static readonly int StrideID = Shader.PropertyToID("_Stride");
    

    /// <summary>
    /// 地面 shader 参数 ID。
    /// 对应 WaterRipple/InteractiveWaterRippleGround 里的属性。
    /// </summary>
    private static readonly int WaterRippleTexID = Shader.PropertyToID("_WaterRippleTex");
    private static readonly int WaterRippleRectID = Shader.PropertyToID("_WaterRippleRect");
    private static readonly int EnableWaterRippleID = Shader.PropertyToID("_EnableWaterRipple");

    /// <summary>
    /// stampVersion / consumedStampVersion 用来判断：
    /// “是否有新的水波需要被累积一次”。
    ///
    /// 为什么需要这个？
    ///
    /// 因为 WaterRippleRenderFeature 是每帧执行的。
    /// 如果每帧都把 CurrentBrushRT 累积进 AccumA，
    /// 那么同一个 brush 只要活了多帧，就会被重复累积多次。
    ///
    /// 这样会导致：
    /// - 人物站着不动时水波还在持续变强
    /// - 法线重复叠加
    /// - AccumA 变糊、变亮、变脏
    ///
    /// 所以我们让 WaterRippleBrushSpawner 每生成一个 brush，就调用 NotifyWaterRippleSpawned()。
    /// RenderFeature 只有在检测到新 stamp 时，才真正执行一次累积。
    /// </summary>
    private int stampVersion;
    private int consumedStampVersion;
    
    private Vector2 appliedOffsetThisFrame;
    
    private RenderTexture CurrentFrame
    {
        get { return m_renderTextures[m_textureIdx]; }
    }

    private RenderTexture PrevFrame
    {
        get { return m_renderTextures[(m_textureIdx + 2) % 3]; }
    }

    private RenderTexture PrevPrevFrame
    {
        get { return m_renderTextures[(m_textureIdx + 1) % 3]; }
    }
    
    public void AdvanceWaveFrame()
    {
        m_textureIdx = (m_textureIdx + 1) % m_renderTextures.Length;
    }
    
    public RenderTexture CurrentFrameRT => CurrentFrame;
    public RenderTexture PrevFrameRT => PrevFrame;
    public RenderTexture PrevPrevFrameRT => PrevPrevFrame;

    [Header("Wave Equation")]
    [Range(0.01f, 0.49f)]
    public float waveFactor = 0.25f;

    [Range(0.90f, 1.0f)]
    public float waveDecay = 0.995f;

    public float waveInputStrength = 1f;
    
    /// <summary>
    /// 由 WaterRippleBrushSpawner 在生成新 brush 时调用。
    ///
    /// 每调用一次，就代表“有一个新的水波事件需要累积”。
    /// </summary>
    public void NotifyWaterRippleSpawned()
    {
        stampVersion++;
    }

    [System.Obsolete("Use NotifyWaterRippleSpawned instead. 保留它是为了兼容旧代码引用。")]
    public void NotifyBrushSpawned()
    {
        NotifyWaterRippleSpawned();
    }

    /// <summary>
    /// 当前是否存在尚未被累积消费的新水波。
    /// </summary>
    public bool HasNewStamp()
    {
        return stampVersion != consumedStampVersion;
    }

    /// <summary>
    /// 在 RenderFeature 成功执行一次累积之后调用。
    ///
    /// 表示当前 stamp 已经写入 AccumA，
    /// 站着不动时不应该继续重复累积。
    /// </summary>
    public void ConsumeStamp()
    {
        consumedStampVersion = stampVersion;
    }

    /// <summary>
    /// 判断这一帧是否需要执行 CurrentBrushRT + AccumA -> AccumB。
    ///
    /// 需要累积的情况有三种：
    ///
    /// 1. 有新水波：
    ///    HasNewStamp() == true
    ///
    /// 2. RT 中心发生移动：
    ///    玩家移动后，WaterRippleCamera 中心变了。
    ///    需要通过 _Offset 把旧的 AccumA 内容滚动到新位置。
    ///
    /// 3. 需要淡出：
    ///    reduceVal > 0 时，即使没有新水波，也要每帧更新 alpha。
    ///
    /// 如果三者都没有，说明人物站着不动、没有新水波、也不需要淡出，
    /// 那么这一帧可以跳过累积，避免重复叠加。
    /// </summary>
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
        if (!initialized || target == null || waterRippleCamera == null)
            return;

        UpdateWaterRippleCamera();
        UpdateReceiverMaterial();
        UpdateReceiverPlane();

        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearAllWaterRipples();
        }
    }

    private void LateUpdate()
    {
        if (!initialized || target == null)
            return;

        Vector2 offset = GetCurrentOffset();

        // Debug.Log(
        //     $"target={target.position}, lastCenter={lastCenter}, offset={offset}, " +
        //     $"rect=({target.position.x - radius}, {target.position.z - radius}, {target.position.x + radius}, {target.position.z + radius})"
        // );
    }
    
    /// <summary>
    /// 初始化水波系统。
    ///
    /// 创建三张 RT：
    /// - CurrentBrushRT
    /// - AccumA
    /// - AccumB
    ///
    /// 并把它们全部清成默认法线 + alpha 0。
    /// </summary>
    private void Init()
    {
        if (initialized)
            return;
        

        if (target == null || waterRippleCamera == null || waveEquationMaterial == null)
        {
            Debug.LogError("[WaterRippleRTManager] 参数没绑完整：需要 target、waterRippleCamera 和 waveEquationMaterial。");
            return;
        }

        currentBrushRT = CreateRT("WaterRipple_CurrentBrush_RT");
        accumA = CreateRT("WaterRipple_Accum_A");
        accumB = CreateRT("WaterRipple_Accum_B");

        ClearRT(currentBrushRT, NormalClearColor);
        ClearRT(accumA, NormalClearColor);
        ClearRT(accumB, NormalClearColor);

        for (int i = 0; i < m_renderTextures.Length; i++)
        {
            m_renderTextures[i] = CreateRT("Surface Wave Height RT", i);
            ClearRT(m_renderTextures[i], NormalClearColor);
        }

        // 初始化时没有未消费的新水波。
        stampVersion = 0;
        consumedStampVersion = 0;

        // WaterRippleCamera 主要用于触发 RenderFeature，并提供相机矩阵 / Culling。
        // 真正写入 CurrentBrushRT 的内容由 WaterRippleRenderFeature 控制。
        waterRippleCamera.clearFlags = CameraClearFlags.SolidColor;
        waterRippleCamera.backgroundColor = NormalClearColor;
        waterRippleCamera.targetTexture = CurrentBrushRT;

        waterRippleCamera.orthographic = true;
        waterRippleCamera.orthographicSize = radius;
        waterRippleCamera.aspect = 1f;

        // lastCenter 表示“当前 AccumA 对应的世界中心”。
        lastCenter = target.position;

        initialized = true;
    }

    /// <summary>
    /// 创建一张用于水波系统的 RenderTexture。
    ///
    /// ARGBHalf：
    /// - 用半精度浮点保存 normal 和 mask，避免普通 8-bit 纹理精度不足。
    ///
    /// sRGB = false：
    /// - 这张 RT 存的是数据，不是颜色，不能做 sRGB 转换。
    ///
    /// FilterMode.Point：
    /// - 累积 RT 会不断重采样。
    /// - 如果用 Bilinear，历史水波在每次 _Offset 滚动时会越来越糊。
    /// - Point 可以减少反复重采样导致的模糊。
    /// </summary>
    private RenderTexture CreateRT(string rtName,int index=-1)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(textureSize, textureSize);
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.colorFormat = RenderTextureFormat.ARGBHalf;
        desc.sRGB = false;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        RenderTexture rt = new RenderTexture(desc);
        if (index == -1)
            rt.name = rtName;
        else
            rt.name = rtName + index;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.filterMode = FilterMode.Bilinear;
        rt.Create();

        return rt;
    }

    /// <summary>
    /// 让 WaterRippleCamera 跟随 target。
    ///
    /// 相机从角色上方往下看，正交范围为 radius。
    /// 所以它覆盖的世界区域大约是：
    /// 宽 = radius * 2
    /// 高 = radius * 2
    /// </summary>
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

    /// <summary>
    /// 把最新的 AccumA 和世界空间范围传给地面材质。
    ///
    /// _WaterRippleRect 的含义：
    /// x = min world x
    /// y = min world z
    /// z = max world x
    /// w = max world z
    ///
    /// 地面 shader 会用世界坐标 XZ 映射到这个 rect，
    /// 然后采样 _WaterRippleTex。
    /// </summary>
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

        receiverMaterial.SetVector(WaterRippleRectID, rect);
        // receiverMaterial.SetTexture(WaterRippleTexID, accumA);
        receiverMaterial.SetFloat(EnableWaterRippleID, 1f);
        receiverMaterial.SetTexture(WaterRippleTexID, PrevFrameRT);
    }

    /// <summary>
    /// 可选：同步 receiverPlane 的位置和大小。
    ///
    /// 这个 plane 通常用于调试可视化水波 RT 覆盖范围，
    /// 不一定是最终地形。
    /// </summary>
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

        // 这里假设 plane 默认尺寸是 10x10，所以用 radius * 0.2f。
        // 如果你的 plane 模型尺寸不同，这个比例可能需要调整。
        receiverPlane.localScale = new Vector3(
            radius * 0.2f,
            1f,
            radius * 0.2f
        );
    }

    /// <summary>
    /// 计算历史 AccumA 相对于当前 WaterRippleCamera 中心的 UV 偏移。
    ///
    /// 目的：
    /// 当玩家移动时，WaterRippleCamera 的世界中心也会移动。
    /// 但是 AccumA 里保存的是上一中心下的历史水波。
    /// 为了让历史水波在新 RT 中保持世界位置不变，
    /// 需要在累积 shader 中用 _Offset 对历史 RT 做滚动采样。
    ///
    /// 为什么要按 texel 对齐？
    ///
    /// 如果 _Offset 是任意小数 UV，历史 RT 每帧都会被小数偏移重采样。
    /// 即使用 Point，也容易出现抖动；如果用 Bilinear，更会导致越累积越模糊。
    ///
    /// 把 offset round 到 texel 网格，可以减少历史水波反复重采样导致的模糊。
    /// </summary>
    public Vector2 GetCurrentOffset()
    {
        if (target == null)
            return Vector2.zero;

        Vector3 center = target.position;
        float diameter = radius * 2f;

        Vector2 uvOffset = new Vector2((lastCenter.x - center.x) / diameter,(lastCenter.z - center.z) / diameter);

        float texel = 1f / textureSize;

        uvOffset.x = Mathf.Round(uvOffset.x / texel) * texel;
        uvOffset.y = Mathf.Round(uvOffset.y / texel) * texel;

        return uvOffset;
    }

    /// <summary>
    /// 给累积材质设置当前帧所需参数。
    ///
    /// _LastTex：
    ///     上一帧历史累积图，也就是当前 accumA。
    ///
    /// _Offset：
    ///     历史图相对当前 RT 中心的 UV 偏移。
    ///
    /// _ReduceVal：
    ///     每次累积时 alpha 减少多少。
    ///
    /// _EdgeSoftness：
    ///     接近 RT 边缘时，让水波逐渐消失，避免滚动时出现硬边。
    /// </summary>
    public void SetupAccumulateMaterial()
    {
        if (accumulateMaterial == null)
            return;
        
        appliedOffsetThisFrame = GetCurrentOffset();

        // Debug.Log(
        //     $"[SetupAccumulate] " +
        //     $"target=({target.position.x:F4}, {target.position.y:F4}, {target.position.z:F4}), " +
        //     $"lastCenter=({lastCenter.x:F4}, {lastCenter.y:F4}, {lastCenter.z:F4}), " +
        //     $"offset=({appliedOffsetThisFrame.x:F6}, {appliedOffsetThisFrame.y:F6})"
        // );

        accumulateMaterial.SetTexture(LastTexID, accumA);
        accumulateMaterial.SetVector(OffsetID, appliedOffsetThisFrame);
        accumulateMaterial.SetFloat(ReduceValID, reduceVal);
        accumulateMaterial.SetFloat(EdgeSoftnessID, edgeSoftness);
    }

    public void setWaterRippleEquationMaterial()
    {
        if (waveEquationMaterial == null)
            return;
        
        appliedOffsetThisFrame = GetCurrentOffset();
        
        waveEquationMaterial.SetTexture(PrevTexID, PrevFrameRT);
        waveEquationMaterial.SetTexture(PrevPrevTexID, PrevPrevFrameRT);
        waveEquationMaterial.SetVector(StrideID,new Vector4(1f / textureSize, 1f / textureSize, 0f, 0f));
        waveEquationMaterial.SetVector(ParamID,new Vector4(waveFactor, waveDecay,0f,0f));

    }


    /// <summary>
    /// 累积完成后交换 AccumA 和 AccumB。
    ///
    /// 执行前：
    /// AccumA = 旧历史
    /// AccumB = 新写入结果
    ///
    /// 执行后：
    /// AccumA = 新历史
    /// AccumB = 旧 RT，等待下一次写入
    ///
    /// 同时更新 lastCenter，表示 AccumA 现在已经对齐到当前 target 中心。
    /// </summary>
    public void SwapAccumAfterRenderFeature()
    {
        RenderTexture temp = accumA;
        accumA = accumB;
        accumB = temp;

        if (target != null)
        {
            float diameter = radius * 2f;

            // 关键：
            // _Offset = (lastCenter - currentCenter) / diameter
            //
            // shader 里用 lastUV = uv - _Offset。
            // 所以这次 AccumA 实际对齐到的新中心应该是：
            //
            // newCenter = oldCenter - _Offset * diameter
            //
            // 不能直接 lastCenter = target.position。
            // 否则如果 offset 被四舍五入成 0，AccumA 没滚动，
            // 但 lastCenter 却跳到了 target.position，
            // 地面上的水波就会跟着人物滑。
            lastCenter.x -= appliedOffsetThisFrame.x * diameter;
            lastCenter.z -= appliedOffsetThisFrame.y * diameter;
            lastCenter.y = target.position.y;
            
            // lastCenter = target.position;
        }


        // 交换后 AccumA 已经对齐到新的 lastCenter。
        // 所以这里要同步 _WaterRippleTex 和 _WaterRippleRect。
        UpdateReceiverMaterial();
    }
    
    public void waterRippleAfterRenderFeature()
    {

        if (target != null)
        {
            float diameter = radius * 2f;
            
            lastCenter.x -= appliedOffsetThisFrame.x * diameter;
            lastCenter.z -= appliedOffsetThisFrame.y * diameter;
            lastCenter.y = target.position.y;

        }

        UpdateReceiverMaterial();
    }

    /// <summary>
    /// 清空所有水波。
    ///
    /// 按 C 键会调用这里。
    ///
    /// 三张 RT 都恢复为：
    /// RGB = 默认法线
    /// A   = 0
    /// </summary>
    public void ClearAllWaterRipples()
    {
        if (!initialized)
            return;

        ClearRT(currentBrushRT, NormalClearColor);
        ClearRT(accumA, NormalClearColor);
        ClearRT(accumB, NormalClearColor);

        if (target != null)
            lastCenter = target.position;

        // 清空之后，不应该还保留“未消费的新水波”状态。
        consumedStampVersion = stampVersion;
    }

    [System.Obsolete("Use ClearAllWaterRipples instead. 保留它是为了兼容旧代码引用。")]
    public void ClearAllFootprints()
    {
        ClearAllWaterRipples();
    }

    /// <summary>
    /// 立即把指定 RenderTexture 清成指定颜色。
    ///
    /// 在水波系统里通常用于：
    /// 1. 初始化时清空 CurrentBrushRT / AccumA / AccumB。
    /// 2. 按 C 键清空所有历史水波。
    ///
    /// 注意：
    /// 这里用的是 GL.Clear，适合在普通 MonoBehaviour 逻辑里直接清 RT。
    /// 如果是在 ScriptableRenderPass / RenderFeature 里，每帧清 RT，建议用 CommandBuffer.ClearRenderTarget。
    /// </summary>
    private void ClearRT(RenderTexture rt, Color clearColor)
    {
        // 如果传进来的 RT 是空的，直接退出，避免后面访问空对象报错。
        if (rt == null)
            return;

        // 先保存 Unity 当前正在操作的 RenderTexture。
        //
        // RenderTexture.active 可以理解为：
        // “接下来 GL 操作会作用在哪一张 RT 上”。
        //
        // 因为我们这里只是临时清理某一张水波 RT，
        // 所以清理前要把原来的 active RT 记下来，
        // 清理完以后再恢复，避免影响 Unity 后续的其他渲染。
        RenderTexture old = RenderTexture.active;

        // 临时把当前操作目标切换成我们要清空的这张 RT。
        //
        // 例如：
        // ClearRT(accumA, NormalClearColor);
        //
        // 那这里就是让后面的 GL.Clear 清理 accumA。
        RenderTexture.active = rt;

        // 真正执行清屏。
        //
        // 第一个 true ：清理 depth。
        // 第二个 true ：清理 color。
        // clearColor ：要清成的颜色。
        //
        // 在水波 RT 里，clearColor 通常是：
        // RGB = (0.5, 0.5, 1.0)  默认法线
        // A   = 0                没有水波 mask
        //
        // 也就是：
        // new Color(0.5f, 0.5f, 1f, 0f)
        //
        // 你的 RT 创建时 depthBufferBits = 0，
        // 所以这里清 depth 实际影响不大，但保留 true 也没问题。
        GL.Clear(true, true, clearColor);

        // 清完以后，把 Unity 原本正在操作的 RT 恢复回来。
        //
        // 这一步很重要。
        // 如果不恢复，后面的渲染流程可能会继续把东西画到这张水波 RT 里，
        // 导致 CurrentBrushRT / AccumA 被意外污染。
        RenderTexture.active = old;
    }

    /// <summary>
    /// 释放所有 RT，并关闭地面水波效果。
    /// </summary>
    private void ReleaseRTs()
    {
        if (receiverMaterial != null)
        {
            receiverMaterial.SetFloat(EnableWaterRippleID, 0f);
        }

        if (waterRippleCamera != null)
            waterRippleCamera.targetTexture = null;

        ReleaseRT(currentBrushRT);
        ReleaseRT(accumA);
        ReleaseRT(accumB);

        ReleaseRT(CurrentFrame);
        ReleaseRT(PrevFrame);
        ReleaseRT(PrevPrevFrame);
        
        currentBrushRT = null;
        accumA = null;
        accumB = null;

        // CurrentFrame = null;
        // PrevFrame = null;

        initialized = false;
    }

    /// <summary>
    /// 安全释放单张 RenderTexture。
    /// </summary>
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
