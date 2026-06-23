using UnityEngine;

public class MeshLocalBoundsDebug : MonoBehaviour
{
    private void Start()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("没有找到 MeshFilter 或 Mesh");
            return;
        }

        Bounds b = mf.sharedMesh.bounds;

        Debug.Log($"Mesh Local Bounds Center: {b.center}");
        Debug.Log($"Mesh Local Bounds Size: {b.size}");
        Debug.Log($"Local X Size: {b.size.x}");
        Debug.Log($"Local Y Size: {b.size.y}");
        Debug.Log($"Local Z Size: {b.size.z}");
    }
}