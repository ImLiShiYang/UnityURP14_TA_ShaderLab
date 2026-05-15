using System.Collections;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 基于角色真实脚骨骼生成脚印贴花。
/// 
/// 这个脚本不再根据角色中心移动距离生成脚印，
/// 而是通过左脚 / 右脚骨骼的位置向下 Raycast，
/// 在脚真正落地的位置生成脚印。
/// </summary>
public class FootprintDecalSpawner : MonoBehaviour
{
    // ============================================================
    // References
    // ============================================================

    [Header("References")]
    [Tooltip("角色根节点，一般是 player 物体。用于获取角色朝向。")]
    public Transform characterRoot;

    [Tooltip("角色 Animator，一般挂在模型子物体上，例如 Ayaka。")]
    public Animator animator;

    [Tooltip("玩家移动控制器。用于判断当前是否真的有移动输入。")]
    public ThirdPersonPlayerController playerController;
    
    [Header("Surface Mask")]
    [Tooltip("是否只允许在指定表面区域生成脚印")]
    public bool useSurfaceMask = true;

    [Tooltip("湿地区域判断组件")]
    public FootprintWetlandMask wetlandMask;


    // ============================================================
    // Foot Bones
    // ============================================================

    [Header("Foot Bones")]
    [Tooltip("左脚 Foot 骨骼。通常对应 HumanBodyBones.LeftFoot。")]
    public Transform leftFoot;

    [Tooltip("右脚 Foot 骨骼。通常对应 HumanBodyBones.RightFoot。")]
    public Transform rightFoot;

    [Tooltip("左脚 Toes 骨骼。通常对应 HumanBodyBones.LeftToes。")]
    public Transform leftToes;

    [Tooltip("右脚 Toes 骨骼。通常对应 HumanBodyBones.RightToes。")]
    public Transform rightToes;

    [Tooltip("0 = 使用 Foot 骨骼点，1 = 使用 Toes 骨骼点，0.5~0.7 通常接近脚掌中心。")]
    [Range(0f, 1f)]
    public float toeBlend = 0.6f;


    // ============================================================
    // Footprint Assets
    // ============================================================

    [Header("Footprint Assets")]
    [Tooltip("脚印 Decal prefab，需要挂 ScreenSpaceDecalProjector。")]
    public ScreenSpaceDecalProjector footprintPrefab;

    [Tooltip("左脚脚印贴图。")]
    public Texture2D leftFootTexture;

    [Tooltip("右脚脚印贴图。")]
    public Texture2D rightFootTexture;
    
    [Tooltip("左脚脚印法线贴图，用来制造泥地凹陷 / 凸起边缘效果。")]
    public Texture2D leftFootNormalTexture;

    [Tooltip("右脚脚印法线贴图，用来制造泥地凹陷 / 凸起边缘效果。")]
    public Texture2D rightFootNormalTexture;
    

    [Header("Footprint Visual")]
    [Tooltip(
        "脚印整体透明度。\n" +
        "0 = 完全透明，看不见脚印。\n" +
        "1 = 完全不透明，脚印最明显。\n\n" +
        "湿地脚印建议使用 0.35 ~ 0.6。\n" +
        "如果脚印太像贴纸，就降低这个值。"
    )]
    [Range(0f, 1f)]
    public float footprintOpacity = 0.5f;

    [Tooltip(
        "脚印投射盒边缘淡出范围。\n" +
        "值越大，Decal 盒子边缘越柔和。\n" +
        "值越小，Decal 边缘越硬。\n\n" +
        "注意：这个参数主要控制投射盒边缘的淡出，\n" +
        "不能完全替代脚印贴图本身的 Alpha 羽化。\n\n" +
        "湿地脚印建议使用 0.08 ~ 0.15。\n" +
        "如果脚印边缘太硬，就增大这个值。"
    )]
    [Range(0f, 0.5f)]
    public float footprintEdgeFade = 0.08f;
    
    [Tooltip(
        "脚印法线效果强度。\n" +
        "0 = 没有凹凸感。\n" +
        "1 = 凹凸感最强。\n\n" +
        "湿地脚印建议 0.25 ~ 0.45。"
    )]
    [Range(0f, 1f)]
    public float footprintNormalStrength = 0.35f;


