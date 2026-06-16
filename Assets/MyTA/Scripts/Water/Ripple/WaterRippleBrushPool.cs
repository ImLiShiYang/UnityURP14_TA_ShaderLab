using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 水面波纹 Brush 对象池。
///
/// WaterRippleBrushSpawner 在需要产生水波输入时，不直接 Instantiate / Destroy Brush，
/// 而是通过这个对象池复用一批短生命周期的 Brush Quad。
///
/// 每个 Brush 会被放到指定 Layer 上，让 WaterRippleRenderFeature 只渲染这些 Brush 到水波 RT。
/// 同时，本类会通过 MaterialPropertyBlock 给每个 Brush 设置独立的法线贴图、高度贴图和强度参数。
/// </summary>
public class WaterRippleBrushPool : MonoBehaviour
{
    [Header("Pool Settings")]
    [Tooltip("水波 Brush 预制体。通常是一个使用 WaterRipple/URP_WaterRippleBrush_NormalHeightSeparate 材质的 Quad。")]
    public GameObject brushPrefab;

    [Tooltip("对象池允许存在的最大 Brush 数量，包含正在使用和空闲的对象。每帧持续水波通常需要更大的池。")]
    public int maxBrushes = 128;

    [Tooltip("是否在 Awake 时提前创建对象池中的 Brush。开启后首次生成水波时更稳定，但场景加载时会多一点开销。")]
    public bool prewarmOnAwake = true;

    [Tooltip("当对象池已满时，是否复用最早生成且仍在使用中的 Brush。开启后不会因为池满而丢失新水波，但旧水波可能提前消失。")]
    public bool recycleOldestWhenFull = true;

    [Header("Layer")]
    [Tooltip("池中的 Brush 会被强制设置到这个 Layer，方便 WaterRippleRenderFeature 只渲染水波输入对象。")]
    public string brushLayerName = "WaterRippleBrush";

    [Header("Safety")]
    [Tooltip("Brush 只负责写入水波 RT，不需要投射或接收场景阴影。开启后会关闭 Renderer 的阴影相关选项。")]
    public bool disableRendererShadows = true;

    [Tooltip("禁用池中 Brush 的所有 Collider，避免它们参与角色移动、物理碰撞或射线检测。")]
    public bool disableColliders = true;

    [Header("Debug")]
    [Tooltip("是否在 Game 视图左上角绘制对象池占用情况。")]
    public bool showDebugGUI = true;

    [Tooltip("对象池调试 UI 的左上角位置。")]
    public Vector2 debugGUIPosition = new Vector2(10f, 230f);

    [Tooltip("对象池调试 UI 的宽高。")]
    public Vector2 debugGUISize = new Vector2(340f, 132f);

    [Tooltip("对象池调试 UI 的字体大小。")]
    [Range(12, 32)]
    public int debugGUIFontSize = 18;

    [Tooltip("对象池调试 UI 的背景颜色。")]
    public Color debugGUIBackgroundColor = new Color(0f, 0f, 0f, 0.62f);

    [Tooltip("对象池调试 UI 的边框颜色。")]
    public Color debugGUIBorderColor = new Color(1f, 1f, 1f, 0.22f);

    [Tooltip("对象池调试 UI 标题文字颜色。")]
    public Color debugGUITitleColor = new Color(0.72f, 0.9f, 1f, 1f);

    [Tooltip("对象池调试 UI 正文文字颜色。")]
    public Color debugGUITextColor = new Color(1f, 1f, 1f, 0.96f);

    [Tooltip("对象池调试 UI 文字阴影颜色。")]
    public Color debugGUIShadowColor = new Color(0f, 0f, 0f, 0.75f);

    [Tooltip("调试 UI 文本刷新间隔。只影响显示刷新，不影响对象池真实数据。")]
    [Range(0.1f, 2f)]
    public float debugGUIRefreshInterval = 0.25f;

    // 空闲队列：已经创建、当前没有使用的 Brush。
    // Spawn 时优先从这里 Dequeue，Release 时重新 Enqueue。
    private readonly Queue<PooledWaterRippleBrush> available = new Queue<PooledWaterRippleBrush>();

    // 正在使用的 Brush 链表。
    // 使用 LinkedList 是为了在池满时快速拿到最早使用的对象，也就是 active.First。
    private readonly LinkedList<PooledWaterRippleBrush> active = new LinkedList<PooledWaterRippleBrush>();

    // 下面这些 GUIContent 复用同一个对象，避免 OnGUI 里频繁分配字符串包装对象。
    private readonly GUIContent debugTitleContent = new GUIContent();
    private readonly GUIContent debugUsedContent = new GUIContent();
    private readonly GUIContent debugAvailableContent = new GUIContent();
    private readonly GUIContent debugCreatedContent = new GUIContent();

