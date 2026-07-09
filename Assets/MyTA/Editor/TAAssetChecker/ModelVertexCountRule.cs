using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class ModelVertexCountRule : AssetCheckRule
{
    private readonly int maxVertexCount;

    public override string RuleName => "模型顶点数限制";

    public ModelVertexCountRule(int maxVertexCount)
    {
        this.maxVertexCount = maxVertexCount;
    }

    public override CheckResult Check(string assetPath)
    {
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

        List<MeshInfo> meshInfos = new List<MeshInfo>();

        int totalVertexCount = 0;

        foreach (Object asset in allAssets)
        {
            Mesh mesh = asset as Mesh;

            if (mesh == null)
                continue;

            int vertexCount = mesh.vertexCount;

            meshInfos.Add(new MeshInfo
            {
                meshName = mesh.name,
                vertexCount = vertexCount
            });

            totalVertexCount += vertexCount;
        }

        if (meshInfos.Count == 0)
            return null;

        meshInfos.Sort((a, b) => b.vertexCount.CompareTo(a.vertexCount));

        bool passed = totalVertexCount <= maxVertexCount;

        CheckResult result = new CheckResult
        {
            assetPath = assetPath,
            assetType = "Model",

            ruleName = RuleName,
            currentValue = $"Mesh 数量：{meshInfos.Count}，总顶点数：{totalVertexCount}",
            limitValue = $"总顶点数 <= {maxVertexCount}",
            passed = passed,

            detailMessage = BuildMeshDetailMessage(meshInfos),

            // 模型顶点数不能安全自动修复，所以不提供修复按钮。
            canFix = false,
            rule = this
        };

        if (passed)
        {
            result.message = "模型顶点数符合要求。";
        }
        else
        {
            result.message = $"模型顶点数超过限制。当前总顶点数为 {totalVertexCount}，限制为 {maxVertexCount}。";
        }

        return result;
    }

    private string BuildMeshDetailMessage(List<MeshInfo> meshInfos)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("Mesh 明细：");

        for (int i = 0; i < meshInfos.Count; i++)
        {
            MeshInfo info = meshInfos[i];

            builder.AppendLine($"{i + 1}. {info.meshName} : {info.vertexCount} 顶点");
        }

        return builder.ToString();
    }

    private class MeshInfo
    {
        public string meshName;
        public int vertexCount;
    }
}