    // ============================================================
    // Pooling
    // ============================================================

    [Header("Pooling")]
    [Tooltip("是否使用对象池生成脚印。关闭后会使用 Instantiate / Destroy fallback。")]
    public bool usePooling = true;

    [Tooltip("脚印对象池。负责复用 ScreenSpaceDecalProjector。")]
    public FootprintDecalPool footprintPool;


    // ============================================================
    // Ground Raycast
    // ============================================================

    [Header("Ground Raycast")]
    [Tooltip("地面检测 Layer。建议只检测 Ground 层，不要一直用 Everything。")]
    public LayerMask groundMask = ~0;

    [Tooltip("Raycast 起点相对脚掌中心向上的高度。新模型建议 0.08~0.12。")]
    [Min(0f)]
    public float rayStartHeight = 0.1f;

    [Tooltip("从 Ray Origin 向下检测地面的距离。")]
    [Min(0f)]
    public float rayDistance = 0.8f;

    [Tooltip("让脚印略微离开地面，避免深度闪烁。")]
    public float surfaceOffset = 0.02f;


    // ============================================================
    // Footprint Placement
    // ============================================================

    [Header("Footprint Placement")]
    [Tooltip("走路时脚印沿角色前进方向的额外偏移。脚印偏后就增大，偏前就减小。")]
    public float walkFootForwardOffset = 0.08f;

    [Tooltip("跑步时脚印沿角色前进方向的额外偏移。")]
    public float runFootForwardOffset = 0.04f;

    [Tooltip("修正脚印贴图朝向。如果脚印横着或反了，就调这个角度。")]
    public float footprintYawOffset = 0f;


    // ============================================================
    // Footprint Size
    // ============================================================

    [Header("Footprint Size")]
    [Tooltip("走路脚印尺寸。x = 宽度，y = 长度，z = 投射深度。")]
    public Vector3 walkFootprintSize = new Vector3(0.18f, 0.32f, 0.10f);

    [Tooltip("跑步脚印尺寸。x = 宽度，y = 长度，z = 投射深度。")]
    public Vector3 runFootprintSize = new Vector3(0.20f, 0.36f, 0.10f);
    
    


    // ============================================================
    // Lifetime
    // ============================================================

    [Header("Lifetime")]
    [Tooltip("脚印完整显示时间。")]
    [Min(0f)]
    public float footprintVisibleTime = 3f;

    [Tooltip("脚印淡出时间。")]
    [Min(0f)]
    public float footprintFadeTime = 2f;


    // ============================================================
    // Spawn Conditions
    // ============================================================

    [Header("Spawn Conditions")]
    [Tooltip("Animator MoveSpeed 小于该值时，不生成脚印。用于避免 Idle 时生成脚印。")]
    public float minAnimatorMoveSpeed = 0.05f;

    [Tooltip("防止同一只脚在短时间内重复生成脚印。")]
    public float minTimeBetweenSameFoot = 0.15f;


    // ============================================================
    // Screenshot Debug
    // ============================================================

    [Header("Screenshot Debug")]
    [Tooltip("生成脚印后自动暂停 Unity，方便截图。")]
    public bool pauseOnFootprintSpawn = false;

    [Tooltip("只自动暂停一次。")]
    public bool pauseOnlyOnce = true;


    // ============================================================
    // Debug Gizmos
    // ============================================================

    [Header("Debug Gizmos")]
    public bool showFootprintDebugGizmos = true;

    [Tooltip("只显示左脚调试数据。")]
    public bool showLeftFootDebug = true;

    [Tooltip("只显示右脚调试数据。")]
    public bool showRightFootDebug = false;

    [Tooltip("显示 Foot / Toes 骨骼点。")]
    public bool showFootBones = false;

    [Tooltip("显示由 Foot 和 Toes 插值得到的脚掌中心点。")]
    public bool showFootCenter = true;

    [Tooltip("显示从脚掌中心向下的 Raycast。")]
    public bool showRaycast = true;

    [Tooltip("显示 Raycast 命中点。")]
    public bool showHitPoint = true;

    [Tooltip("显示最终脚印生成点。")]
    public bool showSpawnPoint = true;

