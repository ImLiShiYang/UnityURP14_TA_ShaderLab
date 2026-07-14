using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public class TAAssetCheckerWindow : EditorWindow
{
    private string scanFolder = "Assets";
    private int maxTextureSize = 1024;
    private bool expectedMipMapEnabled = true;
    private int maxModelVertexCount = 30000;
    private bool allowNameAutoFix = false;
    private bool checkNameReferenceOnScan = false;
    
    private Vector2 scrollPos;
    private ResultFilter resultFilter = ResultFilter.All;
    private ScanScope lastScanScope = ScanScope.Textures;
    
    // 是否点击过扫描按钮
    private bool hasSelectedScanScope = false;
    
    private UnusedAssetTypeFilter unusedAssetTypeFilter = UnusedAssetTypeFilter.All;
    
    // 扫描按钮被选中后显示的颜色
    private static readonly Color SelectedScanButtonColor =
        new Color(0.25f, 0.50f, 0.85f, 1.0f);
    
    // 当前选择的细分规则
    private RuleDetailFilter ruleDetailFilter = RuleDetailFilter.All;
    
    // 缓存上一次生成的项目引用索引
    private AssetReferenceIndex cachedReferenceIndex;

    // 记录建立缓存时的 AssetDatabase 版本
    private uint cachedReferenceVersion = uint.MaxValue;
    
    private bool allowMaterialShaderDefaultQueue = true;
    private int minMaterialRenderQueue = 2000;
    private int maxMaterialRenderQueue = 3000;

    private readonly List<CheckResult> results = new List<CheckResult>();
    private readonly Stack<FixRollbackGroup> rollbackGroups = new Stack<FixRollbackGroup>();
    
    private bool allowUnusedAssetSoftDelete = false;

    private readonly Stack<SoftDeleteRollbackRecord> softDeleteRollbackRecords =
        new Stack<SoftDeleteRollbackRecord>();

    private enum ScanScope
    {
        Textures,
        Materials,
        Models,
        Prefabs,
        Unused,
        All
    }
    
    private enum RuleDetailFilter
    {
        All,

        // 贴图
        TextureSize,
        TextureMipMap,
        TextureName,

        // 材质
        MaterialName,
        MaterialShader,
        MaterialRenderQueue,
        MaterialNormalMap,

        // 模型
        ModelVertexCount,
        ModelName,

        // Prefab
        PrefabMissingScript,
        PrefabName,

        // 未使用资产
        UnusedAsset
    }
    
    private enum UnusedAssetTypeFilter
    {
        All,
        Material,
        Texture,
        Prefab
    }

    [MenuItem("Tools/TA Asset Checker")]
    public static void OpenWindow()
    {
        TAAssetCheckerWindow window = GetWindow<TAAssetCheckerWindow>();
        window.titleContent = new GUIContent("TA Asset Checker");
        window.Show();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawSettings();
        DrawToolbar();
        DrawSummary();
        DrawFilter();
        DrawResults();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("TA Asset Checker", EditorStyles.boldLabel);
        

        EditorGUILayout.Space(8);
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("检测设置", EditorStyles.boldLabel);
        
        

        EditorGUILayout.BeginHorizontal();

        scanFolder = EditorGUILayout.TextField("扫描目录", scanFolder);

        if (GUILayout.Button("选择", GUILayout.Width(60)))
        {
            SelectScanFolder();
        }

        EditorGUILayout.EndHorizontal();

        maxTextureSize = EditorGUILayout.IntField("最大贴图尺寸", maxTextureSize);
        expectedMipMapEnabled = EditorGUILayout.Toggle("要求开启 MipMap", expectedMipMapEnabled);
        maxModelVertexCount = EditorGUILayout.IntField("最大模型顶点数", maxModelVertexCount);
        
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("材质检测设置", EditorStyles.boldLabel);

        allowMaterialShaderDefaultQueue = EditorGUILayout.Toggle(
            "允许 Shader 默认队列",
            allowMaterialShaderDefaultQueue
        );

        minMaterialRenderQueue = EditorGUILayout.IntField(
            "最小 Render Queue",
            minMaterialRenderQueue
        );

        maxMaterialRenderQueue = EditorGUILayout.IntField(
            "最大 Render Queue",
            maxMaterialRenderQueue
        );
        
        checkNameReferenceOnScan = EditorGUILayout.Toggle("扫描时检查命名引用风险", checkNameReferenceOnScan);
        EditorGUILayout.HelpBox(
            "扫描时检查命名引用风险会读取项目中的代码/文本文件，项目大时会明显变慢。\n" +
            "建议平时关闭；点击命名修复时仍会自动检查引用风险。",
            MessageType.Warning
        );
        allowNameAutoFix = EditorGUILayout.Toggle("允许命名规则自动修复", allowNameAutoFix);
        
        allowUnusedAssetSoftDelete = EditorGUILayout.Toggle(
            "允许未使用资产软删除",
            allowUnusedAssetSoftDelete
        );

        EditorGUILayout.HelpBox(
            "未使用资产检测只处理 Material、Texture、Prefab。\n" +
            "删除采用软删除：资源会移动到 Assets/__TAAssetCheckerTrash，不会永久删除。\n" +
            "可以点击 Rollback Last Soft Delete 撤回最近一次软删除。",
            MessageType.Warning
        );
        
        EditorGUILayout.HelpBox(
            "贴图说明：\n" +
            "角色、场景、道具贴图通常建议开启 MipMap。\n" +
            "UI、图标、字体贴图通常不建议开启 MipMap。\n\n" +
            "材质说明：\n" +
            "材质检测会检查命名前缀、Shader 是否丢失、Render Queue 是否异常，以及法线贴图槽位是否使用 Normal Map 类型贴图。\n\n" +
            "模型说明：\n" +
            "模型顶点数检测会统计一个 FBX / OBJ 文件中所有 Mesh 的顶点数总和。",
            MessageType.None
        );

        EditorGUILayout.Space(8);
    }

    /// <summary>
    /// 绘制扫描按钮。
    /// 当前按钮对应最近一次扫描类型时，持续显示蓝色。
    /// </summary>
    private bool DrawScanButton(string buttonName, ScanScope buttonScope)
    {
        // 保存原来的按钮颜色
        Color oldBackgroundColor = GUI.backgroundColor;

        // 必须点击过扫描按钮，并且是当前扫描类型，才显示选中颜色
        if (hasSelectedScanScope && lastScanScope == buttonScope)
        {
            GUI.backgroundColor = SelectedScanButtonColor;
        }

        bool clicked = GUILayout.Button(
            buttonName,
            GUILayout.Height(30)
        );

        // 恢复按钮颜色
        GUI.backgroundColor = oldBackgroundColor;

        // 点击后记录当前选中的扫描类型
        if (clicked)
        {
            hasSelectedScanScope = true;
            lastScanScope = buttonScope;
        }

        return clicked;
    }
    
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();

        if (DrawScanButton("扫描贴图", ScanScope.Textures))
        {
            ScanTextures();
        }

        if (DrawScanButton("扫描材质", ScanScope.Materials))
        {
            ScanMaterials();
        }

        if (DrawScanButton("扫描模型", ScanScope.Models))
        {
            ScanModels();
        }

        if (DrawScanButton("扫描预制体", ScanScope.Prefabs))
        {
            ScanPrefabs();
        }

        if (DrawScanButton("扫描全部", ScanScope.All))
        {
            ScanAll();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (DrawScanButton("扫描未使用资产", ScanScope.Unused))
        {
            ScanUnusedAssets();
        }

        if (GUILayout.Button("修复全部失败项", GUILayout.Height(30)))
        {
            FixAllFailed();
        }

        EditorGUI.BeginDisabledGroup(rollbackGroups.Count == 0);

        if (GUILayout.Button("撤销上次修复", GUILayout.Height(30)))
        {
            RollbackLastFix();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(softDeleteRollbackRecords.Count == 0);

        if (GUILayout.Button("撤销上次软删除", GUILayout.Height(30)))
        {
            RollbackLastSoftDelete();
        }

        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("清空结果", GUILayout.Height(30)))
        {
            results.Clear();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(8);
    }

    private void DrawSummary()
    {
        int totalCount = results.Count;
        int passedCount = 0;
        int failedCount = 0;

        foreach (CheckResult result in results)
        {
            if (result.passed)
                passedCount++;
            else
                failedCount++;
        }

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField("检测统计", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("检测项总数", totalCount.ToString());
        EditorGUILayout.LabelField("通过", passedCount.ToString());
        EditorGUILayout.LabelField("失败", failedCount.ToString());

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);
    }

    private void DrawFilter()
    {
        resultFilter = (ResultFilter)EditorGUILayout.EnumPopup(
            "显示结果",
            resultFilter
        );

        EditorGUILayout.Space(6);

        // 普通扫描显示规则细分类。
        // 未使用资产已经有材质、纹理、预制体分类，因此不重复显示。
        if (lastScanScope != ScanScope.Unused)
        {
            DrawRuleDetailFilter();
        }

        if (lastScanScope == ScanScope.Unused)
        {
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(
                "未使用资产分类",
                EditorStyles.boldLabel
            );

            unusedAssetTypeFilter =
                (UnusedAssetTypeFilter)GUILayout.Toolbar(
                    (int)unusedAssetTypeFilter,
                    new string[]
                    {
                        "全部",
                        "材质",
                        "纹理",
                        "预制体"
                    },
                    GUILayout.Height(24)
                );

            DrawUnusedAssetTypeSummary();
        }

        EditorGUILayout.Space(8);
    }
    
    private void DrawRuleDetailFilter()
    {
        EditorGUILayout.LabelField(
            "检测规则分类",
            EditorStyles.boldLabel
        );

        RuleDetailFilter[] options = GetCurrentRuleFilterOptions();

        int selectedIndex =
            System.Array.IndexOf(options, ruleDetailFilter);

        // 切换了扫描类型后，旧分类可能不属于当前类型。
        if (selectedIndex < 0)
        {
            ruleDetailFilter = RuleDetailFilter.All;
            selectedIndex = 0;
        }

        string[] labels = new string[options.Length];

        for (int i = 0; i < options.Length; i++)
        {
            RuleDetailFilter option = options[i];

            int count = GetRuleFilterResultCount(option);

            labels[i] =
                $"{GetRuleFilterDisplayName(option)} ({count})";
        }

        // 每行最多显示 4 个分类按钮
        int columnCount = Mathf.Min(options.Length, 4);

        int newIndex = GUILayout.SelectionGrid(
            selectedIndex,
            labels,
            columnCount
        );

        if (newIndex >= 0 && newIndex < options.Length)
        {
            ruleDetailFilter = options[newIndex];
        }
    }
    
    private RuleDetailFilter[] GetCurrentRuleFilterOptions()
    {
        switch (lastScanScope)
        {
            case ScanScope.Textures:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All,
                    RuleDetailFilter.TextureSize,
                    RuleDetailFilter.TextureMipMap,
                    RuleDetailFilter.TextureName
                };

            case ScanScope.Materials:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All,
                    RuleDetailFilter.MaterialName,
                    RuleDetailFilter.MaterialShader,
                    RuleDetailFilter.MaterialRenderQueue,
                    RuleDetailFilter.MaterialNormalMap
                };

            case ScanScope.Models:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All,
                    RuleDetailFilter.ModelVertexCount,
                    RuleDetailFilter.ModelName
                };

            case ScanScope.Prefabs:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All,
                    RuleDetailFilter.PrefabMissingScript,
                    RuleDetailFilter.PrefabName
                };

            case ScanScope.All:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All,

                    RuleDetailFilter.TextureSize,
                    RuleDetailFilter.TextureMipMap,
                    RuleDetailFilter.TextureName,

                    RuleDetailFilter.MaterialName,
                    RuleDetailFilter.MaterialShader,
                    RuleDetailFilter.MaterialRenderQueue,
                    RuleDetailFilter.MaterialNormalMap,

                    RuleDetailFilter.ModelVertexCount,
                    RuleDetailFilter.ModelName,

                    RuleDetailFilter.PrefabMissingScript,
                    RuleDetailFilter.PrefabName
                };

            default:
                return new RuleDetailFilter[]
                {
                    RuleDetailFilter.All
                };
        }
    }
    
    private string GetRuleFilterDisplayName(
        RuleDetailFilter filter)
    {
        switch (filter)
        {
            case RuleDetailFilter.All:
                return "全部";

            case RuleDetailFilter.TextureSize:
                return "贴图尺寸";

            case RuleDetailFilter.TextureMipMap:
                return "贴图 MipMap";

            case RuleDetailFilter.TextureName:
                return "贴图命名";

            case RuleDetailFilter.MaterialName:
                return "材质命名";

            case RuleDetailFilter.MaterialShader:
                return "材质 Shader";

            case RuleDetailFilter.MaterialRenderQueue:
                return "Render Queue";

            case RuleDetailFilter.MaterialNormalMap:
                return "法线贴图";

            case RuleDetailFilter.ModelVertexCount:
                return "模型顶点数";

            case RuleDetailFilter.ModelName:
                return "模型命名";

            case RuleDetailFilter.PrefabMissingScript:
                return "脚本缺失";

            case RuleDetailFilter.PrefabName:
                return "预制体命名";

            case RuleDetailFilter.UnusedAsset:
                return "未使用资产";

            default:
                return filter.ToString();
        }
    }
    
    private int GetRuleFilterResultCount(
        RuleDetailFilter filter)
    {
        int count = 0;

        foreach (CheckResult result in results)
        {
            if (!ShouldShowByResultState(result))
                continue;

            if (DoesResultMatchRuleFilter(result, filter))
            {
                count++;
            }
        }

        return count;
    }
    
    private bool ShouldShowByResultState(CheckResult result)
    {
        switch (resultFilter)
        {
            case ResultFilter.All:
                return true;

            case ResultFilter.Failed:
                return !result.passed;

            case ResultFilter.Passed:
                return result.passed;

            default:
                return true;
        }
    }
    
    private bool DoesResultMatchRuleFilter(
        CheckResult result,
        RuleDetailFilter filter)
    {
        RuleDetailFilter oldFilter = ruleDetailFilter;

        ruleDetailFilter = filter;

        bool matches = ShouldShowRuleDetail(result);

        ruleDetailFilter = oldFilter;

        return matches;
    }
    
    private bool ShouldShowRuleDetail(CheckResult result)
    {
        if (result == null)
            return false;

        switch (ruleDetailFilter)
        {
            case RuleDetailFilter.All:
                return true;

            // =========================
            // Texture
            // =========================

            case RuleDetailFilter.TextureSize:
                return result.rule is TextureMaxSizeRule;

            case RuleDetailFilter.TextureMipMap:
                return result.rule is TextureMipMapRule;

            case RuleDetailFilter.TextureName:
                return result.rule is AssetNamePrefixRule
                       && result.assetType == "Texture2D";

            // =========================
            // Material
            // =========================

            case RuleDetailFilter.MaterialName:
                return result.rule is AssetNamePrefixRule
                       && result.assetType == "Material";

            case RuleDetailFilter.MaterialShader:
                return result.rule is MaterialShaderRule;

            case RuleDetailFilter.MaterialRenderQueue:
                return result.rule is MaterialRenderQueueRule;

            case RuleDetailFilter.MaterialNormalMap:
                return result.rule is MaterialNormalMapRule;

            // =========================
            // Model
            // =========================

            case RuleDetailFilter.ModelVertexCount:
                return result.rule is ModelVertexCountRule;

            case RuleDetailFilter.ModelName:
                return result.rule is AssetNamePrefixRule
                       && result.assetType == "Model";

            // =========================
            // Prefab
            // =========================

            case RuleDetailFilter.PrefabMissingScript:
                return result.rule is PrefabMissingScriptRule;

            case RuleDetailFilter.PrefabName:
                return result.rule is AssetNamePrefixRule
                       && result.assetType == "Prefab";

            // =========================
            // Unused
            // =========================

            case RuleDetailFilter.UnusedAsset:
                return result.rule is UnusedAssetRule;

            default:
                return true;
        }
    }
    
    private void DrawUnusedAssetTypeSummary()
    {
        int materialCount = 0;
        int textureCount = 0;
        int prefabCount = 0;

        foreach (CheckResult result in results)
        {
            if (result.assetType == "Material")
            {
                materialCount++;
            }
            else if (result.assetType == "Texture")
            {
                textureCount++;
            }
            else if (result.assetType == "Prefab")
            {
                prefabCount++;
            }
        }

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField("未使用资产分类统计", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("材质 Material", materialCount.ToString());
        EditorGUILayout.LabelField("纹理 Texture", textureCount.ToString());
        EditorGUILayout.LabelField("预制体 Prefab", prefabCount.ToString());

        EditorGUILayout.EndVertical();
    }

    private void DrawResults()
    {
        int visibleCount = GetVisibleResultCount();

        EditorGUILayout.LabelField($"当前显示：{visibleCount} 个检测项", EditorStyles.boldLabel);

        if (results.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "还没有检测结果。点击扫描贴图、扫描模型或扫描全部开始扫描。",
                MessageType.None
            );
            return;
        }

        if (visibleCount == 0)
        {
            EditorGUILayout.HelpBox("当前筛选条件下没有结果。", MessageType.None);
            return;
        }

        Dictionary<string, List<CheckResult>> groupedResults = GroupResultsByAssetPath();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        foreach (KeyValuePair<string, List<CheckResult>> pair in groupedResults)
        {
            string assetPath = pair.Key;
            List<CheckResult> assetResults = pair.Value;

            if (!ShouldShowAssetGroup(assetResults))
                continue;

            DrawAssetGroupItem(assetPath, assetResults);
        }

        EditorGUILayout.EndScrollView();
    }

    private Dictionary<string, List<CheckResult>> GroupResultsByAssetPath()
    {
        Dictionary<string, List<CheckResult>> groupedResults = new Dictionary<string, List<CheckResult>>();

        foreach (CheckResult result in results)
        {
            if (!groupedResults.ContainsKey(result.assetPath))
            {
                groupedResults.Add(result.assetPath, new List<CheckResult>());
            }

            groupedResults[result.assetPath].Add(result);
        }

        return groupedResults;
    }

    private bool ShouldShowAssetGroup(List<CheckResult> assetResults)
    {
        foreach (CheckResult result in assetResults)
        {
            if (ShouldShowResult(result))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawAssetGroupItem(string assetPath, List<CheckResult> assetResults)
    {
        List<CheckResult> visibleResults = new List<CheckResult>();

        foreach (CheckResult result in assetResults)
        {
            if (ShouldShowResult(result))
            {
                visibleResults.Add(result);
            }
        }

        if (visibleResults.Count == 0)
            return;

        int visibleFailedCount = 0;
        int visiblePassedCount = 0;

        foreach (CheckResult result in visibleResults)
        {
            if (result.passed)
                visiblePassedCount++;
            else
                visibleFailedCount++;
        }

        bool visibleHasFailed = visibleFailedCount > 0;

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.normal.textColor = visibleHasFailed ? Color.red : Color.green;

        string titleText;

        if (visibleHasFailed)
        {
            titleText = $"Failed  失败 {visibleFailedCount} 项";
        }
        else
        {
            titleText = $"Pass  通过 {visiblePassedCount} 项";
        }

        EditorGUILayout.LabelField(
            titleText,
            titleStyle,
            GUILayout.Width(150)
        );

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("定位", GUILayout.Width(60), GUILayout.Height(22)))
        {
            PingAsset(assetPath);
        }
        

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.TextField("资源路径", assetPath);

        EditorGUILayout.LabelField("资产规则总数", assetResults.Count.ToString());
        EditorGUILayout.LabelField("当前显示规则数", visibleResults.Count.ToString());
        EditorGUILayout.LabelField("当前显示通过数", visiblePassedCount.ToString());
        EditorGUILayout.LabelField("当前显示失败数", visibleFailedCount.ToString());

        EditorGUILayout.Space(4);

        foreach (CheckResult result in visibleResults)
        {
            DrawRuleResultItem(result);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawRuleResultItem(CheckResult result)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();

        GUIStyle statusStyle = new GUIStyle(EditorStyles.boldLabel);
        statusStyle.normal.textColor = result.passed ? Color.green : Color.red;

        EditorGUILayout.LabelField(result.passed ? "Pass" : "Failed", statusStyle, GUILayout.Width(60));

        EditorGUILayout.LabelField(result.ruleName, EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();

        if (!result.passed && result.rule is UnusedAssetRule unusedAssetRule)
        {
            EditorGUI.BeginDisabledGroup(!allowUnusedAssetSoftDelete);

            if (GUILayout.Button("软删除", GUILayout.Width(70), GUILayout.Height(22)))
            {
                SoftDeleteUnusedAssetWithRollback(result, unusedAssetRule);
                RefreshLastScan();

                GUIUtility.ExitGUI();
            }

            EditorGUI.EndDisabledGroup();
        }
        else if (!result.passed && result.canFix)
        {
            if (GUILayout.Button("修复", GUILayout.Width(60), GUILayout.Height(22)))
            {
                FixSingleResultWithRollback(result);
                RefreshLastScan();

                GUIUtility.ExitGUI();
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("资源类型", result.assetType);
        EditorGUILayout.LabelField("当前值", result.currentValue);
        EditorGUILayout.LabelField("限制值", result.limitValue);

        if (!string.IsNullOrEmpty(result.detailMessage))
        {
            EditorGUILayout.LabelField("明细", EditorStyles.boldLabel);

            GUIStyle detailStyle = new GUIStyle(EditorStyles.textArea);
            detailStyle.wordWrap = false;

            EditorGUILayout.TextArea(
                result.detailMessage,
                detailStyle,
                GUILayout.MinHeight(60)
            );
        }

        if (!result.passed)
        {
            EditorGUILayout.HelpBox(result.message, MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    private bool HasFixableFailedResult(List<CheckResult> assetResults)
    {
        foreach (CheckResult result in assetResults)
        {
            if (!result.passed && result.canFix)
            {
                return true;
            }
        }

        return false;
    }

    private void FixAssetFailedResultsWithRollback(List<CheckResult> assetResults)
    {
        FixRollbackGroup rollbackGroup = new FixRollbackGroup
        {
            title = "Fix Asset Failed Results"
        };

        foreach (CheckResult result in assetResults)
        {
            if (!result.passed && result.canFix)
            {
                FixResultWithRollback(result, rollbackGroup);
            }
        }

        PushRollbackGroupIfNeeded(rollbackGroup);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void ScanTextures()
    {
        lastScanScope = ScanScope.Textures;
        ruleDetailFilter = RuleDetailFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        ScanTexturesInternal(true);
    }
    
    private void ScanMaterials()
    {
        lastScanScope = ScanScope.Materials;
        ruleDetailFilter = RuleDetailFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        ScanMaterialsInternal(true);
    }

    private void ScanModels()
    {
        lastScanScope = ScanScope.Models;
        ruleDetailFilter = RuleDetailFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        ScanModelsInternal(true);
    }
    
    /// <summary>
    /// 项目资产没有变化时复用引用索引；
    /// 资产发生导入、移动、删除或修改后自动重新生成。
    /// </summary>
    private AssetReferenceIndex GetOrBuildReferenceIndex()
    {
        uint currentVersion =
            AssetDatabase.GlobalArtifactDependencyVersion;

        bool cacheInvalid =
            cachedReferenceIndex == null ||
            cachedReferenceVersion != currentVersion;

        if (cacheInvalid)
        {
            cachedReferenceIndex = AssetReferenceIndex.Build();
            cachedReferenceVersion = currentVersion;
        }

        return cachedReferenceIndex;
    }

    private void ScanPrefabs()
    {
        lastScanScope = ScanScope.Prefabs;
        ruleDetailFilter = RuleDetailFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        ScanPrefabsInternal(true);
    }
    
    private void ScanAll()
    {
        lastScanScope = ScanScope.All;
        ruleDetailFilter = RuleDetailFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        ScanTexturesInternal(false);
        ScanMaterialsInternal(false);
        ScanModelsInternal(false);
        ScanPrefabsInternal(false);

        Debug.Log($"TA Asset Checker: 全部扫描完成，共生成 {results.Count} 个检测项。");
    }
    
    private void ScanMaterialsInternal(bool log)
    {
        List<AssetCheckRule> rules = new List<AssetCheckRule>
        {
            new AssetNamePrefixRule("Material", allowNameAutoFix, checkNameReferenceOnScan, "M_"),
            new MaterialShaderRule(),
            new MaterialRenderQueueRule(
                allowMaterialShaderDefaultQueue,
                minMaterialRenderQueue,
                maxMaterialRenderQueue
            ),
            new MaterialNormalMapRule()
        };

        string[] searchFolders = { scanFolder };

        string[] guids = AssetDatabase.FindAssets("t:Material", searchFolders);

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            foreach (AssetCheckRule rule in rules)
            {
                CheckResult result = rule.Check(assetPath);

                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        if (log)
        {
            Debug.Log($"TA Asset Checker: 材质扫描完成，共生成 {results.Count} 个检测项。");
        }
    }

    private void ScanTexturesInternal(bool log)
    {
        List<AssetCheckRule> rules = new List<AssetCheckRule>
        {
            new TextureMaxSizeRule(maxTextureSize),
            new TextureMipMapRule(expectedMipMapEnabled),
            new AssetNamePrefixRule("Texture2D", allowNameAutoFix, checkNameReferenceOnScan, "T_")
        };

        string[] searchFolders = { scanFolder };

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", searchFolders);

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            foreach (AssetCheckRule rule in rules)
            {
                CheckResult result = rule.Check(assetPath);

                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        if (log)
        {
            Debug.Log($"TA Asset Checker: 贴图扫描完成，共生成 {results.Count} 个检测项。");
        }
    }

    private void ScanModelsInternal(bool log)
    {
        List<AssetCheckRule> rules = new List<AssetCheckRule>
        {
            new ModelVertexCountRule(maxModelVertexCount),
            new AssetNamePrefixRule("Model", allowNameAutoFix, checkNameReferenceOnScan, "SM_", "SK_")
        };

        string[] searchFolders = { scanFolder };

        string[] guids = AssetDatabase.FindAssets("", searchFolders);

        HashSet<string> checkedPaths = new HashSet<string>();

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            if (checkedPaths.Contains(assetPath))
                continue;

            checkedPaths.Add(assetPath);

            if (!IsModelAssetPath(assetPath))
                continue;

            foreach (AssetCheckRule rule in rules)
            {
                CheckResult result = rule.Check(assetPath);

                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        if (log)
        {
            Debug.Log($"TA Asset Checker: 模型扫描完成，共生成 {results.Count} 个检测项。");
        }
    }
    
    private void ScanUnusedAssets()
    {
        lastScanScope = ScanScope.Unused;
        resultFilter = ResultFilter.Failed;
        unusedAssetTypeFilter = UnusedAssetTypeFilter.All;

        results.Clear();

        if (!IsScanFolderValid())
            return;

        // 重点：未保存的场景改动不会写入 .unity 文件。
        // 如果你刚把 Prefab 拖进某个 Scene，但没保存，依赖扫描可能读不到。
        bool saveScenes = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        if (!saveScenes)
        {
            Debug.LogWarning("TA Asset Checker: 用户取消保存场景，已取消未使用资产扫描。");
            return;
        }

        AssetDatabase.SaveAssets();

        ScanUnusedAssetsInternal(true);
    }

    private void ScanUnusedAssetsInternal(bool log)
    {
        AssetReferenceIndex referenceIndex = GetOrBuildReferenceIndex();

        UnusedAssetRule rule = new UnusedAssetRule(
            referenceIndex,
            allowUnusedAssetSoftDelete
        );

        string[] searchFolders = { scanFolder };
        string[] guids = AssetDatabase.FindAssets("", searchFolders);

        HashSet<string> checkedPaths = new HashSet<string>();

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                EditorUtility.DisplayProgressBar(
                    "Scanning Unused Assets",
                    assetPath,
                    guids.Length > 0 ? (float)i / guids.Length : 1f
                );

                if (string.IsNullOrEmpty(assetPath))
                    continue;

                if (checkedPaths.Contains(assetPath))
                    continue;

                checkedPaths.Add(assetPath);

                if (!IsUnusedCandidatePath(assetPath))
                    continue;

                CheckResult result = rule.Check(assetPath);

                // 只显示疑似未使用项，避免列表太长。
                if (result != null && !result.passed)
                {
                    results.Add(result);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        resultFilter = ResultFilter.Failed;

        if (log)
        {
            Debug.Log($"TA Asset Checker: 未使用资产扫描完成，发现 {results.Count} 个疑似未使用资产。");
        }
    }

    private bool IsUnusedCandidatePath(string assetPath)
    {
        if (AssetReferenceIndex.IsInTrashFolder(assetPath))
            return false;

        string extension = System.IO.Path.GetExtension(assetPath).ToLower();

        if (IsTextureAssetExtension(extension))
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

    private bool IsTextureAssetExtension(string extension)
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
    
    private void ScanPrefabsInternal(bool log)
    {
        List<AssetCheckRule> rules = new List<AssetCheckRule>
        {
            new PrefabMissingScriptRule(),
            new AssetNamePrefixRule("Prefab", allowNameAutoFix, checkNameReferenceOnScan, "PF_")
        };

        string[] searchFolders = { scanFolder };

        string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            foreach (AssetCheckRule rule in rules)
            {
                CheckResult result = rule.Check(assetPath);

                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        if (log)
        {
            Debug.Log($"TA Asset Checker: Prefab 扫描完成，共生成 {results.Count} 个检测项。");
        }
    }

    private bool IsModelAssetPath(string assetPath)
    {
        string lowerPath = assetPath.ToLower();

        return lowerPath.EndsWith(".fbx")
               || lowerPath.EndsWith(".obj")
               || lowerPath.EndsWith(".dae")
               || lowerPath.EndsWith(".blend")
               || lowerPath.EndsWith(".3ds");
    }

    private bool IsScanFolderValid()
    {
        if (string.IsNullOrEmpty(scanFolder))
        {
            Debug.LogWarning("扫描目录为空。");
            return false;
        }

        if (!AssetDatabase.IsValidFolder(scanFolder))
        {
            Debug.LogWarning($"扫描目录不存在：{scanFolder}");
            return false;
        }

        return true;
    }

    private void RefreshLastScan()
    {
        switch (lastScanScope)
        {
            case ScanScope.Textures:
                ScanTextures();
                break;

            case ScanScope.Materials:
                ScanMaterials();
                break;

            case ScanScope.Models:
                ScanModels();
                break;

            case ScanScope.Prefabs:
                ScanPrefabs();
                break;

            case ScanScope.Unused:
                ScanUnusedAssets();
                break;

            case ScanScope.All:
                ScanAll();
                break;
        }
    }
    
    private void FixSingleResultWithRollback(CheckResult result)
    {
        FixRollbackGroup rollbackGroup = new FixRollbackGroup
        {
            title = "Fix Single Result"
        };

        FixResultWithRollback(result, rollbackGroup);

        PushRollbackGroupIfNeeded(rollbackGroup);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void FixResultWithRollback(CheckResult result, FixRollbackGroup rollbackGroup)
    {
        if (result == null || result.rule == null || !result.canFix)
            return;

        FixRollbackRecord record = CreateRollbackRecordBeforeFix(result);

        result.rule.Fix(result.assetPath);

        CompleteRollbackRecordAfterFix(record);

        if (record.HasAnyChange())
        {
            rollbackGroup.records.Add(record);
        }
    }

    private FixRollbackRecord CreateRollbackRecordBeforeFix(CheckResult result)
    {
        FixRollbackRecord record = new FixRollbackRecord
        {
            ruleName = result.ruleName,
            oldAssetPath = result.assetPath,
            assetGuid = AssetDatabase.AssetPathToGUID(result.assetPath)
        };

        TextureImporter importer = AssetImporter.GetAtPath(result.assetPath) as TextureImporter;

        if (importer != null)
        {
            record.hadTextureImporter = true;
            record.oldMaxTextureSize = importer.maxTextureSize;
            record.oldMipmapEnabled = importer.mipmapEnabled;
            record.oldTextureCompression = importer.textureCompression;
        }

        return record;
    }

    private void CompleteRollbackRecordAfterFix(FixRollbackRecord record)
    {
        if (record == null)
            return;

        record.newAssetPath = AssetDatabase.GUIDToAssetPath(record.assetGuid);

        if (string.IsNullOrEmpty(record.newAssetPath))
        {
            record.newAssetPath = record.oldAssetPath;
        }

        TextureImporter importer = AssetImporter.GetAtPath(record.newAssetPath) as TextureImporter;

        if (importer != null)
        {
            record.hasNewTextureImporter = true;
            record.newMaxTextureSize = importer.maxTextureSize;
            record.newMipmapEnabled = importer.mipmapEnabled;
            record.newTextureCompression = importer.textureCompression;
        }
    }

    private void PushRollbackGroupIfNeeded(FixRollbackGroup rollbackGroup)
    {
        if (rollbackGroup == null || rollbackGroup.records.Count == 0)
            return;

        rollbackGroups.Push(rollbackGroup);
    }

    private void RollbackLastFix()
    {
        if (rollbackGroups.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "回退修复",
                "没有可以回退的修复记录。",
                "确定"
            );

            return;
        }

        FixRollbackGroup group = rollbackGroups.Pop();

        StringBuilder summary = new StringBuilder();
        summary.AppendLine($"回退批次：{group.title}");
        summary.AppendLine();

        int rollbackCount = 0;

        // 反向回退，避免批量重命名时顺序冲突。
        for (int i = group.records.Count - 1; i >= 0; i--)
        {
            FixRollbackRecord record = group.records[i];

            bool success = RollbackRecord(record, summary);

            if (success)
            {
                rollbackCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshLastScan();

        summary.Insert(0, $"已回退 {rollbackCount} 个修改。\n\n");

        EditorUtility.DisplayDialog(
            "回退完成",
            summary.ToString(),
            "确定"
        );

        Debug.Log(summary.ToString());
    }

    private bool RollbackRecord(FixRollbackRecord record, StringBuilder summary)
    {
        if (record == null)
            return false;

        bool changed = false;

        string currentPath = AssetDatabase.GUIDToAssetPath(record.assetGuid);

        if (string.IsNullOrEmpty(currentPath))
        {
            summary.AppendLine($"回退失败：找不到资源 GUID：{record.assetGuid}");
            summary.AppendLine();
            return false;
        }

        // 1. 回退重命名
        if (!string.IsNullOrEmpty(record.oldAssetPath) &&
            !string.IsNullOrEmpty(record.newAssetPath) &&
            record.oldAssetPath != record.newAssetPath)
        {
            string oldName = System.IO.Path.GetFileNameWithoutExtension(record.oldAssetPath);
            string currentName = System.IO.Path.GetFileNameWithoutExtension(currentPath);

            string targetExistingPath = record.oldAssetPath;
            Object existingAsset = AssetDatabase.LoadAssetAtPath<Object>(targetExistingPath);

            if (existingAsset != null && targetExistingPath != currentPath)
            {
                summary.AppendLine($"命名回退失败：目标路径已存在：{targetExistingPath}");
                summary.AppendLine();
            }
            else
            {
                string error = AssetDatabase.RenameAsset(currentPath, oldName);

                if (!string.IsNullOrEmpty(error))
                {
                    summary.AppendLine($"命名回退失败：{currentPath}");
                    summary.AppendLine($"原因：{error}");
                    summary.AppendLine();
                }
                else
                {
                    summary.AppendLine($"命名回退：{currentName} -> {oldName}");
                    summary.AppendLine($"资源：{record.oldAssetPath}");
                    changed = true;
                }
            }
        }

        string pathAfterRenameRollback = AssetDatabase.GUIDToAssetPath(record.assetGuid);

        if (string.IsNullOrEmpty(pathAfterRenameRollback))
        {
            pathAfterRenameRollback = record.oldAssetPath;
        }

        // 2. 回退 TextureImporter 设置
        if (record.hadTextureImporter)
        {
            TextureImporter importer = AssetImporter.GetAtPath(pathAfterRenameRollback) as TextureImporter;

            if (importer != null)
            {
                bool importerChanged = false;
                StringBuilder importerSummary = new StringBuilder();

                if (importer.maxTextureSize != record.oldMaxTextureSize)
                {
                    importerSummary.AppendLine(
                        $"Max Size：{importer.maxTextureSize} -> {record.oldMaxTextureSize}"
                    );

                    importer.maxTextureSize = record.oldMaxTextureSize;
                    importerChanged = true;
                }

                if (importer.mipmapEnabled != record.oldMipmapEnabled)
                {
                    importerSummary.AppendLine(
                        $"MipMap：{importer.mipmapEnabled} -> {record.oldMipmapEnabled}"
                    );

                    importer.mipmapEnabled = record.oldMipmapEnabled;
                    importerChanged = true;
                }

                if (importer.textureCompression != record.oldTextureCompression)
                {
                    importerSummary.AppendLine(
                        $"Compression：{importer.textureCompression} -> {record.oldTextureCompression}"
                    );

                    importer.textureCompression = record.oldTextureCompression;
                    importerChanged = true;
                }

                if (importerChanged)
                {
                    importer.SaveAndReimport();

                    summary.AppendLine($"导入设置回退：{pathAfterRenameRollback}");
                    summary.Append(importerSummary.ToString());
                    summary.AppendLine();

                    changed = true;
                }
            }
            else
            {
                summary.AppendLine($"导入设置回退失败：找不到 TextureImporter：{pathAfterRenameRollback}");
                summary.AppendLine();
            }
        }

        return changed;
    }
    
    private void SoftDeleteUnusedAssetWithRollback(CheckResult result, UnusedAssetRule rule)
    {
        if (result == null || rule == null)
            return;

        if (!rule.CanSoftDelete(result.assetPath, out string reason))
        {
            EditorUtility.DisplayDialog(
                "无法软删除",
                reason,
                "确定"
            );

            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "软删除疑似未使用资产",
            $"即将把资源移动到 TAAssetCheckerTrash：\n\n{result.assetPath}\n\n" +
            "这个操作不是永久删除，可以通过 Rollback Last Soft Delete 撤回。\n\n是否继续？",
            "软删除",
            "取消"
        );

        if (!confirm)
            return;

        string oldPath = result.assetPath;
        string oldGuid = AssetDatabase.AssetPathToGUID(oldPath);

        bool success = rule.SoftDelete(
            oldPath,
            out string trashPath,
            out string message
        );

        if (!success)
        {
            EditorUtility.DisplayDialog(
                "软删除失败",
                message,
                "确定"
            );

            return;
        }

        SoftDeleteRollbackRecord record = new SoftDeleteRollbackRecord
        {
            assetGuid = oldGuid,
            oldPath = oldPath,
            trashPath = trashPath
        };

        softDeleteRollbackRecords.Push(record);

        EditorUtility.DisplayDialog(
            "软删除完成",
            $"已软删除资源：\n{oldPath}\n\n移动到：\n{trashPath}\n\n可以点击 Rollback Last Soft Delete 撤回。",
            "确定"
        );
    }

    private void RollbackLastSoftDelete()
    {
        if (softDeleteRollbackRecords.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "撤回软删除",
                "没有可以撤回的软删除记录。",
                "确定"
            );

            return;
        }

        SoftDeleteRollbackRecord record = softDeleteRollbackRecords.Pop();

        string currentPath = AssetDatabase.GUIDToAssetPath(record.assetGuid);

        if (string.IsNullOrEmpty(currentPath))
        {
            EditorUtility.DisplayDialog(
                "撤回失败",
                $"找不到被软删除资源的 GUID：\n{record.assetGuid}",
                "确定"
            );

            return;
        }

        Object existingAsset = AssetDatabase.LoadAssetAtPath<Object>(record.oldPath);

        if (existingAsset != null && currentPath != record.oldPath)
        {
            EditorUtility.DisplayDialog(
                "撤回失败",
                $"原路径已经存在资源，无法撤回：\n{record.oldPath}",
                "确定"
            );

            return;
        }

        EnsureUnityFolderForAssetPath(record.oldPath);

        string error = AssetDatabase.MoveAsset(currentPath, record.oldPath);

        if (!string.IsNullOrEmpty(error))
        {
            EditorUtility.DisplayDialog(
                "撤回失败",
                $"资源撤回失败：\n{currentPath}\n\n目标路径：\n{record.oldPath}\n\n原因：\n{error}",
                "确定"
            );

            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshLastScan();

        string summary =
            "已撤回软删除：\n\n" +
            $"从：\n{currentPath}\n\n" +
            $"恢复到：\n{record.oldPath}";

        EditorUtility.DisplayDialog(
            "撤回完成",
            summary,
            "确定"
        );

        Debug.Log(summary);
    }

    private void EnsureUnityFolderForAssetPath(string assetPath)
    {
        string folderPath = System.IO.Path.GetDirectoryName(assetPath)?.Replace("\\", "/");

        if (string.IsNullOrEmpty(folderPath))
            return;

        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
    
    private void FixAllFailed()
    {
        if (results.Count == 0)
        {
            Debug.LogWarning("没有检测结果，请先点击 Scan Textures / Scan Models / Scan All。");
            return;
        }

        List<CheckResult> fixableResults = new List<CheckResult>();

        foreach (CheckResult result in results)
        {
            if (!result.passed && result.canFix)
            {
                fixableResults.Add(result);
            }
        }

        if (fixableResults.Count == 0)
        {
            Debug.Log("TA Asset Checker: 没有可修复的失败项。");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Fix All Failed",
            $"即将修复 {fixableResults.Count} 个失败检测项。\n\n是否继续？",
            "继续修复",
            "取消"
        );

        if (!confirm)
            return;

        FixRollbackGroup rollbackGroup = new FixRollbackGroup
        {
            title = "Fix All Failed"
        };

        try
        {
            for (int i = 0; i < fixableResults.Count; i++)
            {
                CheckResult result = fixableResults[i];

                EditorUtility.DisplayProgressBar(
                    "Fixing Assets",
                    result.assetPath,
                    (float)i / fixableResults.Count
                );

                FixResultWithRollback(result, rollbackGroup);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        PushRollbackGroupIfNeeded(rollbackGroup);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshLastScan();

        Debug.Log($"TA Asset Checker: 已批量修复 {fixableResults.Count} 个失败检测项。");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshLastScan();

        Debug.Log($"TA Asset Checker: 已批量修复 {fixableResults.Count} 个失败检测项。");
    }

    private void SelectScanFolder()
    {
        string selectedFolder = EditorUtility.OpenFolderPanel("选择扫描目录", Application.dataPath, "");

        if (string.IsNullOrEmpty(selectedFolder))
            return;

        if (!selectedFolder.StartsWith(Application.dataPath))
        {
            EditorUtility.DisplayDialog(
                "目录无效",
                "请选择当前 Unity 项目 Assets 目录下的文件夹。",
                "确定"
            );

            return;
        }

        string relativePath = "Assets" + selectedFolder.Substring(Application.dataPath.Length);
        scanFolder = relativePath.Replace("\\", "/");
    }

    private void PingAsset(string assetPath)
    {
        Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);

        if (asset == null)
        {
            Debug.LogWarning($"找不到资产：{assetPath}");
            return;
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private bool ShouldShowResult(CheckResult result)
    {
        // 第一层：通过 / 失败 / 全部
        if (!ShouldShowByResultState(result))
            return false;

        // 第二层：命名、Shader、队列、尺寸等规则分类
        if (!ShouldShowRuleDetail(result))
            return false;

        // 第三层：未使用资产的材质、纹理、Prefab 分类
        if (lastScanScope == ScanScope.Unused)
        {
            return ShouldShowUnusedAssetType(result);
        }

        return true;
    }
    
    private bool ShouldShowUnusedAssetType(CheckResult result)
    {
        switch (unusedAssetTypeFilter)
        {
            case UnusedAssetTypeFilter.All:
                return true;

            case UnusedAssetTypeFilter.Material:
                return result.assetType == "Material";

            case UnusedAssetTypeFilter.Texture:
                return result.assetType == "Texture";

            case UnusedAssetTypeFilter.Prefab:
                return result.assetType == "Prefab";

            default:
                return true;
        }
    }

    private int GetVisibleResultCount()
    {
        int count = 0;

        foreach (CheckResult result in results)
        {
            if (ShouldShowResult(result))
            {
                count++;
            }
        }

        return count;
    }
    
    private class FixRollbackGroup
    {
        public string title;
        public readonly List<FixRollbackRecord> records = new List<FixRollbackRecord>();
    }

    private class FixRollbackRecord
    {
        public string ruleName;

        public string assetGuid;
        public string oldAssetPath;
        public string newAssetPath;

        public bool hadTextureImporter;
        public bool hasNewTextureImporter;

        public int oldMaxTextureSize;
        public int newMaxTextureSize;

        public bool oldMipmapEnabled;
        public bool newMipmapEnabled;

        public TextureImporterCompression oldTextureCompression;
        public TextureImporterCompression newTextureCompression;

        public bool HasAnyChange()
        {
            if (oldAssetPath != newAssetPath)
                return true;

            if (hadTextureImporter && hasNewTextureImporter)
            {
                if (oldMaxTextureSize != newMaxTextureSize)
                    return true;

                if (oldMipmapEnabled != newMipmapEnabled)
                    return true;

                if (oldTextureCompression != newTextureCompression)
                    return true;
            }

            return false;
        }
    }
    
    private class SoftDeleteRollbackRecord
    {
        public string assetGuid;
        public string oldPath;
        public string trashPath;
    }
    
}