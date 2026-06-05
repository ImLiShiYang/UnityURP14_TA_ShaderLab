using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// 水波 RT 屏幕调试窗口。
///
/// 说明：
/// 水波 RT 协议通常是：
/// RGB = encoded normal，默认约为 (0.5, 0.5, 1)
/// A   = signed height，0.5 表示无高度变化，低于 0.5 表示下陷，高于 0.5 表示隆起。
///
/// 因此查看 AccumA 时，推荐优先使用：
/// - WaterSignedHeightA：直接看 signed height，0.5 灰色表示无变化。
/// - WaterHeightMagnitudeA：看绝对波纹强度，黑色表示无变化。
/// - WaterComposite：把法线和高度变化合成到一起看。
/// </summary>
[MovedFrom(false, null, null, "TextureDebugUI")]
public class WaterRippleTextureDebugUI : MonoBehaviour
{
    public enum DebugMode
    {
        RGB = 0,
        Alpha = 1,
        R = 2,
        G = 3,
        B = 4,
        NormalDiff = 5,
        NormalEncoded = 6,
        RGBWithAlphaBackground = 7,

        // Water Ripple RT 调试模式。
        // 当前水波 RT 协议：
        // RGB = encoded normal。
        // A = signed height，0.5 表示无变化，低于 0.5 下陷，高于 0.5 隆起。
        WaterSignedHeightA = 8,
        WaterHeightMagnitudeA = 9,
        WaterMaskA = 10,
        WaterComposite = 11
    }

    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    [Header("Water Ripple Texture")]
    public Texture texture;

    [Header("UI")]
    public Corner corner = Corner.TopRight;
    public Vector2 margin = new Vector2(16, 16);
    public Vector2 baseSize = new Vector2(360, 260);
    [Range(0.4f, 3f)]
    public float uiScale = 1f;
    public float scaleStep = 0.15f;
    public bool visible = true;

    [Header("View")]
    public DebugMode mode = DebugMode.WaterHeightMagnitudeA;
    [Range(0f, 10f)]
    public float exposure = 1f;
    [Range(1f, 100f)]
    public float normalDiffStrength = 20f;
    public Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    public bool flipY;

    [Header("Shortcut")]
    public KeyCode toggleKey = KeyCode.F3;
    public KeyCode biggerKey = KeyCode.Equals;
    public KeyCode smallerKey = KeyCode.Minus;

    [Header("Shader")]
    public Shader debugShader;

    private Canvas canvas;
    private RectTransform panelRect;
    private RectTransform miniButtonRect;
    private RawImage rawImage;
    private Text titleText;
    private Text modeText;
    private Text scaleText;
    private Material runtimeMaterial;

    private static readonly int ModeID = Shader.PropertyToID("_Mode");
    private static readonly int ExposureID = Shader.PropertyToID("_Exposure");
    private static readonly int NormalDiffStrengthID = Shader.PropertyToID("_NormalDiffStrength");
    private static readonly int BackgroundColorID = Shader.PropertyToID("_BackgroundColor");
    private static readonly int FlipYID = Shader.PropertyToID("_FlipY");

    private void Awake()
    {
        BuildUI();
        ApplyAll();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetVisible(!visible);

        if (Input.GetKeyDown(biggerKey))
            ChangeScale(scaleStep);

        if (Input.GetKeyDown(smallerKey))
            ChangeScale(-scaleStep);

        ApplyAll();
    }

    public void SetTexture(Texture newTexture)
    {
        texture = newTexture;
        ApplyAll();
    }

    public void SetTitle(string title)
    {
        if (titleText != null)
            titleText.text = title;
    }

    public void SetMode(DebugMode newMode)
    {
        mode = newMode;
        ApplyAll();
    }

    public void SetVisible(bool value)
    {
        visible = value;

        if (panelRect != null)
            panelRect.gameObject.SetActive(visible);

        if (miniButtonRect != null)
            miniButtonRect.gameObject.SetActive(!visible);
    }

    public void ChangeScale(float delta)
    {
        uiScale = Mathf.Clamp(uiScale + delta, 0.4f, 3f);
        ApplyLayout();
    }

    public void NextMode()
    {
        int count = System.Enum.GetValues(typeof(DebugMode)).Length;
        int next = ((int)mode + 1) % count;
        mode = (DebugMode)next;
        ApplyAll();
    }

    public void PreviousMode()
    {
        int count = System.Enum.GetValues(typeof(DebugMode)).Length;
        int prev = ((int)mode - 1 + count) % count;
        mode = (DebugMode)prev;
        ApplyAll();
    }

    private void BuildUI()
    {
        EnsureEventSystem();

        if (debugShader == null)
            debugShader = Shader.Find("WaterRipple/Debug/UI_WaterRippleTextureDebugView");

        if (debugShader == null)
        {
            Debug.LogError("[WaterRippleTextureDebugUI] 找不到 Shader: WaterRipple/Debug/UI_WaterRippleTextureDebugView");
            return;
        }

        runtimeMaterial = new Material(debugShader);
        runtimeMaterial.name = "M_Runtime_WaterRippleTextureDebugUI";

        GameObject canvasGo = new GameObject("WaterRippleTextureDebugUI_Canvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject panelGo = new GameObject("WaterRippleDebugPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelRect = panelGo.AddComponent<RectTransform>();

        Image panelBg = panelGo.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.55f);

        GameObject rawGo = new GameObject("WaterRippleTextureView");
        rawGo.transform.SetParent(panelGo.transform, false);
        RectTransform rawRect = rawGo.AddComponent<RectTransform>();
        rawRect.anchorMin = new Vector2(0, 0);
        rawRect.anchorMax = new Vector2(1, 1);
        rawRect.offsetMin = new Vector2(8, 8);
        rawRect.offsetMax = new Vector2(-8, -42);

        rawImage = rawGo.AddComponent<RawImage>();
        rawImage.material = runtimeMaterial;
        rawImage.color = Color.white;

        titleText = CreateText(panelGo.transform, "WaterRippleTitle", "Water Ripple Texture Debug", 14, TextAnchor.MiddleLeft);
        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.offsetMin = new Vector2(8, -34);
        titleRect.offsetMax = new Vector2(-170, -4);

        Button closeBtn = CreateButton(panelGo.transform, "WaterRippleCloseButton", "×");
        RectTransform closeRect = closeBtn.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1, 1);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.pivot = new Vector2(1, 1);
        closeRect.anchoredPosition = new Vector2(-6, -6);
        closeRect.sizeDelta = new Vector2(28, 28);
        closeBtn.onClick.AddListener(() => SetVisible(false));