    [Tooltip("显示地面法线。")]
    public bool showGroundNormal = false;

    [Tooltip("显示脚印朝向。")]
    public bool showFootForward = true;

    [Tooltip("显示 Decal 投射盒。")]
    public bool showDecalBox = true;

    [Tooltip("显示 Scene View 文字标签。")]
    public bool showDebugLabels = false;

    [Tooltip("Debug 点大小。")]
    [Range(0.005f, 0.1f)]
    public float debugPointSize = 0.015f;

    [Tooltip("地面法线显示长度。")]
    public float debugNormalLength = 0.25f;

    [Tooltip("脚印朝向线显示长度。")]
    public float debugForwardLength = 0.25f;

    [Tooltip("文字标签向上偏移高度。")]
    public float debugLabelHeight = 0.035f;


    // ============================================================
    // Runtime State
    // ============================================================

    private bool _hasPausedForScreenshot;

    private float _lastLeftFootTime = -999f;
    private float _lastRightFootTime = -999f;


    /// <summary>
    /// 当前调试的是左脚还是右脚。
    /// </summary>
    private enum DebugFootSide
    {
        Left,
        Right
    }


    /// <summary>
    /// 单次脚印生成的调试数据。
    /// 
    /// 这里只保存“最后一次生成脚印时”的关键数据，
    /// 用于 OnDrawGizmos 在 Scene View 中可视化。
    /// </summary>
    private struct FootprintDebugData
    {
        public bool valid;

        public DebugFootSide footSide;

        public Vector3 footBonePosition;
        public Vector3 toeBonePosition;
        public bool hasToe;

        public Vector3 footCenterPosition;

        public Vector3 rayOrigin;
        public Vector3 rayEnd;

        public bool rayHit;
        public Vector3 hitPoint;
        public Vector3 groundNormal;

        public Vector3 spawnPosition;
        public Quaternion spawnRotation;

        public Vector3 forwardOnSurface;

        public Vector3 decalSize;
        public Vector3 decalPivot;

        public float time;
    }


    /// <summary>
    /// 最后一次左脚脚印调试数据。
    /// </summary>
    private FootprintDebugData _lastLeftDebugData;


    /// <summary>
    /// 最后一次右脚脚印调试数据。
    /// </summary>
    private FootprintDebugData _lastRightDebugData;

