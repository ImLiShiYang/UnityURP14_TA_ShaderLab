using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;

public class MaterialShaderRule : AssetCheckRule
{
    public override string RuleName => "材质 Shader 检查";

    public override CheckResult Check(string assetPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

        if (material == null)
            return null;

        Shader shader = material.shader;

        // 1. Shader 引用是否丢失
        bool shaderMissing = shader == null;

        // 2. 是否被 Unity 替换成内部错误 Shader
        bool isErrorShader = IsErrorShader(shader);

        // 3. 获取 Shader 的实际编译错误
        List<ShaderMessage> compileErrors = GetCompileErrors(shader);

        bool hasCompileError = compileErrors.Count > 0;

        // 三项都没有问题才算通过
        bool passed =
            !shaderMissing &&
            !isErrorShader &&
            !hasCompileError;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Material",

            ruleName = RuleName,

            currentValue = BuildCurrentValue(
                shader,
                shaderMissing,
                isErrorShader,
                compileErrors.Count
            ),

            limitValue =
                "Shader 必须有效，不能 Missing、不能是 Error Shader，并且不能有编译错误",

            passed = passed,

            detailMessage = BuildDetailMessage(
                material,
                shader,
                shaderMissing,
                isErrorShader,
                compileErrors
            ),

            // Shader 编译错误不适合自动修改
            canFix = false,
            rule = this
        };

        if (passed)
        {
            result.message = "材质 Shader 正常。";
        }
        else if (shaderMissing)
        {
            result.message = "材质 Shader 丢失。";
        }
        else if (isErrorShader)
        {
            result.message = "材质使用了 Unity InternalErrorShader，可能会显示为粉色。";
        }
        else if (hasCompileError)
        {
            result.message =
                $"材质 Shader 存在 {compileErrors.Count} 个编译错误。";
        }

        return result;
    }

    /// <summary>
    /// 获取指定 Shader 的所有编译错误。
    /// ShaderUtil.GetShaderMessages 会同时返回 Error 和 Warning，
    /// 这里仅保留 Error。
    /// </summary>
    private List<ShaderMessage> GetCompileErrors(Shader shader)
    {
        List<ShaderMessage> compileErrors =
            new List<ShaderMessage>();

        if (shader == null)
            return compileErrors;

        ShaderMessage[] messages;

        try
        {
            messages = ShaderUtil.GetShaderMessages(shader);
        }
        catch
        {
            // 某些特殊 Shader 无法读取编译消息时，不让整个扫描中断
            return compileErrors;
        }

        foreach (ShaderMessage message in messages)
        {
            if (message.severity ==
                ShaderCompilerMessageSeverity.Error)
            {
                compileErrors.Add(message);
            }
        }

        return compileErrors;
    }

    private bool IsErrorShader(Shader shader)
    {
        if (shader == null)
            return false;

        return shader.name == "Hidden/InternalErrorShader"
               || shader.name.Contains("InternalErrorShader");
    }

    private string BuildCurrentValue(
        Shader shader,
        bool shaderMissing,
        bool isErrorShader,
        int compileErrorCount)
    {
        if (shaderMissing)
        {
            return "Shader 为空";
        }

        if (isErrorShader)
        {
            return $"Shader：{shader.name}";
        }

        if (compileErrorCount > 0)
        {
            return $"Shader：{shader.name}，编译错误：{compileErrorCount} 个";
        }

        return $"Shader：{shader.name}，编译正常";
    }

    private string BuildDetailMessage(
        Material material,
        Shader shader,
        bool shaderMissing,
        bool isErrorShader,
        List<ShaderMessage> compileErrors)
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

        if (shaderMissing)
        {
            builder.AppendLine("问题：");
            builder.AppendLine("当前材质没有有效的 Shader 引用。");
        }
        else if (isErrorShader)
        {
            builder.AppendLine("问题：");
            builder.AppendLine(
                "当前材质使用了 Unity 的 InternalErrorShader。"
            );
            builder.AppendLine(
                "这通常表示原 Shader 丢失、编译失败，或者渲染管线不兼容。"
            );
        }
        else if (compileErrors.Count > 0)
        {
            builder.AppendLine(
                $"发现 {compileErrors.Count} 个 Shader 编译错误："
            );
            builder.AppendLine();

            for (int i = 0; i < compileErrors.Count; i++)
            {
                ShaderMessage error = compileErrors[i];

                builder.AppendLine(
                    $"{i + 1}. {error.message}"
                );

                if (!string.IsNullOrEmpty(error.file))
                {
                    builder.AppendLine(
                        $"   文件：{error.file}"
                    );
                }

                if (error.line > 0)
                {
                    builder.AppendLine(
                        $"   行号：{error.line}"
                    );
                }

                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("当前 Shader 没有发现编译错误。");
        }

        builder.AppendLine();
        builder.AppendLine("建议：");

        if (shaderMissing)
        {
            builder.AppendLine(
                "重新为材质指定正确的 Shader。"
            );
        }
        else if (isErrorShader)
        {
            builder.AppendLine(
                "检查原 Shader 是否存在，以及是否兼容当前 URP 渲染管线。"
            );
        }
        else if (compileErrors.Count > 0)
        {
            builder.AppendLine(
                "根据上面的文件路径、行号和错误信息修改 Shader 或 HLSL 文件。"
            );
        }
        else
        {
            builder.AppendLine(
                "当前材质 Shader 状态正常，无需处理。"
            );
        }

        return builder.ToString();
    }
}