        Button plusBtn = CreateButton(panelGo.transform, "WaterRipplePlusButton", "+");
        RectTransform plusRect = plusBtn.GetComponent<RectTransform>();
        plusRect.anchorMin = new Vector2(1, 1);
        plusRect.anchorMax = new Vector2(1, 1);
        plusRect.pivot = new Vector2(1, 1);
        plusRect.anchoredPosition = new Vector2(-40, -6);
        plusRect.sizeDelta = new Vector2(28, 28);
        plusBtn.onClick.AddListener(() => ChangeScale(scaleStep));

        Button minusBtn = CreateButton(panelGo.transform, "WaterRippleMinusButton", "-");
        RectTransform minusRect = minusBtn.GetComponent<RectTransform>();
        minusRect.anchorMin = new Vector2(1, 1);
        minusRect.anchorMax = new Vector2(1, 1);
        minusRect.pivot = new Vector2(1, 1);
        minusRect.anchoredPosition = new Vector2(-74, -6);
        minusRect.sizeDelta = new Vector2(28, 28);
        minusBtn.onClick.AddListener(() => ChangeScale(-scaleStep));

        Button modeBtn = CreateButton(panelGo.transform, "WaterRippleModeButton", "Mode");
        RectTransform modeRect = modeBtn.GetComponent<RectTransform>();
        modeRect.anchorMin = new Vector2(1, 1);
        modeRect.anchorMax = new Vector2(1, 1);
        modeRect.pivot = new Vector2(1, 1);
        modeRect.anchoredPosition = new Vector2(-108, -6);
        modeRect.sizeDelta = new Vector2(58, 28);
        modeBtn.onClick.AddListener(NextMode);
        modeText = modeBtn.GetComponentInChildren<Text>();

        scaleText = CreateText(panelGo.transform, "WaterRippleScaleText", "100%", 12, TextAnchor.MiddleRight);
        RectTransform scaleRect = scaleText.rectTransform;
        scaleRect.anchorMin = new Vector2(1, 1);
        scaleRect.anchorMax = new Vector2(1, 1);
        scaleRect.pivot = new Vector2(1, 1);
        scaleRect.anchoredPosition = new Vector2(-172, -8);
        scaleRect.sizeDelta = new Vector2(58, 24);

        Button miniBtn = CreateButton(canvasGo.transform, "WaterRippleMiniToggleButton", "WR");
        miniButtonRect = miniBtn.GetComponent<RectTransform>();
        miniButtonRect.sizeDelta = new Vector2(52, 32);
        miniBtn.onClick.AddListener(() => SetVisible(true));

        ApplyLayout();
        SetVisible(visible);
    }

    private void ApplyAll()
    {
        if (rawImage != null)
        {
            rawImage.texture = texture;
        }

        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat(ModeID, (float)mode);
            runtimeMaterial.SetFloat(ExposureID, exposure);
            runtimeMaterial.SetFloat(NormalDiffStrengthID, normalDiffStrength);
            runtimeMaterial.SetColor(BackgroundColorID, backgroundColor);
            runtimeMaterial.SetFloat(FlipYID, flipY ? 1f : 0f);
        }

        if (modeText != null)
            modeText.text = mode.ToString();

        if (scaleText != null)
            scaleText.text = Mathf.RoundToInt(uiScale * 100f) + "%";

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (panelRect != null)
        {
            panelRect.sizeDelta = baseSize * uiScale;
            ApplyCorner(panelRect, corner, margin);
        }

        if (miniButtonRect != null)
        {
            ApplyCorner(miniButtonRect, corner, margin);
        }
    }

    private void ApplyCorner(RectTransform rect, Corner targetCorner, Vector2 targetMargin)
    {
        switch (targetCorner)
        {
            case Corner.TopLeft:
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(targetMargin.x, -targetMargin.y);
                break;

            case Corner.TopRight:
                rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-targetMargin.x, -targetMargin.y);
                break;

            case Corner.BottomLeft:
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(targetMargin.x, targetMargin.y);
                break;

            case Corner.BottomRight:
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
                rect.pivot = new Vector2(1, 0);
                rect.anchoredPosition = new Vector2(-targetMargin.x, targetMargin.y);
                break;
        }
    }

    private Text CreateText(Transform parent, string objectName, string text, int fontSize, TextAnchor alignment)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);

        Text t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = alignment;
        t.color = Color.white;
        t.raycastTarget = false;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return t;
    }

    private Button CreateButton(Transform parent, string objectName, string label)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);

        Button button = go.AddComponent<Button>();

        Text text = CreateText(go.transform, "Text", label, 14, TextAnchor.MiddleCenter);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}
