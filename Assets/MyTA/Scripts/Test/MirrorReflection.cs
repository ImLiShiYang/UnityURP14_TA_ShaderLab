using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Renderer))]
public class MirrorReflection : MonoBehaviour
{
    [Header("反射设置")]
    public Camera mainCamera;
    public LayerMask reflectionLayers = -1;
    public int textureSize = 1024;
    public float clipPlaneOffset = 0.07f;

    private Camera reflectionCamera;
    private RenderTexture reflectionRT;
    private Renderer rend;
    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        rend = GetComponent<Renderer>();
        if (mainCamera == null)
            mainCamera = Camera.main;

        // 创建反射相机
        GameObject reflectionGO = new GameObject("MirrorReflectionCamera");
        reflectionCamera = reflectionGO.AddComponent<Camera>();
        reflectionCamera.CopyFrom(mainCamera);
        reflectionCamera.enabled = false;
        reflectionCamera.cullingMask = reflectionLayers;

        var urpCam = reflectionCamera.GetUniversalAdditionalCameraData();
        if (urpCam != null)
        {
            urpCam.renderShadows = false;
            urpCam.requiresDepthTexture = false;
            urpCam.requiresColorTexture = false;
        }

        reflectionRT = new RenderTexture(textureSize, textureSize, 16);
        reflectionRT.name = "MirrorReflectionRT";
        reflectionCamera.targetTexture = reflectionRT;

        propertyBlock = new MaterialPropertyBlock();
        rend.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture("_ReflectionTex", reflectionRT);
        rend.SetPropertyBlock(propertyBlock);
    }

    void OnDestroy()
    {
        if (reflectionRT != null)
            reflectionRT.Release();
        if (reflectionCamera != null)
            Destroy(reflectionCamera.gameObject);
    }

    void LateUpdate()
    {
        if (mainCamera == null || rend == null) return;

        // 镜子的世界法线（正面方向）和位置
        Vector3 mirrorNormal = -transform.forward;
        Vector3 mirrorPosition = transform.position;
        Debug.DrawRay(transform.position, transform.forward * 1f, Color.red);

        // 计算主相机关于镜面的镜像位置
        Vector3 reflectedCamPos = ReflectPoint(mainCamera.transform.position, mirrorPosition, mirrorNormal);
        // 反射相机看向镜子中心
        Vector3 lookAtPoint = mirrorPosition;
        Vector3 reflectedDir = (lookAtPoint - reflectedCamPos).normalized;

        reflectionCamera.transform.position = reflectedCamPos;
        reflectionCamera.transform.rotation = Quaternion.LookRotation(reflectedDir, Vector3.up);

        Debug.Log("Reflection Camera Position: " + reflectionCamera.transform.position);
        
        // 计算镜子平面到反射相机的距离（沿法线方向）
        float distanceToMirror = Vector3.Dot(mirrorNormal, reflectedCamPos - mirrorPosition);
        // 如果反射相机跑到了镜子背面（理论上不应该，但防止数值误差），修正
        if (distanceToMirror < 0.05f)
        {
            reflectedCamPos = mirrorPosition + mirrorNormal * 0.1f;
            reflectionCamera.transform.position = reflectedCamPos;
            reflectedDir = (lookAtPoint - reflectedCamPos).normalized;
            reflectionCamera.transform.rotation = Quaternion.LookRotation(reflectedDir, Vector3.up);
            distanceToMirror = 0.1f;
        }
        // 调整近裁剪面，避免渲染镜子背后的物体（穿模）
        reflectionCamera.nearClipPlane = Mathf.Max(0.01f, distanceToMirror * 0.9f);

        // 使用官方 API 设置斜裁剪矩阵，避免手动计算导致的无效矩阵错误
        // 计算裁剪平面（世界空间）：方程为 normal·x + d = 0，其中 d = -normal·point
        Vector4 clipPlaneWorld = new Vector4(
            mirrorNormal.x,
            mirrorNormal.y,
            mirrorNormal.z,
            -Vector3.Dot(mirrorNormal, mirrorPosition) - clipPlaneOffset
        );
        // 设置投影矩阵（CalculateObliqueMatrix 内部会处理相机空间变换）
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlaneWorld);

        // 手动渲染
        reflectionCamera.Render();
    }

    /// <summary>
    /// 点关于平面的反射
    /// </summary>
    private Vector3 ReflectPoint(Vector3 point, Vector3 planePos, Vector3 planeNormal)
    {
        Vector3 toPoint = point - planePos;
        float distance = Vector3.Dot(toPoint, planeNormal);
        return point - 2f * distance * planeNormal;
    }
}