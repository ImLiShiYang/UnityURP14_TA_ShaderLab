using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FootprintDoubleLayerTextureGenerator : EditorWindow
{
    [Header("Input")]
    [SerializeField] private float alphaThreshold = 0.5f;
    [SerializeField] private bool preferAlphaChannel = true;

    [Header("Main Decal")]
    [SerializeField] private Color mainBaseColor = new Color(0.18f, 0.22f, 0.16f, 1f);
    [SerializeField] private float mainInnerAlpha = 0.9f;
    [SerializeField] private float mainSoftEdgePixels = 4f;
    [SerializeField] private float visualWallWidth = 12f;
    [SerializeField] private float edgeDarkStrength = 0.28f;
    [SerializeField] private float pitDarkStrength = 0.10f;
    [SerializeField] private float highlightStrength = 0.10f;
    [SerializeField] private float noiseStrength = 0.12f;

    [Header("Shadow Decal")]
    [SerializeField] private float shadowInsideAlpha = 0.18f;
    [SerializeField] private float shadowOutsideAlpha = 0.22f;
    [SerializeField] private float shadowOutsideWidthPixels = 18f;
    [SerializeField] private int shadowBlurIterations = 2;

    [Header("Output")]
    [SerializeField] private bool putInGeneratedFolder = true;

    private Vector2 scroll;

    [MenuItem("Tools/Footprints/Open Double Layer Decal Generator")]
    private static void OpenWindow()
    {
        FootprintDoubleLayerTextureGenerator window = GetWindow<FootprintDoubleLayerTextureGenerator>();
        window.titleContent = new GUIContent("Footprint Double Layer");
        window.minSize = new Vector2(430, 560);
        window.Show();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox("选择原始脚印 Mask / Alpha 图，然后点击 Generate。会生成 MainDecal 和 ShadowDecal。MainDecal 负责清楚脚印，ShadowDecal 负责压下去的软阴影。", MessageType.Info);

        DrawTitle("Input");
        alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", alphaThreshold, 0.01f, 0.99f);
        preferAlphaChannel = EditorGUILayout.Toggle("Prefer Alpha Channel", preferAlphaChannel);

        DrawTitle("Main Decal");
        mainBaseColor = EditorGUILayout.ColorField("Main Base Color", mainBaseColor);
        mainInnerAlpha = EditorGUILayout.Slider("Main Inner Alpha", mainInnerAlpha, 0f, 1f);
        mainSoftEdgePixels = EditorGUILayout.Slider("Main Soft Edge Pixels", mainSoftEdgePixels, 0f, 16f);
        visualWallWidth = EditorGUILayout.Slider("Visual Wall Width", visualWallWidth, 2f, 32f);
        edgeDarkStrength = EditorGUILayout.Slider("Edge Dark Strength", edgeDarkStrength, 0f, 0.8f);
        pitDarkStrength = EditorGUILayout.Slider("Pit Dark Strength", pitDarkStrength, 0f, 0.5f);
        highlightStrength = EditorGUILayout.Slider("Highlight Strength", highlightStrength, 0f, 0.4f);
        noiseStrength = EditorGUILayout.Slider("Noise Strength", noiseStrength, 0f, 0.4f);

        DrawTitle("Shadow Decal");
        shadowInsideAlpha = EditorGUILayout.Slider("Shadow Inside Alpha", shadowInsideAlpha, 0f, 0.6f);
        shadowOutsideAlpha = EditorGUILayout.Slider("Shadow Outside Alpha", shadowOutsideAlpha, 0f, 0.6f);
        shadowOutsideWidthPixels = EditorGUILayout.Slider("Shadow Outside Width Pixels", shadowOutsideWidthPixels, 0f, 48f);
        shadowBlurIterations = EditorGUILayout.IntSlider("Shadow Blur Iterations", shadowBlurIterations, 0, 8);

        DrawTitle("Output");
        putInGeneratedFolder = EditorGUILayout.Toggle("Put In Generated Folder", putInGeneratedFolder);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(!HasSelectedTexture()))
        {
            if (GUILayout.Button("Generate Double Layer Decals", GUILayout.Height(38)))
            {
                GenerateForSelection();
            }
        }

        if (GUILayout.Button("Reset Recommended Values", GUILayout.Height(28)))
        {
            ResetValues();
        }

        EditorGUILayout.EndScrollView();
    }

    private static void DrawTitle(string title)
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

    private void ResetValues()
    {
        alphaThreshold = 0.5f;
        preferAlphaChannel = true;

        mainBaseColor = new Color(0.18f, 0.22f, 0.16f, 1f);
        mainInnerAlpha = 0.9f;
        mainSoftEdgePixels = 4f;
        visualWallWidth = 12f;
        edgeDarkStrength = 0.28f;
        pitDarkStrength = 0.10f;
        highlightStrength = 0.10f;
        noiseStrength = 0.12f;

        shadowInsideAlpha = 0.18f;
        shadowOutsideAlpha = 0.22f;
        shadowOutsideWidthPixels = 18f;
        shadowBlurIterations = 2;

        putInGeneratedFolder = true;
    }

    private void GenerateForSelection()
    {
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            if (obj is Texture2D tex) GenerateForTexture(tex);
        }

        AssetDatabase.Refresh();
    }

    private void GenerateForTexture(Texture2D source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(sourcePath)) return;

        MakeTextureReadable(sourcePath);

        Texture2D readable = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        int width = readable.width;
        int height = readable.height;
        Color32[] pixels = readable.GetPixels32();

        bool[] mask = BuildMask(pixels, width, height, out int insideCount);

        if (insideCount <= 0)
        {
            Debug.LogWarning($"No footprint pixels found in {source.name}");
            return;
        }

        float[] distanceToOutside = ComputeDistanceToOutside(mask, width, height);
        float[] distanceToInside = ComputeDistanceToInside(mask, width, height);

        Texture2D main = CreateMainDecal(mask, distanceToOutside, width, height);
        Texture2D shadow = CreateShadowDecal(mask, distanceToInside, width, height);

        string folder = GetOutputFolder(sourcePath);
        string name = Path.GetFileNameWithoutExtension(sourcePath);

        string mainPath = $"{folder}/{name}_MainDecal.png";
        string shadowPath = $"{folder}/{name}_ShadowDecal.png";

        SavePng(main, mainPath);
        SavePng(shadow, shadowPath);

        ConfigureDecalImport(mainPath);
        ConfigureDecalImport(shadowPath);

        Debug.Log($"Generated double layer decals: {mainPath}, {shadowPath}");
    }

    private bool[] BuildMask(Color32[] pixels, int width, int height, out int insideCount)
    {
        bool[] mask = new bool[width * height];
        byte minA = 255;
        byte maxA = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            minA = Math.Min(minA, pixels[i].a);
            maxA = Math.Max(maxA, pixels[i].a);
        }

        bool hasUsefulAlpha = preferAlphaChannel && maxA - minA > 8;
        insideCount = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            float v = hasUsefulAlpha ? c.a / 255f : (c.r / 255f * 0.299f + c.g / 255f * 0.587f + c.b / 255f * 0.114f);
            bool inside = v >= alphaThreshold;
            mask[i] = inside;
            if (inside) insideCount++;
        }

        return mask;
    }

    private Texture2D CreateMainDecal(bool[] mask, float[] distanceToOutside, int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        Color[] colors = new Color[width * height];
        Vector2 fakeLightDir = new Vector2(-0.55f, 0.85f).normalized;
        float safeSoft = Mathf.Max(0.001f, mainSoftEdgePixels);
        float safeWall = Mathf.Max(0.001f, visualWallWidth);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                if (!mask[i])
                {
                    colors[i] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float d = distanceToOutside[i];

                float a = Mathf.Clamp01(d / safeSoft);
                a = Smooth01(a);
                float alpha = Mathf.Lerp(0f, mainInnerAlpha, a);

                float edge01 = 1f - Mathf.Clamp01(d / safeWall);
                edge01 = Smooth01(edge01);

                float inner01 = Mathf.Clamp01(d / 8f);
                inner01 = Smooth01(inner01);

                float dL = Sample(distanceToOutside, width, height, x - 1, y);
                float dR = Sample(distanceToOutside, width, height, x + 1, y);
                float dD = Sample(distanceToOutside, width, height, x, y - 1);
                float dU = Sample(distanceToOutside, width, height, x, y + 1);

                Vector2 grad = new Vector2(dR - dL, dU - dD);
                if (grad.sqrMagnitude > 0.00001f) grad.Normalize();

                float side = Vector2.Dot(grad, fakeLightDir);
                float sideShadow = Mathf.Clamp01(-side) * edge01 * 0.16f;
                float sideHighlight = Mathf.Clamp01(side) * edge01 * highlightStrength;

                float n1 = SignedNoise(x / 30f, y / 30f, 101);
                float n2 = SignedNoise(x / 9f, y / 9f, 203);
                float noise = (n1 * 0.65f + n2 * 0.35f) * noiseStrength * inner01;

                float shade = 1f;
                shade -= pitDarkStrength;
                shade -= edge01 * edgeDarkStrength;
                shade -= sideShadow;
                shade += sideHighlight;
                shade += noise;
                shade = Mathf.Clamp(shade, 0.38f, 1.15f);

                Color c = mainBaseColor;
                c.r = Mathf.Clamp01(c.r * shade);
                c.g = Mathf.Clamp01(c.g * shade);
                c.b = Mathf.Clamp01(c.b * shade);
                c.a = Mathf.Clamp01(alpha);

                colors[i] = c;
            }
        }

        tex.SetPixels(colors);
        tex.Apply(false, false);
        return tex;
    }

    private Texture2D CreateShadowDecal(bool[] mask, float[] distanceToInside, int width, int height)
    {
        float[] alpha = new float[width * height];
        float safeWidth = Mathf.Max(0.001f, shadowOutsideWidthPixels);

        for (int i = 0; i < alpha.Length; i++)
        {
            if (mask[i])
            {
                alpha[i] = shadowInsideAlpha;
            }
            else
            {
                float d = distanceToInside[i];
                if (d <= safeWidth)
                {
                    float t = 1f - Mathf.Clamp01(d / safeWidth);
                    t = Smooth01(t);
                    alpha[i] = shadowOutsideAlpha * t;
                }
                else
                {
                    alpha[i] = 0f;
                }
            }
        }

        if (shadowBlurIterations > 0)
        {
            alpha = Blur(alpha, width, height, shadowBlurIterations);
        }

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        Color[] colors = new Color[width * height];

        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha[i]));
        }

        tex.SetPixels(colors);
        tex.Apply(false, false);
        return tex;
    }

    private static float[] ComputeDistanceToOutside(bool[] mask, int width, int height)
    {
        bool[] outside = new bool[width * height];
        for (int i = 0; i < mask.Length; i++) outside[i] = !mask[i];
        return ComputeChamferDistance(outside, width, height);
    }

    private static float[] ComputeDistanceToInside(bool[] mask, int width, int height)
    {
        bool[] inside = new bool[width * height];
        for (int i = 0; i < mask.Length; i++) inside[i] = mask[i];
        return ComputeChamferDistance(inside, width, height);
    }

    private static float[] ComputeChamferDistance(bool[] target, int width, int height)
    {
        const float INF = 999999f;
        float[] dist = new float[width * height];

        for (int i = 0; i < dist.Length; i++) dist[i] = target[i] ? 0f : INF;

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
        int nIndex = ny * width + nx;
        float candidate = dist[nIndex] + cost;

        if (candidate < dist[index]) dist[index] = candidate;
    }

    private static float[] Blur(float[] src, int width, int height, int iterations)
    {
        float[] current = src;
        float[] temp = new float[src.Length];

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    sum += Sample(current, width, height, x, y) * 4f;
                    sum += Sample(current, width, height, x - 1, y) * 2f;
                    sum += Sample(current, width, height, x + 1, y) * 2f;
                    sum += Sample(current, width, height, x, y - 1) * 2f;
                    sum += Sample(current, width, height, x, y + 1) * 2f;
                    sum += Sample(current, width, height, x - 1, y - 1);
                    sum += Sample(current, width, height, x + 1, y - 1);
                    sum += Sample(current, width, height, x - 1, y + 1);
                    sum += Sample(current, width, height, x + 1, y + 1);
                    temp[y * width + x] = sum / 16f;
                }
            }

            float[] swap = current;
            current = temp;
            temp = swap;
        }

        if (ReferenceEquals(current, src)) return current;

        float[] result = new float[src.Length];
        Array.Copy(current, result, current.Length);
        return result;
    }

    private static float Sample(float[] map, int width, int height, int x, int y)
    {
        x = Mathf.Clamp(x, 0, width - 1);
        y = Mathf.Clamp(y, 0, height - 1);
        return map[y * width + x];
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
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

    private string GetOutputFolder(string sourcePath)
    {
        string sourceFolder = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(sourceFolder)) sourceFolder = "Assets";

        if (!putInGeneratedFolder) return sourceFolder;

        string generatedFolder = $"{sourceFolder}/Generated";
        if (!AssetDatabase.IsValidFolder(generatedFolder)) AssetDatabase.CreateFolder(sourceFolder, "Generated");
        return generatedFolder;
    }

    private static void SavePng(Texture2D tex, string assetPath)
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
        DestroyImmediate(tex);
    }

    private static void MakeTextureReadable(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

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

        if (changed) importer.SaveAndReimport();
    }

    private static void ConfigureDecalImport(string assetPath)
    {
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return;

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
}