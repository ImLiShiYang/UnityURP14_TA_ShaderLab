
using System.Collections;
using UnityEngine;

/// <summary>
/// 运行时脚印贴花生成器。
///
/// 根据角色移动距离自动生成脚印 decal。
/// 每次生成时，会向地面做 Raycast，获取地面位置和法线，
/// 然后创建一个新的 ScreenSpaceDecalProjector，
/// 并让它的 local +Z 指向地面内部，local Y 对齐角色前进方向。
/// </summary>
public class FootprintDecalSpawner : MonoBehaviour
{
    /// <summary>
    /// 角色根节点。
    ///
    /// 用它的位置判断角色移动距离，
    /// 用它的 forward 判断脚印朝向。
    /// </summary>
    [Header("References")]
    public Transform characterRoot;

    /// <summary>
    /// 脚印 decal prefab。
    ///
    /// 这个 prefab 上需要挂 ScreenSpaceDecalProjector。
    /// 运行时会 Instantiate 它来生成脚印。
    /// </summary>
    public ScreenSpaceDecalProjector footprintPrefab;
    
    [Header("Foot Textures")]
    public Texture2D leftFootTexture;
    public Texture2D rightFootTexture;

    /// <summary>
    /// 地面检测使用的 LayerMask。
    ///
    /// 只有在这个 LayerMask 中的物体才会被 Raycast 检测到。
    /// 默认 ~0 表示检测所有 Layer。
    /// </summary>
    [Header("Raycast")]
    public LayerMask groundMask = ~0;

    /// <summary>
    /// Raycast 起点相对角色位置向上的高度。
    ///
    /// 射线会从角色上方一点开始往下打，
    /// 避免起点刚好在地面内部导致检测不稳定。
    /// </summary>
    public float rayStartHeight = 0.5f;

    /// <summary>
    /// 向下检测地面的最大距离。
    ///
    /// 如果角色离地面太远，超过这个距离就不会生成脚印。
    /// </summary>
    public float rayDistance = 2f;

    /// <summary>
    /// 每移动多远生成一个脚印。
    ///
    /// 值越小，脚印越密。
    /// 值越大，脚印越稀。
    /// </summary>
    [Header("Footprint Placement")]
    public float stepDistance = 0.6f;

    /// <summary>
    /// 左右脚相对角色中心线的横向偏移。
    ///
    /// 生成脚印时会左右交替：
    /// 左脚向左偏移，右脚向右偏移。
    /// </summary>
    public float footSideOffset = 0.12f;

    /// <summary>
    /// 脚印沿地面法线方向抬起一点的距离。
    ///
    /// 这样可以避免 decal projector 的位置刚好贴在表面上，
    /// 导致深度或投射判断不稳定。
    /// </summary>
    public float surfaceOffset = 0.02f;

    /// <summary>
    /// 脚印 decal 投射盒尺寸。
    ///
    /// x = 脚印宽度。
    /// y = 脚印长度。
    /// z = 投射深度。
    ///
    /// 因为 shader 使用 local XY 作为贴图平面，
    /// 所以脚印图片的宽高分别对应 size.x 和 size.y。
    /// </summary>
    [Header("Footprint Size")]
    public Vector3 footprintSize = new Vector3(0.25f, 0.45f, 0.12f);

    // 上一次生成脚印时角色的位置。
    // 用它和当前角色位置计算移动距离。
    private Vector3 _lastSpawnPosition;

    // 是否已经生成过第一个脚印。
    // 第一次运行时没有历史位置，所以要特殊处理。
    private bool _hasSpawned = false;

    // 当前是否生成左脚。
    // true 表示下一次生成左脚，false 表示下一次生成右脚。
    private bool _leftFoot = true;
    
    
    [Header("Lifetime")]
    public float footprintVisibleTime = 3f;
    public float footprintFadeTime = 2f;

    /// <summary>
    /// Reset 是 Unity 编辑器回调。
    ///
    /// 当脚本第一次挂到 GameObject 上，或者在 Inspector 右键 Reset 时调用。
    /// 这里默认把 characterRoot 设置成当前物体的 transform，方便快速使用。
    /// </summary>
    private void Reset()
    {
        characterRoot = transform;
    }

    /// <summary>
    /// Update 是 Unity 每帧调用的函数。
    ///
    /// 这里每帧检查角色是否移动了足够距离。
    /// 如果移动距离超过 stepDistance，就尝试生成一个新脚印。
    /// </summary>
    private void Update()
    {
        // 必要引用没设置时，不执行生成逻辑。
        if (characterRoot == null || footprintPrefab == null)
            return;

        // 第一次运行时，先生成一个脚印，并记录当前位置。
        if (!_hasSpawned)
        {
            TrySpawnFootprint();
            _lastSpawnPosition = characterRoot.position;
            _hasSpawned = true;
            return;
        }

        // 计算角色当前位置和上次生成脚印位置之间的距离。
        float movedDistance = Vector3.Distance(characterRoot.position, _lastSpawnPosition);

        // 移动距离达到步距后，生成下一个脚印。
        if (movedDistance >= stepDistance)
        {
            TrySpawnFootprint();
            _lastSpawnPosition = characterRoot.position;
        }
    }

