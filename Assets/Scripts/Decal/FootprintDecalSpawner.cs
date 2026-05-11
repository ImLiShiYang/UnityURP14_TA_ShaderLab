using System.Collections;
using UnityEngine;

/// <summary>
/// 基于角色真实脚骨骼生成脚印贴花。
/// 
/// 这个脚本不再根据角色中心移动距离生成脚印，
/// 而是通过左脚 / 右脚骨骼的位置向下 Raycast，
/// 在脚真正落地的位置生成脚印。
/// </summary>
public class FootprintDecalSpawner : MonoBehaviour
{
    [Header("Screenshot Debug")]
    public bool pauseOnFootprintSpawn = false;
    public bool pauseOnlyOnce = true;
    
    private bool _hasPausedForScreenshot;
    
    [Header("Pooling")]
    public bool usePooling = true;
    public FootprintDecalPool footprintPool;
    
    [Header("References")]
    // 角色根节点，一般是父物体 player
    public Transform characterRoot;

    // 角色 Animator，一般在子物体 X Bot 上
    public Animator animator;

    // 左脚骨骼 Transform
    public Transform leftFoot;

    // 右脚骨骼 Transform
    public Transform rightFoot;

    // 脚印 Decal prefab
    public ScreenSpaceDecalProjector footprintPrefab;

   

    [Header("Foot Textures")]
    public Texture2D leftFootTexture;
    public Texture2D rightFootTexture;

    [Header("Raycast")]
    // 建议只检测 Ground 层，不要用 Everything
    public LayerMask groundMask = ~0;

    // 从脚上方多高开始射线检测
    public float rayStartHeight = 0.25f;

    // 从脚附近向下检测多远
    public float rayDistance = 0.8f;

    // 让脚印稍微离开地面一点点，避免深度闪烁
    public float surfaceOffset = 0.02f;

    // [Header("Footprint Placement")]
    // // 脚骨骼通常在脚踝位置，所以脚印可以稍微往脚尖方向偏移一点
    // public float footForwardOffset = 0.08f;
    [Header("Footprint Placement")]
    public float walkFootForwardOffset = 0.14f;
    public float runFootForwardOffset = 0.06f;

    // 如果脚印图片方向不对，可以调这个角度
    public float footprintYawOffset = 0f;

    [Header("Footprint Size")]
    // public Vector3 footprintSize = new Vector3(0.25f, 0.45f, 0.12f);
    public Vector3 walkFootprintSize = new Vector3(0.25f, 0.45f, 0.12f);
    public Vector3 runFootprintSize = new Vector3(0.25f, 0.45f, 0.12f);

    [Header("Lifetime")]
    public float footprintVisibleTime = 3f;
    public float footprintFadeTime = 2f;

    [Header("Anti Duplicate")]
    // 防止同一只脚短时间内重复生成多个脚印
    public float minTimeBetweenSameFoot = 0.15f;

    private float _lastLeftFootTime = -999f;
    private float _lastRightFootTime = -999f;
    
    [Header("Spawn Conditions")]
    public ThirdPersonPlayerController playerController;

    public float minAnimatorMoveSpeed = 0.05f;

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
        SpawnFootprint(leftFoot, leftFootTexture);
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

        SpawnFootprint(rightFoot, rightFootTexture);
    }

    /// <summary>
    /// 根据指定脚骨骼的位置生成一个脚印。
    /// 
    /// 这个方法会从脚的位置向下 Raycast，
    /// 找到地面后，把脚印贴花放到真实落地点。
    /// </summary>
    private void SpawnFootprint(Transform footTransform, Texture2D footTexture)
    {
        if (footTransform == null || footprintPrefab == null)
            return;

        // 从脚骨骼上方开始向下检测地面
        Vector3 rayOrigin = footTransform.position + Vector3.up * rayStartHeight;
        float totalRayDistance = rayStartHeight + rayDistance;

        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, totalRayDistance, groundMask, QueryTriggerInteraction.Ignore))
            return;

        Vector3 normal = hit.normal;

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
        
        if (usePooling && footprintPool != null)
        {
            footprintPool.SpawnFootprint(spawnPosition,spawnRotation,footTexture,currentFootprintSize,pivot,footprintVisibleTime,footprintFadeTime);
        }
        else
        {
            // 没有设置对象池时，保留一个 fallback，方便调试
            ScreenSpaceDecalProjector decal = Instantiate(footprintPrefab, spawnPosition, spawnRotation);

            decal.decalTexture = footTexture;
            decal.size = currentFootprintSize;
            decal.pivot = pivot;

            //协程管理，脚印随时间淡出  
            StartCoroutine(FadeAndDestroyFootprint(decal));
        }
        
        // StartCoroutine(FadeAndDestroyFootprint(decal));
        
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
    private IEnumerator FadeAndDestroyFootprint(ScreenSpaceDecalProjector decal)
    {
        if (decal == null)
            yield break;

        decal.opacity = 1f;

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
}