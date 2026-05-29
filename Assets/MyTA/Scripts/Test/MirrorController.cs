// MirrorController.cs
// 控制反射相机，实现正确的反射视角
 
using UnityEngine;
 
[ExecuteInEditMode] // 允许在编辑器中预览
public class MirrorController : MonoBehaviour
{
    [Header("相机设置")]
    public Camera reflectionCamera;    // 反射相机
    public Camera mainCamera;          // 主相机（自动获取）
    
    [Header("镜子设置")]
    public bool invertX = true;       // 水平翻转
    public bool invertY = false;      // 垂直翻转
    public LayerMask reflectionLayers = ~0; // 反射层
    
    [Header("高级选项")]
    public bool useObliqueProjection = false; // 使用斜投影裁剪
    public float clipPlaneOffset = 0.07f;    // 裁剪面偏移
 
    private Transform mirrorTransform;
    private RenderTexture reflectionTexture;
 
    void OnEnable()
    {
        mirrorTransform = transform;
        
        // 自动获取主相机
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        // 创建 Render Texture
        CreateRenderTexture();
    }
 
    void CreateRenderTexture()
    {
        // 1. 确保基础尺寸至少为64（避免0值），同时不超过屏幕宽度的一半
        int baseSize = Mathf.Max(64, (int)(Screen.width * 0.5f));
    
        // 2. 获取当前硬件支持的最大纹理尺寸，避免超出限制<citation>1</citation>
        int maxTextureSize = SystemInfo.maxTextureSize;
    
        // 3. 计算最终尺寸：取基础尺寸与最大支持尺寸的较小值，并转换为2的幂（符合RenderTexture最佳实践<citation>6</citation>）
        int textureSize = Mathf.NextPowerOfTwo(Mathf.Min(baseSize, maxTextureSize));

        // 安全创建RenderTexture
        reflectionTexture = new RenderTexture(textureSize, textureSize, 24);
        
        reflectionTexture.antiAliasing = 2;
        reflectionTexture.filterMode = FilterMode.Bilinear;
    
        // 赋给反射相机
        if (reflectionCamera != null)
            reflectionCamera.targetTexture = reflectionTexture;
    
        // 赋给材质
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
            rend.sharedMaterial.SetTexture("_MainTex", reflectionTexture);
    }
 
    void Update()
    {
        if (reflectionCamera == null || mainCamera == null)
            return;
 
        // 计算反射相机的位置和旋转
        UpdateReflectionCamera();
    }
 
    void UpdateReflectionCamera()
    {
        Vector3 mirrorPos = mirrorTransform.position;
        Vector3 mirrorNormal = mirrorTransform.forward;
        
        if (Input.GetKeyDown(KeyCode.Space)) // 按空格键打印一次
        {
            Debug.Log($"主相机位置: {mainCamera.transform.position}");
            Debug.Log($"反射相机位置: {reflectionCamera.transform.position}");
        }
        
        // 计算主相机相对于镜面的反射位置
        float distance = -Vector3.Dot(mirrorNormal, mainCamera.transform.position - mirrorPos);
            
        Vector3 reflectionPos = mainCamera.transform.position + 2 * distance * mirrorNormal;
        
        // 设置反射相机位置
        reflectionCamera.transform.position = reflectionPos;
        
        // 计算反射旋转
        Vector3 forward = Vector3.Reflect(mainCamera.transform.forward, mirrorNormal);
            
        Vector3 up = Vector3.Reflect(mainCamera.transform.up, mirrorNormal);
            
        reflectionCamera.transform.rotation = Quaternion.LookRotation(forward, up);
        
        // 同步相机参数
        reflectionCamera.fieldOfView = mainCamera.fieldOfView;
        reflectionCamera.nearClipPlane = mainCamera.nearClipPlane;
        reflectionCamera.farClipPlane = mainCamera.farClipPlane;
        reflectionCamera.cullingMask = reflectionLayers;
        
        // 可选：使用斜投影避免渲染镜子后面的物体
        if (useObliqueProjection)
        {
            reflectionCamera.ResetProjectionMatrix();
            SetObliqueProjection(reflectionCamera, mirrorPos, mirrorNormal);
        }
        else
        {
            // 关键：恢复默认投影矩阵，确保透视正确
            reflectionCamera.ResetProjectionMatrix();
        }

    }

    void SetObliqueProjection(Camera cam, Vector3 pos, Vector3 normal)
    {
        Matrix4x4 viewMatrix = cam.worldToCameraMatrix;
        Vector3 viewPos = viewMatrix.MultiplyPoint(pos);
        Vector3 viewNormal = viewMatrix.MultiplyVector(normal).normalized;

        Vector4 clipPlane = new Vector4(
            viewNormal.x, viewNormal.y, viewNormal.z,
            -Vector3.Dot(viewPos, viewNormal) + clipPlaneOffset);

        cam.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

    }


    // void OnDisable()
    // {
    //     // 清理资源
    //     if (reflectionTexture != null)
    //     {
    //         reflectionCamera.targetTexture = null;
    //         DestroyImmediate(reflectionTexture);
    //     }
    // }
    
    void OnDisable()
    {
        // 清理资源前先检查reflectionCamera是否有效
        if (reflectionCamera != null)
        {
            reflectionCamera.targetTexture = null;
        }
    
        // 安全销毁RenderTexture
        if (reflectionTexture != null)
        {
            DestroyImmediate(reflectionTexture);
            reflectionTexture = null; // 显式置空，避免后续误访问
        }
    }
}