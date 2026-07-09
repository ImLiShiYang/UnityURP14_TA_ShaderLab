using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class AssetNamePrefixRule : AssetCheckRule
{
    private const int MaxReferenceFilesToShow = 20;
    private const long MaxSearchFileSizeBytes = 512 * 1024;

    private readonly string assetTypeName;
    private readonly bool allowRenameFix;
    private readonly bool checkReferenceOnScan;
    private readonly string[] allowedPrefixes;

    public override string RuleName => "资产命名前缀检查";

    public AssetNamePrefixRule(
        string assetTypeName,
        bool allowRenameFix,
        bool checkReferenceOnScan,
        params string[] allowedPrefixes)
    {
        this.assetTypeName = assetTypeName;
        this.allowRenameFix = allowRenameFix;
        this.checkReferenceOnScan = checkReferenceOnScan;
        this.allowedPrefixes = allowedPrefixes;
    }

    public override CheckResult Check(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        string assetName = Path.GetFileNameWithoutExtension(assetPath);

        if (string.IsNullOrEmpty(assetName))
            return null;

        bool passed = HasAllowedPrefix(assetName);

        List<string> codeReferenceFiles = new List<string>();
        bool referenceSearchSkipped = false;

        // 重点：扫描阶段默认不查引用，避免 Scan All 很慢。
        if (!passed && checkReferenceOnScan)
        {
            codeReferenceFiles = FindCodeReferenceFiles(assetPath, assetName, MaxReferenceFilesToShow);
        }
        else if (!passed)
        {
            referenceSearchSkipped = true;
        }

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = assetTypeName,

            ruleName = $"{assetTypeName} 命名前缀检查",
            currentValue = $"当前名称：{assetName}",
            limitValue = BuildLimitMessage(),
            passed = passed,

            detailMessage = passed
                ? string.Empty
                : BuildDetailMessage(assetName, codeReferenceFiles, referenceSearchSkipped),

            canFix = !passed && CanFix(assetPath),
            rule = this
        };

        result.message = passed
            ? $"{assetTypeName} 命名符合规范。"
            : $"{assetTypeName} 命名不符合规范，当前名称为 {assetName}。";

        return result;
    }

    public override bool CanFix(string assetPath)
    {
        if (!allowRenameFix)
            return false;

        if (string.IsNullOrEmpty(assetPath))
            return false;

        string extension = Path.GetExtension(assetPath).ToLower();

        if (extension == ".cs")
            return false;

        string assetName = Path.GetFileNameWithoutExtension(assetPath);

        if (HasAllowedPrefix(assetName))
            return false;

        string suggestedName = GetSuggestedName(assetName);
        string directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        string newPath = $"{directory}/{suggestedName}{extension}";

        Object existingAsset = AssetDatabase.LoadAssetAtPath<Object>(newPath);
        if (existingAsset != null)
            return false;

        return true;
    }

    public override void Fix(string assetPath)
    {
        if (!CanFix(assetPath))
        {
            Debug.LogWarning($"命名规则无法自动修复：{assetPath}");
            return;
        }

        string oldName = Path.GetFileNameWithoutExtension(assetPath);
        string newName = GetSuggestedName(oldName);

        // 点击修复时再做引用风险扫描。
        List<string> codeReferenceFiles = FindCodeReferenceFiles(assetPath, oldName, MaxReferenceFilesToShow);

        StringBuilder dialogMessage = new StringBuilder();

        dialogMessage.AppendLine("即将重命名资源：");
        dialogMessage.AppendLine(assetPath);
        dialogMessage.AppendLine();
        dialogMessage.AppendLine($"新名称：{newName}");
        dialogMessage.AppendLine();

        if (codeReferenceFiles.Count > 0)
        {
            dialogMessage.AppendLine("警告：在以下代码/文本文件中发现旧名称或旧路径引用：");

            foreach (string file in codeReferenceFiles)
            {
                dialogMessage.AppendLine(file);
            }

            dialogMessage.AppendLine();
            dialogMessage.AppendLine("这些引用不会自动修改。");
            dialogMessage.AppendLine("如果代码里写死了旧名字，重命名后可能需要手动同步修改。");
            dialogMessage.AppendLine();
        }
        else
        {
            dialogMessage.AppendLine("没有在常见代码文件中发现旧名称或旧路径引用。");
            dialogMessage.AppendLine();
        }

        dialogMessage.AppendLine("Unity 的普通拖拽引用通常依赖 GUID，一般不会因为改名断开。");
        dialogMessage.AppendLine("是否继续重命名？");

        bool confirm = EditorUtility.DisplayDialog(
            "重命名资源",
            dialogMessage.ToString(),
            "继续重命名",
            "取消"
        );

        if (!confirm)
            return;

        string error = AssetDatabase.RenameAsset(assetPath, newName);

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"资源重命名失败：{assetPath}\n原因：{error}");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"TA Asset Checker: 已重命名资源：{assetPath} -> {newName}");
    }

    private bool HasAllowedPrefix(string assetName)
    {
        if (allowedPrefixes == null || allowedPrefixes.Length == 0)
            return true;

        foreach (string prefix in allowedPrefixes)
        {
            if (assetName.StartsWith(prefix))
                return true;
        }

        return false;
    }

    private string GetSuggestedName(string assetName)
    {
        if (allowedPrefixes == null || allowedPrefixes.Length == 0)
            return assetName;

        return allowedPrefixes[0] + assetName;
    }

    private string BuildLimitMessage()
    {
        if (allowedPrefixes == null || allowedPrefixes.Length == 0)
            return "未设置命名前缀规则";

        if (allowedPrefixes.Length == 1)
            return $"{assetTypeName} 名称必须以 {allowedPrefixes[0]} 开头";

        StringBuilder builder = new StringBuilder();

        builder.Append($"{assetTypeName} 名称必须以 ");

        for (int i = 0; i < allowedPrefixes.Length; i++)
        {
            builder.Append(allowedPrefixes[i]);

            if (i < allowedPrefixes.Length - 1)
                builder.Append(" 或 ");
        }

        builder.Append(" 开头");

        return builder.ToString();
    }

    private string BuildDetailMessage(
        string assetName,
        List<string> codeReferenceFiles,
        bool referenceSearchSkipped)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("命名建议：");

        foreach (string prefix in allowedPrefixes)
        {
            if (assetName.StartsWith(prefix))
                continue;

            builder.AppendLine($"{prefix}{assetName}");
        }

        builder.AppendLine();

        if (referenceSearchSkipped)
        {
            builder.AppendLine("引用风险：");
            builder.AppendLine("为了提升扫描速度，当前扫描阶段没有检查代码引用。");
            builder.AppendLine("点击“修复”时会再次扫描常见代码文件，并在确认弹窗里提示风险。");
        }
        else if (codeReferenceFiles.Count > 0)
        {
            builder.AppendLine("引用风险：");
            builder.AppendLine("在以下代码/文本文件中发现旧名称或旧路径引用：");

            foreach (string file in codeReferenceFiles)
            {
                builder.AppendLine(file);
            }

            builder.AppendLine();
            builder.AppendLine("说明：这些引用不会自动修改。");
            builder.AppendLine("如果这里是 Resources.Load、AssetDatabase.LoadAssetAtPath、Addressables key 等字符串引用，重命名后需要手动同步修改。");
        }
        else
        {
            builder.AppendLine("引用风险：");
            builder.AppendLine("没有在常见代码文件中发现旧名称或旧路径引用。");
        }

        builder.AppendLine();
        builder.AppendLine("说明：");

        if (allowRenameFix)
        {
            builder.AppendLine("当前已允许自动重命名。");
            builder.AppendLine("点击修复后，会使用 AssetDatabase.RenameAsset 修改资源名称。");
            builder.AppendLine("普通 Inspector 拖拽引用通常依赖 GUID，一般不会因为改名断开。");
        }
        else
        {
            builder.AppendLine("当前规则只负责检测命名，不会自动重命名。");
            builder.AppendLine("如需自动重命名，请在工具面板中开启“允许命名规则自动修复”。");
        }

        return builder.ToString();
    }

    private List<string> FindCodeReferenceFiles(string assetPath, string assetName, int maxResultCount)
    {
        List<string> result = new List<string>();

        string oldPath = assetPath.Replace("\\", "/");
        string oldPathWithoutExtension = RemoveExtension(oldPath);

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });

        foreach (string guid in guids)
        {
            if (result.Count >= maxResultCount)
                break;

            string candidatePath = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(candidatePath))
                continue;

            if (candidatePath == assetPath)
                continue;

            if (!IsReferenceSearchFile(candidatePath))
                continue;

            string fullPath = Path.Combine(projectRoot, candidatePath);

            if (!File.Exists(fullPath))
                continue;

            FileInfo fileInfo = new FileInfo(fullPath);

            // 太大的文本文件先不扫，避免卡死。
            if (fileInfo.Length > MaxSearchFileSizeBytes)
                continue;

            string content;

            try
            {
                content = File.ReadAllText(fullPath);
            }
            catch
            {
                continue;
            }

            if (ContainsReference(content, oldPath, oldPathWithoutExtension, assetName))
            {
                result.Add(candidatePath);
            }
        }

        return result;
    }

    private bool ContainsReference(string content, string oldPath, string oldPathWithoutExtension, string assetName)
    {
        if (content.Contains(oldPath))
            return true;

        if (content.Contains(oldPathWithoutExtension))
            return true;

        if (content.Contains(assetName))
            return true;

        return false;
    }

    private bool IsReferenceSearchFile(string assetPath)
    {
        string extension = Path.GetExtension(assetPath).ToLower();

        switch (extension)
        {
            case ".cs":
            case ".shader":
            case ".hlsl":
            case ".cginc":
            case ".compute":
            case ".asmdef":
            case ".json":
                return true;

            default:
                return false;
        }
    }

    private string RemoveExtension(string assetPath)
    {
        string directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(assetPath);

        if (string.IsNullOrEmpty(directory))
            return fileNameWithoutExtension;

        return $"{directory}/{fileNameWithoutExtension}";
    }
}