    // 调试 UI 使用的样式。字体大小变化时会重新创建。
    private GUIStyle debugTitleStyle;
    private GUIStyle debugLabelStyle;
    private GUIStyle debugShadowStyle;
    private int cachedDebugGUIFontSize = -1;

    // 下一次刷新调试文字的时间点，使用 unscaledTime，避免 Time.timeScale 影响调试显示。
    private float nextDebugGUIRefreshTime;

    // 调试 UI 的各个绘制区域，集中缓存，避免每个 Label 分散计算。
    private Rect debugAreaRect;
    private Rect debugTitleRect;
    private Rect debugUsedRect;
    private Rect debugAvailableRect;
    private Rect debugCreatedRect;

    // Brush Shader 中使用的属性 ID。
    // 提前缓存 PropertyToID，避免每次 SpawnBrush 时用字符串查找属性。
    private static readonly int NormalTexID = Shader.PropertyToID("_NormalTex");
    private static readonly int HeightTexID = Shader.PropertyToID("_HeightTex");
    private static readonly int NormalStrengthID = Shader.PropertyToID("_NormalStrength");
    private static readonly int HeightStrengthID = Shader.PropertyToID("_HeightStrength");
    private static readonly int InvertHeightID = Shader.PropertyToID("_InvertHeight");

    /// <summary>
    /// 当前正在使用中的 Brush 数量。
    /// </summary>
    public int ActiveCount => active.Count;

    /// <summary>
    /// 当前空闲可复用的 Brush 数量。
    /// </summary>
    public int AvailableCount => available.Count;

    /// <summary>
    /// 当前对象池已经创建出来的 Brush 总数。
    /// 等于正在使用数量 + 空闲数量。
    /// </summary>
    public int CreatedCount => active.Count + available.Count;

    /// <summary>
    /// 当前对象池是否配置了 Brush 预制体。
    /// 外部可以用它判断这个池是否可用。
    /// </summary>
    public bool HasPrefab => brushPrefab != null;

    /// <summary>
    /// 调试 UI 中显示的对象池名称。
    /// </summary>
    private string DebugPoolName => brushPrefab != null ? brushPrefab.name : "No Prefab";

    /// <summary>
    /// Unity 生命周期函数。
    /// 如果开启了预热，会在场景启动时提前创建 Brush，避免第一次水波生成时卡顿。
    /// </summary>
    private void Awake()
    {
        if (prewarmOnAwake && brushPrefab != null)
            Prewarm();
    }

    /// <summary>
    /// 按 maxBrushes 数量预热对象池。
    /// </summary>
    public void Prewarm()
    {
        Prewarm(maxBrushes);
    }

