/*
 * FootprintReliefTextureGenerator - Bowl Depression / Signed Height Version
 *
 * 这版用于路线 B：Signed Height。
 *
 * HeightTex 语义：
 *     0.5 = 原始地面 / 背景
 *   < 0.5 = 脚印下陷
 *   > 0.5 = 泥边隆起
 *
 * 新增：Interior Bowl Depression
 * - 内部不再只是平底坑。
 * - 通过 distanceToOutside / maxInsideDistance 生成“碗形凹陷”。
 * - 前掌和脚跟可以更深，脚心区域可以稍浅。
 *
 * 使用注意：
 * - RT ClearColor.a 要是 0.5。
 * - Brush / Accumulate / Ground shader 都要按 signed height 解码 A 通道。
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class FootprintReliefTextureGenerator : EditorWindow
{
    private static class DefaultParams
    {
        public const float AlphaThreshold = 0.45f;
        public const bool PreferAlphaChannel = true;

        public const bool GenerateDecalTexture = true;
        public static readonly Color DecalRgb = new Color(0.34f, 0.37f, 0.25f, 1f);
        public const float InnerAlpha = 0.5f;
        public const float SoftEdgePixels = 10.0f;

        public const bool GenerateHeightTexture = true;
        public const bool GenerateNormalTexture = true;
        public const bool AddGeneratedNoise = false;

        // false = 内部不再是平底坑，而是有“碗形凹陷”坡度。
        public const bool FlatInterior = false;

        // Flat Interior 开启时是否保留脚跟/前掌深浅差。
        // 默认 true，避免完全死平。
        public const bool KeepHeelDepthDifferenceWhenFlat = true;

        // Flat Interior 开启时的基础深度。
        public const float FlatInteriorDepthScale = 0.58f;

        // 内部碗形凹陷强度。
        // 值越大，脚印内部越明显向中心/压力区下陷。
        public const float InteriorBowlStrength = 0.42f;

        // 内部碗形曲线。
        // 小于 1 会让内部过渡更宽、更柔；大于 1 会让凹陷更集中。
        public const float InteriorBowlPower = 0.65f;

        // 脚心区域回弹 / 变浅强度。
        // 值越大，脚心越浅，前掌和脚跟越突出。
        public const float ArchLiftStrength = 0.16f;

        public const float DepressionDepth = 1.0f;
        public const int ReliefBlurIterations = 6;
        public const float GeneratedNormalStrength = 5.0f;
        public const bool InvertNormalXY = false;
        public const bool FlipGreenChannel = false;

        public const bool InnerSideOnRight = true;
        public const bool ToeAtTop = true;
        public const float OuterWallWidth = 60f;
        public const float InnerWallWidth = 30f;

        public const float OuterLipWidth = 44f;
        public const float OuterLipHeight = 0.10f;
        public const float OuterLipRoundness = 0.98f;

        public const float FrontDepthScale = 1.08f;
        public const float HeelDepthScale = 1.18f;
        public const float WholeSoleInfluence = 0.26f;
        public const bool SignedHeightOutput = true;

        // 路线 B / Signed Height：0.5 = 原始地面，<0.5 = 下陷，>0.5 = 泥边隆起。
        public const float HeightBackgroundValue = 0.5f;

        public const bool PutInGeneratedSubfolder = true;
        public const bool SelectGeneratedNormal = true;
    }
    [Header("Input")]
    [SerializeField] private float alphaThreshold = DefaultParams.AlphaThreshold;
    [SerializeField] private bool preferAlphaChannel = DefaultParams.PreferAlphaChannel;

    [Header("Generated Decal Texture")]
    [SerializeField] private bool generateDecalTexture = DefaultParams.GenerateDecalTexture;
    [SerializeField] private Color decalRgb = DefaultParams.DecalRgb;
    [SerializeField] private float innerAlpha = DefaultParams.InnerAlpha;
    [SerializeField] private float softEdgePixels = DefaultParams.SoftEdgePixels;

    [Header("Vertical Depression")]
    [SerializeField] private bool generateHeightTexture = DefaultParams.GenerateHeightTexture;
    [SerializeField] private bool generateNormalTexture = DefaultParams.GenerateNormalTexture;
    [SerializeField] private float depressionDepth = DefaultParams.DepressionDepth;

    [Header("Generated Noise")]
    [Tooltip("关闭后泥边和坑底更平滑；打开后只给坑底加很轻微噪声，不会生成泥块。Flat Interior 开启时，内部噪声会被强制关闭。")]
    [SerializeField] private bool addGeneratedNoise = DefaultParams.AddGeneratedNoise;

    [Header("Interior Shape")]
    [Tooltip("开启后鞋印内部更平整：去掉内部泥面起伏，并让坑底更快进入平面；关闭后内部保留轻微起伏。")]
    [SerializeField] private bool flatInterior = DefaultParams.FlatInterior;

    [Tooltip("Flat Interior 开启时是否仍保留脚跟比前掌略深。关闭时坑底才会完全平。")]
    [SerializeField] private bool keepHeelDepthDifferenceWhenFlat = DefaultParams.KeepHeelDepthDifferenceWhenFlat;

    [Tooltip("Flat Interior 开启且不保留前后深浅差时，整个坑底的统一深度系数。")]
    [SerializeField] private float flatInteriorDepthScale = DefaultParams.FlatInteriorDepthScale;

    [Tooltip("内部碗形凹陷强度。值越大，脚印内部越向中心/压力区下陷。")]
    [Range(0f, 1f)]
    [SerializeField] private float interiorBowlStrength = DefaultParams.InteriorBowlStrength;

    [Tooltip("内部碗形曲线。小于 1 更宽更柔，大于 1 更集中。")]
    [Range(0.25f, 2f)]
    [SerializeField] private float interiorBowlPower = DefaultParams.InteriorBowlPower;

    [Tooltip("脚心区域变浅强度。值越大，脚心越浅，前掌/脚跟压痕越明显。")]
    [Range(0f, 0.5f)]
    [SerializeField] private float archLiftStrength = DefaultParams.ArchLiftStrength;

    [Header("Asymmetric Foot Shape")]
    [Tooltip("脚外侧更斜的坑壁宽度。")]
    [SerializeField] private float outerWallWidth = DefaultParams.OuterWallWidth;

    [Tooltip("脚内侧更接近垂直的坑壁宽度。数值越小越直。")]
    [SerializeField] private float innerWallWidth = DefaultParams.InnerWallWidth;

    [Tooltip("内侧是否在右边。左脚通常打开。")]
    [SerializeField] private bool innerSideOnRight = DefaultParams.InnerSideOnRight;

    [Tooltip("脚尖是否朝上。")]
    [SerializeField] private bool toeAtTop = DefaultParams.ToeAtTop;

    [Header("Smooth Mud Lip")]
    [Tooltip("连续粗泥边宽度。")]
    [SerializeField] private float outerLipWidth = DefaultParams.OuterLipWidth;

    [Tooltip("连续粗泥边高度。")]
    [SerializeField] private float outerLipHeight = DefaultParams.OuterLipHeight;

    [Tooltip("泥边圆滑程度。越大越宽圆，越小越紧。")]
    [Range(0.2f, 1.4f)]
    [SerializeField] private float outerLipRoundness = DefaultParams.OuterLipRoundness;

    [Header("Front / Heel Pressure")]
    [Tooltip("前掌深度系数。")]
    [SerializeField] private float frontDepthScale = DefaultParams.FrontDepthScale;

    [Tooltip("脚跟深度系数。建议比前掌略大。")]
    [SerializeField] private float heelDepthScale = DefaultParams.HeelDepthScale;

    [Tooltip("整体鞋底保留多少轻微下压。")]
    [Range(0f, 1f)]
    [SerializeField] private float wholeSoleInfluence = DefaultParams.WholeSoleInfluence;

    [Header("Normal Map")]
    [SerializeField] private float generatedNormalStrength = DefaultParams.GeneratedNormalStrength;

    [Range(0, 10)]
    [SerializeField] private int reliefBlurIterations = DefaultParams.ReliefBlurIterations;

    [SerializeField] private bool invertNormalXY = DefaultParams.InvertNormalXY;
    [SerializeField] private bool flipGreenChannel = DefaultParams.FlipGreenChannel;

    [Header("Height Output")]
    [Tooltip("开启后高度图以“背景值”为基准，向下表达凹陷，向上表达凸起。关闭后只表达凹陷。")]
    [SerializeField] private bool signedHeightOutput = DefaultParams.SignedHeightOutput;

    [Range(0f, 1f)]
    [Tooltip("直接设置高度图背景 / 原地面的灰度值。0 = 黑，1 = 白。你现在要求纯白背景时就设为 1。")]
    [SerializeField] private float heightBackgroundValue = DefaultParams.HeightBackgroundValue;

    [Header("Output")]
    [SerializeField] private bool putInGeneratedSubfolder = DefaultParams.PutInGeneratedSubfolder;
    [SerializeField] private bool selectGeneratedNormal = DefaultParams.SelectGeneratedNormal;

    private Vector2 scroll;

    [MenuItem("Tools/Footprints/Open Relief Texture Generator")]
    private static void OpenWindow()
    {
        var window = GetWindow<FootprintReliefTextureGenerator>();
        window.titleContent = new GUIContent("Smooth Mud Lip Footprint");
        window.minSize = new Vector2(470, 690);
        window.Show();
    }

    [MenuItem("Tools/Footprints/Generate Relief Textures From Selected")]
    private static void GenerateSelectedWithDefaults()
    {
        var temp = CreateInstance<FootprintReliefTextureGenerator>();
        temp.ApplyDefaults();
        temp.GenerateForCurrentSelection();
        DestroyImmediate(temp);
    }

    [MenuItem("Tools/Footprints/Generate Relief Textures From Selected", true)]
    private static bool ValidateGenerateSelected()
    {
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Texture2D)
                return true;
        }

        return false;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "选择一张脚印 Mask / Alpha 贴图，然后点 Generate。\n" +
            "这版目标：左脚、脚内侧更直、外侧更斜、脚跟略深、外圈是连续平滑粗泥边，不生成小泥块；新增 Flat Interior 开关控制鞋印内部是否平整。Flat Interior 开启时默认关闭前后深浅差，坑底会真正变平。",
            MessageType.Info
        );

        DrawSectionTitle("Input");
        alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", alphaThreshold, 0.01f, 0.99f);
        preferAlphaChannel = EditorGUILayout.Toggle("Prefer Alpha Channel", preferAlphaChannel);

        DrawSectionTitle("Generated Decal Texture");
        generateDecalTexture = EditorGUILayout.Toggle("Generate Decal Texture", generateDecalTexture);
        decalRgb = EditorGUILayout.ColorField("Decal RGB", decalRgb);
        innerAlpha = EditorGUILayout.Slider("Inner Alpha", innerAlpha, 0f, 1f);
        softEdgePixels = EditorGUILayout.Slider("Soft Edge Pixels", softEdgePixels, 0f, 16f);

        DrawSectionTitle("Vertical Depression");
        generateHeightTexture = EditorGUILayout.Toggle("Generate Height Texture", generateHeightTexture);
        generateNormalTexture = EditorGUILayout.Toggle("Generate Normal Texture", generateNormalTexture);
        depressionDepth = EditorGUILayout.Slider("Depression Depth", depressionDepth, 0f, 4f);

        DrawSectionTitle("Generated Noise");
        addGeneratedNoise = EditorGUILayout.Toggle("Add Very Subtle Interior Noise", addGeneratedNoise);

        DrawSectionTitle("Interior Shape");
        flatInterior = EditorGUILayout.Toggle("Flat Interior", flatInterior);
        using (new EditorGUI.DisabledScope(!flatInterior))
        {
            keepHeelDepthDifferenceWhenFlat = EditorGUILayout.Toggle("Keep Heel Deeper When Flat", keepHeelDepthDifferenceWhenFlat);
            flatInteriorDepthScale = EditorGUILayout.Slider("Flat Interior Depth Scale", flatInteriorDepthScale, 0.2f, 2f);
        }

        DrawSectionTitle("Interior Bowl Depression");
        interiorBowlStrength = EditorGUILayout.Slider("Interior Bowl Strength", interiorBowlStrength, 0f, 1f);
        interiorBowlPower = EditorGUILayout.Slider("Interior Bowl Power", interiorBowlPower, 0.25f, 2f);
        archLiftStrength = EditorGUILayout.Slider("Arch Lift Strength", archLiftStrength, 0f, 0.5f);

        DrawSectionTitle("Asymmetric Foot Shape");
        outerWallWidth = EditorGUILayout.Slider("Outer Wall Width", outerWallWidth, 1f, 90f);
        innerWallWidth = EditorGUILayout.Slider("Inner Wall Width", innerWallWidth, 1f, 60f);
        innerSideOnRight = EditorGUILayout.Toggle("Inner Side On Right", innerSideOnRight);
        toeAtTop = EditorGUILayout.Toggle("Toe At Top", toeAtTop);

        DrawSectionTitle("Smooth Mud Lip");
        outerLipWidth = EditorGUILayout.Slider("Outer Lip Width", outerLipWidth, 0f, 80f);
        outerLipHeight = EditorGUILayout.Slider("Outer Lip Height", outerLipHeight, 0f, 1.8f);
        outerLipRoundness = EditorGUILayout.Slider("Outer Lip Roundness", outerLipRoundness, 0.2f, 1.4f);

        DrawSectionTitle("Front / Heel Pressure");
        frontDepthScale = EditorGUILayout.Slider("Front Depth Scale", frontDepthScale, 0.2f, 2f);
        heelDepthScale = EditorGUILayout.Slider("Heel Depth Scale", heelDepthScale, 0.2f, 2f);
        wholeSoleInfluence = EditorGUILayout.Slider("Whole Sole Influence", wholeSoleInfluence, 0f, 1f);

        DrawSectionTitle("Normal Map");
        generatedNormalStrength = EditorGUILayout.Slider("Generated Normal Strength", generatedNormalStrength, 1f, 40f);
        reliefBlurIterations = EditorGUILayout.IntSlider("Relief Blur Iterations", reliefBlurIterations, 0, 10);
        invertNormalXY = EditorGUILayout.Toggle("Invert Normal XY", invertNormalXY);
        flipGreenChannel = EditorGUILayout.Toggle("Flip Green Channel", flipGreenChannel);

        DrawSectionTitle("Height Output");
        signedHeightOutput = EditorGUILayout.Toggle("Signed Height Output", signedHeightOutput);
        heightBackgroundValue = EditorGUILayout.Slider("Height Background Value", heightBackgroundValue, 0f, 1f);

        DrawSectionTitle("Output");
        putInGeneratedSubfolder = EditorGUILayout.Toggle("Put In Generated Folder", putInGeneratedSubfolder);
        selectGeneratedNormal = EditorGUILayout.Toggle("Select Generated Normal", selectGeneratedNormal);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(!HasSelectedTexture()))
        {
            if (GUILayout.Button("Generate For Selected Texture(s)", GUILayout.Height(38)))
                GenerateForCurrentSelection();
        }

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Reset To Recommended Smooth Lip Defaults", GUILayout.Height(28)))
        {
            ApplyDefaults();
            Repaint();
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawSectionTitle(string title)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private static bool HasSelectedTexture()
    {
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Texture2D)
                return true;
        }

        return false;
    }

    private void ApplyDefaults()
    {
        alphaThreshold = DefaultParams.AlphaThreshold;
        preferAlphaChannel = DefaultParams.PreferAlphaChannel;

        generateDecalTexture = DefaultParams.GenerateDecalTexture;
        decalRgb = DefaultParams.DecalRgb;
        innerAlpha = DefaultParams.InnerAlpha;
        softEdgePixels = DefaultParams.SoftEdgePixels;

        generateHeightTexture = DefaultParams.GenerateHeightTexture;
        generateNormalTexture = DefaultParams.GenerateNormalTexture;
        addGeneratedNoise = DefaultParams.AddGeneratedNoise;
        flatInterior = DefaultParams.FlatInterior;
        keepHeelDepthDifferenceWhenFlat = DefaultParams.KeepHeelDepthDifferenceWhenFlat;
        flatInteriorDepthScale = DefaultParams.FlatInteriorDepthScale;
        interiorBowlStrength = DefaultParams.InteriorBowlStrength;
        interiorBowlPower = DefaultParams.InteriorBowlPower;
        archLiftStrength = DefaultParams.ArchLiftStrength;
        depressionDepth = DefaultParams.DepressionDepth;

        outerWallWidth = DefaultParams.OuterWallWidth;
        innerWallWidth = DefaultParams.InnerWallWidth;
        innerSideOnRight = DefaultParams.InnerSideOnRight;
        toeAtTop = DefaultParams.ToeAtTop;

        outerLipWidth = DefaultParams.OuterLipWidth;
        outerLipHeight = DefaultParams.OuterLipHeight;
        outerLipRoundness = DefaultParams.OuterLipRoundness;

        frontDepthScale = DefaultParams.FrontDepthScale;
        heelDepthScale = DefaultParams.HeelDepthScale;
        wholeSoleInfluence = DefaultParams.WholeSoleInfluence;

        generatedNormalStrength = DefaultParams.GeneratedNormalStrength;
        reliefBlurIterations = DefaultParams.ReliefBlurIterations;
        invertNormalXY = DefaultParams.InvertNormalXY;
        flipGreenChannel = DefaultParams.FlipGreenChannel;

        signedHeightOutput = DefaultParams.SignedHeightOutput;
        heightBackgroundValue = DefaultParams.HeightBackgroundValue;

        putInGeneratedSubfolder = DefaultParams.PutInGeneratedSubfolder;
        selectGeneratedNormal = DefaultParams.SelectGeneratedNormal;
    }

    private void GenerateForCurrentSelection()
    {
        Texture2D[] selectedTextures = GetSelectedTextures();

        if (selectedTextures.Length == 0)
        {
            Debug.LogWarning("[FootprintReliefGenerator] Please select at least one Texture2D.");
            return;
        }

        UnityEngine.Object lastGenerated = null;

        foreach (Texture2D texture in selectedTextures)
        {
            UnityEngine.Object generated = GenerateForTexture(texture);
            if (generated != null)
                lastGenerated = generated;
        }

        AssetDatabase.Refresh();

        if (selectGeneratedNormal && lastGenerated != null)
        {
            Selection.activeObject = lastGenerated;
            EditorGUIUtility.PingObject(lastGenerated);
        }
    }

    private static Texture2D[] GetSelectedTextures()
    {
        List<Texture2D> list = new List<Texture2D>();

        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Texture2D texture)
                list.Add(texture);
        }

        return list.ToArray();
    }

    private UnityEngine.Object GenerateForTexture(Texture2D source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogWarning($"[FootprintReliefGenerator] Cannot find asset path for {source.name}.");
            return null;
        }

        MakeTextureReadable(sourcePath);
        Texture2D readableSource = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        if (readableSource == null)
        {
            Debug.LogWarning($"[FootprintReliefGenerator] Cannot reload readable texture at {sourcePath}.");
            return null;
        }

        int width = readableSource.width;
        int height = readableSource.height;
        Color32[] pixels = readableSource.GetPixels32();

        MaskData maskData = BuildMaskData(pixels, width, height);
        if (maskData.insidePixelCount == 0)
        {
            Debug.LogWarning($"[FootprintReliefGenerator] No footprint mask found in {source.name}.");
            return null;
        }

        DistanceData distanceData = BuildDistanceData(maskData.mask, width, height);
        float[] relief = GenerateReliefMap(maskData, distanceData, width, height);

        if (reliefBlurIterations > 0)
            relief = BlurFloatMap(relief, width, height, reliefBlurIterations);

        string outputFolder = GetOutputFolder(sourcePath);
        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);

        UnityEngine.Object lastGenerated = null;

        if (generateDecalTexture)
        {
            Texture2D decalTexture = CreateGeneratedDecalTexture(maskData, distanceData, relief, width, height);
            string decalPath = $"{outputFolder}/{sourceName}_GeneratedDecal.png";
            SavePngTexture(decalTexture, decalPath);
            ConfigureDecalImport(decalPath);
            lastGenerated = AssetDatabase.LoadAssetAtPath<Texture2D>(decalPath);
        }

        if (generateHeightTexture)
        {
            Texture2D heightTexture = CreateHeightTexture(relief, width, height);
            string heightPath = $"{outputFolder}/{sourceName}_GeneratedHeight.png";
            SavePngTexture(heightTexture, heightPath);
            ConfigureLinearDefaultImport(heightPath);
            lastGenerated = AssetDatabase.LoadAssetAtPath<Texture2D>(heightPath);
        }

        if (generateNormalTexture)
        {
            Texture2D normalTexture = CreateNormalTextureFromRelief(relief, width, height, generatedNormalStrength, invertNormalXY, flipGreenChannel);
            string normalPath = $"{outputFolder}/{sourceName}_GeneratedNormal.png";
            SavePngTexture(normalTexture, normalPath);
            ConfigureNormalImport(normalPath);
            lastGenerated = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        }

        Debug.Log($"[FootprintReliefGenerator] Generated textures for {source.name} in {outputFolder}");
        return lastGenerated;
    }

    private struct MaskData
    {
        public bool[] mask;
        public int insidePixelCount;
        public int minX;
        public int maxX;
        public int minY;
        public int maxY;
    }

    private MaskData BuildMaskData(Color32[] pixels, int width, int height)
    {
        bool[] mask = new bool[width * height];

        byte minA = 255;
        byte maxA = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            byte a = pixels[i].a;
            if (a < minA) minA = a;
            if (a > maxA) maxA = a;
        }

        bool hasUsefulAlpha = preferAlphaChannel && (maxA - minA > 8);

        int insideCount = 0;
        int minX = width - 1;
        int maxX = 0;
        int minY = height - 1;
        int maxY = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                Color32 c = pixels[i];

                float value = hasUsefulAlpha
                    ? c.a / 255f
                    : (c.r / 255f * 0.299f + c.g / 255f * 0.587f + c.b / 255f * 0.114f);

                bool inside = value >= alphaThreshold;
                mask[i] = inside;

                if (!inside)
                    continue;

                insideCount++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (insideCount == 0)
        {
            minX = minY = 0;
            maxX = maxY = 0;
        }

        return new MaskData
        {
            mask = mask,
            insidePixelCount = insideCount,
            minX = minX,
            maxX = maxX,
            minY = minY,
            maxY = maxY
        };
    }

    private struct DistanceData
    {
        public float[] distanceToOutside;
        public float[] distanceToInside;
    }

    private DistanceData BuildDistanceData(bool[] mask, int width, int height)
    {
        bool[] outsideTargets = new bool[width * height];
        bool[] insideTargets = new bool[width * height];

        for (int i = 0; i < mask.Length; i++)
        {
            outsideTargets[i] = !mask[i];
            insideTargets[i] = mask[i];
        }

        return new DistanceData
        {
            distanceToOutside = ComputeChamferDistance(outsideTargets, width, height),
            distanceToInside = ComputeChamferDistance(insideTargets, width, height)
        };
    }

    private static float[] ComputeChamferDistance(bool[] target, int width, int height)
    {
        const float INF = 999999f;
        float[] dist = new float[width * height];

        for (int i = 0; i < dist.Length; i++)
            dist[i] = target[i] ? 0f : INF;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Relax(dist, width, height, x, y, x - 1, y, 1f);
                Relax(dist, width, height, x, y, x, y - 1, 1f);
                Relax(dist, width, height, x, y, x - 1, y - 1, 1.41421356f);
                Relax(dist, width, height, x, y, x + 1, y - 1, 1.41421356f);
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                Relax(dist, width, height, x, y, x + 1, y, 1f);
                Relax(dist, width, height, x, y, x, y + 1, 1f);
                Relax(dist, width, height, x, y, x + 1, y + 1, 1.41421356f);
                Relax(dist, width, height, x, y, x - 1, y + 1, 1.41421356f);
            }
        }

        return dist;
    }

    private static void Relax(float[] dist, int width, int height, int x, int y, int nx, int ny, float cost)
    {
        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            return;

        int index = y * width + x;
        int neighborIndex = ny * width + nx;
        float candidate = dist[neighborIndex] + cost;

        if (candidate < dist[index])
            dist[index] = candidate;
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float Smoother01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float Gaussian01(float x, float center, float sigma)
    {
        sigma = Mathf.Max(0.0001f, sigma);
        float t = (x - center) / sigma;
        return Mathf.Exp(-0.5f * t * t);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0x00FFFFFF) / 16777215f;
        }
    }

    private static float ValueNoise01(float x, float y, int seed)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        float tx = x - x0;
        float ty = y - y0;
        tx = Smooth01(tx);
        ty = Smooth01(ty);

        float a = Hash01(x0, y0, seed);
        float b = Hash01(x1, y0, seed);
        float c = Hash01(x0, y1, seed);
        float d = Hash01(x1, y1, seed);

        float ab = Mathf.Lerp(a, b, tx);
        float cd = Mathf.Lerp(c, d, tx);
        return Mathf.Lerp(ab, cd, ty);
    }

    private static float SignedNoise(float x, float y, int seed)
    {
        return ValueNoise01(x, y, seed) * 2f - 1f;
    }

    private float GetToeAxis01(MaskData maskData, int y)
    {
        if (maskData.maxY <= maskData.minY)
            return 0.5f;

        float t = Mathf.InverseLerp(maskData.minY, maskData.maxY, y);
        return toeAtTop ? (1f - t) : t;
    }

    private float GetInnerSide01(MaskData maskData, int x)
    {
        if (maskData.maxX <= maskData.minX)
            return 0.5f;

        float t = Mathf.InverseLerp(maskData.minX, maskData.maxX, x);
        return innerSideOnRight ? t : (1f - t);
    }

    private float[] GenerateReliefMap(MaskData maskData, DistanceData distanceData, int width, int height)
    {
        float[] relief = new float[width * height];

        float depth = Mathf.Max(0f, depressionDepth);
        float safeOuterWall = Mathf.Max(0.001f, outerWallWidth);
        float safeInnerWall = Mathf.Max(0.001f, innerWallWidth);
        float lipWidth = Mathf.Max(0.001f, outerLipWidth);
        float lipHeight = Mathf.Max(0f, outerLipHeight);
        float lipRoundness = Mathf.Max(0.2f, outerLipRoundness);

        // 找出脚印内部最大距离，用来把“离边缘距离”归一化成 0~1。
        // 这个值是内部碗形凹陷的基础：边缘浅，越靠中心越深。
        float maxInsideDistance = 1f;
        for (int i = 0; i < maskData.mask.Length; i++)
        {
            if (maskData.mask[i])
                maxInsideDistance = Mathf.Max(maxInsideDistance, distanceData.distanceToOutside[i]);
        }

        float bowlStrength = Mathf.Clamp01(interiorBowlStrength);
        float bowlPower = Mathf.Max(0.25f, interiorBowlPower);
        float archLift = Mathf.Clamp01(archLiftStrength);

        float interiorNoiseStrength = (!flatInterior && addGeneratedNoise) ? depth * 0.014f : 0f;

        for (int y = 0; y < height; y++)
        {
            float toe01 = GetToeAxis01(maskData, y);
            float heel01 = 1f - toe01;

            // 压力分布：前掌和脚跟更深，脚心略浅。
            // 注意这里不要直接让 pressure 变成横向色带；后面会乘以 bowl01，形成连续内部坡度。
            float heelPatch = Gaussian01(toe01, 0.17f, 0.18f) * heelDepthScale;
            float forePatch = Gaussian01(toe01, 0.76f, 0.20f) * frontDepthScale;
            float archPatch = Gaussian01(toe01, 0.48f, 0.18f);
            float midPatch = Gaussian01(toe01, 0.46f, 0.25f) * (wholeSoleInfluence * 0.35f);

            float pressure = wholeSoleInfluence + heelPatch * 0.38f + forePatch * 0.34f + midPatch;
            pressure -= archPatch * archLift;
            pressure = Mathf.Clamp01(pressure);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                float innerSide01 = Smoother01(GetInnerSide01(maskData, x));
                float outerSide01 = 1f - innerSide01;

                if (maskData.mask[i])
                {
                    float localWallWidth = safeOuterWall * outerSide01 + safeInnerWall * innerSide01;
                    float dIn = distanceData.distanceToOutside[i];

                    // 坑壁坡度：边缘 0，进入脚印内部后逐渐到 1。
                    // 它负责边缘软过渡。
                    float wall01 = Smoother01(Mathf.Clamp01(dIn / localWallWidth));

                    // 内部碗形：基于“离边缘距离 / 内部最大距离”。
                    // 边缘浅，越靠内部越深；power < 1 会让凹陷更宽、更柔。
                    float center01 = Smoother01(Mathf.Clamp01(dIn / maxInsideDistance));
                    float bowl01 = Mathf.Pow(center01, bowlPower);

                    // 轻微脚心回弹：让脚心区域比前掌/脚跟浅一些。
                    float archLift01 = archPatch * archLift;

                    float depthScale;

                    if (flatInterior)
                    {
                        // 仍允许 FlatInterior，但不再强制死平：
                        // 基础是统一深度，叠加少量 bowl，让内部也能有一点凹陷坡度。
                        if (keepHeelDepthDifferenceWhenFlat)
                        {
                            float heelBlend = Smoother01(heel01);
                            depthScale = Mathf.Lerp(frontDepthScale, heelDepthScale, heelBlend) * flatInteriorDepthScale;
                        }
                        else
                        {
                            depthScale = flatInteriorDepthScale;
                        }

                        depthScale += bowl01 * bowlStrength * 0.35f;
                        depthScale -= archLift01 * 0.25f;
                    }
                    else
                    {
                        // 非平底模式：真正的内部碗形凹陷。
                        // pressure 决定前掌/脚跟更深，bowl01 决定从边缘到内部的连续坡度。
                        depthScale = wholeSoleInfluence;
                        depthScale += bowl01 * bowlStrength;
                        depthScale += pressure;
                        depthScale -= archLift01;
                    }

                    depthScale = Mathf.Clamp01(depthScale);

                    float noise = 0f;
                    if (addGeneratedNoise && interiorNoiseStrength > 0f)
                    {
                        float noiseFade = Smoother01(Mathf.Clamp01((dIn - 5f) / 24f));
                        float n1 = SignedNoise(x / 52f, y / 52f, 211);
                        float n2 = SignedNoise(x / 24f, y / 24f, 227);
                        noise = (n1 * 0.72f + n2 * 0.28f) * interiorNoiseStrength * noiseFade;
                    }

                    // 最终内部高度：
                    // wall01 控制边缘软坡，depthScale + bowl01 控制内部不是平底。
                    relief[i] = -depth * depthScale * wall01 + noise;
                }
                else
                {
                    // 外部泥边 / 隆起。
                    // 只在脚印外侧 lipWidth 范围内产生正高度。
                    float dOut = distanceData.distanceToInside[i];
                    if (dOut >= lipWidth)
                    {
                        relief[i] = 0f;
                        continue;
                    }

                    float t = Mathf.Clamp01(dOut / lipWidth);

                    // 宽而低的圆润泥边，不做尖锐高峰。
                    float shoulder = Mathf.Pow(1f - t, lipRoundness);
                    float peakCenter = 0.42f;
                    float peakWidth = 0.34f + (1.0f - lipRoundness) * 0.08f;
                    float peak = Mathf.Exp(-0.5f * Mathf.Pow((t - peakCenter) / Mathf.Max(0.001f, peakWidth), 2f));
                    float outFade = Smoother01(1f - t);
                    float edgeFade = Mathf.Lerp(0.45f, 1f, Smoother01(Mathf.Clamp01(t / 0.24f)));

                    float rim = lipHeight * (0.36f * shoulder + 0.70f * peak) * outFade * edgeFade;

                    // 外侧泥边略强，脚跟略强，模拟被脚挤出去的泥。
                    float sideBias = 1f + 0.08f * outerSide01 - 0.04f * innerSide01;
                    float heelBias = 1f + 0.06f * heel01;

                    relief[i] = Mathf.Max(0f, rim * sideBias * heelBias);
                }
            }
        }

        return relief;
    }

    private Texture2D CreateGeneratedDecalTexture(MaskData maskData, DistanceData distanceData, float[] relief, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        Color[] colors = new Color[width * height];

        float safeSoftEdge = Mathf.Max(0.001f, softEdgePixels);
        float maxDown = Mathf.Max(0.0001f, depressionDepth * Mathf.Max(frontDepthScale, heelDepthScale));
        float maxUp = Mathf.Max(0.0001f, outerLipHeight * 1.8f);

        for (int y = 0; y < height; y++)
        {
            float toe01 = GetToeAxis01(maskData, y);
            float heel01 = 1f - toe01;

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                bool inside = maskData.mask[i];

                float depression01 = Mathf.Clamp01(-relief[i] / maxDown);
                float lip01 = Mathf.Clamp01(relief[i] / maxUp);

                if (!inside && lip01 <= 0.001f)
                {
                    colors[i] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float alpha;
                float shade = 1f;

                if (inside)
                {
                    float d = distanceData.distanceToOutside[i];
                    float a = Smoother01(Mathf.Clamp01(d / safeSoftEdge));
                    alpha = Mathf.Lerp(0f, innerAlpha, a);
                    shade -= depression01 * 0.22f;
                    shade -= heel01 * depression01 * 0.06f;
                }
                else
                {
                    alpha = lip01 * innerAlpha * 0.78f;
                    shade += lip01 * 0.08f;
                }

                if (addGeneratedNoise)
                {
                    float n = SignedNoise(x / 42f, y / 42f, 101);
                    shade += n * 0.018f;
                }

                shade = Mathf.Clamp(shade, 0.48f, 1.12f);

                Color c = decalRgb;
                c.r = Mathf.Clamp01(c.r * shade);
                c.g = Mathf.Clamp01(c.g * shade);
                c.b = Mathf.Clamp01(c.b * shade);
                c.a = Mathf.Clamp01(alpha);
                colors[i] = c;
            }
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    private Texture2D CreateHeightTexture(float[] relief, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Color[] colors = new Color[width * height];

        float maxDown = Mathf.Max(0.0001f, depressionDepth * Mathf.Max(frontDepthScale, heelDepthScale));
        float maxUp = Mathf.Max(0.0001f, outerLipHeight * 1.8f);
        float maxAbs = Mathf.Max(maxDown, maxUp);

        for (int i = 0; i < colors.Length; i++)
        {
            float h;

            float baseHeight = Mathf.Clamp01(heightBackgroundValue);

            if (signedHeightOutput)
            {
                // 以“背景值 / 原地面值”为基准：
                // - relief < 0：往更黑的方向走，表示凹陷
                // - relief > 0：往更白的方向走，表示凸起
                //
                // 这里不用固定 0.5 作为地面，而是直接用用户设置的背景值。
                // 例如：
                // - baseHeight = 1   -> 背景纯白，只能完整表达凹陷；凸起没有更高空间了
                // - baseHeight = 0.5 -> 和传统 signed height 很接近
                // - baseHeight = 0.8 -> 还能留一点空间给凸起，但背景已经比较亮
                float upRoom = 1f - baseHeight;
                float downRoom = baseHeight;

                if (relief[i] >= 0f)
                    h = maxUp > 0f ? baseHeight + (relief[i] / maxUp) * upRoom : baseHeight;
                else
                    h = maxDown > 0f ? baseHeight + (relief[i] / maxDown) * downRoom : baseHeight;
            }
            else
            {
                // 非 signed 模式：
                // 高度图只表达凹陷，不表达凸起。
                // 背景固定为用户设置值，脚印凹陷向更黑的方向下降。
                h = relief[i] < 0f
                    ? Mathf.Clamp01(baseHeight * (1f + relief[i] / maxDown))
                    : baseHeight;
            }

            h = Mathf.Clamp01(h);
            colors[i] = new Color(h, h, h, 1f);
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    private Texture2D CreateNormalTextureFromRelief(float[] relief, int width, int height, float strength, bool invertXY, bool flipGreen)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        Color[] colors = new Color[width * height];

        float sign = invertXY ? 1f : -1f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float hTL = SampleFloatMap(relief, width, height, x - 1, y + 1);
                float hTC = SampleFloatMap(relief, width, height, x, y + 1);
                float hTR = SampleFloatMap(relief, width, height, x + 1, y + 1);
                float hML = SampleFloatMap(relief, width, height, x - 1, y);
                float hMR = SampleFloatMap(relief, width, height, x + 1, y);
                float hBL = SampleFloatMap(relief, width, height, x - 1, y - 1);
                float hBC = SampleFloatMap(relief, width, height, x, y - 1);
                float hBR = SampleFloatMap(relief, width, height, x + 1, y - 1);

                float dx = (hTR + 2f * hMR + hBR) - (hTL + 2f * hML + hBL);
                float dy = (hBL + 2f * hBC + hBR) - (hTL + 2f * hTC + hTR);

                dx *= strength;
                dy *= strength;

                if (flipGreen)
                    dy = -dy;

                Vector3 n = new Vector3(sign * dx, sign * dy, 1f).normalized;

                colors[y * width + x] = new Color(
                    n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f,
                    n.z * 0.5f + 0.5f,
                    1f
                );
            }
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    private static float SampleFloatMap(float[] map, int width, int height, int x, int y)
    {
        x = Mathf.Clamp(x, 0, width - 1);
        y = Mathf.Clamp(y, 0, height - 1);
        return map[y * width + x];
    }

    private static float[] BlurFloatMap(float[] source, int width, int height, int iterations)
    {
        float[] current = source;
        float[] temp = new float[source.Length];

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;

                    sum += SampleFloatMap(current, width, height, x, y) * 4f;
                    sum += SampleFloatMap(current, width, height, x - 1, y) * 2f;
                    sum += SampleFloatMap(current, width, height, x + 1, y) * 2f;
                    sum += SampleFloatMap(current, width, height, x, y - 1) * 2f;
                    sum += SampleFloatMap(current, width, height, x, y + 1) * 2f;
                    sum += SampleFloatMap(current, width, height, x - 1, y - 1);
                    sum += SampleFloatMap(current, width, height, x + 1, y - 1);
                    sum += SampleFloatMap(current, width, height, x - 1, y + 1);
                    sum += SampleFloatMap(current, width, height, x + 1, y + 1);

                    temp[y * width + x] = sum / 16f;
                }
            }

            float[] swap = current;
            current = temp;
            temp = swap;
        }

        if (!ReferenceEquals(current, source))
        {
            float[] result = new float[source.Length];
            Array.Copy(current, result, current.Length);
            return result;
        }

        return current;
    }

    private string GetOutputFolder(string sourcePath)
    {
        string sourceFolder = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");

        if (string.IsNullOrEmpty(sourceFolder))
            sourceFolder = "Assets";

        if (!putInGeneratedSubfolder)
            return sourceFolder;

        string generatedFolder = $"{sourceFolder}/Generated";

        if (!AssetDatabase.IsValidFolder(generatedFolder))
            AssetDatabase.CreateFolder(sourceFolder, "Generated");

        return generatedFolder;
    }

    private static void SavePngTexture(Texture2D texture, string assetPath)
    {
        byte[] bytes = texture.EncodeToPNG();
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);

        string folder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        File.WriteAllBytes(fullPath, bytes);
        DestroyImmediate(texture);
    }

    private static void MakeTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            changed = true;
        }

        if (changed)
            importer.SaveAndReimport();
    }

    private static void ConfigureDecalImport(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        importer.SaveAndReimport();
    }

    private static void ConfigureLinearDefaultImport(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        importer.SaveAndReimport();
    }

    private static void ConfigureNormalImport(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.convertToNormalmap = false;

        importer.SaveAndReimport();
    }
}
