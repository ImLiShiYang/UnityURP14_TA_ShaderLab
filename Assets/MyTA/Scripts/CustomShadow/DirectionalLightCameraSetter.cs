using System;
using UnityEngine;

/// <summary>
/// 让一个 Directional Light 同时管理多个“光源相机”。
///
/// 这个脚本挂在 Directional Light 物体上。
///
/// 原来的版本：
/// 一个 DirectionalLightCameraSetter 只控制一个 LightCamera，
/// 所以你需要在 Light 上挂两个脚本，分别控制 Cascade0 / Cascade1。
///
/// 当前版本：
/// 一个 DirectionalLightCameraSetter 可以控制多个 LightCamera。
/// 每个 LightCamera 都有自己独立的：
/// 1. 正交尺寸 orthographicSize
/// 2. nearClipPlane
/// 3. farClipPlane
/// 4. 是否跟随光源位置
/// 5. 是否跟随光源旋转
///
/// 用途：
/// 1. 用多个 LightCamera 渲染多张自定义深度图 / Shadow Map
/// 2. 每个 LightCamera 对应一个 Cascade
/// 3. 让多个 LightCamera 统一跟随同一个 Directional Light 的方向
/// 4. 实现简单的级联阴影相机管理
/// </summary>
[RequireComponent(typeof(Light))]
[ExecuteAlways]
[DisallowMultipleComponent]
public class DirectionalLightCameraSetter : MonoBehaviour
{
    /// <summary>
    /// 单个 LightCamera 的设置。
    ///
    /// 每一个元素代表一个 Cascade 相机。
    ///
    /// 例如：
    /// Element 0 -> LightCamera_Cascade0
    /// Element 1 -> LightCamera_Cascade1
    ///
    /// 注意：
    /// 这里只负责设置 Camera 的 Transform 和投影参数。
    /// 真正把哪一张深度图写入 _MyCustomDepthTexture0 / _MyCustomDepthTexture1，
    /// 仍然由你的 CustomDepthTextureFeature 里的 cascadeIndex 决定。
    /// </summary>
    [Serializable]
    public class LightCameraSettings
    {
        /// <summary>
        /// 仅用于 Inspector 里区分每个相机。
        /// 不参与实际渲染逻辑。
        /// </summary>
        [Header("Name")]
        public string name = "Cascade Camera";

        /// <summary>
        /// 用来渲染自定义深度图的光源相机。
        ///
        /// 注意：
        /// 这里要拖入你专门创建的 LightCamera，
        /// 不要拖 Main Camera。
        ///
        /// 多级级联阴影时：
        /// Cascade 0 拖入近处阴影相机；
        /// Cascade 1 拖入远处阴影相机。
        /// </summary>
        [Header("Light Camera Settings")]
        public Camera lightCamera;

        /// <summary>
        /// 正交相机的半高。
        ///
        /// 这个值决定 shadow map 覆盖的世界范围。
        ///
        /// 值越大：
        /// - 覆盖范围越大
        /// - 单位世界空间分到的 shadow map 像素越少
        /// - 阴影越容易糊
        ///
        /// 值越小：
        /// - 阴影越清晰
        /// - 但覆盖范围变小，物体可能超出 shadow map
        ///
        /// 常见设置：
        /// Cascade 0 用较小值，例如 6
        /// Cascade 1 用较大值，例如 18
        /// </summary>
        public float orthographicSize = 6f;

        /// <summary>
        /// 光源相机近裁剪面。
        ///
        /// 太小会浪费深度精度；
        /// 太大可能裁掉靠近相机的投影物。
        /// </summary>
        public float nearClipPlane = 0.3f;

        /// <summary>
        /// 光源相机远裁剪面。
        ///
        /// 要保证 caster 和 receiver 都在 near/far 范围内。
        ///
        /// 但 far 太大也会降低线性深度精度。
        ///
        /// 常见设置：
        /// Cascade 0 可以用 30
        /// Cascade 1 可以用 60
        /// </summary>
        public float farClipPlane = 30f;

