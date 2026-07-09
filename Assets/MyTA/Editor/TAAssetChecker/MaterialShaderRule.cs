using System.Text;
using UnityEditor;
using UnityEngine;

public class MaterialShaderRule : AssetCheckRule
{
    public override string RuleName => "材质 Shader 检查";

    public override CheckResult Check(string assetPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

        if (material == null)
            return null;

        Shader shader = material.shader;

        bool shaderMissing = shader == null;
        bool isErrorShader = IsErrorShader(shader);

        bool passed = !shaderMissing && !isErrorShader;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Material",

            ruleName = RuleName,
            currentValue = shaderMissing
                ? "Shader 为空"
                : $"Shader：{shader.name}",

            limitValue = "Shader 必须有效，不能是 Missing / Error Shader",
            passed = passed,

            detailMessage = BuildDetailMessage(material, shader, shaderMissing, isErrorShader),

            canFix = false,
            rule = this
        };

        result.message = passed
            ? "材质 Shader 正常。"
            : "材质 Shader 丢失或使用了 Error Shader，可能会显示为粉色。";

        return result;
    }

    private bool IsErrorShader(Shader shader)
    {
        if (shader == null)
            return false;

        return shader.name == "Hidden/InternalErrorShader"
               || shader.name.Contains("InternalErrorShader");
    }

    private string BuildDetailMessage(
        Material material,
        Shader shader,
        bool shaderMissing,
        bool isErrorShader)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("材质信息：");
        builder.AppendLine($"材质名称：{material.name}");

        if (shaderMissing)
        {
            builder.AppendLine("Shader 状态：Missing / 空");
        }
        else
        {
            builder.AppendLine($"Shader 名称：{shader.name}");
        }

        builder.AppendLine();

        if (isErrorShader)
        {
            builder.AppendLine("问题：");
            builder.AppendLine("当前材质使用了 Unity 的 InternalErrorShader。");
            builder.AppendLine("这通常表示原 Shader 丢失、编译失败，或者当前渲染管线不兼容。");
        }
        else if (shaderMissing)
        {
            builder.AppendLine("问题：");
            builder.AppendLine("当前材质没有有效 Shader。");
        }
        else
        {
            builder.AppendLine("当前 Shader 正常。");
        }

        builder.AppendLine();
        builder.AppendLine("建议：");
        builder.AppendLine("如果材质显示粉色，优先检查 Shader 是否存在、是否编译报错、是否兼容当前 URP 管线。");

        return builder.ToString();
    }
}