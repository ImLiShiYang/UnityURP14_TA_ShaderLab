using UnityEditor;
using UnityEngine;

public class TextureMipMapRule : AssetCheckRule
{
    private readonly bool expectedEnabled;

    public override string RuleName => "贴图 MipMap 开关检查";

    public TextureMipMapRule(bool expectedEnabled)
    {
        this.expectedEnabled = expectedEnabled;
    }

    public override CheckResult Check(string assetPath)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

        if (texture == null)
            return null;

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

        if (importer == null)
            return null;

        bool currentEnabled = importer.mipmapEnabled;
        bool passed = currentEnabled == expectedEnabled;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Texture2D",

            ruleName = RuleName,
            currentValue = currentEnabled ? "已开启 MipMap" : "未开启 MipMap",
            limitValue = expectedEnabled ? "要求开启 MipMap" : "要求关闭 MipMap",
            passed = passed,

            canFix = !passed && CanFix(assetPath),
            rule = this
        };

        if (passed)
        {
            result.message = "贴图 MipMap 设置符合要求。";
        }
        else
        {
            result.message = expectedEnabled
                ? "贴图未开启 MipMap，但当前规则要求开启。"
                : "贴图开启了 MipMap，但当前规则要求关闭。";
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

        Undo.RecordObject(importer, "Fix Texture MipMap");

        importer.mipmapEnabled = expectedEnabled;

        importer.SaveAndReimport();

        Debug.Log(
            expectedEnabled
                ? $"TA Asset Checker: 已开启贴图 MipMap：{assetPath}"
                : $"TA Asset Checker: 已关闭贴图 MipMap：{assetPath}"
        );
    }
}