    /// <summary>
    /// 将对象池预热到指定总数量。
    ///
    /// 如果当前已经创建的数量大于等于目标数量，则不会额外创建。
    /// 如果目标数量超过 maxBrushes，会被限制到 maxBrushes。
    /// </summary>
    /// <param name="targetTotalCount">希望对象池中存在的 Brush 总数。</param>
    public void Prewarm(int targetTotalCount)
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[WaterRippleBrushPool] brushPrefab is null.", this);
            return;
        }

        int totalCount = available.Count + active.Count;
        int targetCount = Mathf.Clamp(targetTotalCount, 0, maxBrushes);
        int createCount = Mathf.Max(0, targetCount - totalCount);

        // 创建出来的对象先进入空闲队列，等真正需要水波时再取出使用。
        for (int i = 0; i < createCount; i++)
        {
            PooledWaterRippleBrush instance = CreateNewInstance();
            ReleaseToAvailable(instance);
        }
    }

    /// <summary>
    /// 从对象池中取出一个 Brush，并设置它的 Transform、材质参数和生命周期。
    /// </summary>
    /// <param name="position">Brush 的世界坐标。</param>
    /// <param name="rotation">Brush 的世界旋转。通常需要让 Quad 朝向水面 RT 摄像机。</param>
    /// <param name="scale">Brush 的局部缩放，用来控制本次水波输入的范围。</param>
    /// <param name="lifeTime">Brush 保持激活的时间，时间到后自动回收。</param>
    /// <param name="normalTex">写入法线扰动的贴图。</param>
    /// <param name="heightTex">写入高度扰动的贴图。</param>
    /// <param name="normalStrength">法线扰动强度。</param>
    /// <param name="heightStrength">高度扰动强度。</param>
    /// <param name="invertHeight">是否反转高度方向，对应 Shader 中的 _InvertHeight。</param>
    /// <param name="strengthMultiplier">额外强度倍率。用于脚步、武器等不同来源共享同一套基础参数但临时放大或减弱。</param>
    /// <returns>成功生成时返回 Brush 根物体；如果对象池不可用或已满且不允许复用，则返回 null。</returns>
    public GameObject SpawnBrush(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float lifeTime,
        Texture normalTex,
        Texture heightTex,
        float normalStrength,
        float heightStrength,
        float invertHeight,
        float strengthMultiplier = 1f)
    {
        if (brushPrefab == null)
        {
            Debug.LogWarning("[WaterRippleBrushPool] brushPrefab is null.", this);
            return null;
        }

        // 优先复用空闲对象；没有空闲对象时，可能创建新对象或回收最旧的 active 对象。
        PooledWaterRippleBrush pooled = GetAvailableInstance();

        if (pooled == null || pooled.BrushObject == null)
            return null;

        GameObject brush = pooled.BrushObject;

        // 激活并设置本次水波输入的空间信息。
        brush.SetActive(true);
        brush.transform.SetPositionAndRotation(position, rotation);
        brush.transform.localScale = scale;

        // 使用 MaterialPropertyBlock 设置本次独立的贴图和强度，不实例化材质。
        SetupBrushMaterial(pooled, normalTex, heightTex, normalStrength, heightStrength, invertHeight, strengthMultiplier);

        // 标记进入 active 链表，并启动生命周期倒计时。
        pooled.IsActiveInPool = true;
        active.AddLast(pooled);
        pooled.PlayLifetime(Mathf.Max(0.001f, lifeTime));

        return brush;
    }

    /// <summary>
    /// 创建一个新的池对象实例，并完成组件初始化、Layer 设置和安全设置。
    /// </summary>
    /// <returns>新创建的池对象包装组件。</returns>
    private PooledWaterRippleBrush CreateNewInstance()
    {
        GameObject go = Instantiate(brushPrefab, transform);
        go.name = "Pooled Water Ripple Brush";

        // 如果预制体上已经挂了 PooledWaterRippleBrush，就直接复用；否则运行时补一个。
        PooledWaterRippleBrush pooled = go.GetComponent<PooledWaterRippleBrush>();

        if (pooled == null)
            pooled = go.AddComponent<PooledWaterRippleBrush>();

        pooled.Initialize(this, go);

        // 设置 Layer，让 RenderFeature 可以通过 LayerMask 只抓取水波 Brush。
        int brushLayer = LayerMask.NameToLayer(brushLayerName);

        if (brushLayer >= 0)
            SetLayerRecursively(go, brushLayer);
        else
            Debug.LogWarning($"[WaterRippleBrushPool] Layer not found: {brushLayerName}", this);

        // 关闭阴影、碰撞等和水波 RT 输入无关的功能。
        PrepareBrushObject(pooled);

        return pooled;
    }

    /// <summary>
    /// 获取一个可用的池对象。
    ///
    /// 优先级：
    /// 1. 从 available 队列取空闲对象；
    /// 2. 未达到 maxBrushes 时创建新对象；
    /// 3. 池满且允许复用时，拿 active 链表中最旧的对象；
    /// 4. 以上都不满足则返回 null。
    /// </summary>
    /// <returns>可使用的池对象；如果没有可用对象则返回 null。</returns>
    private PooledWaterRippleBrush GetAvailableInstance()
    {
        if (available.Count > 0)
            return available.Dequeue();

        int totalCount = available.Count + active.Count;

        if (totalCount < maxBrushes)
            return CreateNewInstance();

        if (recycleOldestWhenFull && active.Count > 0)
        {
            // active.First 是最早被 Spawn 的 Brush。
            // 回收它可以保证新水波能出现，但代价是旧水波生命周期被提前结束。
            PooledWaterRippleBrush oldest = active.First.Value;
            active.RemoveFirst();

            oldest.StopLifetime();
            oldest.IsActiveInPool = false;

            return oldest;
        }

        return null;
    }

    /// <summary>
    /// 将一个正在使用中的 Brush 归还给对象池。
    /// 通常由 PooledWaterRippleBrush 生命周期结束后调用，也可以由对象池内部强制回收。
    /// </summary>
    /// <param name="pooled">需要归还的池对象。</param>
    public void Release(PooledWaterRippleBrush pooled)
    {
        if (pooled == null)
            return;

        // 如果它已经不在 active 池里，就不重复回收。
        if (!pooled.IsActiveInPool)
            return;

        pooled.IsActiveInPool = false;
        active.Remove(pooled);
        ReleaseToAvailable(pooled);
    }

    /// <summary>
    /// 把池对象放入 available 队列。
    /// 进入 available 前会停止生命周期计时，并隐藏 Brush 物体。
    /// </summary>
    /// <param name="pooled">需要放回空闲队列的池对象。</param>
    private void ReleaseToAvailable(PooledWaterRippleBrush pooled)
    {
        if (pooled == null)
            return;

        pooled.StopLifetime();

        if (pooled.BrushObject != null)
            pooled.BrushObject.SetActive(false);

        available.Enqueue(pooled);
    }

    /// <summary>
    /// 对新创建的 Brush 做一次性准备。
    /// 主要是关闭阴影和碰撞，避免水波输入对象影响正常场景表现与角色逻辑。
    /// </summary>
    /// <param name="pooled">需要准备的池对象。</param>
    private void PrepareBrushObject(PooledWaterRippleBrush pooled)
    {
        if (pooled == null)
            return;

        if (disableRendererShadows)
        {
            Renderer[] renderers = pooled.Renderers;

            if (renderers != null)
            {
                foreach (Renderer r in renderers)
                {
                    if (r == null)
                        continue;

                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
        }

        if (disableColliders)
        {
            Collider[] colliders = pooled.Colliders;

            if (colliders != null)
            {
                foreach (Collider c in colliders)
                {
                    if (c != null)
                        c.enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// 设置本次 Brush 的材质属性。
    ///
    /// 这里不直接改 Renderer.sharedMaterial，也不 new 材质，
    /// 而是用每个池对象缓存的 MaterialPropertyBlock 写入参数。
    /// 这样可以让多个 Brush 共享同一个材质，同时拥有不同贴图和强度。
    /// </summary>
    /// <param name="pooled">正在设置的池对象。</param>
    /// <param name="normalTex">法线水波贴图。</param>
    /// <param name="heightTex">高度水波贴图。</param>
    /// <param name="normalStrength">基础法线强度。</param>
    /// <param name="heightStrength">基础高度强度。</param>
    /// <param name="invertHeight">高度是否反向。</param>
    /// <param name="strengthMultiplier">额外强度倍率。</param>
    private void SetupBrushMaterial(
        PooledWaterRippleBrush pooled,
        Texture normalTex,
        Texture heightTex,
        float normalStrength,
        float heightStrength,
        float invertHeight,
        float strengthMultiplier)
    {
        if (pooled == null || pooled.PropertyBlock == null)
            return;

        // 强度倍率不允许为负，避免把波纹方向和强度逻辑搞混。
        float safeStrengthMultiplier = Mathf.Max(0f, strengthMultiplier);
        MaterialPropertyBlock mpb = pooled.PropertyBlock;
        Renderer[] renderers = pooled.Renderers;

        // 清掉上一次复用时残留的属性。
        mpb.Clear();

        if (normalTex != null)
            mpb.SetTexture(NormalTexID, normalTex);

        if (heightTex != null)
            mpb.SetTexture(HeightTexID, heightTex);

        // 基础强度 * 额外倍率。
        // 这样脚步、身体、武器等不同来源可以共用同一个 Brush shader。
        mpb.SetFloat(NormalStrengthID, normalStrength * safeStrengthMultiplier);
        mpb.SetFloat(HeightStrengthID, heightStrength * safeStrengthMultiplier);
        mpb.SetFloat(InvertHeightID, invertHeight);

        if (renderers == null)
            return;

        // 把同一个 MPB 应用到该 Brush 下所有 Renderer。
        foreach (Renderer r in renderers)
        {
            if (r != null)
                r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>
    /// 递归设置物体及其所有子物体的 Layer。
    /// </summary>
    /// <param name="go">需要设置 Layer 的根物体。</param>
    /// <param name="layer">目标 Layer 编号。</param>
    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;

        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>
    /// 绘制对象池调试 UI。
    ///
    /// 注意：OnGUI 会被 Unity 多次调用，因此这里仅在 Repaint 事件中真正绘制，
    /// 并且通过 debugGUIRefreshInterval 降低文本刷新频率。
    /// </summary>
    private void OnGUI()
    {
        if (!showDebugGUI)
            return;

        if (Event.current.type != EventType.Repaint)
            return;

        EnsureDebugGUIStyles();
        UpdateDebugGUIRects();

        // 调试文本不需要每一帧都重新拼接，降低一点 GC 压力。
        if (Time.unscaledTime >= nextDebugGUIRefreshTime)
        {
            RefreshDebugGUIText();
            nextDebugGUIRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, debugGUIRefreshInterval);
        }

        DrawDebugGUIBackground();
        DrawDebugLabel(debugTitleRect, debugTitleContent, debugTitleStyle);
        DrawDebugLabel(debugUsedRect, debugUsedContent, debugLabelStyle);
        DrawDebugLabel(debugAvailableRect, debugAvailableContent, debugLabelStyle);
        DrawDebugLabel(debugCreatedRect, debugCreatedContent, debugLabelStyle);
    }

    /// <summary>
    /// 确保调试 UI 的 GUIStyle 已创建，并同步颜色与字体大小。
    /// </summary>
    private void EnsureDebugGUIStyles()
    {
        // 第一次绘制，或者字体大小变化时，重新创建样式。
        if (debugLabelStyle == null || debugTitleStyle == null || debugShadowStyle == null || cachedDebugGUIFontSize != debugGUIFontSize)
        {
            cachedDebugGUIFontSize = debugGUIFontSize;

            debugLabelStyle = new GUIStyle();
            debugLabelStyle.fontSize = debugGUIFontSize;
            debugLabelStyle.fontStyle = FontStyle.Bold;
            debugLabelStyle.alignment = TextAnchor.MiddleLeft;
            debugLabelStyle.clipping = TextClipping.Overflow;

            debugTitleStyle = new GUIStyle(debugLabelStyle);
            debugTitleStyle.fontStyle = FontStyle.Bold;

            debugShadowStyle = new GUIStyle(debugLabelStyle);
        }

        // 颜色可能在 Inspector 中运行时修改，所以每次绘制前都同步一次。
        debugLabelStyle.normal.textColor = debugGUITextColor;
        debugTitleStyle.normal.textColor = debugGUITitleColor;
        debugShadowStyle.normal.textColor = debugGUIShadowColor;
    }

    /// <summary>
    /// 根据 Inspector 中的 UI 位置、大小和字体设置，计算调试 UI 各行 Rect。
    /// </summary>
    private void UpdateDebugGUIRects()
    {
        float x = debugGUIPosition.x;
        float y = debugGUIPosition.y;
        float lineHeight = Mathf.Max(24f, debugGUIFontSize + 8f);
        float padding = 12f;
        float width = Mathf.Max(260f, debugGUISize.x);
        float height = Mathf.Max(padding * 2f + lineHeight * 4f, debugGUISize.y);

        debugAreaRect = new Rect(x, y, width, height);
        debugTitleRect = new Rect(x + padding, y + padding - 2f, width - padding * 2f, lineHeight);
        debugUsedRect = new Rect(x + padding, debugTitleRect.yMax, width - padding * 2f, lineHeight);
        debugAvailableRect = new Rect(x + padding, debugUsedRect.yMax, width - padding * 2f, lineHeight);
        debugCreatedRect = new Rect(x + padding, debugAvailableRect.yMax, width - padding * 2f, lineHeight);
    }

    /// <summary>
    /// 绘制调试 UI 背景和边框。
    /// </summary>
    private void DrawDebugGUIBackground()
    {
        Color oldColor = GUI.color;

        GUI.color = debugGUIBackgroundColor;
        GUI.DrawTexture(debugAreaRect, Texture2D.whiteTexture);

        GUI.color = debugGUIBorderColor;
        GUI.DrawTexture(new Rect(debugAreaRect.x, debugAreaRect.y, debugAreaRect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(debugAreaRect.x, debugAreaRect.yMax - 1f, debugAreaRect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(debugAreaRect.x, debugAreaRect.y, 1f, debugAreaRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(debugAreaRect.xMax - 1f, debugAreaRect.y, 1f, debugAreaRect.height), Texture2D.whiteTexture);

        // 还原 GUI.color，避免影响其他 OnGUI 绘制。
        GUI.color = oldColor;
    }

    /// <summary>
    /// 绘制一行带阴影的调试文字。
    /// </summary>
    /// <param name="rect">文字绘制区域。</param>
    /// <param name="content">要绘制的文字内容。</param>
    /// <param name="style">正文样式。</param>
    private void DrawDebugLabel(Rect rect, GUIContent content, GUIStyle style)
    {
        // 先画偏移 1 像素的阴影，再画正文。
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), content, debugShadowStyle);
        GUI.Label(rect, content, style);
    }

    /// <summary>
    /// 刷新调试 UI 显示的文本。
    /// </summary>
    private void RefreshDebugGUIText()
    {
        debugTitleContent.text = $"Brush Pool: {DebugPoolName}";
        debugUsedContent.text = $"Used: {ActiveCount} / {maxBrushes}";
        debugAvailableContent.text = $"Available: {AvailableCount}";
        debugCreatedContent.text = $"Created: {CreatedCount} / {maxBrushes}";
    }
}