        /// <summary>
        /// 是否让 LightCamera 的位置跟随当前 Directional Light 的位置。
        ///
        /// 对 Directional Light 来说，真正影响阴影方向的是旋转。
        /// 位置本身不代表真实光源位置。
        ///
        /// 如果你的 LightCamera 已经手动摆好了位置，
        /// 通常可以关闭这个选项。
        ///
        /// 如果你想让 LightCamera 和 Light 物体一起移动，
        /// 再开启这个选项。
        /// </summary>
        [Header("Follow Light Transform")]
        public bool followLightPosition = false;

        /// <summary>
        /// 在跟随 Light 位置时使用的位置偏移。
        ///
        /// 最终位置计算方式：
        /// LightCamera.position = Light.position + Light.rotation * localPositionOffset
        ///
        /// 这样偏移会跟随光源旋转一起变化。
        /// </summary>
        public Vector3 localPositionOffset = Vector3.zero;

        /// <summary>
        /// 是否让 LightCamera 的旋转跟随当前 Directional Light 的旋转。
        ///
        /// 对 Directional Light 阴影来说，这个通常应该开启。
        ///
        /// 因为你的 Shadow Map 方向需要和光照方向一致。
        /// </summary>
        public bool followLightRotation = true;

        /// <summary>
        /// 在跟随 Light 旋转时额外叠加的欧拉角偏移。
        ///
        /// 最终旋转计算方式：
        /// LightCamera.rotation = Light.rotation * Quaternion.Euler(localRotationOffsetEuler)
        ///
        /// 一般保持 0 即可。
        /// 如果你发现阴影方向和预期差 90 度或 180 度，
        /// 可以临时用这个字段调试。
        /// </summary>
        public Vector3 localRotationOffsetEuler = Vector3.zero;

        /// <summary>
        /// 是否启用这个 LightCamera 设置。
        ///
        /// 关闭后，这个元素不会再修改对应 Camera。
        /// 适合临时禁用某一级 Cascade。
        /// </summary>
        [Header("Enable")]
        public bool enabled = true;
    }

    /// <summary>
    /// 多个 LightCamera 的设置列表。
    ///
    /// 一个元素对应一个 Cascade 相机。
    ///
    /// 推荐：
    /// Element 0 = Cascade 0，近处阴影，高精度，小范围
    /// Element 1 = Cascade 1，远处阴影，低精度，大范围
    ///
    /// 你的 Shader 目前主要使用：
    /// _MyCustomDepthTexture0
    /// _MyCustomDepthTexture1
    /// _WorldToLightUVMatrix0
    /// _WorldToLightUVMatrix1
    ///
    /// 所以这里先配置两个相机最合适。
    /// </summary>
    [Header("Light Cameras")]
    public LightCameraSettings[] lightCameras =
    {
        new LightCameraSettings
        {
            name = "Cascade 0",
            orthographicSize = 6f,
            nearClipPlane = 0.3f,
            farClipPlane = 30f,
            followLightPosition = false,
            followLightRotation = true
        },

        new LightCameraSettings
        {
            name = "Cascade 1",
            orthographicSize = 18f,
            nearClipPlane = 0.3f,
            farClipPlane = 60f,
            followLightPosition = false,
            followLightRotation = true
        }
    };

    /// <summary>
    /// 脚本启用时调用。
    ///
    /// ExecuteAlways 使它在编辑器模式下也会执行，
    /// 所以你拖动光源或修改参数时能实时更新所有 LightCamera。
    /// </summary>
    private void OnEnable()
    {
        SetupCameras();
    }

    /// <summary>
    /// Inspector 中修改参数时调用。
    ///
    /// 这样修改 orthographicSize、near/far、Camera 引用等参数后，
    /// 所有 LightCamera 会立刻同步。
    /// </summary>
    private void OnValidate()
    {
        ClampSettings();
        SetupCameras();
    }

