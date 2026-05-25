/*
 * ============================================================
 *  FootprintReliefTextureGenerator_LeftFootSmoothLip_FlatFixed_Commented
 * ============================================================
 *  这是“左脚平滑泥边版脚印生成器”的带详细注释版本。
 *
 *  这个工具做的事情：
 *  1. 读取一张脚印 Mask / Alpha 贴图。
 *  2. 根据脚印轮廓生成一张“浮雕高度场（relief）”：
 *     - 脚印内部是凹陷
 *     - 脚印外圈是一圈平滑隆起的泥边
 *     - 左脚默认：内侧（两脚并拢的一边）更直，外侧更斜
 *     - 可选：坑底是否平整（Flat Interior）
 *     - 可选：脚跟是否比前掌更深
 *  3. 再由这张 relief 同时生成：
 *     - Decal 贴图（颜色/透明度）
 *     - Height 贴图（支持 signed height）
 *     - Normal 贴图
 *
 *  ------------------------------------------------------------
 *  参数分组说明（和 Inspector 面板一一对应）
 *  ------------------------------------------------------------
 *
 *  [Input]
 *  - Alpha Threshold
 *      用来判断“哪些像素算脚印内部”。
 *      如果源贴图是黑白/灰度轮廓，值越大，脚印会越小。
 *
 *  - Prefer Alpha Channel
 *      勾上后，优先读取源图的 Alpha 通道作为脚印轮廓。
 *      如果源图没有可用 Alpha，就退回用 RGB 灰度判断。
 *
 *  [Generated Decal Texture]
 *  - Generate Decal Texture
 *      是否输出一张 Decal 颜色贴图。
 *
 *  - Decal RGB
 *      脚印颜色基色，通常是泥土色、湿泥色。
 *
 *  - Inner Alpha
 *      脚印内部的最大透明度。
 *      越高，Decal 越明显；越低，越像“只靠法线/高度表现”。
 *
 *  - Soft Edge Pixels
 *      Decal 从边缘过渡到内部的柔和宽度。
 *      值越大，Decal 边缘越柔。
 *
 *  [Vertical Depression]
 *  - Generate Height Texture
 *      是否输出 Height 贴图。
 *
 *  - Generate Normal Texture
 *      是否输出 Normal 贴图。
 *
 *  - Depression Depth
 *      整体脚印凹陷的基础深度。
 *      它会影响 Height 以及最终 Normal 的起伏强度。
 *
 *  [Generated Noise]
 *  - Add Very Subtle Interior Noise
 *      是否给鞋印内部加入轻微泥面噪声。
 *      只影响内部，不再生成外圈泥块。
 *
 *  [Interior Shape]
 *  - Flat Interior
 *      开启后：脚印内部尽量做成平底。
 *      关闭后：内部保留更自然的连续凹陷变化。
 *
 *  - Keep Heel Deeper When Flat
 *      只有 Flat Interior 开启时才有意义。
 *      开启后：即使坑底整体较平，也会保留“脚跟略深于前掌”的趋势。
 *      关闭后：坑底真正接近统一深度。
 *
 *  - Flat Interior Depth Scale
 *      Flat Interior 开启且“不保留脚跟更深”时，
 *      用它控制这个“统一平底”的深度系数。
 *
 *  [Asymmetric Foot Shape]
 *  - Outer Wall Width
 *      脚外侧坑壁的过渡宽度。
 *      值越大，坑壁越斜、越缓。
 *
 *  - Inner Wall Width
 *      脚内侧坑壁的过渡宽度。
 *      值越小，内侧越接近垂直。
 *
 *  - Inner Side On Right
 *      是否认为“内侧在右边”。
 *      左脚通常为 true，右脚通常为 false。
 *
 *  - Toe At Top
 *      脚尖是否朝贴图上方。
 *      影响“脚跟更深 / 前掌更浅”的方向判断。
 *
 *  [Smooth Mud Lip]
 *  - Outer Lip Width
 *      外圈泥边的宽度。
 *
 *  - Outer Lip Height
 *      外圈泥边的抬起高度。
 *
 *  - Outer Lip Roundness
 *      泥边轮廓的圆滑程度：越大越宽圆，越小越紧。
 *
 *  [Front / Heel Pressure]
 *  - Front Depth Scale
 *      前掌的深度权重。
 *
 *  - Heel Depth Scale
 *      脚跟的深度权重。
 *
 *  - Whole Sole Influence
 *      整个鞋底保留多少整体下压感。
 *      值越大，整个内部都会更明显地一起下压；
 *      值越小，更多由前掌/脚跟局部决定。
 *
 *  [Normal Map]
 *  - Generated Normal Strength
 *      从 relief 推导 Normal 时的强度倍率。
 *      值越高，法线越“硬”、凹凸越明显。
 *
 *  - Relief Blur Iterations
 *      生成完 relief 后，做多少次模糊。
 *      值越大，泥边和坑底越柔和。
 *
 *  - Invert Normal XY
 *      如果你发现凹陷看起来像凸起，可以切换这个。
 *
 *  - Flip Green Channel
 *      如果法线 Y 方向上下反了，可以切换这个。
 *
 *  [Height Output]
 *  - Signed Height Output
 *      开启后：0.5 = 原地面，< 0.5 = 凹陷，> 0.5 = 凸起泥边。
 *      这对“既有凹陷又有外圈隆起”的脚印更合理。
 *
 *  [Output]
 *  - Put In Generated Folder
 *      是否把输出贴图放到源贴图同目录下的 Generated 文件夹。
 *
 *  - Select Generated Normal
 *      生成后是否自动选中 Normal 贴图。
 *
 *  ------------------------------------------------------------
 *  生成流程概览
 *  ------------------------------------------------------------
 *  BuildMaskData()
 *      读取源图，得到脚印内部布尔 mask。
 *
 *  BuildDistanceData()
 *      计算：
 *      - 脚印内部每个像素到外边界的距离
 *      - 脚印外部每个像素到脚印边界的距离
 *
 *  GenerateReliefMap()
 *      核心步骤。根据 mask + 距离场，生成一张 signed relief：
 *      - 内部是负值（凹陷）
 *      - 外圈泥边是正值（凸起）
 *
 *  CreateGeneratedDecalTexture()
 *      按 relief 生成颜色/透明度贴图。
 *
 *  CreateHeightTexture()
 *      把 signed relief 编码为高度图。
 *
 *  CreateNormalTextureFromRelief()
 *      从 relief 求梯度并编码成法线贴图。
 *
 *  ------------------------------------------------------------
 *  推荐起调参数
 *  ------------------------------------------------------------
 *  - 内部尽量平：
 *      Flat Interior = true
 *      Keep Heel Deeper When Flat = false
 *      Add Very Subtle Interior Noise = false
 *
 *  - 外圈更柔和：
 *      Generated Normal Strength = 5.5 ~ 6.8
 *      Relief Blur Iterations = 3 ~ 4
 *
 *  - 泥边更厚：
 *      Outer Lip Width = 22 ~ 28
 *      Outer Lip Height = 0.45 ~ 0.65
 * ============================================================
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class FootprintReliefTextureGenerator_LeftFootSmoothLip_FlatFixed_Commented : EditorWindow
{
    private static class DefaultParams
    {
        public const float AlphaThreshold = 0.5f;
        public const bool PreferAlphaChannel = true;

        public const bool GenerateDecalTexture = true;
        public static readonly Color DecalRgb = new Color(0.34f, 0.37f, 0.25f, 1f);
        public const float InnerAlpha = 0.68f;
        public const float SoftEdgePixels = 5.0f;

        public const bool GenerateHeightTexture = true;
        public const bool GenerateNormalTexture = true;
        public const bool AddGeneratedNoise = false;

        // 开关：鞋印内部是否保持平整。
        // true = 坑底更像平面，只保留边缘过渡和前后深浅差；false = 坑底允许轻微泥面起伏。
        public const bool FlatInterior = true;

        // Flat Interior 开启时，默认不再保留脚跟/前掌深浅差，这样坑底才是真正平的。
        // 如果你想平底里仍然有很轻微的脚跟更深效果，可以打开这个开关。
        public const bool KeepHeelDepthDifferenceWhenFlat = false;

        public const float FlatInteriorDepthScale = 1.0f;

        public const float DepressionDepth = 1.18f;
        public const int ReliefBlurIterations = 3;
        public const float GeneratedNormalStrength = 6.8f;
        public const bool InvertNormalXY = false;
        public const bool FlipGreenChannel = false;

        public const bool InnerSideOnRight = true;
        public const bool ToeAtTop = true;
        public const float OuterWallWidth = 22f;
        public const float InnerWallWidth = 7.2f;

        public const float OuterLipWidth = 22f;
        public const float OuterLipHeight = 0.58f;
        public const float OuterLipRoundness = 0.72f;

        public const float FrontDepthScale = 0.82f;
        public const float HeelDepthScale = 1.12f;
        public const float WholeSoleInfluence = 0.18f;
        public const bool SignedHeightOutput = true;

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

    [Range(0, 5)]
    [SerializeField] private int reliefBlurIterations = DefaultParams.ReliefBlurIterations;

    [SerializeField] private bool invertNormalXY = DefaultParams.InvertNormalXY;
    [SerializeField] private bool flipGreenChannel = DefaultParams.FlipGreenChannel;

    [Header("Height Output")]
    [Tooltip("开启后 0.5 = 原地面，< 0.5 = 凹陷，> 0.5 = 粗泥边凸起。")]
    [SerializeField] private bool signedHeightOutput = DefaultParams.SignedHeightOutput;

    [Header("Output")]
    [SerializeField] private bool putInGeneratedSubfolder = DefaultParams.PutInGeneratedSubfolder;
    [SerializeField] private bool selectGeneratedNormal = DefaultParams.SelectGeneratedNormal;

    private Vector2 scroll;

    [MenuItem("Tools/Footprints/Open Left-Foot Smooth Mud Lip Generator (Commented)")]
    private static void OpenWindow()
    {
        var window = GetWindow<FootprintReliefTextureGenerator_LeftFootSmoothLip_FlatFixed_Commented>();
        window.titleContent = new GUIContent("Smooth Mud Lip Footprint");
        window.minSize = new Vector2(470, 690);
        window.Show();
    }

    [MenuItem("Tools/Footprints/Generate Left-Foot Smooth Mud Lip (Commented) From Selected")]
    private static void GenerateSelectedWithDefaults()
    {
        var temp = CreateInstance<FootprintReliefTextureGenerator_LeftFootSmoothLip_FlatFixed_Commented>();
        temp.ApplyDefaults();
        temp.GenerateForCurrentSelection();
        DestroyImmediate(temp);
    }

    [MenuItem("Tools/Footprints/Generate Left-Foot Smooth Mud Lip (Commented) From Selected", true)]
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

        DrawSectionTitle("Asymmetric Foot Shape");
        outerWallWidth = EditorGUILayout.Slider("Outer Wall Width", outerWallWidth, 1f, 50f);
        innerWallWidth = EditorGUILayout.Slider("Inner Wall Width", innerWallWidth, 1f, 24f);
        innerSideOnRight = EditorGUILayout.Toggle("Inner Side On Right", innerSideOnRight);
        toeAtTop = EditorGUILayout.Toggle("Toe At Top", toeAtTop);

        DrawSectionTitle("Smooth Mud Lip");
        outerLipWidth = EditorGUILayout.Slider("Outer Lip Width", outerLipWidth, 0f, 36f);
        outerLipHeight = EditorGUILayout.Slider("Outer Lip Height", outerLipHeight, 0f, 1.8f);
        outerLipRoundness = EditorGUILayout.Slider("Outer Lip Roundness", outerLipRoundness, 0.2f, 1.4f);

        DrawSectionTitle("Front / Heel Pressure");
        frontDepthScale = EditorGUILayout.Slider("Front Depth Scale", frontDepthScale, 0.2f, 2f);
        heelDepthScale = EditorGUILayout.Slider("Heel Depth Scale", heelDepthScale, 0.2f, 2f);
        wholeSoleInfluence = EditorGUILayout.Slider("Whole Sole Influence", wholeSoleInfluence, 0f, 1f);

        DrawSectionTitle("Normal Map");
        generatedNormalStrength = EditorGUILayout.Slider("Generated Normal Strength", generatedNormalStrength, 1f, 40f);
        reliefBlurIterations = EditorGUILayout.IntSlider("Relief Blur Iterations", reliefBlurIterations, 0, 5);
        invertNormalXY = EditorGUILayout.Toggle("Invert Normal XY", invertNormalXY);
        flipGreenChannel = EditorGUILayout.Toggle("Flip Green Channel", flipGreenChannel);

        DrawSectionTitle("Height Output");
        signedHeightOutput = EditorGUILayout.Toggle("Signed Height Output", signedHeightOutput);

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

    /// <summary>
    /// 生成 signed relief（浮雕高度场）。
    /// 约定：
    /// - 负值：脚印内部的凹陷
    /// - 正值：脚印外圈的泥边隆起
    /// 这一步是整个工具最核心的逻辑。
    /// </summary>
    private float[] GenerateReliefMap(MaskData maskData, DistanceData distanceData, int width, int height)
    {
        float[] relief = new float[width * height];

        float depth = Mathf.Max(0f, depressionDepth);
        float safeOuterWall = Mathf.Max(0.001f, outerWallWidth);
        float safeInnerWall = Mathf.Max(0.001f, innerWallWidth);
        float lipWidth = Mathf.Max(0.001f, outerLipWidth);
        float lipHeight = Mathf.Max(0f, outerLipHeight);
        float lipRoundness = Mathf.Max(0.2f, outerLipRoundness);

        float interiorNoiseStrength = (!flatInterior && addGeneratedNoise) ? depth * 0.018f : 0f;

        for (int y = 0; y < height; y++)
        {
            float toe01 = GetToeAxis01(maskData, y);
            float heel01 = 1f - toe01;

            float heelPatch = Gaussian01(toe01, 0.17f, 0.18f) * heelDepthScale;
            float forePatch = Gaussian01(toe01, 0.74f, 0.18f) * frontDepthScale;
            float midPatch = Gaussian01(toe01, 0.46f, 0.25f) * (wholeSoleInfluence * 0.38f);
            float pressure = Mathf.Clamp01(wholeSoleInfluence + heelPatch + forePatch + midPatch);

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                float innerSide01 = Smoother01(GetInnerSide01(maskData, x));
                float outerSide01 = 1f - innerSide01;

                if (maskData.mask[i])
                {
                    float localWallWidth = safeOuterWall * outerSide01 + safeInnerWall * innerSide01;
                    float dIn = distanceData.distanceToOutside[i];

                    // Flat Interior 开启时：边缘仍然有斜坡 / 垂直边差异，
                    // 但坑底会更快进入平面，内部不会一直缓慢弯曲。
                    float effectiveWallWidth = flatInterior ? localWallWidth * 0.68f : localWallWidth;
                    float inner01 = Smoother01(Mathf.Clamp01(dIn / Mathf.Max(0.001f, effectiveWallWidth)));
                    if (flatInterior && dIn >= effectiveWallWidth)
                        inner01 = 1f;

                    float noise = 0f;
                    if (addGeneratedNoise && interiorNoiseStrength > 0f)
                    {
                        float noiseFade = Smoother01(Mathf.Clamp01((dIn - 5f) / 16f));
                        float n1 = SignedNoise(x / 42f, y / 42f, 211);
                        float n2 = SignedNoise(x / 18f, y / 18f, 227);
                        noise = (n1 * 0.70f + n2 * 0.30f) * interiorNoiseStrength * noiseFade;
                    }

                    float interiorDepthScale;
                    if (flatInterior)
                    {
                        // 关键修正：Flat Interior 开启时，坑底不能再继续使用 pressure，
                        // 否则前掌 / 脚跟压力曲线会在内部形成你截图里的横向色带。
                        // 默认用统一深度，让坑底真正平整。
                        if (keepHeelDepthDifferenceWhenFlat)
                        {
                            float heelBlend = Smoother01(heel01);
                            interiorDepthScale = Mathf.Lerp(frontDepthScale, heelDepthScale, heelBlend);
                        }
                        else
                        {
                            interiorDepthScale = flatInteriorDepthScale;
                        }
                    }
                    else
                    {
                        interiorDepthScale = pressure;
                    }

                    relief[i] = -depth * interiorDepthScale * inner01 + noise;
                }
                else
                {
                    float dOut = distanceData.distanceToInside[i];
                    if (dOut >= lipWidth)
                    {
                        relief[i] = 0f;
                        continue;
                    }

                    float t = Mathf.Clamp01(dOut / lipWidth);
                    float shoulder = Mathf.Pow(1f - t, lipRoundness);
                    float peakCenter = 0.40f;
                    float peakWidth = 0.30f + (1.0f - lipRoundness) * 0.08f;
                    float peak = Mathf.Exp(-0.5f * Mathf.Pow((t - peakCenter) / Mathf.Max(0.001f, peakWidth), 2f));
                    float outFade = Smoother01(1f - t);
                    float edgeFade = Mathf.Lerp(0.55f, 1f, Smoother01(Mathf.Clamp01(t / 0.22f)));

                    float rim = lipHeight * (0.44f * shoulder + 0.78f * peak) * outFade * edgeFade;
                    float sideBias = 1f + 0.08f * outerSide01 - 0.04f * innerSide01;
                    float heelBias = 1f + 0.08f * heel01;

                    relief[i] = Mathf.Max(0f, rim * sideBias * heelBias);
                }
            }
        }

        return relief;
    }

    /// <summary>
    /// 根据 relief 生成一张颜色贴图（Decal）。
    /// 内部凹陷区域会更明显，泥边区域会略带抬起感和更高 alpha。
    /// </summary>
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

    /// <summary>
    /// 把 signed relief 编码成高度图。
    /// 开启 SignedHeightOutput 时：0.5 代表原地面，低于 0.5 是凹陷，高于 0.5 是凸起。
    /// </summary>
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

            if (signedHeightOutput)
                h = 0.5f + relief[i] / (2f * maxAbs);
            else
                h = relief[i] < 0f ? Mathf.Clamp01(1f + relief[i] / maxDown) : 1f;

            h = Mathf.Clamp01(h);
            colors[i] = new Color(h, h, h, 1f);
        }

        texture.SetPixels(colors);
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>
    /// 从 relief 生成法线贴图。
    /// 这里用的是 Sobel 风格的邻域梯度近似。
    /// </summary>
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
