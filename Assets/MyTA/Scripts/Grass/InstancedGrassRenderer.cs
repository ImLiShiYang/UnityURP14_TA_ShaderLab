using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 草实例化渲染器。
///
/// 当前版本目标：
/// 1. 以 Player / target 为中心随机撒一批草点。
/// 2. 用 Raycast 把草落到地面上。
/// 3. 用 Graphics.DrawMeshInstanced 批量绘制，不创建大量草 GameObject。
/// 4. 把草按世界坐标切成 Chunk，方便下一步做视锥剔除和无限加载。
/// 5. 可选：根据 target 位置动态创建 / 回收 Chunk，形成第一版“无限草”。
///
/// 当前版本还不做：
/// GPU Indirect。
/// 后续可以在这个基础上继续扩展。
/// </summary>
[DisallowMultipleComponent]
public class InstancedGrassRenderer : MonoBehaviour
{
    public enum GrassRenderMode
    {
        DrawMeshInstanced,
        DrawMeshInstancedIndirect
    }

    private enum DensityLodLevel
    {
        Near = 0,
        Mid = 1,
        Far = 2
    }

    // Unity 的 Graphics.DrawMeshInstanced 单次最多绘制 1023 个实例。
    // 超过这个数量时，需要拆成多个 draw batch。
    private const int MaxInstancesPerDrawCall = 1023;
    private static readonly int GrassMatricesId = Shader.PropertyToID("_GrassMatrices");
    private static readonly int UseIndirectGrassId = Shader.PropertyToID("_UseIndirectGrass");
    private static readonly int AllGrassMatricesId = Shader.PropertyToID("_AllGrassMatrices");
    private static readonly int VisibleGrassMatricesId = Shader.PropertyToID("_VisibleGrassMatrices");
    private static readonly int InstanceCountId = Shader.PropertyToID("_InstanceCount");
    private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
    private static readonly int CameraPositionWSId = Shader.PropertyToID("_CameraPositionWS");
    private static readonly int MaxDrawDistanceId = Shader.PropertyToID("_MaxDrawDistance");
    private static readonly int CullRadiusId = Shader.PropertyToID("_CullRadius");
    private const int GpuCullThreadGroupSize = 64;
    private const string GrassIndirectKeyword = "GRASS_INDIRECT";

    [Header("Target")]
    [Tooltip("第一版以这个目标为中心生成草。建议拖 Player 根节点。")]
    public Transform target;

    [Header("Grass")]
    [Tooltip("要实例化绘制的草 Mesh。可以从当前 Grass prefab 的 MeshFilter 上拖。")]
    public Mesh grassMesh;

    [Tooltip("草材质。需要使用支持 GPU Instancing 的草 Shader。")]
    public Material grassMaterial;

    [Tooltip("草 Mesh 的 submesh index。通常为 0。")]
    [Min(0)]
    public int submeshIndex = 0;

    [Header("Spawn")]
    [Tooltip("要生成的草数量。DrawMeshInstanced 单次最多 1023，本脚本会自动拆批。")]
    [Min(0)]
    public int instanceCount = 1000;

    [Tooltip("以 target 为中心的圆形生成半径。")]
    [Min(0.1f)]
    public float spawnRadius = 12f;

    [Tooltip("随机种子。同一个 seed 会生成同一批草。")]
    public int randomSeed = 12345;

    [Tooltip("草根稍微离开地面，避免和地面 Z-fighting。")]
    public float groundOffset = 0.01f;

    [Header("Chunk")]
    [Tooltip("草地分块大小。第二步只负责分块，还不做视锥剔除。")]
    [Min(0.5f)]
    public float chunkSize = 4f;

    [Tooltip("选中物体时显示生成出来的 Chunk 边界。")]
    public bool showChunkGizmos = true;

    [Header("Infinite Loading")]
    [Tooltip("开启后，不再只生成一次固定圆形草地，而是根据 target 位置动态创建 / 回收 Chunk。")]
    public bool enableInfiniteLoading = false;

    [Tooltip("无限模式下，target 周围多远范围内的 Chunk 会被保留。")]
    [Min(0.5f)]
    public float loadRadius = 20f;

    [Tooltip("无限模式下，每个 Chunk 内尝试生成多少棵草。")]
    [Min(0)]
    public int instancesPerChunk = 800;

    [Tooltip("无限模式下，每隔多少秒检查一次需要加载 / 回收的 Chunk。")]
    [Min(0.02f)]
    public float infiniteUpdateInterval = 0.2f;

    [Tooltip("每次检查最多新生成多少个 Chunk。数值越大，移动时越不容易空，但生成瞬间越容易卡。")]
    [Min(1)]
    public int maxNewChunksPerUpdate = 4;

    [Header("Density LOD")]
    [Tooltip("根据 Chunk 到 target 的距离降低远处草密度。")]
    public bool enableDensityLod = true;

    [Tooltip("从这个距离开始使用中距离密度。")]
    [Min(0f)]
    public float midLodStartDistance = 10f;

    [Tooltip("从这个距离开始使用远距离密度。")]
    [Min(0f)]
    public float farLodStartDistance = 16f;

    [Tooltip("近处 Chunk 的密度倍率。")]
    [Range(0.01f, 1f)]
    public float nearLodDensity = 1f;

    [Tooltip("中距离 Chunk 的密度倍率。")]
    [Range(0.01f, 1f)]
    public float midLodDensity = 0.6f;

    [Tooltip("远距离 Chunk 的密度倍率。")]
    [Range(0.01f, 1f)]
    public float farLodDensity = 0.3f;

    [Tooltip("玩家靠近已有低密度 Chunk 时，是否把它重建成更高密度。只升级，不主动降级，减少闪烁。")]
    public bool upgradeLoadedChunksToCloserLod = true;

    [Tooltip("每次无限加载检查最多升级多少个已有 Chunk，避免靠近时瞬间重建太多。")]
    [Min(0)]
    public int maxLodUpgradesPerUpdate = 2;

    [Header("Culling")]
    [Tooltip("用于视锥剔除的主相机。不填时会自动使用 Camera.main。")]
    public Camera cullingCamera;

    [Tooltip("是否跳过主相机视锥外的 Chunk。")]
    public bool enableFrustumCulling = true;

