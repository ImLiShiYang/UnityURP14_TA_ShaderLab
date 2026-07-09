using UnityEngine;

public abstract class AssetCheckRule
{
    public abstract string RuleName { get; }

    public abstract CheckResult Check(string assetPath);

    public virtual bool CanFix(string assetPath)
    {
        return false;
    }

    public virtual void Fix(string assetPath)
    {
        Debug.LogWarning($"规则 {RuleName} 不支持自动修复：{assetPath}");
    }
}