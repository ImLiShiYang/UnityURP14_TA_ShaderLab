using System.Collections.Generic;
using System.IO;
using UnityEditor;

public class AssetReferenceIndex
{
    private readonly Dictionary<string, List<string>> referencedByMap =
        new Dictionary<string, List<string>>();

    private static readonly List<string> EmptyList = new List<string>();

    public IReadOnlyList<string> GetReferencers(string assetPath)
    {
        if (referencedByMap.TryGetValue(assetPath, out List<string> referencers))
        {
            return referencers;
        }

        return EmptyList;
    }

    public static AssetReferenceIndex Build()
    {
        AssetReferenceIndex index = new AssetReferenceIndex();

        // 先明确扫描所有 Scene。
        // index.BuildSceneReferences();

        // 再扫描其他常见资源。
        index.BuildAssetReferences();

        return index;
    }

    private void BuildSceneReferences()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

        try
        {
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);

                // 进度条不需要每个资源都更新，否则编辑器 UI 刷新本身也会产生开销。
                if (i == 0 || i % 25 == 0 || i == sceneGuids.Length - 1)
                {
                    EditorUtility.DisplayProgressBar(
                        "Building Asset Reference Index",
                        scenePath,
                        sceneGuids.Length > 0 ? (float)i / sceneGuids.Length : 1f
                    );
                }

                if (string.IsNullOrEmpty(scenePath))
                    continue;

                if (IsInTrashFolder(scenePath))
                    continue;

                AddDependenciesFromRoot(scenePath);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void BuildAssetReferences()
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string rootPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                EditorUtility.DisplayProgressBar(
                    "Building Asset Reference Index",
                    rootPath,
                    guids.Length > 0 ? (float)i / guids.Length : 1f
                );

                if (string.IsNullOrEmpty(rootPath))
                    continue;

                if (IsInTrashFolder(rootPath))
                    continue;

                if (!IsDependencyRootFile(rootPath))
                    continue;

                AddDependenciesFromRoot(rootPath);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void AddDependenciesFromRoot(string rootPath)
    {
        string[] dependencies;

        try
        {
            dependencies = AssetDatabase.GetDependencies(rootPath, false);
        }
        catch
        {
            return;
        }

        foreach (string dependency in dependencies)
        {
            if (string.IsNullOrEmpty(dependency))
                continue;

            if (!dependency.StartsWith("Assets/"))
                continue;

            if (dependency == rootPath)
                continue;

            if (IsInTrashFolder(dependency))
                continue;

            AddReference(dependency, rootPath);
        }
    }

    private void AddReference(string dependencyPath, string referencerPath)
    {
        if (!referencedByMap.TryGetValue(dependencyPath, out List<string> referencers))
        {
            referencers = new List<string>();
            referencedByMap.Add(dependencyPath, referencers);
        }

        if (!referencers.Contains(referencerPath))
        {
            referencers.Add(referencerPath);
        }
    }

    private static bool IsDependencyRootFile(string assetPath)
    {
        string extension = Path.GetExtension(assetPath).ToLower();

        switch (extension)
        {
            case ".unity":
            case ".prefab":
            case ".mat":
            case ".asset":
            case ".controller":
            case ".overridecontroller":
            case ".anim":
            case ".playable":
            case ".spriteatlas":
                return true;

            default:
                return false;
        }
    }

    public static bool IsInTrashFolder(string assetPath)
    {
        return assetPath.StartsWith("Assets/__TAAssetCheckerTrash/");
    }
}