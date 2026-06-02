using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// URP 14 版本的交互水波脚本。
///
/// 这个版本不使用 Built-in 管线的 OnPreRender()，
/// 而是使用 SRP/URP 的 RenderPipelineManager.beginCameraRendering 事件。
///
/// 工作流程：
/// 1. 外部点击/碰撞调用 OnClickDown 或 AddRippleWorld。
/// 2. 脚本把扰动写入 m_inputTexture。
/// 3. URP 准备渲染 Camera 前触发 OnBeginCameraRendering。
/// 4. UpdateWaveTexture() 使用 wave_equation 材质计算新一帧水波。
/// 5. 把当前 RenderTexture 设置给水面材质的 _WaveTex。
/// </summary>
public class SurfaceWave : MonoBehaviour
{
    [Header("水面对象")]
    [Tooltip("水面物体上的 Mesh Renderer。计算出的 _WaveTex 会设置到这个 Renderer 的材质上。")]
    [SerializeField] private Renderer m_targetRenderer;

    [Tooltip("负责计算水波扩散的材质。Shader 应该使用 Custom/URP/wave_equation。")]
    [SerializeField] private Material m_equationMaterial;

    [Header("更新相机")]
    [Tooltip("只在这个 Camera 渲染前更新水波。为空时会自动使用 Camera.main。")]
    [SerializeField] private Camera m_updateCamera;

    [Tooltip("一帧内只更新一次水波。多相机项目建议开启，避免水波被重复迭代。")]
    [SerializeField] private bool m_updateOncePerFrame = true;

    [Header("输入波源贴图")]
    [Tooltip("输入扰动贴图宽度。只记录哪里产生了波源，不是最终水波分辨率。")]
    [SerializeField] private int m_inputTextureWidth = 64;

    [Tooltip("输入扰动贴图高度。")]
    [SerializeField] private int m_inputTextureHeight = 64;

    [Header("水波模拟贴图")]
    [Tooltip("水波高度图宽度。越高越细腻，但性能开销越大。")]
    [SerializeField] private int m_textureWidth = 256;

    [Tooltip("水波高度图高度。越高越细腻，但性能开销越大。")]
    [SerializeField] private int m_textureHeight = 256;

    [Header("水面尺寸")]
    [Tooltip("水面网格的实际尺寸。需要和 ProceduralGrid 的 size 保持一致。")]
    [SerializeField] private Vector2 m_meshSize = new Vector2(100, 100);

    [Tooltip("如果你的水面网格在本地 XZ 平面上，打开它；如果是 Demo 里的本地 XY 平面，保持关闭。")]
    [SerializeField] private bool m_useXZPlane = false;

    [Header("水波参数")]
    [Tooltip("扩散强度。越大传播越明显，太大可能抖动或炸开。")]
    [Range(0.01f, 0.49f)]
    [SerializeField] private float m_waveFactor = 0.25f;

    [Tooltip("衰减系数。越接近 1，波纹持续越久。")]
    [Range(0.90f, 1.0f)]
    [SerializeField] private float m_decay = 0.995f;

    [Tooltip("旧版项目的舍入修正。URP/Linear RenderTexture 下建议保持 0。")]
    [SerializeField] private float m_roundAdjuster = 0f;

    [Header("默认点击波源")]
    [Tooltip("鼠标点击产生的波源半径，单位是输入贴图像素。")]
    [Min(1)]
    [SerializeField] private int m_defaultRippleRadius = 2;

    [Tooltip("鼠标点击产生的波源强度。1 是白色波峰，-1 是黑色波谷。")]
    [Range(-1f, 1f)]
    [SerializeField] private float m_defaultRippleStrength = 1f;

    [Tooltip("是否使用圆形衰减笔刷。开启后波源边缘更柔和。")]
    [SerializeField] private bool m_useCircularBrush = true;

    [Header("调试")]
    [SerializeField] private bool m_logClick = false;
    [SerializeField] private bool m_logUpdateEvent = false;

    private Material m_surfaceMaterial;
    private Texture2D m_inputTexture;
    private RenderTexture[] m_renderTextures = new RenderTexture[3];

    private int m_textureIdx = 0;
    private int m_lastUpdatedFrame = -1;

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