    /// <summary>
    /// 初始化引用。
    /// 
    /// 如果没有手动指定 Animator 或左右脚骨骼，
    /// 会自动从 Humanoid Avatar 中查找 LeftFoot 和 RightFoot。
    /// </summary>
    private void Awake()
    {
        if (characterRoot == null)
        {
            characterRoot = transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            if (leftFoot == null)
            {
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            }

            if (rightFoot == null)
            {
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            
            if (leftToes == null)
            {
                leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            }

            if (rightToes == null)
            {
                rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            }
        }
        
        if (playerController == null)
        {
            playerController = GetComponentInParent<ThirdPersonPlayerController>();

            if (playerController == null)
            {
                playerController = GetComponent<ThirdPersonPlayerController>();
            }
        }
        
        if (usePooling && footprintPool == null)
        {
            #if UNITY_2023_1_OR_NEWER
                footprintPool = FindFirstObjectByType<FootprintDecalPool>();
            #else
                footprintPool = FindObjectOfType<FootprintDecalPool>();
            #endif
        }
        
        if (wetlandMask == null)
        {
            wetlandMask = FindObjectOfType<FootprintWetlandMask>();
        }
        
    }
    
    private Vector3 GetCurrentFootprintSize()
    {
        if (animator != null && animator.GetFloat("MoveSpeed") > 0.75f)
            return runFootprintSize;

        return walkFootprintSize;
    }
    
    private float GetCurrentFootForwardOffset()
    {
        if (animator == null)
            return walkFootForwardOffset;

        float moveSpeed = animator.GetFloat("MoveSpeed");

        // 你的 Blend Tree 里一般是：
        // 0 = Idle
        // 0.5 = Walk
        // 1 = Run
        if (moveSpeed > 0.75f)
            return runFootForwardOffset;

        return walkFootForwardOffset;
    }

    /// <summary>
    /// 判断当前是否允许生成脚印。
    /// 
    /// 即使 Animation Event 触发了，
    /// 如果角色当前没有移动输入，或者 Animator 的 MoveSpeed 已经接近 0，
    /// 也不生成脚印。
    /// </summary>
    private bool CanSpawnFootprint()
    {
        if (playerController != null && !playerController.HasMoveInput)
            return false;

        if (animator != null && animator.GetFloat("MoveSpeed") < minAnimatorMoveSpeed)
            return false;

        return true;
    }
    
    /// <summary>
    /// 生成左脚脚印。
    /// 
    /// 这个方法通常由 Animation Event 调用。
    /// 当走路或跑步动画播放到“左脚踩到地面”的那一帧时，
    /// 动画事件会调用这个方法。
    /// </summary>
    public void SpawnLeftFootprint()
    {
        
        if (!CanSpawnFootprint())
            return;
        
        // Time.time 表示游戏从开始运行到现在经过了多少秒。
        //
        // _lastLeftFootTime 记录的是上一次生成左脚脚印的时间。
        //
        // Time.time - _lastLeftFootTime
        // 就表示“距离上一次生成左脚脚印已经过了多久”。
        //
        // minTimeBetweenSameFoot 是允许同一只脚再次生成脚印的最小间隔时间。
        //
        // 如果间隔时间太短，说明可能是：
        // 1. 动画事件重复触发了
        // 2. 同一个动画帧附近连续生成了多次
        // 3. 动画循环或 Blend Tree 切换时造成重复调用
        //
        // 这种情况下直接 return，不生成新的脚印。
        if (Time.time - _lastLeftFootTime < minTimeBetweenSameFoot)
            return;

        // 记录这一次左脚生成脚印的时间。
        //
        // 下次再调用 SpawnLeftFootprint() 时，
        // 就会拿当前时间和这个时间进行比较，
        // 判断是否距离上一次已经足够久。
        _lastLeftFootTime = Time.time;

        // 真正生成脚印。
        //
        // leftFoot 是左脚骨骼的 Transform。
        // SpawnFootprint 会根据 leftFoot.position 从脚的位置向下 Raycast，
        // 找到地面后，在对应地面位置生成一个 decal 脚印。
        //
        // leftFootTexture 是左脚脚印贴图。
        // 传进去后，生成的 decal 会使用左脚的纹理。
        // SpawnFootprint(leftFoot, leftFootTexture);
        SpawnFootprint(DebugFootSide.Left, leftFoot, leftToes, leftFootTexture);
    }

    /// <summary>
    /// 动画事件调用：生成右脚脚印。
    /// 
    /// 在 Walking / Running 动画中，
    /// 右脚踩到地面的那一帧调用这个方法。
    /// </summary>
    public void SpawnRightFootprint()
    {
        if (!CanSpawnFootprint())
            return;
        
        if (Time.time - _lastRightFootTime < minTimeBetweenSameFoot)
            return;

        _lastRightFootTime = Time.time;

        // SpawnFootprint(rightFoot, rightFootTexture);
        SpawnFootprint(DebugFootSide.Right, rightFoot, rightToes, rightFootTexture);
    }

    /// <summary>
    /// 根据指定脚骨骼的位置生成一个脚印。
    /// 
    /// 这个方法会从脚的位置向下 Raycast，
    /// 找到地面后，把脚印贴花放到真实落地点。
    /// </summary>
    // private void SpawnFootprint(Transform footTransform, Texture2D footTexture)
    private void SpawnFootprint(DebugFootSide footSide,Transform footTransform,Transform toeTransform, Texture2D footTexture)
    {
        if (footTransform == null || footprintPrefab == null)
            return;

        // 记录脚骨骼位置
        Vector3 footBonePosition = footTransform.position;
        
        // 如果有 Toes 骨骼，就用 Foot 和 Toes 插值得到脚掌中心
        Vector3 footCenterPosition = footBonePosition;
        Vector3 toeBonePosition = Vector3.zero;
        bool hasToe = toeTransform != null;
        
        // Debug.Log($"Foot = {footTransform.name}, Toe = {(toeTransform != null ? toeTransform.name : "NULL")}, toeBlend = {toeBlend}");
        
        if (hasToe)
        {
            toeBonePosition = toeTransform.position;
            
            footCenterPosition = Vector3.Lerp(footTransform.position,toeTransform.position,toeBlend);
        }

        // 从脚掌中心上方开始向下检测地面
        Vector3 rayOrigin = footCenterPosition + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;
        Vector3 rayEnd = rayOrigin + Vector3.down * totalRayDistance;
        
        if (!Physics.Raycast(rayOrigin,Vector3.down,out RaycastHit hit,totalRayDistance,groundMask,QueryTriggerInteraction.Ignore))
        {
            CacheFootprintDebugData(
                footSide,
                footBonePosition,
                toeBonePosition,
                hasToe,
                footCenterPosition,
                rayOrigin,
                rayEnd,
                false,
                Vector3.zero,
                Vector3.up,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero
            );

            return;
        }
        
        // 从脚骨骼上方开始向下检测地面
        // Vector3 rayOrigin = footTransform.position + Vector3.up * rayStartHeight;
        // float totalRayDistance = rayStartHeight + rayDistance;
        //
        // if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, totalRayDistance, groundMask, QueryTriggerInteraction.Ignore))
        //     return;

        Vector3 normal = hit.normal;
        
        if (useSurfaceMask && wetlandMask != null)
        {
            if (!wetlandMask.CanSpawnAt(hit.point))
                return;
        }

        // 用角色朝向作为脚印朝向
        // 这样脚印会和角色移动方向保持一致
        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(characterRoot.forward, normal);

        if (forwardOnSurface.sqrMagnitude < 0.0001f)
        {
            forwardOnSurface = Vector3.ProjectOnPlane(footTransform.forward, normal);
        }

        if (forwardOnSurface.sqrMagnitude < 0.0001f)
            return;

        forwardOnSurface.Normalize();

        
        float currentForwardOffset = GetCurrentFootForwardOffset();
        // 脚印位置：
        // hit.point 是地面点
        // footForwardOffset 让脚印从脚踝稍微移动到脚掌中心
        // surfaceOffset 避免贴花和地面完全重合导致闪烁
        Vector3 spawnPosition =hit.point +forwardOnSurface * currentForwardOffset + normal * surfaceOffset;

        // local +Z 指向地面内部
        // local +Y 对齐脚尖方向
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        // 用于修正脚印贴图方向
        spawnRotation = Quaternion.AngleAxis(footprintYawOffset, normal) * spawnRotation;

        // ScreenSpaceDecalProjector decal = Instantiate(footprintPrefab, spawnPosition, spawnRotation);

        // decal.decalTexture = footTexture;
        // decal.size = GetCurrentFootprintSize();
        //
        // // 让投射盒从地面附近往地面内部投射
        // decal.pivot = new Vector3(0f, 0f, GetCurrentFootprintSize().z * 0.5f);
        
        
        Vector3 currentFootprintSize = GetCurrentFootprintSize();
        
        Vector3 pivot = new Vector3(0f, 0f, currentFootprintSize.z * 0.5f);
        
        CacheFootprintDebugData(
            footSide,
            footBonePosition,
            toeBonePosition,
            hasToe,
            footCenterPosition,
            rayOrigin,
            rayEnd,
            true,
            hit.point,
            normal,
            spawnPosition,
            spawnRotation,
            forwardOnSurface,
            currentFootprintSize,
            pivot
        );
        
        Texture2D footNormalTexture =footSide == DebugFootSide.Left ? leftFootNormalTexture: rightFootNormalTexture;
            
               
                
        
        if (usePooling && footprintPool != null)
        {
            footprintPool.SpawnFootprint(spawnPosition,spawnRotation,footTexture,footNormalTexture,footprintNormalStrength,currentFootprintSize,pivot,footprintOpacity,
                footprintEdgeFade,footprintVisibleTime,footprintFadeTime);
        }
        else
        {
            // 没有设置对象池时，保留一个 fallback，方便调试
            ScreenSpaceDecalProjector decal = Instantiate(footprintPrefab, spawnPosition, spawnRotation);

            decal.decalTexture = footTexture;
            decal.size = currentFootprintSize;
            decal.pivot = pivot;
            decal.opacity = footprintOpacity;
            decal.edgeFade = footprintEdgeFade;
            decal.decalNormalTexture = footNormalTexture;
            decal.normalStrength = footprintNormalStrength;

            //协程管理，脚印随时间淡出  
            StartCoroutine(FadeAndDestroyFootprint(decal,footprintOpacity));
        }
        
        
        // 调试用：脚印生成后，在这一帧结束时自动暂停 Unity
        StartCoroutine(PauseAtEndOfFrameForScreenshot());
    }

    
    /// <summary>
    /// 脚印生成后自动暂停 Unity。
    /// 
    /// 用于截图调试：
    /// 当脚印生成完成后，等待当前帧渲染结束，
    /// 然后自动暂停编辑器，这样可以精准截图脚和脚印的位置。
    /// </summary>
    private IEnumerator PauseAtEndOfFrameForScreenshot()
    {
        #if UNITY_EDITOR
            if (!pauseOnFootprintSpawn)
                yield break;

            if (pauseOnlyOnce && _hasPausedForScreenshot)
                yield break;

            _hasPausedForScreenshot = true;

            // 等当前帧渲染结束，确保脚印已经显示出来
            yield return new WaitForEndOfFrame();

            // 自动暂停 Unity Editor
            Debug.Break();
        #else
            yield break;
        #endif
    }
    
    /// <summary>
    /// 脚印显示一段时间后逐渐淡出，并最终销毁。
    /// </summary>
    private IEnumerator FadeAndDestroyFootprint(ScreenSpaceDecalProjector decal, float opacity)
    {
        if (decal == null)
            yield break;

        decal.opacity = opacity;

        yield return new WaitForSeconds(footprintVisibleTime);

        float startOpacity = decal.opacity;
        float timer = 0f;

        while (timer < footprintFadeTime)
        {
            if (decal == null)
                yield break;

            timer += Time.deltaTime;

            float t = timer / footprintFadeTime;
            decal.opacity = Mathf.Lerp(startOpacity, 0f, t);

            yield return null;
        }

        if (decal != null)
        {
            decal.opacity = 0f;
            Destroy(decal.gameObject);
        }
    }
    
    /// <summary>
    /// 缓存脚印生成调试数据。
    /// 
    /// SpawnFootprint 每次执行时会调用这个方法，
    /// OnDrawGizmos 会根据这里缓存的数据在 Scene View 中绘制调试信息。
    /// </summary>
    private void CacheFootprintDebugData(
        DebugFootSide footSide,
        Vector3 footBonePosition,
        Vector3 toeBonePosition,
        bool hasToe,
        Vector3 footCenterPosition,
        Vector3 rayOrigin,
        Vector3 rayEnd,
        bool rayHit,
        Vector3 hitPoint,
        Vector3 groundNormal,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 forwardOnSurface,
        Vector3 decalSize,
        Vector3 decalPivot)
    {
        FootprintDebugData data = new FootprintDebugData
        {
            valid = true,
            footSide = footSide,

            footBonePosition = footBonePosition,
            toeBonePosition = toeBonePosition,
            hasToe = hasToe,

            footCenterPosition = footCenterPosition,

            rayOrigin = rayOrigin,
            rayEnd = rayEnd,

            rayHit = rayHit,
            hitPoint = hitPoint,
            groundNormal = groundNormal,

            spawnPosition = spawnPosition,
            spawnRotation = spawnRotation,

            forwardOnSurface = forwardOnSurface,

            decalSize = decalSize,
            decalPivot = decalPivot,

            time = Time.time
        };

        if (footSide == DebugFootSide.Left)
        {
            _lastLeftDebugData = data;
        }
        else
        {
            _lastRightDebugData = data;
        }
    }

    /// <summary>
    /// Scene View 调试绘制。
    /// 
    /// 用于观察：
    /// 1. 脚骨骼点
    /// 2. Toes 点
    /// 3. 脚掌中心
    /// 4. Raycast
    /// 5. 地面命中点
    /// 6. 法线
    /// 7. 脚印生成位置
    /// 8. Decal 投射盒
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showFootprintDebugGizmos)
            return;