    /// <summary>
    /// 每帧后期更新。
    ///
    /// 用 LateUpdate 是为了尽量保证：
    /// 如果其他脚本在 Update 中移动/旋转了 Directional Light，
    /// 这里可以拿到最终 Transform 状态。
    /// </summary>
    private void LateUpdate()
    {
        SetupCameras();
    }

    /// <summary>
    /// 限制 Inspector 中的参数，避免出现非法值。
    ///
    /// 例如：
    /// orthographicSize 不能小于等于 0
    /// nearClipPlane 不能小于等于 0
    /// farClipPlane 必须大于 nearClipPlane
    /// </summary>
    private void ClampSettings()
    {
        if (lightCameras == null)
            return;

        for (int i = 0; i < lightCameras.Length; i++)
        {
            LightCameraSettings setting = lightCameras[i];

            if (setting == null)
                continue;

            setting.orthographicSize = Mathf.Max(0.0001f, setting.orthographicSize);
            setting.nearClipPlane = Mathf.Max(0.0001f, setting.nearClipPlane);
            setting.farClipPlane = Mathf.Max(setting.nearClipPlane + 0.0001f, setting.farClipPlane);
        }
    }

    /// <summary>
    /// 设置所有 LightCamera。
    ///
    /// 这个函数会遍历 lightCameras 数组，
    /// 然后逐个调用 SetupCamera。
    /// </summary>
    private void SetupCameras()
    {
        if (lightCameras == null)
            return;

        for (int i = 0; i < lightCameras.Length; i++)
        {
            SetupCamera(lightCameras[i]);
        }
    }

    /// <summary>
    /// 设置单个 LightCamera 的核心函数。
    ///
    /// 主要做四件事：
    /// 1. 设置 LightCamera 为正交相机
    /// 2. 设置正交尺寸和裁剪面
    /// 3. 根据需要同步 LightCamera 的位置
    /// 4. 根据需要同步 LightCamera 的旋转
    /// </summary>
    private void SetupCamera(LightCameraSettings setting)
    {
        // 没有设置数据时直接返回。
        if (setting == null)
            return;

        // 这个 Cascade 被禁用时直接返回。
        if (!setting.enabled)
            return;

        // 没有指定 LightCamera 时直接返回。
        // 避免空引用报错。
        if (setting.lightCamera == null)
            return;

        Camera cam = setting.lightCamera;

        // ==========================================
        // 1. 设置光源相机的投影参数
        // ==========================================

        // Shadow map 通常使用正交投影。
        //
        // 对 Directional Light 来说，光线近似平行，
        // 所以用 orthographic camera 更符合方向光阴影。
        cam.orthographic = true;

        // 设置正交范围。
        cam.orthographicSize = setting.orthographicSize;

        // 设置 near/far。
        cam.nearClipPlane = setting.nearClipPlane;
        cam.farClipPlane = setting.farClipPlane;

        // ==========================================
        // 2. 同步 LightCamera 的位置
        // ==========================================

        if (setting.followLightPosition)
        {
            // 让光源相机的位置跟随当前 Light 物体的位置。
            //
            // localPositionOffset 会被 Light 的旋转影响。
            // 这样可以让偏移始终保持在光源局部空间下。
            cam.transform.position =
                transform.position + transform.rotation * setting.localPositionOffset;
        }

        // ==========================================
        // 3. 设置 LightCamera 的旋转
        // ==========================================

        if (setting.followLightRotation)
        {
            // 最直接的同步方式：
            // LightCamera 的旋转基于 Light 的旋转。
            //
            // localRotationOffsetEuler 用于额外微调。
            cam.transform.rotation =
                transform.rotation * Quaternion.Euler(setting.localRotationOffsetEuler);
        }
    }
}