    private void Reset()
    {
        m_targetRenderer = GetComponent<Renderer>();
        m_updateCamera = Camera.main;
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        // URP/SRP 的 Camera 渲染前事件。
        // 会在每个 Camera 渲染前触发一次。
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void OnDestroy()
    {
        ReleaseRenderTextures();

        // m_targetRenderer.material 会在运行时创建材质实例。
        // 销毁它可以避免运行时材质实例泄漏。
        if (Application.isPlaying && m_surfaceMaterial != null)
        {
            Destroy(m_surfaceMaterial);
        }
    }

    private void Initialize()
    {
        if (m_targetRenderer == null)
        {
            m_targetRenderer = GetComponent<Renderer>();
        }

        if (m_updateCamera == null)
        {
            m_updateCamera = Camera.main;
        }

        if (m_targetRenderer == null)
        {
            Debug.LogError("SurfaceWaveURP：Target Renderer 没有设置。", this);
            enabled = false;
            return;
        }

        if (m_equationMaterial == null)
        {
            Debug.LogError("SurfaceWaveURP：Equation Material 没有设置。", this);
            enabled = false;
            return;
        }

        // 使用 material 而不是 sharedMaterial。
        // material 会为当前 Renderer 创建一个运行时材质实例，避免修改到项目资源里的材质资产。
        m_surfaceMaterial = m_targetRenderer.material;

        CreateInputTexture();
        CreateRenderTextures();

        ApplyCommonShaderParams();

        // 初始时先把第一张水波图传给水面，避免材质里 _WaveTex 为空。
        m_surfaceMaterial.SetTexture("_WaveTex", CurrentFrame);
    }

    private void CreateInputTexture()
    {
        m_inputTexture = new Texture2D(
            m_inputTextureWidth,
            m_inputTextureHeight,
            TextureFormat.RGBA32,
            false,
            true // linear texture，避免 0.5 灰色受 Gamma/Linear 影响
        );

        m_inputTexture.name = "Surface Wave Input Texture";
        m_inputTexture.wrapMode = TextureWrapMode.Clamp;
        m_inputTexture.filterMode = FilterMode.Point;

        ClearInputTexture();
    }

    private void CreateRenderTextures()
    {
        ReleaseRenderTextures();

        for (int i = 0; i < m_renderTextures.Length; i++)
        {
            RenderTexture rt = new RenderTexture(
                m_textureWidth,
                m_textureHeight,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear
            );

            rt.name = "Surface Wave Height RT " + i;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = FilterMode.Bilinear;
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
            rt.Create();

            Graphics.SetRenderTarget(rt);
            GL.Clear(false, true, Color.gray);

            m_renderTextures[i] = rt;
        }

        Graphics.SetRenderTarget(null);
    }

    private void ReleaseRenderTextures()
    {
        if (m_renderTextures == null)
        {
            return;
        }

        for (int i = 0; i < m_renderTextures.Length; i++)
        {
            if (m_renderTextures[i] != null)
            {
                m_renderTextures[i].Release();
                Destroy(m_renderTextures[i]);
                m_renderTextures[i] = null;
            }
        }
    }

    private void ApplyCommonShaderParams()
    {
        Vector2 stride = new Vector2(1f / m_textureWidth, 1f / m_textureHeight);

        if (m_equationMaterial != null)
        {
            m_equationMaterial.SetVector("_Stride", stride);
            m_equationMaterial.SetFloat("_RoundAdjuster", m_roundAdjuster);
        }

        if (m_surfaceMaterial != null)
        {
            m_surfaceMaterial.SetVector("_Stride", stride);
        }
    }

    /// <summary>
    /// URP/SRP Camera 渲染前事件。
    /// 这里替代 Built-in 管线里的 OnPreRender()。
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        // 如果指定了更新相机，只在这台相机渲染前更新。
        if (m_updateCamera != null && camera != m_updateCamera)
        {
            return;
        }

        // 如果没有指定相机，只响应 Game Camera，避免 SceneView 也推动水波。
        if (m_updateCamera == null && camera.cameraType != CameraType.Game)
        {
            return;
        }

        // 多相机场景下 beginCameraRendering 一帧会触发多次。
        // 如果不做限制，水波会一帧迭代多次，速度会变快。
        if (m_updateOncePerFrame && m_lastUpdatedFrame == Time.frameCount)
        {
            return;
        }

        if (m_logUpdateEvent)
        {
            Debug.Log("SurfaceWaveURP：beginCameraRendering 更新水波，Camera = " + camera.name, this);
        }

        UpdateWaveTexture();
        m_lastUpdatedFrame = Time.frameCount;
    }

