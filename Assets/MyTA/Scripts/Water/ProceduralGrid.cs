using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// RequireComponent 的作用：
// 只要这个脚本挂到某个 GameObject 上，Unity 会自动确保这个物体上有 MeshFilter 和 MeshRenderer。
//
// MeshFilter：负责保存网格数据，比如顶点、UV、三角形。
// MeshRenderer：负责把网格渲染出来，需要配合材质使用。
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralGrid : MonoBehaviour
{
    // Start 会在游戏开始运行后调用一次。
    // 这里调用 Generate()，表示运行时自动生成一张网格。
    void Start()
    {
        Generate();
    }

    // OnValidate 会在 Inspector 参数发生变化时调用。
    // 比如你在面板里修改 xSegment、ySegment、size，Unity 会重新生成网格。
    //
    // 注意：OnValidate 只在编辑器里很常用，方便你不运行游戏也能看到网格变化。
    void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= GenerateInEditor;
        EditorApplication.delayCall += GenerateInEditor;
#else
        Generate();
#endif
    }

#if UNITY_EDITOR
    void OnDisable()
    {
        EditorApplication.delayCall -= GenerateInEditor;
    }

    void GenerateInEditor()
    {
        EditorApplication.delayCall -= GenerateInEditor;

        if (this == null)
        {
            return;
        }

        Generate();
    }
#endif

    // 每个顶点的颜色。
    // 如果材质或 Shader 使用 vertex color，就能看到这个颜色。
    public Color verticeColor = Color.white;

    // X 方向分成多少段。
    // 比如 xSegment = 100，就代表横向有 100 个小格子。
    public int xSegment = 1;

    // Y 方向分成多少段。
    // 比如 ySegment = 100，就代表纵向有 100 个小格子。
    public int ySegment = 1;

    // 整张网格的尺寸。
    // size.x 是宽度，size.y 是高度。
    // 在你的水面例子里，一般是 100 x 100。
    public Vector2 size = Vector2.one;

    // 运行时生成出来的 Mesh。
    Mesh m_mesh;

    // 生成网格的核心函数。
    public void Generate()
    {
        // 防止分段数非法。
        // xSegment 和 ySegment 必须是正数，否则无法生成格子。
        //
        // 这里原代码写的是 ySegment == 0，严格来说可以改成 ySegment <= 0 更安全。
        if (xSegment <= 0 || ySegment == 0)
        {
            throw new System.InvalidOperationException("xSegment and ySegment must be positive int");
        }

        // 创建一张新的 Mesh。
        m_mesh = new Mesh();
        m_mesh.name = "Procedural Grid";

        // 顶点数量。
        //
        // 如果 xSegment = 1，ySegment = 1：
        // 会生成一个四边形，需要 2 x 2 = 4 个顶点。
        //
        // 如果 xSegment = 100，ySegment = 100：
        // 会生成 100 x 100 个小格子，顶点数量是 101 x 101。
        int verticeCount = (xSegment + 1) * (ySegment + 1);

        // 顶点数组：保存每个顶点的位置。
        Vector3[] vertices = new Vector3[verticeCount];

        // UV 数组：保存每个顶点对应贴图上的坐标，范围通常是 0 到 1。
        Vector2[] uv = new Vector2[verticeCount];

        // 顶点颜色数组：保存每个顶点的颜色。
        Color[] colors = new Color[verticeCount];

        // 三角形索引数组。
        //
        // Unity 的 Mesh 是由三角形组成的。
        // 一个小方格 = 2 个三角形。
        // 一个三角形 = 3 个顶点索引。
        // 所以每个小方格需要 2 * 3 = 6 个索引。
        int[] triangles = new int[xSegment * ySegment * 6];

        // 生成所有顶点、UV、顶点颜色。
        //
        // y 从 0 到 ySegment，总共 ySegment + 1 行。
        // x 从 0 到 xSegment，总共 xSegment + 1 列。
        for (int vIdx = 0, y = 0; y <= ySegment; y++)
        {
            for (int x = 0; x <= xSegment; x++, vIdx++)
            {
                // 计算当前顶点的位置。
                //
                // (float)x / xSegment 的范围是 0 到 1。
                // 减去 0.5f 后，范围变成 -0.5 到 0.5。
                // 再乘以 size.x，就得到 -size.x/2 到 size.x/2。
                //
                // 这样生成出来的网格中心就在物体原点，而不是从左下角开始。
                vertices[vIdx] = new Vector3(
                    size.x * ((float)x / xSegment - 0.5f),
                    size.y * ((float)y / ySegment - 0.5f)
                );

                // UV 坐标范围是 0 到 1。
                // 左下角是 (0,0)，右上角是 (1,1)。
                //
                // 后面水波贴图 _WaveTex 就是根据 UV 贴到这张网格上的。
                uv[vIdx] = new Vector2(
                    (float)x / xSegment,
                    (float)y / ySegment
                );

                // 给当前顶点设置颜色。
                colors[vIdx] = verticeColor;
            }
        }

        // 生成所有三角形。
        //
        // 每个小格子会生成两个三角形：
        // 第一个三角形：左下、左上、右下
        // 第二个三角形：右下、左上、右上
        //
        // triangles 数组里存的不是顶点坐标，而是 vertices 数组的下标。
        for (int vIdx = 0, tIdx = 0, y = 0; y < ySegment; y++, vIdx++)
        {
            for (int x = 0; x < xSegment; x++, vIdx++, tIdx += 6)
            {
                // 当前格子的左下角顶点索引。
                triangles[tIdx] = vIdx;

                // 当前格子的左上角顶点索引。
                triangles[tIdx + 1] = triangles[tIdx + 4] = vIdx + xSegment + 1;

                // 当前格子的右下角顶点索引。
                triangles[tIdx + 2] = triangles[tIdx + 3] = vIdx + 1;

                // 当前格子的右上角顶点索引。
                triangles[tIdx + 5] = vIdx + xSegment + 2;
            }
        }

        // 把生成好的数据交给 Mesh。
        m_mesh.vertices = vertices;
        m_mesh.uv = uv;
        m_mesh.triangles = triangles;
        m_mesh.colors = colors;

        // 重新计算法线。
        // 法线影响光照，比如水面哪里亮、哪里暗。
        m_mesh.RecalculateNormals();

        // 重新计算切线。
        // 切线通常用于法线贴图、切线空间计算等。
        m_mesh.RecalculateTangents();

        // 重新计算包围盒。
        // Unity 会用 Bounds 判断物体是否在摄像机视野里。
        m_mesh.RecalculateBounds();

        // 把 Mesh 赋给 MeshFilter，这样物体才真正拥有这张网格。
        this.GetComponent<MeshFilter>().mesh = m_mesh;

        // 如果这个物体上有 MeshCollider，也把同一张网格赋给 Collider。
        // 这样鼠标射线 Physics.Raycast 才能打到这张水面。
        var meshCollider = this.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = m_mesh;
        }
    }
}
