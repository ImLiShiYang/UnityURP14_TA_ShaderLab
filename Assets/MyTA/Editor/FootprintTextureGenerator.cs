using System.IO;
using UnityEditor;
using UnityEngine;

public static class FootprintTextureGenerator
{
    [MenuItem("Tools/Footprints/Generate Height Normal From Selected Texture")]
    private static void Generate()
    {
        float maxInsideDistance = 18f;
        float outerRimWidth = 12f;
        float generatedNormalStrength = 8.0f;
        
        Texture2D source = Selection.activeObject as Texture2D;

        if (source == null)
        {
            Debug.LogWarning("Please select a footprint texture first.");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(source);
        MakeTextureReadable(sourcePath);

        source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

        int width = source.width;
        int height = source.height;

        Color32[] pixels = source.GetPixels32();

        bool[] mask = new bool[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            mask[i] = pixels[i].a > 127;
        }

        // 到“非脚印区域”的距离：脚印内部用
        float[] insideDistance = ComputeChamferDistance(mask, width, height, targetIsInside: false);

        // 到“脚印区域”的距离：脚印外部泥边用
        float[] outsideDistance = ComputeChamferDistance(mask, width, height, targetIsInside: true);

        float[] heightMap = new float[width * height];

        

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                bool inside = mask[index];

                float h = 0f;

                if (inside)
                {
                    // 脚印内部：离边缘越远越深
                    float d = Mathf.Clamp01(insideDistance[index] / maxInsideDistance);

                    // 边缘浅，中心深
                    h = Mathf.SmoothStep(0.25f, 1.0f, d);

                    // 加一点程序化泥地噪声，不需要手画
                    float noise = Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
                    h += (noise - 0.5f) * 0.08f;

                    h = Mathf.Clamp01(h);
                }
                else
                {
                    // 脚印外侧一圈泥边
                    float d = outsideDistance[index];

                    if (d < outerRimWidth)
                    {
                        float rim = 1f - Mathf.Clamp01(d / outerRimWidth);

                        // 泥边不要太高，只做一圈轻微隆起
                        h = rim * 0.35f;
                    }
                }

                heightMap[index] = Mathf.Clamp01(h);
            }
        }

        Texture2D heightTexture = CreateHeightTexture(heightMap, width, height);
        Texture2D normalTexture = CreateNormalTexture(heightMap, width, height, normalStrength: generatedNormalStrength);

        string folder = Path.GetDirectoryName(sourcePath);
        string name = Path.GetFileNameWithoutExtension(sourcePath);

        string heightPath = $"{folder}/{name}_GeneratedHeight.png";
        string normalPath = $"{folder}/{name}_GeneratedNormal.png";

        File.WriteAllBytes(heightPath, heightTexture.EncodeToPNG());
        File.WriteAllBytes(normalPath, normalTexture.EncodeToPNG());

        AssetDatabase.Refresh();

        SetNormalMapImport(normalPath);

        Debug.Log($"Generated:\n{heightPath}\n{normalPath}");
    }

    private static Texture2D CreateHeightTexture(float[] heightMap, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

        Color[] colors = new Color[width * height];

        for (int i = 0; i < heightMap.Length; i++)
        {
            float h = heightMap[i];
            colors[i] = new Color(h, h, h, 1f);
        }

        texture.SetPixels(colors);
        texture.Apply();

        return texture;
    }

    private static Texture2D CreateNormalTexture(float[] heightMap, int width, int height, float normalStrength)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

        Color[] colors = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float hL = SampleHeight(heightMap, width, height, x - 1, y);
                float hR = SampleHeight(heightMap, width, height, x + 1, y);
                float hD = SampleHeight(heightMap, width, height, x, y - 1);
                float hU = SampleHeight(heightMap, width, height, x, y + 1);

                float dx = (hR - hL) * normalStrength;
                float dy = (hU - hD) * normalStrength;

                // 如果凹凸方向反了，就把 dx/dy 前面的负号去掉或加上
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;

                Color c = new Color(
                    n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f,
                    n.z * 0.5f + 0.5f,
                    1f
                );

                colors[y * width + x] = c;
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        return texture;
    }

    private static float SampleHeight(float[] heightMap, int width, int height, int x, int y)
    {
        x = Mathf.Clamp(x, 0, width - 1);
        y = Mathf.Clamp(y, 0, height - 1);

        return heightMap[y * width + x];
    }

    private static float[] ComputeChamferDistance(bool[] mask, int width, int height, bool targetIsInside)
    {
        const float INF = 999999f;

        float[] dist = new float[width * height];

        for (int i = 0; i < dist.Length; i++)
        {
            bool isInside = mask[i];
            bool isTarget = isInside == targetIsInside;

            dist[i] = isTarget ? 0f : INF;
        }

        // forward pass
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                TryRelax(dist, width, height, x, y, x - 1, y, 1f);
                TryRelax(dist, width, height, x, y, x, y - 1, 1f);
                TryRelax(dist, width, height, x, y, x - 1, y - 1, 1.4142f);
                TryRelax(dist, width, height, x, y, x + 1, y - 1, 1.4142f);
            }
        }

        // backward pass
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1; x >= 0; x--)
            {
                TryRelax(dist, width, height, x, y, x + 1, y, 1f);
                TryRelax(dist, width, height, x, y, x, y + 1, 1f);
                TryRelax(dist, width, height, x, y, x + 1, y + 1, 1.4142f);
                TryRelax(dist, width, height, x, y, x - 1, y + 1, 1.4142f);
            }
        }

        return dist;
    }

    private static void TryRelax(
        float[] dist,
        int width,
        int height,
        int x,
        int y,
        int nx,
        int ny,
        float cost)
    {
        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            return;

        int i = y * width + x;
        int ni = ny * width + nx;

        float candidate = dist[ni] + cost;

        if (candidate < dist[i])
        {
            dist[i] = candidate;
        }
    }

    private static void MakeTextureReadable(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            return;

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }

    private static void SetNormalMapImport(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }
}