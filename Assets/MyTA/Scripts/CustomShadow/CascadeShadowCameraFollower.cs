using UnityEngine;

/// <summary>
/// 单个 Cascade LightCamera 的跟随脚本。
///
/// 这个脚本挂在 LightCamera_Cascade0 / LightCamera_Cascade1 上。
///
/// 它不管理其他相机，只管理自己。
///
/// 核心逻辑：
/// 1. Light 提供方向
/// 2. 当前相机有自己的 targetCenter
/// 3. 当前相机围绕自己的 targetCenter 旋转
///
/// 公式：
/// camera.rotation = light.rotation
/// camera.position = targetCenter - light.forward * distanceFromCenter
///
/// 这样：
/// Cascade0 可以围绕近处小球中心旋转；
/// Cascade1 可以围绕整个场景中心旋转。
/// </summary>
[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class CascadeShadowCameraFollower : MonoBehaviour
{
    /// <summary>
    /// 提供方向的 Directional Light。
    /// 把场景里的 Directional Light 拖进来。
    /// </summary>
    [Header("Light Source")]
    public Transform directionalLight;

    /// <summary>
    /// 当前 Cascade 相机围绕哪个点旋转。
    ///
    /// 推荐做法：
    /// 在场景里创建空物体：
    /// Cascade0_Center
    /// Cascade1_Center
    ///
    /// 然后分别拖进来。
    /// </summary>
    [Header("Cascade Center")]
    public Transform targetCenterTransform;

    /// <summary>
    /// 如果没有指定 targetCenterTransform，
    /// 就使用这个世界坐标作为中心点。
    /// </summary>
    public Vector3 targetCenterWS = Vector3.zero;

    /// <summary>
    /// 相机距离中心点多远。
    ///
    /// 注意：
    /// 这个不是 shadow map 覆盖范围。
    /// 覆盖范围由 orthographicSize 决定。
    ///
    /// distanceFromCenter 主要决定相机站在中心点反方向多远。
    /// </summary>
    public float distanceFromCenter = 30f;

    /// <summary>
    /// 正交相机半高。
    ///
    /// Cascade0 通常小一些，例如 6。
    /// Cascade1 通常大一些，例如 18。
    /// </summary>
    [Header("Camera Projection")]
    public float orthographicSize = 6f;

    /// <summary>
    /// 光源相机近裁剪面。
    /// </summary>
    public float nearClipPlane = 0.3f;

    /// <summary>
    /// 光源相机远裁剪面。
    /// </summary>
    public float farClipPlane = 30f;

    /// <summary>
    /// 额外旋转偏移。
    ///
    /// 正常情况下保持 0。
    /// 如果你的阴影方向刚好差 90 度或 180 度，
    /// 可以用这个调试。
    /// </summary>
    [Header("Rotation Offset")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    /// <summary>
    /// 是否在编辑器非运行模式下也更新相机。
    /// 建议开启，方便你在 Scene 里实时看相机范围。
    /// </summary>
    [Header("Editor")]
    public bool updateInEditMode = true;

    private Camera _camera;

    private void OnEnable()
    {
        SetupCamera();
    }

    private void OnValidate()
    {
        ClampSettings();
        SetupCamera();
    }

    private void LateUpdate()
    {
        SetupCamera();
    }

    private void ClampSettings()
    {
        orthographicSize = Mathf.Max(0.0001f, orthographicSize);
        nearClipPlane = Mathf.Max(0.0001f, nearClipPlane);
        farClipPlane = Mathf.Max(nearClipPlane + 0.0001f, farClipPlane);
        distanceFromCenter = Mathf.Max(0.0001f, distanceFromCenter);
    }

    private Vector3 GetTargetCenter()
    {
        if (targetCenterTransform != null)
            return targetCenterTransform.position;

        return targetCenterWS;
    }

    private void SetupCamera()
    {
        if (!Application.isPlaying && !updateInEditMode)
            return;

        if (directionalLight == null)
            return;

        if (_camera == null)
            _camera = GetComponent<Camera>();

        Vector3 center = GetTargetCenter();

        // 设置为正交相机。
        _camera.orthographic = true;
        _camera.orthographicSize = orthographicSize;
        _camera.nearClipPlane = nearClipPlane;
        _camera.farClipPlane = farClipPlane;

        // Light 的旋转就是 Shadow Camera 的方向。
        Quaternion lightRotation =directionalLight.rotation * Quaternion.Euler(rotationOffsetEuler);
            

        Vector3 lightForward = lightRotation * Vector3.forward;

        // 核心：
        // 相机站在 center 的反光照方向上，
        // 并朝着 lightForward 方向看。
        //
        // 因为：
        // position = center - forward * distance
        //
        // 所以相机的 forward 正好指向 center。
        Vector3 cameraPosition =center - lightForward.normalized * distanceFromCenter;
            

        transform.SetPositionAndRotation(cameraPosition, lightRotation);
    }

    /// <summary>
    /// 根据当前相机和中心点的距离，自动填 distanceFromCenter。
    ///
    /// 用法：
    /// 1. 先手动摆好相机和 target center。
    /// 2. 右键组件菜单，点击这个选项。
    /// 3. 脚本会把当前距离记录下来。
    /// </summary>
    [ContextMenu("Capture Distance From Current Position")]
    private void CaptureDistanceFromCurrentPosition()
    {
        Vector3 center = GetTargetCenter();
        distanceFromCenter = Vector3.Distance(transform.position, center);
        SetupCamera();
    }
}