    [Tooltip("视锥剔除 Bounds 的额外扩张。数值越大，越不容易在屏幕边缘突然消失。")]
    [Min(0f)]
    public float cullingBoundsPadding = 1f;

    [Tooltip("选中物体时用绿色显示可见 Chunk，用灰色显示被剔除 Chunk。")]
    public bool showCullingStateInGizmos = true;

    [Header("GPU Instance Culling")]
    [Tooltip("只在 DrawMeshInstancedIndirect 模式下生效。CPU 仍然管理 chunk，Compute Shader 再剔除 chunk 内不可见的草。")]
    public bool enableGpuInstanceCulling = false;

    [Tooltip("负责把当前 chunk 内可见草筛到 Visible Matrix Buffer 的 Compute Shader。")]
    public ComputeShader gpuInstanceCullShader;

    [Tooltip("GPU 草实例剔除距离。0 表示只做视锥剔除，不额外按距离剔除。")]
    [Min(0f)]
    public float gpuCullMaxDistance = 0f;

    [Tooltip("每棵草用于 GPU 视锥剔除的近似包围球半径。太小会边缘闪烁，太大会少剔除。")]
    [Min(0f)]
    public float gpuCullBoundsRadius = 1.5f;

    [Header("Raycast")]
    [Tooltip("从随机点上方多高开始向下打射线。")]
    [Min(0.1f)]
    public float raycastHeight = 20f;

    [Tooltip("射线从起点向下检测多远。")]
    [Min(0.1f)]
    public float raycastDistance = 60f;

    [Tooltip("哪些 Layer 可以长草。")]
    public LayerMask groundMask = ~0;

    [Tooltip("最大可生草坡度。0 只允许水平面，90 允许垂直面。")]
    [Range(0f, 90f)]
    public float maxSlopeAngle = 60f;

    [Header("Transform")]
    [Tooltip("当前草模型本地 Z 是高度轴，所以默认需要 -90 度 X 旋转把草立起来。")]
    public Vector3 baseRotationEuler = new Vector3(-90f, 0f, 0f);

    [Tooltip("把 Mesh 沿高度轴的最低点对齐到地面，而不是把 pivot 对齐到地面。适合 pivot 在草中间的模型。")]
    public bool alignMeshRootToGround = true;

    [Tooltip("草模型本地空间里的高度轴。当前草模型是本地 Z 轴从根部长到草尖。")]
    public Vector3 meshHeightAxis = Vector3.forward;

    [Tooltip("根部对齐后额外沿地面法线抬高/压低。正数更高，负数更低。")]
    public float extraRootOffset = 0f;

    [Tooltip("是否让草沿地面法线倾斜。第一版默认关闭，避免 billboard 草在斜坡上表现过怪。")]
    public bool alignToGroundNormal = false;

    [Tooltip("草的随机等比缩放下限。")]
    [Min(0.001f)]
    public float minScale = 0.8f;

    [Tooltip("草的随机等比缩放上限。")]
    [Min(0.001f)]
    public float maxScale = 1.2f;

    [Header("Render")]
    [Tooltip("绘制模式。DrawMeshInstanced 是旧版安全路径；DrawMeshInstancedIndirect 每个可见 Chunk 一次 GPU Indirect 绘制。")]
    public GrassRenderMode renderMode = GrassRenderMode.DrawMeshInstanced;

    public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
    public bool receiveShadows = true;

    [Tooltip("自动打开材质的 Enable GPU Instancing。")]
    public bool autoEnableMaterialInstancing = true;

    [Tooltip("按下这个键重新生成当前这批草。")]
    public KeyCode regenerateKey = KeyCode.G;

    [Header("Debug Panel")]
    [Tooltip("运行时显示无限草调试面板。")]
    public bool showDebugPanel = true;

    [Tooltip("运行时开关调试面板的快捷键。")]
    public KeyCode debugPanelToggleKey = KeyCode.F8;

    [Tooltip("调试面板在屏幕上的位置和大小。")]
    public Rect debugPanelRect = new Rect(12f, 80f, 340f, 360f);

    // 第二步：不再把所有草放进一个大列表，而是按世界坐标分到多个 Chunk。
    // 下一步做视锥剔除时，就可以按 chunk.bounds 判断“整块画 / 整块不画”。
    private readonly Dictionary<Vector2Int, GrassChunk> chunks =
        new Dictionary<Vector2Int, GrassChunk>();

    private int generatedCount;
    private int drawCallCount;
    private int submittedDrawCallCount;
    private int visibleChunkCount;
    private int visibleInstanceCount;
    private readonly Plane[] frustumPlanes = new Plane[6];
    private readonly Vector4[] gpuFrustumPlaneVectors = new Vector4[6];
    private float nextInfiniteUpdateTime;
    private GrassRenderMode lastAppliedRenderMode;
    private bool hasAppliedRenderMode;
    private int gpuCullKernel = -1;
    private int gpuCulledChunkCount;
    private bool warnedMissingGpuCullShader;
    private float debugFps;
    private float debugFrameMs;
    private int lastInfiniteCreatedChunks;
    private int lastInfiniteRemovedChunks;
    private int lastInfiniteCreatedInstances;
    private int lastLodUpgradedChunks;
    private float lastInfiniteUpdateMs;

    public int GeneratedCount => generatedCount;
    public int DrawCallCount => drawCallCount;
    public int SubmittedDrawCallCount => submittedDrawCallCount;
    public int ChunkCount => chunks.Count;
    public int VisibleChunkCount => visibleChunkCount;
    public int VisibleInstanceCount => visibleInstanceCount;
    public int GpuCulledChunkCount => gpuCulledChunkCount;

    private void Awake()
    {
        // 如果没有手动指定 target，就尝试用 Player Tag 自动找角色。
        // 第一版草是围绕 target 生成的，所以 target 不能为空。
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
                target = player.transform;
        }

