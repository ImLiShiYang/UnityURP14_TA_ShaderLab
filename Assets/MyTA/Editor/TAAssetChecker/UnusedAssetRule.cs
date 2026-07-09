using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class UnusedAssetRule : AssetCheckRule
{
    private const string TrashRoot = "Assets/__TAAssetCheckerTrash";

    private readonly AssetReferenceIndex referenceIndex;
    private readonly bool allowSoftDelete;

    public override string RuleName => "疑似未使用资产检查";

    public UnusedAssetRule(AssetReferenceIndex referenceIndex, bool allowSoftDelete)
    {
        this.referenceIndex = referenceIndex;
        this.allowSoftDelete = allowSoftDelete;
    }

    public override CheckResult Check(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        if (!IsSupportedCandidate(assetPath))
            return null;

        IReadOnlyList<string> referencers = referenceIndex.GetReferencers(assetPath);

        bool used = referencers.Count > 0;
        bool passed = used;

        string assetType = GetAssetTypeName(assetPath);

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = assetType,

            ruleName = RuleName,
            currentValue = used
                ? $"被 {referencers.Count} 个资源引用"
                : "没有发现其他资源引用它",

            limitValue = "如果确认无用，可以软删除到 TAAssetCheckerTrash",
            passed = passed,

            detailMessage = BuildDetailMessage(assetPath, assetType, referencers),

            // 不走普通 Fix All，避免一键删太多。
            canFix = false,
            rule = this
        };

        result.message = used
            ? "资产被其他资源引用。"
            : $"{assetType} 疑似未使用。";

        return result;
    }

    public bool CanSoftDelete(string assetPath, out string reason)
    {
        reason = string.Empty;

        if (!allowSoftDelete)
        {
            reason = "未开启“允许未使用资产软删除”。";
            return false;
        }

        if (string.IsNullOrEmpty(assetPath))
        {
            reason = "资源路径为空。";
            return false;
        }

        if (AssetReferenceIndex.IsInTrashFolder(assetPath))
        {
            reason = "资源已经在 TAAssetCheckerTrash 中。";
            return false;
        }

        if (!IsSupportedCandidate(assetPath))
        {
            reason = "资源类型不支持软删除。";
            return false;
        }

        return true;
    }

    public bool SoftDelete(string assetPath, out string trashPath, out string message)
    {
        trashPath = string.Empty;
        message = string.Empty;

        if (!CanSoftDelete(assetPath, out string reason))
        {
            message = reason;
            return false;
        }

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        string fileName = Path.GetFileName(assetPath);

        string trashFolder = GetOrCreateTrashSessionFolder();

        trashPath = AssetDatabase.GenerateUniqueAssetPath($"{trashFolder}/{guid}_{fileName}");

        string error = AssetDatabase.MoveAsset(assetPath, trashPath);

        if (!string.IsNullOrEmpty(error))
        {
            message = $"软删除失败：{error}";
            return false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        message = $"已移动到隔离目录：{trashPath}";
        return true;
    }

    private string BuildDetailMessage(
        string assetPath,
        string assetType,
        IReadOnlyList<string> referencers)
    {
        StringBuilder builder = new StringBuilder();

        if (referencers.Count > 0)
        {
            builder.AppendLine("引用它的资源：");

            int maxShowCount = Mathf.Min(referencers.Count, 20);

            for (int i = 0; i < maxShowCount; i++)
            {
                builder.AppendLine(referencers[i]);
            }

            if (referencers.Count > maxShowCount)
            {
                builder.AppendLine($"还有 {referencers.Count - maxShowCount} 个引用未显示。");
            }
        }
        else
        {
            builder.AppendLine("没有在常见资源依赖中发现引用。");
            builder.AppendLine();
            builder.AppendLine("删除策略：");
            builder.AppendLine("不会真正删除，会移动到 Assets/__TAAssetCheckerTrash。");
            builder.AppendLine("可以通过 Rollback Last Soft Delete 撤回。");
        }

        builder.AppendLine();
        builder.AppendLine("说明：");
        builder.AppendLine("这是疑似未使用检测，不是 100% 确认无用。");
        builder.AppendLine("Resources.Load、Addressables、代码字符串、运行时动态加载可能无法被依赖图完全识别。");
        builder.AppendLine($"资源路径：{assetPath}");

        return builder.ToString();
    }

    private bool IsSupportedCandidate(string assetPath)
    {
        if (AssetReferenceIndex.IsInTrashFolder(assetPath))
            return false;

        string extension = Path.GetExtension(assetPath).ToLower();

        if (IsTextureExtension(extension))
            return true;

        switch (extension)
        {
            case ".mat":
            case ".prefab":
                return true;

            default:
                return false;
        }
    }

    private bool IsTextureExtension(string extension)
    {
        switch (extension)
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".tga":
            case ".psd":
            case ".exr":
            case ".hdr":
            case ".tif":
            case ".tiff":
            case ".bmp":
            case ".cubemap":
                return true;

            default:
                return false;
        }
    }

    private string GetAssetTypeName(string assetPath)
    {
        string extension = Path.GetExtension(assetPath).ToLower();

        if (IsTextureExtension(extension))
            return "Texture";

        switch (extension)
        {
            case ".mat":
                return "Material";

            case ".prefab":
                return "Prefab";

            default:
                return "Asset";
        }
    }

    private string GetOrCreateTrashSessionFolder()
    {
        if (!AssetDatabase.IsValidFolder(TrashRoot))
        {
            AssetDatabase.CreateFolder("Assets", "__TAAssetCheckerTrash");
        }

        string sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sessionFolder = $"{TrashRoot}/{sessionName}";

        if (!AssetDatabase.IsValidFolder(sessionFolder))
        {
            AssetDatabase.CreateFolder(TrashRoot, sessionName);
        }

        return sessionFolder;
    }
}