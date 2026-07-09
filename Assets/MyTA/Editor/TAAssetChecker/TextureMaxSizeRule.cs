using UnityEditor;
using UnityEngine;

public class TextureMaxSizeRule : AssetCheckRule
{
    private readonly int maxSize;

    public override string RuleName => "贴图最大尺寸限制";

    public TextureMaxSizeRule(int maxSize)
    {
        this.maxSize = maxSize;
    }

    public override CheckResult Check(string assetPath)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

        if (texture == null)
            return null;

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
            return null;

        int width = texture.width;
        int height = texture.height;
        int largestSize = Mathf.Max(width, height);

        bool passed = largestSize <= maxSize;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Texture2D",

            ruleName = RuleName,
            currentValue = $"{width} x {height}",
            limitValue = $"最大边 <= {maxSize}",
            passed = passed,

            canFix = !passed && CanFix(assetPath),
            rule = this
        };

        if (passed)
        {
            result.message = "贴图尺寸符合要求。";
        }
        else
        {
            result.message = $"贴图尺寸超过限制。当前最大边为 {largestSize}，限制为 {maxSize}。";
        }

        return result;
    }

    public override bool CanFix(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        return importer != null;
    }

    public override void Fix(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
        {
            Debug.LogWarning($"不是有效的贴图导入器：{assetPath}");
            return;
        }

        Undo.RecordObject(importer, "Fix Texture Max Size");

        importer.maxTextureSize = maxSize;

        importer.SaveAndReimport();

        Debug.Log($"TA Asset Checker: 已修复贴图 Max Size：{assetPath} -> {maxSize}");
    }
}