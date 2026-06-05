#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CreateWaterRippleCircleBrushPrefab
{
    private const string RootFolder = "Assets/WaterRippleGenerated";
    private const string MaterialFolder = RootFolder + "/Materials";
    private const string PrefabFolder = RootFolder + "/Prefabs";
    private const string MaterialPath = MaterialFolder + "/WaterRippleCircleStampBrush.mat";
    private const string PrefabPath = PrefabFolder + "/WaterRippleCircleBrush.prefab";

    [MenuItem("Tools/Water Ripple/Create Circle Brush Prefab")]
    public static void Create()
    {
        EnsureFolder("Assets", "WaterRippleGenerated");
        EnsureFolder(RootFolder, "Materials");
        EnsureFolder(RootFolder, "Prefabs");

        Shader shader = Shader.Find("WaterRipple/WaterRippleCircleStampBrush");
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Water Ripple",
                "没有找到 Shader：WaterRipple/WaterRippleCircleStampBrush\n请先把 WaterRippleCircleStampBrush.shader 导入项目。",
                "OK");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        // 默认参数：偏保守，适合第一版调试。
        material.SetFloat("_NormalStrength", 1.0f);
        material.SetFloat("_HeightStrength", 1.0f);
        material.SetFloat("_InvertHeight", 0.0f);
        material.SetFloat("_CenterStrength", -0.35f);
        material.SetFloat("_RingStrength", 0.18f);
        material.SetFloat("_InnerRadius", 0.14f);
        material.SetFloat("_OuterRadius", 0.42f);
        material.SetFloat("_EdgeSoftness", 0.02f);
        EditorUtility.SetDirty(material);

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "WaterRippleCircleBrush";
        quad.transform.position = Vector3.zero;
        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = Vector3.one;

        Collider col = quad.GetComponent<Collider>();
        if (col != null)
            Object.DestroyImmediate(col);

        Renderer renderer = quad.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null)
        {
            PrefabUtility.SaveAsPrefabAsset(quad, PrefabPath);
        }
        else
        {
            PrefabUtility.SaveAsPrefabAssetAndConnect(quad, PrefabPath, InteractionMode.AutomatedAction);
        }

        Object.DestroyImmediate(quad);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        EditorGUIUtility.PingObject(Selection.activeObject);

        EditorUtility.DisplayDialog(
            "Water Ripple",
            "已生成：\n" + PrefabPath + "\n\n同时生成材质：\n" + MaterialPath,
            "OK");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