        if (cullingCamera == null)
            cullingCamera = Camera.main;
    }

    private void Start()
    {
        // 开始运行时生成一次草实例数据。
        // 之后每帧只负责绘制，不会重复随机生成。
        Regenerate();
    }

    private void Update()
    {
        UpdateDebugTiming();

        if (Input.GetKeyDown(debugPanelToggleKey))
            showDebugPanel = !showDebugPanel;

        // 调试用：运行时按 G 重新撒草。
        // 方便你修改半径、数量、地面 Layer 后快速看效果。
        if (Input.GetKeyDown(regenerateKey))
            Regenerate();

        if (enableInfiniteLoading && Time.time >= nextInfiniteUpdateTime)
        {
            nextInfiniteUpdateTime = Time.time + infiniteUpdateInterval;
            UpdateInfiniteChunks(false);
        }

        // DrawMeshInstanced 不是 Renderer 组件，
        // 所以需要每帧主动调用一次绘制。
        DrawGrass();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (gpuInstanceCullShader == null)
        {
            gpuInstanceCullShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/MyTA/Shaders/Grass/GrassInstanceCull.compute"
            );
        }
#endif

        // 保证缩放范围合法。
        if (maxScale < minScale)
            maxScale = minScale;

        if (chunkSize < 0.5f)
            chunkSize = 0.5f;

        if (loadRadius < chunkSize)
            loadRadius = chunkSize;

        if (farLodStartDistance < midLodStartDistance)
            farLodStartDistance = midLodStartDistance;

        if (cullingBoundsPadding < 0f)
            cullingBoundsPadding = 0f;

        if (gpuCullMaxDistance < 0f)
            gpuCullMaxDistance = 0f;

        if (gpuCullBoundsRadius < 0f)
            gpuCullBoundsRadius = 0f;

        // 高度轴不能是 0 向量，否则后面 normalized 会出问题。
        if (meshHeightAxis.sqrMagnitude < 0.0001f)
            meshHeightAxis = Vector3.forward;
    }

    private void OnDrawGizmosSelected()
    {
        Transform centerTarget = target != null ? target : transform;

        // 显示当前随机生成草的范围。
        Gizmos.color = new Color(0.25f, 1f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(centerTarget.position, spawnRadius);

        if (!showChunkGizmos)
            return;

        bool hasCullingCamera = enableFrustumCulling && cullingCamera != null;

        if (hasCullingCamera)
            GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);

        foreach (GrassChunk chunk in chunks.Values)
        {
            Bounds bounds = chunk.bounds;

            if (showCullingStateInGizmos && hasCullingCamera)
            {
                bool visible = IsChunkVisible(chunk);
                Gizmos.color = visible
                    ? new Color(0.15f, 1f, 0.35f, 0.45f)
                    : new Color(0.45f, 0.45f, 0.45f, 0.18f);
            }
            else
            {
                Gizmos.color = new Color(0.15f, 0.65f, 1f, 0.35f);
            }

            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }

    [ContextMenu("Regenerate Grass Instances")]
    public void Regenerate()
    {
        ClearChunks();
        generatedCount = 0;
        drawCallCount = 0;
        visibleChunkCount = 0;
        visibleInstanceCount = 0;
        nextInfiniteUpdateTime = 0f;
        lastInfiniteCreatedChunks = 0;
        lastInfiniteRemovedChunks = 0;
        lastInfiniteCreatedInstances = 0;
        lastLodUpgradedChunks = 0;
        lastInfiniteUpdateMs = 0f;

        if (target == null)
        {
            Debug.LogWarning("[InstancedGrassRenderer] Missing target. Drag Player to target.", this);
            return;
        }

        if (grassMesh == null || grassMaterial == null)
        {
            Debug.LogWarning("[InstancedGrassRenderer] Missing grassMesh or grassMaterial.", this);
            return;
        }

        // DrawMeshInstanced 要求材质开启 GPU Instancing。
        // 不开启时 Unity 会报错或者不绘制。
        ApplyRenderModeToMaterial();

        if (autoEnableMaterialInstancing && !grassMaterial.enableInstancing)
            grassMaterial.enableInstancing = true;

        if (enableInfiniteLoading)
        {
            UpdateInfiniteChunks(true);

            // Debug.Log(
            //     $"[InstancedGrassRenderer] Infinite mode generated {GeneratedCount} grass instances, chunks={ChunkCount}, drawCalls={DrawCallCount}.",
            //     this
            // );

            return;
        }

        System.Random random = new System.Random(randomSeed);

        Vector3 center = target.position;

        // 为了避免有些随机点 Raycast 不到地面，或者坡度不符合要求，
        // 尝试次数会比目标草数量更多一些。
        int maxAttempts = Mathf.Max(instanceCount * 8, instanceCount);

        Quaternion baseRotation = Quaternion.Euler(baseRotationEuler);

        // 如果模型 pivot 在草片中间，而不是草根，
        // 需要计算草 Mesh 在高度轴上的最低点偏移，
        // 后面用它把草根对齐到地面。
        Vector3 rootLocalOffset = alignMeshRootToGround
            ? CalculateMeshRootLocalOffset(grassMesh, meshHeightAxis)
            : Vector3.zero;

        for (int attempt = 0; attempt < maxAttempts && generatedCount < instanceCount; attempt++)
        {
            // 在 target 周围圆形区域内随机取一个 XZ 位置。
            Vector2 offset = RandomPointInCircle(random, spawnRadius);

            Vector3 rayOrigin = new Vector3(
                center.x + offset.x,
                center.y + raycastHeight,
                center.z + offset.y
            );

            if (!TryCreateGrassInstance(
                    rayOrigin,
                    random,
                    baseRotation,
                    rootLocalOffset,
                    out Matrix4x4 matrix,
                    out Vector3 rootPosition))
            {
                continue;
            }

            // 1. 先生成一棵草的矩阵 matrix
            // 2. 根据草根位置 rootPosition 算出它属于哪个 Chunk
            // 3. 找到这个 Chunk；如果没有就新建
            // 4. 把这棵草加进这个 Chunk
            // 5. 生成数量 +1
            Vector2Int chunkCoord = WorldToChunkCoord(rootPosition, chunkSize);
            GrassChunk chunk = GetOrCreateChunk(chunkCoord);

            chunk.Add(matrix, rootPosition, grassMesh.bounds);
            generatedCount++;
        }

        // 将每个 Chunk 内部的矩阵拆分成 1023 一组的绘制批次。
        BuildDrawBatches();

        Debug.Log(
            $"[InstancedGrassRenderer] Generated {GeneratedCount}/{instanceCount} grass instances, chunks={ChunkCount}, drawCalls={DrawCallCount}.",
            this
        );
    }

    private void UpdateInfiniteChunks(bool generateAllMissing)
    {
        float updateStartTime = Time.realtimeSinceStartup;

        if (target == null || grassMesh == null || grassMaterial == null)
            return;

        if (autoEnableMaterialInstancing && !grassMaterial.enableInstancing)
            grassMaterial.enableInstancing = true;

        Vector3 center = target.position;
        int chunkRadius = Mathf.CeilToInt(loadRadius / Mathf.Max(chunkSize, 0.0001f));
        Vector2Int centerCoord = WorldToChunkCoord(center, chunkSize);

        HashSet<Vector2Int> desiredCoords = new HashSet<Vector2Int>();
        float sqrLoadRadius = loadRadius * loadRadius;

        for (int z = -chunkRadius; z <= chunkRadius; z++)
        {
            for (int x = -chunkRadius; x <= chunkRadius; x++)
            {
                Vector2Int coord = new Vector2Int(centerCoord.x + x, centerCoord.y + z);
                Vector3 chunkCenter = ChunkCoordToWorldCenter(coord, chunkSize, center.y);
                Vector2 delta = new Vector2(chunkCenter.x - center.x, chunkCenter.z - center.z);

                if (delta.sqrMagnitude > sqrLoadRadius)
                    continue;

                desiredCoords.Add(coord);
            }
        }

        List<Vector2Int> coordsToRemove = new List<Vector2Int>();

        foreach (Vector2Int coord in chunks.Keys)
        {
            if (!desiredCoords.Contains(coord))
                coordsToRemove.Add(coord);
        }

        for (int i = 0; i < coordsToRemove.Count; i++)
        {
            GrassChunk chunk = chunks[coordsToRemove[i]];
            generatedCount -= chunk.InstanceCount;
            chunk.ReleaseIndirectBuffers();
            chunks.Remove(coordsToRemove[i]);
        }

        int generatedThisUpdate = 0;
        int generatedInstancesThisUpdate = 0;
        int upgradedThisUpdate = 0;

        if (upgradeLoadedChunksToCloserLod && maxLodUpgradesPerUpdate > 0)
        {
            foreach (Vector2Int coord in desiredCoords)
            {
                if (!chunks.TryGetValue(coord, out GrassChunk chunk))
                    continue;

                DensityLodLevel desiredLod = GetDensityLodLevel(coord, center);

                if (desiredLod >= chunk.densityLodLevel)
                    continue;

                generatedInstancesThisUpdate += RebuildChunk(chunk, desiredLod);
                upgradedThisUpdate++;

                if (!generateAllMissing && upgradedThisUpdate >= maxLodUpgradesPerUpdate)
                    break;
            }
        }

        foreach (Vector2Int coord in desiredCoords)
        {
            if (chunks.ContainsKey(coord))
                continue;

            generatedInstancesThisUpdate += GenerateChunk(coord);
            generatedThisUpdate++;

            if (!generateAllMissing && generatedThisUpdate >= maxNewChunksPerUpdate)
                break;
        }

        RecalculateDrawCallCount();

        lastInfiniteCreatedChunks = generatedThisUpdate;
        lastInfiniteRemovedChunks = coordsToRemove.Count;
        lastInfiniteCreatedInstances = generatedInstancesThisUpdate;
        lastLodUpgradedChunks = upgradedThisUpdate;
        lastInfiniteUpdateMs = (Time.realtimeSinceStartup - updateStartTime) * 1000f;
    }

    private int GenerateChunk(Vector2Int coord)
    {
        GrassChunk chunk = GetOrCreateChunk(coord);
        DensityLodLevel lodLevel = GetDensityLodLevel(coord, target.position);
        return PopulateChunk(chunk, lodLevel);
    }

    private int RebuildChunk(GrassChunk chunk, DensityLodLevel lodLevel)
    {
        generatedCount -= chunk.InstanceCount;
        chunk.Clear(chunkSize);
        return PopulateChunk(chunk, lodLevel);
    }

    private int PopulateChunk(GrassChunk chunk, DensityLodLevel lodLevel)
    {
        Vector2Int coord = chunk.coord;
        System.Random random = new System.Random(GetChunkSeed(coord));
        Quaternion baseRotation = Quaternion.Euler(baseRotationEuler);
        Vector3 rootLocalOffset = alignMeshRootToGround
            ? CalculateMeshRootLocalOffset(grassMesh, meshHeightAxis)
            : Vector3.zero;

        float safeChunkSize = Mathf.Max(chunkSize, 0.0001f);
        float minX = coord.x * safeChunkSize;
        float minZ = coord.y * safeChunkSize;
        float rayY = target.position.y + raycastHeight;
        int targetInstanceCount = GetTargetInstanceCount(lodLevel);
        int maxAttempts = Mathf.Max(targetInstanceCount * 8, targetInstanceCount);
        int generatedInChunk = 0;

        chunk.densityLodLevel = lodLevel;
        chunk.densityMultiplier = GetDensityMultiplier(lodLevel);
        chunk.targetInstanceCount = targetInstanceCount;

        for (int attempt = 0; attempt < maxAttempts && generatedInChunk < targetInstanceCount; attempt++)
        {
            Vector3 rayOrigin = new Vector3(
                minX + RandomRange(random, 0f, safeChunkSize),
                rayY,
                minZ + RandomRange(random, 0f, safeChunkSize)
            );

            if (!TryCreateGrassInstance(
                    rayOrigin,
                    random,
                    baseRotation,
                    rootLocalOffset,
                    out Matrix4x4 matrix,
                    out Vector3 rootPosition))
            {
                continue;
            }

            chunk.Add(matrix, rootPosition, grassMesh.bounds);
            generatedInChunk++;
            generatedCount++;
        }

        chunk.BuildDrawBatches();

        if (renderMode == GrassRenderMode.DrawMeshInstancedIndirect)
            chunk.BuildIndirectBuffers(grassMesh, submeshIndex, GrassMatricesId);

        return generatedInChunk;
    }

    private bool TryCreateGrassInstance(
        Vector3 rayOrigin,
        System.Random random,
        Quaternion baseRotation,
        Vector3 rootLocalOffset,
        out Matrix4x4 matrix,
        out Vector3 rootPosition)
    {
        matrix = Matrix4x4.identity;
        rootPosition = Vector3.zero;

        // 从上往下打射线，找到草应该落到的地面位置。
        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                raycastDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // 过滤太陡的坡面。
        // 比如石壁、垂直墙面不应该长草。
        float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);

        if (slopeAngle > maxSlopeAngle)
            return false;

        // 每棵草随机绕 Y 轴旋转，避免所有草朝向一致。
        float yaw = RandomRange(random, 0f, 360f);

        // 每棵草随机缩放，避免高度完全一致。
        float scale = RandomRange(random, minScale, maxScale);

        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Quaternion rotation = yawRotation * baseRotation;

        // 可选：让草跟随地面法线倾斜。
        // 第一版默认关闭，因为 billboard 草或者竖直草片在斜坡上可能会显得奇怪。
        if (alignToGroundNormal)
        {
            Quaternion groundAlign = Quaternion.FromToRotation(Vector3.up, hit.normal);
            rotation = groundAlign * rotation;
        }

        // rootPosition 表示“草根应该放到的位置”。
        // 这里沿地面法线稍微抬高，避免和地面闪烁。
        rootPosition = hit.point + hit.normal * (groundOffset + extraRootOffset);

        // 如果 pivot 不在草根，需要把整个实例位置反向偏移，
        // 让 Mesh 的最低点，而不是 pivot，对齐到 rootPosition。
        Vector3 position = rootPosition - rotation * (rootLocalOffset * scale);
        Vector3 scaleVector = Vector3.one * scale;

        matrix = Matrix4x4.TRS(position, rotation, scaleVector);
        return true;
    }

    private void DrawGrass()
    {
        if (grassMesh == null || grassMaterial == null || chunks.Count == 0)
            return;

        // 防止运行中材质 Instancing 被关掉。
        ApplyRenderModeToMaterial();

        if (!grassMaterial.enableInstancing)
        {
            if (!autoEnableMaterialInstancing)
                return;

            grassMaterial.enableInstancing = true;
        }

        int layer = gameObject.layer;
        bool useGpuInstanceCulling = IsGpuInstanceCullingActive();
        bool useFrustumCulling = enableFrustumCulling && cullingCamera != null;
        bool needsFrustumPlanes = (useFrustumCulling || useGpuInstanceCulling) && cullingCamera != null;

        if (needsFrustumPlanes)
            GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);

        if (useGpuInstanceCulling)
            UpdateGpuCullShaderGlobals();

        visibleChunkCount = 0;
        visibleInstanceCount = 0;
        submittedDrawCallCount = 0;
        gpuCulledChunkCount = 0;

        foreach (GrassChunk chunk in chunks.Values)
        {
            if (useFrustumCulling && !IsChunkVisible(chunk))
                continue;

            visibleChunkCount++;
            visibleInstanceCount += chunk.InstanceCount;

            if (renderMode == GrassRenderMode.DrawMeshInstancedIndirect)
            {
                if (!chunk.HasIndirectBuffers)
                    chunk.BuildIndirectBuffers(grassMesh, submeshIndex, GrassMatricesId);

                if (chunk.HasIndirectBuffers)
                {
                    ComputeBuffer drawMatrixBuffer = chunk.MatrixBuffer;

                    if (useGpuInstanceCulling && RunGpuInstanceCulling(chunk))
                    {
                        drawMatrixBuffer = chunk.VisibleMatrixBuffer;
                        gpuCulledChunkCount++;
                    }
                    else
                    {
                        chunk.ResetIndirectArgsToFull();
                    }

                    grassMaterial.SetBuffer(GrassMatricesId, drawMatrixBuffer);
                    chunk.PropertyBlock.SetBuffer(GrassMatricesId, drawMatrixBuffer);

                    Graphics.DrawMeshInstancedIndirect(
                        grassMesh,
                        submeshIndex,
                        grassMaterial,
                        chunk.bounds,
                        chunk.ArgsBuffer,
                        0,
                        chunk.PropertyBlock,
                        shadowCastingMode,
                        receiveShadows,
                        layer
                    );

                    submittedDrawCallCount++;
                }

                continue;
            }

            for (int i = 0; i < chunk.drawBatches.Count; i++)
            {
                // 这里没有创建草 GameObject。
                // Unity 会用同一个 grassMesh + grassMaterial，
                // 按照当前 chunk 的 draw batch 里的矩阵画出多棵草。
                Graphics.DrawMeshInstanced(
                    grassMesh,
                    submeshIndex,
                    grassMaterial,
                    chunk.drawBatches[i],
                    chunk.drawBatchCounts[i],
                    null,
                    shadowCastingMode,
                    receiveShadows,
                    layer
                );

                submittedDrawCallCount++;
            }
        }
    }

    private bool IsGpuInstanceCullingActive()
    {
        return renderMode == GrassRenderMode.DrawMeshInstancedIndirect &&
               enableGpuInstanceCulling &&
               cullingCamera != null &&
               EnsureGpuCullKernel();
    }

    private bool EnsureGpuCullKernel()
    {
        if (gpuInstanceCullShader == null)
        {
            if (!warnedMissingGpuCullShader)
            {
                Debug.LogWarning(
                    "[InstancedGrassRenderer] GPU instance culling is enabled, but gpuInstanceCullShader is missing.",
                    this
                );
                warnedMissingGpuCullShader = true;
            }

            return false;
        }

        if (gpuCullKernel >= 0)
            return true;

        try
        {
            gpuCullKernel = gpuInstanceCullShader.FindKernel("CullGrassInstances");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                $"[InstancedGrassRenderer] Cannot find compute kernel CullGrassInstances. {exception.Message}",
                this
            );
            gpuCullKernel = -1;
            return false;
        }

        return gpuCullKernel >= 0;
    }

    private void UpdateGpuCullShaderGlobals()
    {
        if (gpuInstanceCullShader == null || cullingCamera == null)
            return;

        for (int i = 0; i < frustumPlanes.Length; i++)
        {
            Plane plane = frustumPlanes[i];
            Vector3 normal = plane.normal;
            gpuFrustumPlaneVectors[i] = new Vector4(normal.x, normal.y, normal.z, plane.distance);
        }

        gpuInstanceCullShader.SetVectorArray(FrustumPlanesId, gpuFrustumPlaneVectors);
        gpuInstanceCullShader.SetVector(CameraPositionWSId, cullingCamera.transform.position);
        gpuInstanceCullShader.SetFloat(MaxDrawDistanceId, gpuCullMaxDistance);
        gpuInstanceCullShader.SetFloat(CullRadiusId, gpuCullBoundsRadius);
    }

    private bool RunGpuInstanceCulling(GrassChunk chunk)
    {
        if (chunk == null ||
            !chunk.HasIndirectBuffers ||
            !chunk.HasVisibleBuffer ||
            chunk.InstanceCount <= 0)
        {
            return false;
        }

        chunk.VisibleMatrixBuffer.SetCounterValue(0);

        gpuInstanceCullShader.SetInt(InstanceCountId, chunk.InstanceCount);
        gpuInstanceCullShader.SetBuffer(gpuCullKernel, AllGrassMatricesId, chunk.MatrixBuffer);
        gpuInstanceCullShader.SetBuffer(gpuCullKernel, VisibleGrassMatricesId, chunk.VisibleMatrixBuffer);

        int threadGroups = Mathf.CeilToInt(chunk.InstanceCount / (float)GpuCullThreadGroupSize);
        gpuInstanceCullShader.Dispatch(gpuCullKernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(chunk.VisibleMatrixBuffer, chunk.ArgsBuffer, sizeof(uint));
        return true;
    }

    private void BuildDrawBatches()
    {
        foreach (GrassChunk chunk in chunks.Values)
        {
            chunk.BuildDrawBatches();

            if (renderMode == GrassRenderMode.DrawMeshInstancedIndirect)
                chunk.BuildIndirectBuffers(grassMesh, submeshIndex, GrassMatricesId);
        }

        RecalculateDrawCallCount();
    }

    private void RecalculateDrawCallCount()
    {
        drawCallCount = 0;

        foreach (GrassChunk chunk in chunks.Values)
        {
            if (renderMode == GrassRenderMode.DrawMeshInstancedIndirect)
            {
                if (chunk.InstanceCount > 0)
                    drawCallCount++;

                continue;
            }

            drawCallCount += chunk.drawBatches.Count;
        }
    }

    // 根据 Chunk 坐标获取对应的 GrassChunk。
    // 如果这个 Chunk 还不存在，就创建一个新的并存进字典。
    private void ApplyRenderModeToMaterial()
    {
        if (grassMaterial == null)
            return;

        if (hasAppliedRenderMode && lastAppliedRenderMode == renderMode)
            return;

        bool useIndirect = renderMode == GrassRenderMode.DrawMeshInstancedIndirect;

        if (hasAppliedRenderMode &&
            lastAppliedRenderMode == GrassRenderMode.DrawMeshInstancedIndirect &&
            !useIndirect)
        {
            ReleaseAllIndirectBuffers();
        }

        if (useIndirect)
            grassMaterial.EnableKeyword(GrassIndirectKeyword);
        else
            grassMaterial.DisableKeyword(GrassIndirectKeyword);

        grassMaterial.SetFloat(UseIndirectGrassId, useIndirect ? 1f : 0f);

        lastAppliedRenderMode = renderMode;
        hasAppliedRenderMode = true;
    }

    private void ClearChunks()
    {
        ReleaseAllIndirectBuffers();
        chunks.Clear();
    }

    private void ReleaseAllIndirectBuffers()
    {
        foreach (GrassChunk chunk in chunks.Values)
            chunk.ReleaseIndirectBuffers();
    }

    private void UpdateDebugTiming()
    {
        float deltaTime = Time.unscaledDeltaTime;

        if (deltaTime <= 0f)
            return;

        float currentFps = 1f / deltaTime;
        debugFps = debugFps <= 0f
            ? currentFps
            : Mathf.Lerp(debugFps, currentFps, 0.08f);

        debugFrameMs = deltaTime * 1000f;
    }

    private void OnGUI()
    {
        if (!showDebugPanel)
            return;

        debugPanelRect = GUI.Window(
            GetInstanceID(),
            debugPanelRect,
            DrawDebugPanelWindow,
            "Grass Debug"
        );
    }

    private void DrawDebugPanelWindow(int windowId)
    {
        Vector2Int targetChunk = target != null
            ? WorldToChunkCoord(target.position, chunkSize)
            : Vector2Int.zero;

        int culledChunkCount = Mathf.Max(0, ChunkCount - VisibleChunkCount);
        int culledInstanceCount = Mathf.Max(0, GeneratedCount - VisibleInstanceCount);
        float visibleChunkPercent = ChunkCount > 0
            ? VisibleChunkCount * 100f / ChunkCount
            : 0f;
        CountDensityLodChunks(out int nearChunkCount, out int midChunkCount, out int farChunkCount);

        GUILayout.Label($"FPS: {debugFps:F1}  ({debugFrameMs:F2} ms)");
        GUILayout.Label($"Mode: {renderMode}");
        GUILayout.Label($"Target Chunk: ({targetChunk.x}, {targetChunk.y})");
        GUILayout.Space(4f);

        GUILayout.Label($"Chunks: {ChunkCount}  Visible: {VisibleChunkCount} ({visibleChunkPercent:F1}%)  Culled: {culledChunkCount}");
        GUILayout.Label($"Instances: {GeneratedCount}  Visible: {VisibleInstanceCount}  Culled: {culledInstanceCount}");
        GUILayout.Label($"Draw Calls: {SubmittedDrawCallCount} submitted / {DrawCallCount} loaded");
        GUILayout.Space(4f);

        GUILayout.Label($"Infinite: {(enableInfiniteLoading ? "On" : "Off")}  Load Radius: {loadRadius:F1}");
        GUILayout.Label($"Chunk Size: {chunkSize:F1}  Instances/Chunk: {instancesPerChunk}");
        GUILayout.Label($"LOD Chunks: Near {nearChunkCount} / Mid {midChunkCount} / Far {farChunkCount}");
        GUILayout.Label($"LOD Density: {nearLodDensity:F2} / {midLodDensity:F2} / {farLodDensity:F2}");
        GUILayout.Label($"Last Load: +{lastInfiniteCreatedChunks} chunks / -{lastInfiniteRemovedChunks} chunks / up {lastLodUpgradedChunks} LOD");
        GUILayout.Label($"Last New Grass: {lastInfiniteCreatedInstances}  Cost: {lastInfiniteUpdateMs:F2} ms");
        GUILayout.Space(4f);

        GUILayout.Label($"Culling: {(enableFrustumCulling ? "On" : "Off")}  Padding: {cullingBoundsPadding:F1}");
        GUILayout.Label($"GPU Cull: {(IsGpuInstanceCullingActive() ? "On" : "Off")}  Chunks: {GpuCulledChunkCount}");
        GUILayout.Label($"GPU Cull Distance: {gpuCullMaxDistance:F1}  Radius: {gpuCullBoundsRadius:F2}");
        GUILayout.Label($"Toggle: {debugPanelToggleKey}   Regenerate: {regenerateKey}");

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    private void OnDestroy()
    {
        ReleaseAllIndirectBuffers();
    }

    private GrassChunk GetOrCreateChunk(Vector2Int coord)
    {
        if (chunks.TryGetValue(coord, out GrassChunk chunk))
            return chunk;

        chunk = new GrassChunk(coord, chunkSize);
        chunks.Add(coord, chunk);
        return chunk;
    }

    private bool IsChunkVisible(GrassChunk chunk)
    {
        Bounds paddedBounds = chunk.bounds;
        paddedBounds.Expand(cullingBoundsPadding * 2f);
        return GeometryUtility.TestPlanesAABB(frustumPlanes, paddedBounds);
    }

    // 把世界坐标转换成 Chunk 坐标。
    // 这里只看 XZ 平面，因为草地分块是按地面平面划分的。
    private DensityLodLevel GetDensityLodLevel(Vector2Int coord, Vector3 center)
    {
        if (!enableDensityLod)
            return DensityLodLevel.Near;

        Vector3 chunkCenter = ChunkCoordToWorldCenter(coord, chunkSize, center.y);
        Vector2 delta = new Vector2(chunkCenter.x - center.x, chunkCenter.z - center.z);
        float distance = delta.magnitude;

        if (distance >= farLodStartDistance)
            return DensityLodLevel.Far;

        if (distance >= midLodStartDistance)
            return DensityLodLevel.Mid;

        return DensityLodLevel.Near;
    }

    private float GetDensityMultiplier(DensityLodLevel lodLevel)
    {
        if (!enableDensityLod)
            return 1f;

        switch (lodLevel)
        {
            case DensityLodLevel.Mid:
                return Mathf.Clamp01(midLodDensity);

            case DensityLodLevel.Far:
                return Mathf.Clamp01(farLodDensity);

            case DensityLodLevel.Near:
            default:
                return Mathf.Clamp01(nearLodDensity);
        }
    }

    private int GetTargetInstanceCount(DensityLodLevel lodLevel)
    {
        if (instancesPerChunk <= 0)
            return 0;

        int targetCount = Mathf.RoundToInt(instancesPerChunk * GetDensityMultiplier(lodLevel));
        return Mathf.Clamp(targetCount, 1, instancesPerChunk);
    }

    private void CountDensityLodChunks(out int nearCount, out int midCount, out int farCount)
    {
        nearCount = 0;
        midCount = 0;
        farCount = 0;

        foreach (GrassChunk chunk in chunks.Values)
        {
            switch (chunk.densityLodLevel)
            {
                case DensityLodLevel.Mid:
                    midCount++;
                    break;

                case DensityLodLevel.Far:
                    farCount++;
                    break;

                case DensityLodLevel.Near:
                default:
                    nearCount++;
                    break;
            }
        }
    }

    private static Vector2Int WorldToChunkCoord(Vector3 worldPosition, float chunkSize)
    {
        float safeChunkSize = Mathf.Max(chunkSize, 0.0001f);

        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / safeChunkSize),
            Mathf.FloorToInt(worldPosition.z / safeChunkSize)
        );
    }

    private static Vector3 ChunkCoordToWorldCenter(Vector2Int coord, float chunkSize, float y)
    {
        float safeChunkSize = Mathf.Max(chunkSize, 0.0001f);

        return new Vector3(
            (coord.x + 0.5f) * safeChunkSize,
            y,
            (coord.y + 0.5f) * safeChunkSize
        );
    }

    private int GetChunkSeed(Vector2Int coord)
    {
        unchecked
        {
            int hash = randomSeed;
            hash = hash * 73856093 ^ coord.x;
            hash = hash * 19349663 ^ coord.y;
            return hash;
        }
    }

    private class GrassChunk
    {
        // coord            这个 Chunk 的坐标
        // matrices         这个 Chunk 里的所有草矩阵
        // drawBatches      这个 Chunk 自己的绘制批次
        // drawBatchCounts  每个批次实际画多少棵
        public readonly Vector2Int coord;
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        public readonly List<Matrix4x4[]> drawBatches = new List<Matrix4x4[]>();
        public readonly List<int> drawBatchCounts = new List<int>();
        
        public MaterialPropertyBlock PropertyBlock { get; private set; }
        public ComputeBuffer ArgsBuffer { get; private set; }
        public ComputeBuffer MatrixBuffer => matrixBuffer;
        public ComputeBuffer VisibleMatrixBuffer => visibleMatrixBuffer;
        public DensityLodLevel densityLodLevel = DensityLodLevel.Near;
        public float densityMultiplier = 1f;
        public int targetInstanceCount;

        //这个 Chunk 的包围盒
        public Bounds bounds;

        private bool hasBounds;
        private ComputeBuffer matrixBuffer;
        private ComputeBuffer visibleMatrixBuffer;
        private uint[] indirectArgs;

        public int InstanceCount => matrices.Count;
        public bool HasIndirectBuffers => matrixBuffer != null && ArgsBuffer != null && PropertyBlock != null;
        public bool HasVisibleBuffer => visibleMatrixBuffer != null;

        public GrassChunk(Vector2Int coord, float chunkSize)
        {
            this.coord = coord;

            float safeChunkSize = Mathf.Max(chunkSize, 0.0001f);
            Vector3 center = new Vector3(
                (coord.x + 0.5f) * safeChunkSize,
                0f,
                (coord.y + 0.5f) * safeChunkSize
            );

            bounds = new Bounds(center, new Vector3(safeChunkSize, 1f, safeChunkSize));
        }

        public void Clear(float chunkSize)
        {
            ReleaseIndirectBuffers();

            matrices.Clear();
            drawBatches.Clear();
            drawBatchCounts.Clear();
            hasBounds = false;

            float safeChunkSize = Mathf.Max(chunkSize, 0.0001f);
            Vector3 center = new Vector3(
                (coord.x + 0.5f) * safeChunkSize,
                0f,
                (coord.y + 0.5f) * safeChunkSize
            );

            bounds = new Bounds(center, new Vector3(safeChunkSize, 1f, safeChunkSize));
        }

        public void Add(
            Matrix4x4 matrix,
            Vector3 rootPosition,
            Bounds meshBounds)
        {
            matrices.Add(matrix);

            Bounds instanceBounds = TransformBounds(meshBounds, matrix);

            // 保底包含草根位置，避免 mesh bounds 异常时 chunk bounds 高度不对。
            instanceBounds.Encapsulate(rootPosition);

            if (!hasBounds)
            {
                bounds = instanceBounds;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(instanceBounds);
        }

        public void BuildDrawBatches()
        {
            drawBatches.Clear();
            drawBatchCounts.Clear();

            for (int start = 0; start < matrices.Count; start += MaxInstancesPerDrawCall)
            {
                int count = Mathf.Min(MaxInstancesPerDrawCall, matrices.Count - start);

                // 这里数组长度固定为 1023。
                // 实际绘制数量由 drawBatchCounts 传给 DrawMeshInstanced。
                Matrix4x4[] batch = new Matrix4x4[MaxInstancesPerDrawCall];

                for (int i = 0; i < count; i++)
                    batch[i] = matrices[start + i];

                drawBatches.Add(batch);
                drawBatchCounts.Add(count);
            }
        }

        public void BuildIndirectBuffers(Mesh mesh, int submeshIndex, int grassMatricesId)
        {
            ReleaseIndirectBuffers();

            if (mesh == null || matrices.Count == 0)
                return;

            int safeSubmeshIndex = Mathf.Clamp(submeshIndex, 0, mesh.subMeshCount - 1);

            matrixBuffer = new ComputeBuffer(matrices.Count, 64);
            matrixBuffer.SetData(matrices);

            visibleMatrixBuffer = new ComputeBuffer(matrices.Count, 64, ComputeBufferType.Append);
            visibleMatrixBuffer.SetCounterValue(0);

            indirectArgs = new uint[5];
            indirectArgs[0] = mesh.GetIndexCount(safeSubmeshIndex);
            indirectArgs[1] = (uint)matrices.Count;
            indirectArgs[2] = mesh.GetIndexStart(safeSubmeshIndex);
            indirectArgs[3] = mesh.GetBaseVertex(safeSubmeshIndex);
            indirectArgs[4] = 0;

            ArgsBuffer = new ComputeBuffer(1, indirectArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            ArgsBuffer.SetData(indirectArgs);

            PropertyBlock = new MaterialPropertyBlock();
            PropertyBlock.SetBuffer(grassMatricesId, matrixBuffer);
        }

        public void ResetIndirectArgsToFull()
        {
            if (ArgsBuffer == null || indirectArgs == null)
                return;

            indirectArgs[1] = (uint)matrices.Count;
            ArgsBuffer.SetData(indirectArgs);
        }

        public void ReleaseIndirectBuffers()
        {
            if (matrixBuffer != null)
            {
                matrixBuffer.Release();
                matrixBuffer = null;
            }

            if (visibleMatrixBuffer != null)
            {
                visibleMatrixBuffer.Release();
                visibleMatrixBuffer = null;
            }

            if (ArgsBuffer != null)
            {
                ArgsBuffer.Release();
                ArgsBuffer = null;
            }

            PropertyBlock = null;
            indirectArgs = null;
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            Bounds worldBounds = new Bounds(matrix.MultiplyPoint3x4(localBounds.center), Vector3.zero);

            for (int ix = 0; ix <= 1; ix++)
            {
                for (int iy = 0; iy <= 1; iy++)
                {
                    for (int iz = 0; iz <= 1; iz++)
                    {
                        Vector3 corner = new Vector3(
                            ix == 0 ? min.x : max.x,
                            iy == 0 ? min.y : max.y,
                            iz == 0 ? min.z : max.z
                        );

                        worldBounds.Encapsulate(matrix.MultiplyPoint3x4(corner));
                    }
                }
            }

            return worldBounds;
        }
    }

    private static Vector2 RandomPointInCircle(System.Random random, float radius)
    {
        // sqrt(random) 可以保证点在圆面积内均匀分布。
        // 如果直接 random * radius，会导致点更集中在圆心附近。
        float angle = RandomRange(random, 0f, Mathf.PI * 2f);
        float distance = Mathf.Sqrt(Random01(random)) * radius;

        return new Vector2(
            Mathf.Cos(angle) * distance,
            Mathf.Sin(angle) * distance
        );
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return Mathf.Lerp(min, max, Random01(random));
    }

    private static float Random01(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static Vector3 CalculateMeshRootLocalOffset(Mesh mesh, Vector3 heightAxis)
    {
        if (mesh == null)
            return Vector3.zero;

        Vector3 axis = heightAxis.normalized;

        Bounds bounds = mesh.bounds;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        float minProjection = float.PositiveInfinity;

        // Mesh.bounds 是一个 AABB。
        // 这里遍历 bounds 的 8 个角点，
        // 找到它们在 heightAxis 上投影最小的位置。
        // 这个最小投影可以近似认为是草根所在的高度。
        for (int ix = 0; ix <= 1; ix++)
        {
            for (int iy = 0; iy <= 1; iy++)
            {
                for (int iz = 0; iz <= 1; iz++)
                {
                    Vector3 corner = new Vector3(
                        ix == 0 ? min.x : max.x,
                        iy == 0 ? min.y : max.y,
                        iz == 0 ? min.z : max.z
                    );

                    minProjection = Mathf.Min(minProjection, Vector3.Dot(corner, axis));
                }
            }
        }

        if (float.IsInfinity(minProjection))
            return Vector3.zero;

        // 返回草根在模型本地空间中的偏移。
        // 外部会用 position = rootPosition - rotation * rootLocalOffset，
        // 把这个最低点对齐到地面。
        return axis * minProjection;
    }
}
