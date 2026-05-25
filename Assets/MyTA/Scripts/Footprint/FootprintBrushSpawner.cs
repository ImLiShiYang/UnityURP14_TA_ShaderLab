using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 脚印 Brush 生成器。
///
/// 这个脚本一般挂在 Player 上。
///
/// 它的职责不是直接修改地面，也不是直接写 RT，
/// 而是：
///
/// 1. 根据玩家移动距离判断是否需要生成新脚印。
/// 2. 在玩家左右脚位置生成一个临时 brushPrefab。
/// 3. 把 brushPrefab 旋转成平躺状态，使它能被 FootstepCamera 从上方拍到。
/// 4. 通知 FootprintRTManager：有一个新的脚印 stamp 需要累积。
/// 5. 在短时间后销毁 brushPrefab，避免同一个脚印被重复画太多帧。
///
/// 整体流程：
///
/// Player 移动超过 stepDistance
///     ↓
/// SpawnFootprint()
///     ↓
/// 实例化 brushPrefab
///     ↓
/// FootstepCamera + FootprintRenderFeature 把 brush 画进 CurrentBrushRT
///     ↓
/// FootprintRTManager.NotifyBrushSpawned()
///     ↓
/// RenderFeature 执行一次 CurrentBrushRT -> AccumA 的累积
/// </summary>
public class FootprintBrushSpawner : MonoBehaviour
{

    /// <summary>
    /// 脚印 Brush 预制体。
    ///
    /// 通常是一个 Quad。
    /// 材质应该使用：
    /// Footprints/URP_FootprintBrush_NormalHeightSeparate
    ///
    /// 它应该在 FootprintBrush Layer 上，
    /// 这样 FootstepCamera / RenderFeature 才能只绘制它。
    /// </summary>
    [Header("References")]
    public GameObject brushPrefab;

    /// <summary>
    /// 玩家 Transform。
    ///
    /// 用于：
    /// 1. 获取当前位置。
    /// 2. 获取 forward/right，用来决定脚印朝向和左右脚偏移。
    ///
    /// 如果这个脚本就挂在 Player 上，也可以在 Start 里自动赋值：
    /// player = transform;
    /// </summary>
    public Transform player;


    /// <summary>
    /// 每隔多远生成一个脚印。
    ///
    /// 玩家当前位置和上一次生成脚印的位置距离超过这个值时，
    /// 才会生成新的脚印。
    ///
    /// 值越小，脚印越密。
    /// 值越大，脚印越稀。
    /// </summary>
    [Header("Step")]
    public float stepDistance = 0.7f;

    /// <summary>
    /// 左右脚相对玩家中心的横向偏移。
    ///
    /// leftFoot 为 true 时，向左偏移。
    /// leftFoot 为 false 时，向右偏移。
    ///
    /// 这样生成出来的脚印会左右交替，而不是都在角色中心线上。
    /// </summary>
    public float footSideOffset = 0.18f;

    /// <summary>
    /// Brush 生成时离地面的高度。
    ///
    /// 稍微高于地面，可以避免和地面 z-fighting。
    /// 但这个值不能太大，否则可能超出 FootstepCamera 的有效拍摄范围，
    /// 或者和其他剔除/深度逻辑产生问题。
    /// </summary>
    public float groundOffset = 0.05f;

    /// <summary>
    /// Brush 在场景中存活的时间。
    ///
    /// 注意：
    /// CurrentBrushRT 每帧都会拍摄还活着的 brush。
    ///
    /// 如果 brushLife 太长，同一个脚印会被连续多帧拍到，
    /// 然后可能被重复累积进 AccumA。
    ///
    /// 所以这个值一般要比较短，比如 0.05 ~ 0.12。
    /// 如果配合 NotifyBrushSpawned / ShouldAccumulateThisFrame，
    /// 可以避免站着不动时重复累积。
    /// </summary>
    public float brushLife = 0.08f;

    /// <summary>
    /// 上一次生成脚印时的玩家位置。
    ///
    /// 用它和当前玩家位置比较距离，
    /// 判断是否超过 stepDistance。
    /// </summary>
    private Vector3 lastStepPos;

    /// <summary>
    /// 当前该生成左脚还是右脚。
    ///
    /// false / true 每次生成后切换，
    /// 用来实现左右脚交替。
    /// </summary>
    private bool leftFoot;

