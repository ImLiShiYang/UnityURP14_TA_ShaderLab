using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class TAAssetCheckerSceneLocator
{
    private MonoScript sceneLocatorScript;
    private readonly List<GameObject> sceneLocatorResults = new List<GameObject>();
    private Vector2 sceneLocatorScrollPos;
    private bool showSceneScriptLocator = true;
    private string sceneLocatorMessage;
    private MessageType sceneLocatorMessageType = MessageType.Info;

    public void Draw()
    {
        showSceneScriptLocator = EditorGUILayout.BeginFoldoutHeaderGroup(
            showSceneScriptLocator,
            "场景脚本定位"
        );

        if (showSceneScriptLocator)
        {
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            MonoScript newScript = (MonoScript)EditorGUILayout.ObjectField(
                "目标脚本",
                sceneLocatorScript,
                typeof(MonoScript),
                false
            );

            if (EditorGUI.EndChangeCheck())
            {
                sceneLocatorScript = newScript;
                ClearSceneLocatorResults();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("查找当前场景", GUILayout.Height(24)))
            {
                FindObjectsWithSelectedScript();
            }

            using (new EditorGUI.DisabledScope(sceneLocatorResults.Count == 0))
            {
                if (GUILayout.Button("清空结果", GUILayout.Width(90), GUILayout.Height(24)))
                {
                    ClearSceneLocatorResults();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(sceneLocatorMessage))
            {
                EditorGUILayout.HelpBox(sceneLocatorMessage, sceneLocatorMessageType);
            }

            DrawSceneLocatorResults();

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(8);
    }

    private void FindObjectsWithSelectedScript()
    {
        sceneLocatorResults.Clear();

        if (sceneLocatorScript == null)
        {
            SetSceneLocatorMessage("请先选择一个 C# 脚本。", MessageType.Warning);
            return;
        }

        Type scriptType = sceneLocatorScript.GetClass();

        if (scriptType == null)
        {
            SetSceneLocatorMessage(
                "无法从该脚本取得类型。请确认脚本没有编译错误，并且文件名与类名一致。",
                MessageType.Error
            );
            return;
        }

        if (!typeof(Component).IsAssignableFrom(scriptType))
        {
            SetSceneLocatorMessage(
                $"{scriptType.Name} 不是可挂载到 GameObject 的 Component/MonoBehaviour。",
                MessageType.Warning
            );
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            SetSceneLocatorMessage("当前没有有效且已加载的活动场景。", MessageType.Warning);
            return;
        }

        HashSet<GameObject> uniqueObjects = new HashSet<GameObject>();
        GameObject[] roots = activeScene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            Component[] components = roots[i].GetComponentsInChildren(scriptType, true);

            for (int j = 0; j < components.Length; j++)
            {
                Component component = components[j];

                // 精确匹配所选脚本，不把挂载派生类脚本的对象混入结果。
                if (component != null &&
                    component.GetType() == scriptType &&
                    uniqueObjects.Add(component.gameObject))
                {
                    sceneLocatorResults.Add(component.gameObject);
                }
            }
        }

        sceneLocatorResults.Sort((left, right) =>
            string.Compare(
                GetSceneHierarchyPath(left.transform),
                GetSceneHierarchyPath(right.transform),
                StringComparison.OrdinalIgnoreCase
            )
        );

        SetSceneLocatorMessage(
            $"在场景“{activeScene.name}”中找到 {sceneLocatorResults.Count} 个挂载 {scriptType.Name} 的对象。",
            sceneLocatorResults.Count > 0 ? MessageType.Info : MessageType.Warning
        );
    }

    private void DrawSceneLocatorResults()
    {
        RemoveDestroyedSceneLocatorResults();

        if (sceneLocatorResults.Count == 0)
            return;

        float viewHeight = Mathf.Min(240f, sceneLocatorResults.Count * 48f + 4f);
        sceneLocatorScrollPos = EditorGUILayout.BeginScrollView(
            sceneLocatorScrollPos,
            GUILayout.Height(viewHeight)
        );

        for (int i = 0; i < sceneLocatorResults.Count; i++)
        {
            GameObject target = sceneLocatorResults[i];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            string stateLabel = target.activeInHierarchy ? string.Empty : "（未激活）";
            EditorGUILayout.LabelField(target.name + stateLabel, EditorStyles.boldLabel);

            if (GUILayout.Button("定位", GUILayout.Width(60)))
            {
                LocateSceneObject(target);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "路径：" + GetSceneHierarchyPath(target.transform),
                EditorStyles.miniLabel
            );
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();
    }

    private void LocateSceneObject(GameObject target)
    {
        if (target == null)
            return;

        Selection.activeGameObject = target;
        EditorGUIUtility.PingObject(target);

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.FrameSelected();
            sceneView.Repaint();
        }
    }

    private void ClearSceneLocatorResults()
    {
        sceneLocatorResults.Clear();
        sceneLocatorScrollPos = Vector2.zero;
        sceneLocatorMessage = string.Empty;
    }

    private void RemoveDestroyedSceneLocatorResults()
    {
        for (int i = sceneLocatorResults.Count - 1; i >= 0; i--)
        {
            if (sceneLocatorResults[i] == null)
            {
                sceneLocatorResults.RemoveAt(i);
            }
        }
    }

    private void SetSceneLocatorMessage(string message, MessageType messageType)
    {
        sceneLocatorMessage = message;
        sceneLocatorMessageType = messageType;
    }

    private static string GetSceneHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform parent = target.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
