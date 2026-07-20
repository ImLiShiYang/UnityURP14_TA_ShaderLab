using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        // 扫描项目 Assets 下的所有场景，包括当前没有打开的场景。
        index.BuildAllSceneAssetReferences();

        // 再读取已打开场景的内存对象，覆盖未保存修改和未激活对象中的引用。
        index.BuildOpenSceneReferences();

        // 最后扫描 Prefab 和其他常见资源。
        index.BuildAssetReferences();

        return index;
    }

    private void BuildAllSceneAssetReferences()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

        try
        {
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);

                EditorUtility.DisplayProgressBar(
                    "Scanning All Scene References",
                    scenePath,
                    sceneGuids.Length > 0 ? (float)i / sceneGuids.Length : 1f
                );

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

    private void BuildOpenSceneReferences()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);

            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            Object[] rootObjects = new Object[roots.Length];

            for (int i = 0; i < roots.Length; i++)
            {
                rootObjects[i] = roots[i];
            }

            Object[] dependencies;

            try
            {
                dependencies = EditorUtility.CollectDependencies(rootObjects);
            }
            catch
            {
                continue;
            }

            string referencer = string.IsNullOrEmpty(scene.path)
                ? $"当前场景：{scene.name}"
                : scene.path;

            foreach (Object dependency in dependencies)
            {
                if (dependency == null)
                    continue;

                string dependencyPath = AssetDatabase.GetAssetPath(dependency);

                if (string.IsNullOrEmpty(dependencyPath))
                    continue;

                if (!dependencyPath.StartsWith("Assets/"))
                    continue;

                if (dependencyPath == scene.path)
                    continue;

                if (IsInTrashFolder(dependencyPath))
                    continue;

                AddReference(dependencyPath, referencer);
            }
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

                // 场景已经由 BuildAllSceneAssetReferences 统一扫描，避免重复处理。
                if (Path.GetExtension(rootPath).ToLower() == ".unity")
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