    /// <summary>
    /// 真正执行水波迭代。
    /// 输入：m_inputTexture、PrevFrame、PrevPrevFrame。
    /// 输出：CurrentFrame。
    /// </summary>
    private void UpdateWaveTexture()
    {
        if (m_inputTexture == null || m_surfaceMaterial == null || m_equationMaterial == null)
        {
            return;
        }

        ApplyCommonShaderParams();

        m_equationMaterial.SetVector("_Param", new Vector4(m_waveFactor, m_decay, 0f, 0f));

        // 不再用 _MainTex，改用我们自己的 _InputTex
        m_equationMaterial.SetTexture("_InputTex", m_inputTexture);

        m_equationMaterial.SetTexture("_PrevTex", PrevFrame);
        m_equationMaterial.SetTexture("_PrevPrevTex", PrevPrevFrame);

        // source 已经不重要了，因为 shader 里不用 _MainTex
        Graphics.Blit(Texture2D.blackTexture, CurrentFrame, m_equationMaterial);

        ClearInputTexture();

        m_surfaceMaterial.SetTexture("_WaveTex", CurrentFrame);

        m_textureIdx = (m_textureIdx + 1) % m_renderTextures.Length;
    }
    
    /// <summary>
    /// 给 ClickTrigger 用的方法。
    /// ClickTrigger 传进来的是鼠标屏幕坐标。
    /// </summary>
    public void OnClickDown(Vector3 screenPosition)
    {
        Camera cam = GetRaycastCamera();
        if (cam == null)
        {
            Debug.LogError("SurfaceWaveURP：找不到用于 Raycast 的 Camera。请设置 Update Camera 或 MainCamera。", this);
            return;
        }

        Ray ray = cam.ScreenPointToRay(screenPosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            if (m_logClick)
            {
                Debug.Log("SurfaceWaveURP：射线命中 " + hit.collider.name + "，世界坐标 = " + hit.point, this);
            }

            AddRippleWorld(hit.point, m_defaultRippleStrength, m_defaultRippleRadius);
        }
        else
        {
            if (m_logClick)
            {
                Debug.LogWarning("SurfaceWaveURP：点击射线没有命中任何 Collider。", this);
            }
        }
    }

    private Camera GetRaycastCamera()
    {
        if (m_updateCamera != null)
        {
            return m_updateCamera;
        }

        return Camera.main;
    }

    /// <summary>
    /// 从世界坐标添加一个波纹。
    /// 以后角色脚步、物体落水、子弹命中水面，都可以调用这个方法。
    /// </summary>
    public void AddRippleWorld(Vector3 worldPosition, float strength = 1f, int radius = 2)
    {
        if (m_targetRenderer == null || m_inputTexture == null)
        {
            return;
        }

        Vector3 localPos = m_targetRenderer.transform.InverseTransformPoint(worldPosition);

        Vector2 local2D = m_useXZPlane
            ? new Vector2(localPos.x, localPos.z)
            : new Vector2(localPos.x, localPos.y);

        AddRippleLocal(worldPosition,local2D, strength, radius);
    }

    /// <summary>
    /// 从水面本地 2D 坐标添加波纹。
    /// 本地坐标范围通常是：
    /// x: -m_meshSize.x / 2 到 +m_meshSize.x / 2
    /// y: -m_meshSize.y / 2 到 +m_meshSize.y / 2
    /// </summary>
    public void AddRippleLocal(Vector3 worldPosition,Vector2 localPosition, float strength = 1f, int radius = 2)
    {
        Vector2 uv = new Vector2(
            localPosition.x / m_meshSize.x + 0.5f,
            localPosition.y / m_meshSize.y + 0.5f
        );

        Debug.Log(
            "[SurfaceWave] AddRippleWorld " +
            "world=" + worldPosition +
            ", local=" + localPosition +
            ", uv=" + uv +
            ", meshSize=" + m_meshSize,
            this
        );
        
        AddRippleUV(uv, strength, radius);
    }