        if (showLeftFootDebug)
        {
            DrawFootprintDebugData(_lastLeftDebugData);
        }

        if (showRightFootDebug)
        {
            DrawFootprintDebugData(_lastRightDebugData);
        }
    }

    /// <summary>
    /// 绘制单只脚的调试数据。
    /// </summary>
    private void DrawFootprintDebugData(FootprintDebugData data)
    {
        if (!data.valid)
            return;

        Color footColor = data.footSide == DebugFootSide.Left
            ? new Color(0.2f, 0.7f, 1f, 1f)
            : new Color(1f, 0.45f, 0.2f, 1f);

        string footName = data.footSide == DebugFootSide.Left ? "Left" : "Right";

        // Foot 骨骼点
        if (showFootBones)
        {
            Gizmos.color = footColor;
            Gizmos.DrawSphere(data.footBonePosition, debugPointSize);

            DrawDebugLabel(
                data.footBonePosition,
                $"{footName} Foot"
            );

            if (data.hasToe)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(data.toeBonePosition, debugPointSize);

                Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
                Gizmos.DrawLine(data.footBonePosition, data.toeBonePosition);

                DrawDebugLabel(
                    data.toeBonePosition,$"{footName} Toes"
                    
                );
            }
        }

        // 脚掌中心
        if (showFootCenter)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(data.footCenterPosition, debugPointSize * 1.2f);

            DrawDebugLabel(
                data.footCenterPosition,
                $"{footName} Foot Center"
            );
        }

        // Raycast
        if (showRaycast)
        {
            Gizmos.color = data.rayHit ? Color.green : Color.red;
            Gizmos.DrawLine(data.rayOrigin, data.rayEnd);

            Gizmos.DrawWireSphere(data.rayOrigin, debugPointSize * 0.8f);

            DrawDebugLabel(
                data.rayOrigin,
                $"{footName} Ray Origin"
            );
        }

        if (!data.rayHit)
            return;

        // 命中点
        if (showHitPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(data.hitPoint, debugPointSize);

            DrawDebugLabel(
                data.hitPoint,
                $"{footName} Hit"
            );
        }

        // 地面法线
        if (showGroundNormal)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                data.hitPoint,
                data.hitPoint + data.groundNormal.normalized * debugNormalLength
            );

            DrawDebugLabel(
                data.hitPoint + data.groundNormal.normalized * debugNormalLength,
                "Ground Normal"
            );
        }

        // 脚印最终生成点
        if (showSpawnPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(data.spawnPosition, debugPointSize * 1.3f);

            DrawDebugLabel(
                data.spawnPosition,
                $"{footName} Spawn Point"
            );
        }

        // 脚印朝向
        if (showFootForward)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(
                data.spawnPosition,
                data.spawnPosition + data.forwardOnSurface.normalized * debugForwardLength
            );

            DrawDebugLabel(
                data.spawnPosition + data.forwardOnSurface.normalized * debugForwardLength,
                "Foot Forward"
            );
        }

        // Decal 投射盒
        if (showDecalBox)
        {
            DrawDecalBoxGizmo(data);
        }
    }

    /// <summary>
    /// 绘制 Decal 投射盒。
    /// 
    /// 这里模拟 ScreenSpaceDecalProjector 的盒子：
    /// local XY 是贴图平面，local Z 是投射深度。
    /// </summary>
    private void DrawDecalBoxGizmo(FootprintDebugData data)
    {
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Gizmos.color = new Color(0f, 1f, 1f, 0.8f);

        Matrix4x4 matrix =
            Matrix4x4.TRS(data.spawnPosition, data.spawnRotation, Vector3.one)
            * Matrix4x4.Translate(data.decalPivot)
            * Matrix4x4.Scale(data.decalSize);

        Gizmos.matrix = matrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    /// <summary>
    /// 绘制 Scene View 文字标签。
    /// 
    /// 只在 Unity Editor 中生效，打包后不会编译。
    /// </summary>
    private void DrawDebugLabel(Vector3 position, string text)
    {
    #if UNITY_EDITOR
            if (!showDebugLabels)
                return;

            Handles.Label(position + Vector3.up * debugLabelHeight, text);
    #endif
    }
}