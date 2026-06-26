using UnityEngine;

[ExecuteAlways]
public class LocalFogVolume : MonoBehaviour
{
    [Header("Fog Material")]
    [Tooltip("使用 MyTA/Volumetric/SimpleRayMarchFog 的材质。")]
    public Material fogMaterial;

    [Header("Local Fog")]
    [Tooltip("是否启用局部雾。")]
    public bool useLocalFog = true;

    [Tooltip("局部雾边缘柔和范围。数值越大，box 边缘越软。")]
    [Min(0.001f)]
    public float softness = 3f;

    [Tooltip("是否每帧把 Transform 信息同步到材质。")]
    public bool updateEveryFrame = true;

    private static readonly int UseLocalFogId = Shader.PropertyToID("_UseLocalFog");
    private static readonly int LocalFogCenterId = Shader.PropertyToID("_LocalFogCenter");
    private static readonly int LocalFogSizeId = Shader.PropertyToID("_LocalFogSize");
    private static readonly int LocalFogSoftnessId = Shader.PropertyToID("_LocalFogSoftness");

    private void OnEnable()
    {
        ApplyToMaterial();
    }

    private void Update()
    {
        if (!updateEveryFrame)
            return;

        ApplyToMaterial();
    }

    private void OnValidate()
    {
        softness = Mathf.Max(0.001f, softness);
        ApplyToMaterial();
    }

    public void ApplyToMaterial()
    {
        if (fogMaterial == null)
            return;

        fogMaterial.SetFloat(UseLocalFogId, useLocalFog ? 1f : 0f);

        Vector3 center = transform.position;
        Vector3 size = transform.lossyScale;

        fogMaterial.SetVector(LocalFogCenterId, new Vector4(center.x, center.y, center.z, 0f));
        fogMaterial.SetVector(LocalFogSizeId, new Vector4(size.x, size.y, size.z, 0f));
        fogMaterial.SetFloat(LocalFogSoftnessId, softness);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}