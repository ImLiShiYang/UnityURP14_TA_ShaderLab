using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class Utility
{
#if UNITY_EDITOR
    // 添加到 Camera 的右键上下文菜单
    [MenuItem("CONTEXT/Camera/Create Screenshot")]
    private static void CreateScreenShot(MenuCommand command)
    {
        Camera camera = command.context as Camera;
        if (camera == null)
        {
            Debug.LogError("No Camera found!");
            return;
        }
        ScreenCapture.CaptureScreenshot(Application.dataPath + "/Screenshot.png");
    }
#endif
    
    public static RenderTexture CreateRenderTexture(int width, int height, int depth, RenderTextureFormat format, TextureDimension d)
    {
        RenderTexture rt = new RenderTexture(width, height, 0,
            format, RenderTextureReadWrite.Linear)
        {
            volumeDepth = depth,
            dimension = d,
            useMipMap = false,
            autoGenerateMips = false,
            anisoLevel = 6,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Repeat,
            enableRandomWrite = true
        };
        rt.Create();
        return rt;
    }
    
    public static void ReadRT3D<T>(RenderTexture rt, Texture3D tex) where T : struct
    {
        var a = new NativeArray<T>(rt.width * rt.height * rt.volumeDepth, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        AsyncGPUReadback.RequestIntoNativeArray(ref a, rt, 0, _ =>
        {
            tex.SetPixelData(a, 0);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            a.Dispose();
        });
    }
    
    public static void ReadRT2D(RenderTexture rt, Texture2D tex)
    {
        AsyncGPUReadback.Request(rt, 0, data =>
        {
            tex.SetPixelData(data.GetData<byte>(), 0);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            rt.Release();
        });
    }
    
    // TODO：需要根据width和height动态调整子网格数量，因为单个子网格的顶点数量不能超过65535
    public static Mesh CreatePlane(int width, int height, int lengthX, int lengthZ)
    {
        float offsetX = (width >> 1) - 0.5f;
        float offsetY = (height >> 1) - 0.5f;
        float scaleX = lengthX / (float)width;
        float scaleZ = lengthZ / (float)height;
        Mesh plane = new Mesh();
        Vector3[] vertices = new Vector3[width * height];
        Vector2[] uvs = new Vector2[width * height];
        Vector3[] normals = new Vector3[width * height];
        int[] triangles = new int[(width - 1) * (height - 1) * 6];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                vertices[y * width + x] = new Vector3((x - offsetX) * scaleX, 0, (y - offsetY) * scaleZ);
                uvs[y * width + x] = new Vector2((float)x / width, (float)y / height);
                normals[y * width + x] = Vector3.up;
            }
        }

        int triangleIndex = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int vertexIndex = y * width + x;

                triangles[triangleIndex++] = vertexIndex;
                triangles[triangleIndex++] = vertexIndex + width;
                triangles[triangleIndex++] = vertexIndex + 1;

                triangles[triangleIndex++] = vertexIndex + 1;
                triangles[triangleIndex++] = vertexIndex + width;
                triangles[triangleIndex++] = vertexIndex + width + 1;
            }
        }
        
        plane.vertices = vertices;
        plane.triangles = triangles;
        plane.uv = uvs;
        plane.normals = normals;
        plane.RecalculateNormals();

        return plane;
    }
}