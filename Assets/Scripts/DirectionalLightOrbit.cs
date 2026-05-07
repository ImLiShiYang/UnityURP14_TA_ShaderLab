using UnityEngine;

/// <summary>
/// 让 Directional Light 围绕一个中心点旋转。
///
/// 注意：
/// 对 Directional Light 来说，真正影响光照方向的是 rotation，
/// position 本身并不重要。
///
/// 但为了调试直观，可以让 Light 物体真的围绕中心点转。
/// </summary>
[ExecuteAlways]
public class DirectionalLightOrbit : MonoBehaviour
{
    /// <summary>
    /// 旋转中心。
    /// 你的需求里就是世界原点 (0,0,0)。
    /// </summary>
    public Vector3 orbitCenter = Vector3.zero;

    /// <summary>
    /// 如果指定了 Transform，就优先使用它的位置作为旋转中心。
    /// </summary>
    public Transform orbitCenterTransform;

    /// <summary>
    /// 绕哪个世界轴旋转。
    /// Vector3.up 表示绕世界 Y 轴转。
    /// </summary>
    public Vector3 orbitAxisWS = Vector3.up;

    /// <summary>
    /// 旋转速度。
    /// 单位：度 / 秒。
    /// </summary>
    public float orbitSpeed = 20f;

    /// <summary>
    /// 是否在编辑器非运行模式下也旋转。
    /// 一般建议关闭，避免编辑场景时物体一直动。
    /// </summary>
    public bool rotateInEditMode = false;

    private void LateUpdate()
    {
        if (!Application.isPlaying && !rotateInEditMode)
            return;

        Vector3 center = orbitCenterTransform != null? orbitCenterTransform.position: orbitCenter;

        Vector3 axis = orbitAxisWS;

        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.up;

        axis.Normalize();

        float angle = orbitSpeed * Time.deltaTime;

        // 让 Light 真的围绕中心点转。
        // RotateAround 会同时改变 position 和 rotation。
        transform.RotateAround(center, axis, angle);
    }
}