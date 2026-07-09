using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class MaterialNormalMapRule : AssetCheckRule
{
    public override string RuleName => "材质法线贴图类型检查";

    public override CheckResult Check(string assetPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

        if (material == null)
            return null;

        Shader shader = material.shader;

        if (shader == null || IsErrorShader(shader))
        {
            return new CheckResult
            {
                assetPath = assetPath,
                assetType = "Material",

                ruleName = RuleName,
                currentValue = "Shader 无效，跳过法线贴图检查",
                limitValue = "材质 Shader 有效时，法线贴图槽位应使用 Normal Map 类型贴图",
                passed = true,

                detailMessage = "当前材质 Shader 无效，法线贴图槽位无法可靠检查。\n请先修复材质 Shader。",

                canFix = false,
                rule = this,
                message = "Shader 无效，已跳过法线贴图检查。"
            };
        }

        List<NormalMapIssue> issues = FindNormalMapIssues(material, shader);

        bool passed = issues.Count == 0;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Material",

            ruleName = RuleName,
            currentValue = passed
                ? "没有发现错误的法线贴图类型"
                : $"发现 {issues.Count} 个法线贴图类型问题",

            limitValue = "法线贴图槽位中的 Texture Import Type 应为 Normal Map",
            passed = passed,

            detailMessage = BuildDetailMessage(material, issues),

            canFix = false,
            rule = this
        };

        result.message = passed
            ? "材质法线贴图类型正常。"
            : "材质法线贴图槽位中存在非 Normal Map 类型贴图。";

        return result;
    }

    private List<NormalMapIssue> FindNormalMapIssues(Material material, Shader shader)
    {
        List<NormalMapIssue> issues = new List<NormalMapIssue>();

        int propertyCount = ShaderUtil.GetPropertyCount(shader);

        for (int i = 0; i < propertyCount; i++)
        {
            ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, i);

            if (propertyType != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            string propertyDescription = ShaderUtil.GetPropertyDescription(shader, i);

            if (!IsNormalMapProperty(propertyName, propertyDescription))
                continue;

            Texture texture = material.GetTexture(propertyName);

            if (texture == null)
                continue;

            string texturePath = AssetDatabase.GetAssetPath(texture);

            if (string.IsNullOrEmpty(texturePath))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;

            if (importer == null)
                continue;

            if (importer.textureType != TextureImporterType.NormalMap)
            {
                issues.Add(new NormalMapIssue
                {
                    propertyName = propertyName,
                    propertyDescription = propertyDescription,
                    texturePath = texturePath,
                    currentTextureType = importer.textureType
                });
            }
        }

        return issues;
    }

    private bool IsNormalMapProperty(string propertyName, string propertyDescription)
    {
        string lowerName = propertyName.ToLower();
        string lowerDescription = propertyDescription.ToLower();

        return lowerName.Contains("normal")
               || lowerName.Contains("bump")
               || lowerDescription.Contains("normal")
               || lowerDescription.Contains("bump");
    }

    private bool IsErrorShader(Shader shader)
    {
        if (shader == null)
            return false;

        return shader.name == "Hidden/InternalErrorShader"
               || shader.name.Contains("InternalErrorShader");
    }

    private string BuildDetailMessage(Material material, List<NormalMapIssue> issues)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("材质法线贴图检查：");
        builder.AppendLine($"材质名称：{material.name}");
        builder.AppendLine();

        if (issues.Count == 0)
        {
            builder.AppendLine("没有发现法线贴图类型问题。");
            builder.AppendLine("如果材质没有使用法线贴图，也会通过此项检查。");
            return builder.ToString();
        }

        builder.AppendLine("发现以下问题：");

        for (int i = 0; i < issues.Count; i++)
        {
            NormalMapIssue issue = issues[i];

            builder.AppendLine($"{i + 1}. 属性：{issue.propertyName}");
            builder.AppendLine($"   显示名：{issue.propertyDescription}");
            builder.AppendLine($"   贴图：{issue.texturePath}");
            builder.AppendLine($"   当前 Texture Type：{issue.currentTextureType}");
            builder.AppendLine("   要求 Texture Type：Normal Map");
            builder.AppendLine();
        }

        builder.AppendLine("建议：");
        builder.AppendLine("选中对应贴图，在 Inspector 中把 Texture Type 改为 Normal Map，然后 Apply。");

        return builder.ToString();
    }

    private class NormalMapIssue
    {
        public string propertyName;
        public string propertyDescription;
        public string texturePath;
        public TextureImporterType currentTextureType;
    }
}