    /// <summary>
    /// 尝试在角色脚下生成一个脚印 decal。
    ///
    /// 主要流程：
    /// 1. 从角色上方向下 Raycast 找地面。
    /// 2. 获取地面命中点和地面法线。
    /// 3. 计算角色前进方向在地面上的投影。
    /// 4. 根据左右脚偏移生成脚印位置。
    /// 5. 创建脚印 projector，并对齐到地面方向。
    /// </summary>
    private void TrySpawnFootprint()
    {
        // 从角色位置上方一点开始向下检测地面。
        Vector3 rayOrigin = characterRoot.position + Vector3.up * rayStartHeight;

        // Physics.Raycast 是 Unity 物理射线检测函数。
        //
        // 参数含义：
        // rayOrigin：射线起点。
        // Vector3.down：射线方向，世界向下。
        // out RaycastHit hit：输出命中信息，例如命中点、法线、碰撞体。
        // rayDistance：最大检测距离。
        // groundMask：只检测指定 Layer。
        if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance, groundMask))
            return;

        // hit.normal 是被射线打中的表面的法线。
        //
        // 平地上通常是 (0, 1, 0)。
        // 斜坡上会是斜坡表面的垂直方向。
        Vector3 normal = hit.normal;

        // 把角色前进方向投影到当前地面平面上。
        //
        // 不能直接用 characterRoot.forward，
        // 因为角色 forward 可能带有向上或向下分量。
        //
        // Vector3.ProjectOnPlane(vector, planeNormal) 的作用是：
        // 去掉 vector 在 planeNormal 方向上的分量，
        // 得到一个贴着该平面的方向。
        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(characterRoot.forward, normal);

        // 如果投影后的方向太小，说明角色 forward 几乎和地面 normal 平行。
        // 这种情况下用角色 right 方向作为备用方向。
        if (forwardOnSurface.sqrMagnitude < 0.0001f)
            forwardOnSurface = Vector3.ProjectOnPlane(characterRoot.right, normal);

        // Normalize 会把向量长度变为 1。
        // 后面只需要方向，不需要长度。
        forwardOnSurface.Normalize();

        // 计算地面上的右方向。
        //
        // normal 是地面法线。
        // forwardOnSurface 是脚尖方向。
        // 两者叉乘得到沿着地面的左右方向。
        Vector3 rightOnSurface = Vector3.Cross(normal, forwardOnSurface).normalized;

        // 左右脚交替。
        //
        // 左脚使用 -1，右脚使用 +1。
        float sideSign = _leftFoot ? -1f : 1f;

        // 根据左右脚方向计算横向偏移。
        Vector3 sideOffset = rightOnSurface * footSideOffset * sideSign;

        // 最终生成位置。
        //
        // hit.point 是地面命中点。
        // sideOffset 用来区分左脚和右脚。
        // normal * surfaceOffset 让脚印略微离开表面一点，避免数值问题。
        Vector3 spawnPosition = hit.point + sideOffset + normal * surfaceOffset;

        // 关键旋转：
        //
        // Quaternion.LookRotation(forward, upwards) 会创建一个旋转：
        // 让物体的 local +Z 指向 forward，
        // 让物体的 local +Y 尽量对齐 upwards。
        //
        // 我们的 decal 约定是：
        // local +Z = 投射方向。
        // local XY = 贴图平面。
        //
        // 所以：
        // -normal 让 projector 的 local +Z 指向地面内部。
        // forwardOnSurface 让 projector 的 local +Y 对齐角色前进方向，也就是脚尖方向。
        Quaternion spawnRotation = Quaternion.LookRotation(-normal, forwardOnSurface);

        // 实例化脚印 decal prefab。
        //
        // Instantiate 会复制 prefab，并放到指定位置和旋转。
        ScreenSpaceDecalProjector decal = Instantiate(footprintPrefab, spawnPosition, spawnRotation);
        
        decal.decalTexture = _leftFoot ? leftFootTexture : rightFootTexture;

        // 设置脚印投射盒尺寸。
        decal.size = footprintSize;

        // 设置 pivot，让投射盒主要沿 local +Z 方向延伸。
        //
        // 因为 local +Z 是投射方向，
        // pivot.z = footprintSize.z * 0.5f 可以让盒子从表面附近往地面内部覆盖。
        decal.pivot = new Vector3(0f, 0f, footprintSize.z * 0.5f);
        
        // Destroy(decal.gameObject, footprintLifeTime);
        StartCoroutine(FadeAndDestroyFootprint(decal));
        
        // 下一次切换另一只脚。
        _leftFoot = !_leftFoot;
    }
    
    private IEnumerator FadeAndDestroyFootprint(ScreenSpaceDecalProjector decal)
    {
        // 如果传进来的 decal 已经不存在，直接结束协程
        if (decal == null)
            yield break;

        // 生成时先让脚印完全显示
        decal.opacity = 1f;

        // 先等待一段完整显示时间
        // 在这段时间内，脚印不会变淡
        yield return new WaitForSeconds(footprintVisibleTime);

        // 记录开始淡出时的透明度
        // 通常是 1，但这里用 decal.opacity 更安全
        float startOpacity = decal.opacity;

        // 记录淡出已经经过的时间
        float timer = 0f;

        // 在 footprintFadeTime 时间内逐渐淡出
        while (timer < footprintFadeTime)
        {
            // 如果脚印在淡出过程中被其他逻辑销毁了，直接结束协程
            if (decal == null)
                yield break;

            // 累加当前帧经过的时间
            timer += Time.deltaTime;

            // 计算淡出进度
            // t = 0 表示刚开始淡出
            // t = 1 表示淡出结束
            float t = timer / footprintFadeTime;

            // 根据淡出进度，把透明度从 startOpacity 慢慢变成 0
            decal.opacity = Mathf.Lerp(startOpacity, 0f, t);

            // 等待下一帧继续执行
            yield return null;
        }

        // 淡出结束后，再次确认 decal 还存在
        if (decal != null)
        {
            // 确保最终完全透明
            decal.opacity = 0f;

            // 销毁脚印 GameObject
            Destroy(decal.gameObject);
        }
    }
}