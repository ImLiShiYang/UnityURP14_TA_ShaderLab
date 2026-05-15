using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FootprintReliefTextureGenerator : EditorWindow
{
    private static class DefaultParams
    {
        public const float AlphaThreshold = 0.5f;
        public const bool PreferAlphaChannel = true;

        public const bool GenerateDecalTexture = true;
        public static readonly Color DecalRgb = new Color(0.035f, 0.05f, 0.047f, 1f);
        public const float InnerAlpha = 0.8f;
        public const float SoftEdgePixels = 3.0f;

        public const bool GenerateHeightTexture = true;
        public const bool GenerateNormalTexture = true;

        // 只控制“垂直下压”的深度。
        public const float DepressionDepth = 2.4f;

        // 0 = 保持最硬的垂直边缘；1 = 稍微柔一点。
        public const int ReliefBlurIterations = 1;

        public const float GeneratedNormalStrength = 18f;
        public const bool InvertNormalXY = false;
        public const bool FlipGreenChannel = false;

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

    [Tooltip("脚印内部统一下压深度。值越大，Height 越黑，Normal 边缘越强。")]
    [SerializeField] private float depressionDepth = DefaultParams.DepressionDepth;

    [Header("Normal Map")]
    [Tooltip("从 Height 生成 Normal 的强度。")]
    [SerializeField] private float generatedNormalStrength = DefaultParams.GeneratedNormalStrength;

    [Tooltip("0 = 最垂直最硬；1 = 稍微柔化边缘；不建议超过 1。")]
    [Range(0, 3)]
    [SerializeField] private int reliefBlurIterations = DefaultParams.ReliefBlurIterations;

    [Tooltip("如果看起来像凸起而不是踩下去，就切换这个。")]
    [SerializeField] private bool invertNormalXY = DefaultParams.InvertNormalXY;

    [Tooltip("如果 Unity 里 Normal 的上下方向反了，就切换这个。")]
    [SerializeField] private bool flipGreenChannel = DefaultParams.FlipGreenChannel;

    [Header("Output")]
    [SerializeField] private bool putInGeneratedSubfolder = DefaultParams.PutInGeneratedSubfolder;
    [SerializeField] private bool selectGeneratedNormal = DefaultParams.SelectGeneratedNormal;

    private Vector2 scroll;

    [MenuItem("Tools/Footprints/Open Relief Texture Generator")]
    private static void OpenWindow()
    {
        FootprintReliefTextureGenerator window = GetWindow<FootprintReliefTextureGenerator>();
        window.titleContent = new GUIContent("Footprint Relief Generator");
        window.minSize = new Vector2(420, 520);
        window.Show();
    }

    [MenuItem("Tools/Footprints/Generate Relief Textures From Selected")]
    private static void GenerateSelectedWithDefaultSettings()
    {
        FootprintReliefTextureGenerator temp = CreateInstance<FootprintReliefTextureGenerator>();
        temp.ApplyDefaults();
        temp.GenerateForCurrentSelection();
        DestroyImmediate(temp);
    }

    [MenuItem("Tools/Footprints/Generate Relief Textures From Selected", true)]
    private static bool ValidateGenerateSelected()
    {
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Texture2D) return true;
        }

        return false;
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0x00FFFFFF) / 16777215.0f;
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

        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

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
    
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space(6);

        EditorGUILayout.HelpBox(
            "选择一张二值脚印 Mask / Alpha 贴图，然后点击 Generate。\n" +
            "这版只生成：脚印内部整体垂直下压，外部完全保持地面。\n" +
            "没有外圈泥边，没有碗形，没有前掌/后跟压力，没有鞋底纹路。",
            MessageType.Info
        );

        DrawSectionTitle("Input");
        alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", alphaThreshold, 0.01f, 0.99f);
        preferAlphaChannel = EditorGUILayout.Toggle("Prefer Alpha Channel", preferAlphaChannel);

        DrawSectionTitle("Generated Decal Texture");
        generateDecalTexture = EditorGUILayout.Toggle("Generate Decal Texture", generateDecalTexture);
        decalRgb = EditorGUILayout.ColorField("Decal RGB", decalRgb);
        innerAlpha = EditorGUILayout.Slider("Inner Alpha", innerAlpha, 0f, 1f);
        softEdgePixels = EditorGUILayout.Slider("Soft Edge Pixels", softEdgePixels, 0f, 8f);

        DrawSectionTitle("Vertical Depression");
        generateHeightTexture = EditorGUILayout.Toggle("Generate Height Texture", generateHeightTexture);
        generateNormalTexture = EditorGUILayout.Toggle("Generate Normal Texture", generateNormalTexture);
        depressionDepth = EditorGUILayout.Slider("Depression Depth", depressionDepth, 0f, 4f);

        DrawSectionTitle("Normal Map");
        generatedNormalStrength = EditorGUILayout.Slider("Generated Normal Strength", generatedNormalStrength, 1f, 50f);
        reliefBlurIterations = EditorGUILayout.IntSlider("Relief Blur Iterations", reliefBlurIterations, 0, 3);
        invertNormalXY = EditorGUILayout.Toggle("Invert Normal XY", invertNormalXY);
        flipGreenChannel = EditorGUILayout.Toggle("Flip Green Channel", flipGreenChannel);

        DrawSectionTitle("Output");
        putInGeneratedSubfolder = EditorGUILayout.Toggle("Put In Generated Folder", putInGeneratedSubfolder);
        selectGeneratedNormal = EditorGUILayout.Toggle("Select Generated Normal", selectGeneratedNormal);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(!HasSelectedTexture()))
        {
            if (GUILayout.Button("Generate For Selected Texture(s)", GUILayout.Height(38)))
            {
                GenerateForCurrentSelection();
            }
        }

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Reset To Vertical Depression Defaults", GUILayout.Height(28)))
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
            if (obj is Texture2D) return true;
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
        depressionDepth = DefaultParams.DepressionDepth;

        reliefBlurIterations = DefaultParams.ReliefBlurIterations;
        generatedNormalStrength = DefaultParams.GeneratedNormalStrength;
        invertNormalXY = DefaultParams.InvertNormalXY;
        flipGreenChannel = DefaultParams.FlipGreenChannel;

        putInGeneratedSubfolder = DefaultParams.PutInGeneratedSubfolder;
        selectGeneratedNormal = DefaultParams.SelectGeneratedNormal;
    }

    private void GenerateForCurrentSelection()
    {
        Texture2D[] selectedTextures = GetSelectedTextures();

        if (selectedTextures.Length == 0)
        {
            Debug.LogWarning("FootprintReliefTextureGenerator: Please select at least one Texture2D.");
            return;
        }

        UnityEngine.Object lastGenerated = null;

        foreach (Texture2D texture in selectedTextures)
        {
            UnityEngine.Object generated = GenerateForTexture(texture);
            if (generated != null) lastGenerated = generated;
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
            if (obj is Texture2D texture) list.Add(texture);
        }

        return list.ToArray();
    }

    private UnityEngine.Object GenerateForTexture(Texture2D source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);

        if (string.IsNullOrEmpty(sourcePath))
        {
            Debug.LogWarning($"FootprintReliefTextureGenerator: Cannot find asset path for {source.name}.");
            return null;
        }

        MakeTextureReadable(sourcePath);

        Texture2D readableSource = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

        if (readableSource == null)
        {
            Debug.LogWarning($"FootprintReliefTextureGenerator: Cannot reload readable texture at {sourcePath}.");
            return null;
        }

        int width = readableSource.width;
        int height = readableSource.height;
        Color32[] pixels = readableSource.GetPixels32();

        MaskData maskData = BuildMaskData(pixels, width, height);

        if (maskData.insidePixelCount == 0)
        {
            Debug.LogWarning($"FootprintReliefTextureGenerator: No footprint mask found in {source.name}.");
            return null;
        }

        DistanceData distanceData = BuildDistanceData(maskData.mask, width, height);

        float[] relief = GenerateReliefMap(maskData, distanceData, width, height);

        if (reliefBlurIterations > 0)
        {
            relief = BlurFloatMap(relief, width, height, reliefBlurIterations);
        }

        string outputFolder = GetOutputFolder(sourcePath);
        string sourceName = Path.GetFileNameWithoutExtension(sourcePath);

        UnityEngine.Object lastGenerated = null;

        if (generateDecalTexture)
        {
            Texture2D decalTexture = CreateGeneratedDecalTexture(maskData, distanceData, width, height);
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

        Debug.Log($"FootprintReliefTextureGenerator: Generated textures for {source.name} in {outputFolder}");
        return lastGenerated;
    }

    private struct MaskData
    {
        public bool[] mask;
        public int insidePixelCount;
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

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            float value;

            if (hasUsefulAlpha)
            {
                value = c.a / 255f;
            }
            else
            {
                value = c.r / 255f * 0.299f + c.g / 255f * 0.587f + c.b / 255f * 0.114f;
            }

            bool inside = value >= alphaThreshold;
            mask[i] = inside;

            if (inside) insideCount++;
        }

        return new MaskData { mask = mask, insidePixelCount = insideCount };
    }

    private struct DistanceData
    {
        public float[] distanceToOutside;
    }

    private DistanceData BuildDistanceData(bool[] mask, int width, int height)
    {
        bool[] outsideTargets = new bool[width * height];

        for (int i = 0; i < mask.Length; i++)
        {
            outsideTargets[i] = !mask[i];
        }

        float[] distanceToOutside = ComputeChamferDistance(outsideTargets, width, height);

        return new DistanceData { distanceToOutside = distanceToOutside };
    }

    private static float[] ComputeChamferDistance(bool[] target, int width, int height)
    {
        const float INF = 999999f;
        float[] dist = new float[width * height];

        for (int i = 0; i < dist.Length; i++)
        {
            dist[i] = target[i] ? 0f : INF;
        }

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
        if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;

        int index = y * width + x;
        int neighborIndex = ny * width + nx;
        float candidate = dist[neighborIndex] + cost;

        if (candidate < dist[index]) dist[index] = candidate;
    }

    private float[] GenerateReliefMap(MaskData maskData, DistanceData distanceData, int width, int height)
    {
        float[] relief = new float[width * height];

        float depth = Mathf.Max(0f, depressionDepth);

        // 坑底噪声强度。不要太大，否则泥坑会变脏。
        float heightNoiseStrength = depth * 0.055f;

        // 噪声尺寸。越大，噪声越粗；越小，噪声越碎。
        float largeNoiseScale = 32f;
        float smallNoiseScale = 11f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                if (maskData.mask[i])
                {
                    float edgeDistance = distanceData.distanceToOutside[i];

                    // 边缘附近少加一点噪声，避免坑壁变毛。
                    float innerFade = Mathf.Clamp01(edgeDistance / 8f);
                    innerFade = Smooth01(innerFade);

                    float n1 = SignedNoise(x / largeNoiseScale, y / largeNoiseScale, 11);
                    float n2 = SignedNoise(x / smallNoiseScale, y / smallNoiseScale, 37);

                    float noise = (n1 * 0.7f + n2 * 0.3f) * heightNoiseStrength * innerFade;

                    // 基础是整体下压，噪声只是在坑底做轻微起伏。
                    relief[i] = -depth + noise;
                }
                else
                {
                    relief[i] = 0f;
                }
            }
        }

        return relief;
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float SampleDistance(float[] map, int width, int height, int x, int y)
    {
        x = Mathf.Clamp(x, 0, width - 1);
        y = Mathf.Clamp(y, 0, height - 1);
        return map[y * width + x];
    }

    
    
    private Texture2D CreateGeneratedDecalTexture(MaskData maskData, DistanceData distanceData, int width, int height)
{
    Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
    Color[] colors = new Color[width * height];

    float safeSoftEdge = Mathf.Max(0.001f, softEdgePixels);

    // 坑底基础色。建议别太黑，否则细节看不出来。
    Color pitBaseColor = decalRgb;

    // 视觉坑壁宽度：越大，边缘暗圈越宽。
    float visualWallWidth = 10f;

    // 泥土噪声强度：只影响 RGB，不影响 Alpha。
    float noiseStrength = 0.16f;

    // 假光方向：决定哪一侧稍亮、哪一侧稍暗。
    Vector2 fakeLightDir = new Vector2(-0.55f, 0.85f).normalized;

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;

            if (!maskData.mask[i])
            {
                colors[i] = new Color(0f, 0f, 0f, 0f);
                continue;
            }

            float d = distanceData.distanceToOutside[i];

            // Alpha：只负责干净轮廓，不加噪声。
            float alpha;
            if (softEdgePixels <= 0.001f)
            {
                alpha = innerAlpha;
            }
            else
            {
                float a = Mathf.Clamp01(d / safeSoftEdge);
                a = Smooth01(a);
                alpha = Mathf.Lerp(0f, innerAlpha, a);
            }

            // edge01：越靠近脚印内部边缘越接近 1，越往坑底内部越接近 0。
            float edge01 = 1f - Mathf.Clamp01(d / visualWallWidth);
            edge01 = Smooth01(edge01);

            // inner01：离边缘稍远后才逐渐显示坑底噪声，避免边缘毛。
            float inner01 = Mathf.Clamp01(d / 8f);
            inner01 = Smooth01(inner01);

            // 用 distance gradient 估算“坑壁方向”，做一个假的侧向明暗。
            float dL = SampleDistance(distanceData.distanceToOutside, width, height, x - 1, y);
            float dR = SampleDistance(distanceData.distanceToOutside, width, height, x + 1, y);
            float dD = SampleDistance(distanceData.distanceToOutside, width, height, x, y - 1);
            float dU = SampleDistance(distanceData.distanceToOutside, width, height, x, y + 1);

            Vector2 grad = new Vector2(dR - dL, dU - dD);
            if (grad.sqrMagnitude > 0.00001f) grad.Normalize();

            float sideLight = Vector2.Dot(grad, fakeLightDir);

            // 内边缘暗圈：让脚印看起来像有坑壁。
            float innerWallDark = edge01 * 0.34f;

            // 背光侧更暗。
            float sideShadow = Mathf.Clamp01(-sideLight) * edge01 * 0.18f;

            // 受光侧稍亮。
            float sideHighlight = Mathf.Clamp01(sideLight) * edge01 * 0.16f;

            // 坑底整体暗一点，但别全黑。
            float pitDark = 0.12f;

            // 泥土噪声：大块 + 小块混合。
            float n1 = SignedNoise(x / 30f, y / 30f, 101);
            float n2 = SignedNoise(x / 9f, y / 9f, 203);
            float noise = (n1 * 0.65f + n2 * 0.35f) * noiseStrength * inner01;

            // 最终明暗系数。
            float shade = 1f;
            shade -= pitDark;
            shade -= innerWallDark;
            shade -= sideShadow;
            shade += sideHighlight;
            shade += noise;
            shade = Mathf.Clamp(shade, 0.42f, 1.12f);

            Color c = pitBaseColor;
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

        // shader 里 HeightGround = 0.5，所以正常地面写 0.5。
        float ground = 0.5f;

        // 这个越大，脚印内部越黑。只影响 Height 贴图灰度，不改变 relief 本身。
        float depressionScale = 0.16f;

        for (int i = 0; i < relief.Length; i++)
        {
            float h = ground + relief[i] * depressionScale;
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
                float hL = SampleFloatMap(relief, width, height, x - 1, y);
                float hR = SampleFloatMap(relief, width, height, x + 1, y);
                float hD = SampleFloatMap(relief, width, height, x, y - 1);
                float hU = SampleFloatMap(relief, width, height, x, y + 1);

                float dx = (hR - hL) * strength;
                float dy = (hU - hD) * strength;

                if (flipGreen) dy = -dy;

                Vector3 n = new Vector3(sign * dx, sign * dy, 1f).normalized;

                Color c = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                colors[y * width + x] = c;
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
        {
            sourceFolder = "Assets";
        }

        if (!putInGeneratedSubfolder)
        {
            return sourceFolder;
        }

        string generatedFolder = $"{sourceFolder}/Generated";

        if (!AssetDatabase.IsValidFolder(generatedFolder))
        {
            AssetDatabase.CreateFolder(sourceFolder, "Generated");
        }

        return generatedFolder;
    }

    private static void SavePngTexture(Texture2D texture, string assetPath)
    {
        byte[] bytes = texture.EncodeToPNG();
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        File.WriteAllBytes(fullPath, bytes);
        DestroyImmediate(texture);
    }

    private static void MakeTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
        {
            return;
        }

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
        {
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureDecalImport(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
        {
            return;
        }

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
        {
            return;
        }

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
        {
            return;
        }

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // 这里生成的已经是 RGB normal，不需要 Unity 从灰度图再转换一次。
        importer.convertToNormalmap = false;

        importer.SaveAndReimport();
    }
}