using System.Text;
using UnityEditor;
using UnityEngine;

public class MaterialRenderQueueRule : AssetCheckRule
{
    private readonly bool allowShaderDefaultQueue;
    private readonly int minQueue;
    private readonly int maxQueue;

    public override string RuleName => "材质 Render Queue 检查";

    public MaterialRenderQueueRule(bool allowShaderDefaultQueue, int minQueue, int maxQueue)
    {
        this.allowShaderDefaultQueue = allowShaderDefaultQueue;
        this.minQueue = minQueue;
        this.maxQueue = maxQueue;
    }

    public override CheckResult Check(string assetPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

        if (material == null)
            return null;

        int renderQueue = material.renderQueue;

        bool useShaderDefaultQueue = renderQueue < 0;

        bool passed;

        if (useShaderDefaultQueue)
        {
            passed = allowShaderDefaultQueue;
        }
        else
        {
            passed = renderQueue >= minQueue && renderQueue <= maxQueue;
        }

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Material",

            ruleName = RuleName,
            currentValue = useShaderDefaultQueue
                ? "使用 Shader 默认 Render Queue：-1"
                : $"Render Queue：{renderQueue}",

            limitValue = allowShaderDefaultQueue
                ? $"允许 -1，或范围 {minQueue} - {maxQueue}"
                : $"范围 {minQueue} - {maxQueue}",

            passed = passed,

            detailMessage = BuildDetailMessage(material, renderQueue, useShaderDefaultQueue),

            canFix = false,
            rule = this
        };

        result.message = passed
            ? "材质 Render Queue 符合要求。"
            : $"材质 Render Queue 异常，当前值为 {renderQueue}。";

        return result;
    }

    private string BuildDetailMessage(Material material, int renderQueue, bool useShaderDefaultQueue)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("Render Queue 说明：");
        builder.AppendLine($"材质名称：{material.name}");
        builder.AppendLine($"当前 Render Queue：{renderQueue}");

        string renderType = material.GetTag("RenderType", false, "未设置");
        string queueTag = material.GetTag("Queue", false, "未设置");

        builder.AppendLine($"Shader RenderType Tag：{renderType}");
        builder.AppendLine($"Shader Queue Tag：{queueTag}");

        builder.AppendLine();

        if (useShaderDefaultQueue)
        {
            builder.AppendLine("当前材质使用 Shader 默认队列。");
            builder.AppendLine("这通常是推荐状态，除非项目要求每个材质显式指定队列。");
        }
        else
        {
            builder.AppendLine("当前材质覆盖了 Shader 默认队列。");
            builder.AppendLine("如果不是有意设置透明、特效、UI 或特殊排序，建议检查是否误改。");
        }

        builder.AppendLine();

        builder.AppendLine("常见队列：");
        builder.AppendLine("Background：1000");
        builder.AppendLine("Geometry：2000");
        builder.AppendLine("AlphaTest：2450");
        builder.AppendLine("Transparent：3000");
        builder.AppendLine("Overlay：4000");

        return builder.ToString();
    }
}