    private void Start()
    {
        // 初始化上一次脚印位置。
        // 这样玩家刚开始时不会立刻生成脚印，
        // 而是要移动超过 stepDistance 后才生成第一个脚印。
        if (player != null)
        {
            lastStepPos = player.position;
        }
    }

    private void Update()
    {
        // 必要引用没有绑定时直接退出，避免空引用报错。
        if (player == null || brushPrefab == null)
            return;

        // 只计算 XZ 平面上的移动距离。
        //
        // 这样玩家上下跳动、地形高度变化等 Y 方向变化，
        // 不会触发额外脚印。
        Vector3 flatNow = new Vector3(player.position.x, 0, player.position.z);
        Vector3 flatLast = new Vector3(lastStepPos.x, 0, lastStepPos.z);

        // 如果玩家移动距离还不够 stepDistance，
        // 就不生成新脚印。
        if (Vector3.Distance(flatNow, flatLast) < stepDistance)
            return;

        // 移动距离足够，生成一个新的脚印 brush。
        SpawnFootprint();

        // 记录这次生成脚印的位置，
        // 下次继续用它判断移动距离。
        lastStepPos = player.position;

        // 左右脚交替。
        leftFoot = !leftFoot;
    }

    /// <summary>
    /// 生成一个脚印 Brush。
    ///
    /// 这个 Brush 是临时物体：
    /// - 它会被 FootstepCamera 拍进 CurrentBrushRT。
    /// - 然后在 brushLife 秒后销毁。
    ///
    /// 真正的历史脚印不是靠这个物体一直存在，
    /// 而是靠 AccumA RT 保存。
    /// </summary>
    private void SpawnFootprint()
    {
        // 获取玩家前方向，并忽略 Y 分量。
        //
        // 脚印只需要在地面 XZ 平面上朝向玩家移动方向，
        // 不应该受玩家模型上下倾斜影响。
        Vector3 forward = player.forward;
        forward.y = 0;
        forward.Normalize();

        // 获取玩家右方向，并忽略 Y 分量。
        //
        // 用它来计算左右脚偏移。
        Vector3 right = player.right;
        right.y = 0;
        right.Normalize();

        // 根据当前是左脚还是右脚，决定横向偏移方向。
        float side = leftFoot ? -footSideOffset : footSideOffset;

        // 计算脚印生成位置。
        //
        // player.position：
        //     玩家当前位置。
        //
        // right * side：
        //     左右脚偏移。
        //
        // Vector3.up * groundOffset：
        //     让 brush 稍微高于地面，避免和地面重叠。
        Vector3 pos = player.position + right * side + Vector3.up * groundOffset;

        // 创建脚印 brush 实例。
        //
        // 注意：
        // 这里先用 Quaternion.identity 创建，
        // 后面马上会设置正确的旋转。
        GameObject brush = Instantiate(brushPrefab, pos, Quaternion.identity);

        // 通知 FootprintRTManager：
        // 有新的脚印 brush 生成了。
        //
        // 这个通知不会直接画脚印，
        // 它只是让 RTManager 记录一个 stampVersion。
        //
        // 后面 FootprintRenderFeature 会通过：
        // manager.ShouldAccumulateThisFrame()
        //
        // 判断这一帧是否需要把 CurrentBrushRT 累积进 AccumA。
        //
        // 这样可以避免人物站着不动时，同一个脚印被每帧重复累积。
        if (FootprintRTManager.Active != null)
        {
            FootprintRTManager.Active.NotifyBrushSpawned();
        }

        // Quad 默认一般在 XY 平面。
        //
        // 我们希望它平躺在地面 XZ 平面上，
        // 并且贴图朝向玩家 forward 方向。
        //
        // Quaternion.LookRotation(Vector3.down, forward) 的含义：
        // - 让 Quad 的 forward 指向下方，使它能被上方相机看到。
        // - 用玩家 forward 作为 up 参考，控制脚印贴图朝向。
        brush.transform.rotation = Quaternion.LookRotation(Vector3.down, forward);

        // 关闭 brush 的阴影。
        //
        // 这个 brush 只是用来写入脚印 RT 的数据物体，
        // 不应该在主画面里投影或接收阴影。
        foreach (Renderer r in brush.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // 让 brush 在短时间后销毁。
        //
        // 历史脚印由 AccumA 保存，
        // brush 本身不需要一直留在场景里。
        //
        // 如果 brushLife 太长，
        // 它会连续多帧出现在 CurrentBrushRT 中，
        // 导致同一个脚印被重复绘制或重复累积。
        Destroy(brush, brushLife);
    }
}