    /// <summary>
    /// 从 UV 坐标添加波纹。
    /// uv 的范围是 0~1。
    /// </summary>
    public void AddRippleUV(Vector2 uv, float strength = 1f, int radius = 2)
    {
        if (m_inputTexture == null)
        {
            return;
        }

        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
        {
            return;
        }

        strength = Mathf.Clamp(strength, -1f, 1f);
        radius = Mathf.Max(1, radius);

        int centerX = Mathf.RoundToInt((m_inputTexture.width - 1) * uv.x);
        int centerY = Mathf.RoundToInt((m_inputTexture.height - 1) * uv.y);

        if (m_logClick)
        {
            Debug.Log("SurfaceWaveURP：写入波源像素 = " + centerX + ", " + centerY + "，uv = " + uv, this);
        }

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int px = centerX + x;
                int py = centerY + y;

                if (px < 0 || px >= m_inputTexture.width || py < 0 || py >= m_inputTexture.height)
                {
                    continue;
                }

                float falloff = 1f;

                if (m_useCircularBrush)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);
                    if (distance > radius)
                    {
                        continue;
                    }

                    // 中心最强，边缘逐渐变弱。
                    falloff = 1f - distance / radius;
                }

                // 灰色 0.5 是静止。
                // strength =  1 -> 1.0 白色波峰
                // strength = -1 -> 0.0 黑色波谷
                float value = 0.5f + 0.5f * strength * falloff;
                value = Mathf.Clamp01(value);

                m_inputTexture.SetPixel(px, py, new Color(value, value, value, 1f));
            }
        }

        m_inputTexture.Apply(false, false);
    }
    
    public void AddFootstepRippleUV(
        Vector2 uv,
        float centerStrength = -0.35f,
        float ringStrength = 0.18f,
        int innerRadius = 1,
        int outerRadius = 4
    )
    {
        if (m_inputTexture == null)
            return;

        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            return;

        innerRadius = Mathf.Max(1, innerRadius);
        outerRadius = Mathf.Max(innerRadius + 1, outerRadius);

        int centerX = Mathf.RoundToInt((m_inputTexture.width - 1) * uv.x);
        int centerY = Mathf.RoundToInt((m_inputTexture.height - 1) * uv.y);

        for (int y = -outerRadius; y <= outerRadius; y++)
        {
            for (int x = -outerRadius; x <= outerRadius; x++)
            {
                int px = centerX + x;
                int py = centerY + y;

                if (px < 0 || px >= m_inputTexture.width || py < 0 || py >= m_inputTexture.height)
                    continue;

                float distance = Mathf.Sqrt(x * x + y * y);

                if (distance > outerRadius)
                    continue;

                float strength = 0f;

                // 中心区域：脚踩下去，写负值，形成凹陷
                if (distance <= innerRadius)
                {
                    float t = 1f - distance / innerRadius;
                    strength = centerStrength * t;
                }
                // 外圈区域：水被挤到周围，写正值，形成一圈隆起
                else
                {
                    float ringT = Mathf.InverseLerp(innerRadius, outerRadius, distance);

                    // 中间最强，内外边缘变弱
                    float ringShape = Mathf.Sin(ringT * Mathf.PI);

                    strength = ringStrength * ringShape;
                }

                float value = 0.5f + 0.5f * strength;
                value = Mathf.Clamp01(value);

                m_inputTexture.SetPixel(px, py, new Color(value, value, value, 1f));
            }
        }

        m_inputTexture.Apply(false, false);
    }

    /// <summary>
    /// 把输入扰动图清成灰色。
    /// 灰色代表没有新的外部输入。
    /// </summary>
    private void ClearInputTexture()
    {
        if (m_inputTexture == null)
        {
            return;
        }

        Color gray = Color.gray;

        for (int y = 0; y < m_inputTexture.height; y++)
        {
            for (int x = 0; x < m_inputTexture.width; x++)
            {
                m_inputTexture.SetPixel(x, y, gray);
            }
        }

        m_inputTexture.Apply(false, false);
    }
}
