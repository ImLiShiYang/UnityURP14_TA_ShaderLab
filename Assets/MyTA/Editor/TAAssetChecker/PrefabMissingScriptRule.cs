using System.Text;
using UnityEditor;
using UnityEngine;

public class PrefabMissingScriptRule : AssetCheckRule
{
    public override string RuleName => "Prefab Missing Script 检查";

    public override CheckResult Check(string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        if (prefab == null)
            return null;

        Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);

        int missingScriptCount = 0;
        StringBuilder detailBuilder = new StringBuilder();

        foreach (Transform transform in transforms)
        {
            GameObject go = transform.gameObject;

            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);

            if (count <= 0)
                continue;

            missingScriptCount += count;

            string hierarchyPath = GetHierarchyPath(prefab.transform, transform);

            detailBuilder.AppendLine($"{hierarchyPath} : Missing Script 数量 {count}");
        }

        bool passed = missingScriptCount == 0;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Prefab",

            ruleName = RuleName,
            currentValue = passed
                ? "没有 Missing Script"
                : $"Missing Script 数量：{missingScriptCount}",

            limitValue = "Missing Script 数量必须为 0",
            passed = passed,

            detailMessage = detailBuilder.ToString(),

            // 先不自动修复。
            // 因为删除 Missing Script 可能破坏 Prefab，需要用户确认后再做。
            canFix = false,
            rule = this
        };

        if (passed)
        {
            result.message = "Prefab 没有 Missing Script。";
        }
        else
        {
            result.message = $"Prefab 存在 Missing Script，数量为 {missingScriptCount}。";
        }

        return result;
    }

    private string GetHierarchyPath(Transform root, Transform target)
    {
        if (root == target)
            return root.name;

        string path = target.name;
        Transform current = target.parent;

        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return root.name + "/" + path;
    }
}