#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DessiMaterialTextureFixer
{
    private const string MaterialRoot = "Assets/Models/Nikke-Dessi/Materials/";
    private const string TextureRoot = "Assets/Models/Nikke-Dessi/Texture/";
    private const string FaceMaterialPath = MaterialRoot + "頭脸.mat";
    private const string FaceSDFPath = TextureRoot + "face_SDF.png";

    private static readonly Dictionary<string, string> MaterialTextures = new Dictionary<string, string>
    {
        { "牙舌", "face_M_BC.jpg" },
        { "口腔", "face_BC.jpg" },
        { "眼白", "face_BC.jpg" },
        { "頭脸", "face_BC.jpg" },
        { "瞳", "eye_BC.jpg" },
        { "瞳-高光", "eye_BC.jpg" },
        { "睫眉", "eye_BC.jpg" },
        { "体", "body_BC.jpg" },
        { "体-隐", "body_BC.jpg" },
        { "臂指", "body_BC.jpg" },
        { "靴", "Shoes_BC.jpg" },
        { "裤", "Skirt_BC.jpg" },
        { "下裙", "Skirt_BC.jpg" },
        { "腿饰", "Skirt_BC.jpg" },
        { "上衣", "Coat_BC.jpg" },
        { "衣链", "Coat_chain_BC.jpg" },
        { "衣袖", "Coat_BC.jpg" },
        { "手套", "Coat_BC.jpg" },
        { "腰带", "Skirt_BC.jpg" },
        { "腰饰1", "Skirt_BC.jpg" },
        { "腰饰2", "Skirt_BC.jpg" },
        { "帽子", "Cloak_BC.jpg" },
        { "耳环", "Cloak_BC.jpg" },
        { "后髪", "hair_behind_BC.jpg" },
        { "尾髪", "hair_front_BC.jpg" },
        { "侧髪", "hair_side_BC.jpg" },
        { "前髪", "hair_front_BC.jpg" },
        { "领1", "Coat_BC.jpg" },
        { "领2", "Coat_BC.jpg" },
        { "领3", "Coat_BC.jpg" },
        { "领-宝石", "Coat_BC.jpg" },
    };

    [MenuItem("Tools/Models/Fix Dessi Material Textures")]
    public static void FixAll()
    {
        ConfigureFaceSDFImporter();

        int fixedCount = 0;
        int missingCount = 0;

        foreach (KeyValuePair<string, string> pair in MaterialTextures)
        {
            string materialPath = MaterialRoot + pair.Key + ".mat";
            string texturePath = TextureRoot + pair.Value;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);

            if (material == null || texture == null)
            {
                Debug.LogWarning($"Dessi material texture fix skipped: {materialPath} / {texturePath}");
                missingCount++;
                continue;
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }

            EditorUtility.SetDirty(material);
            fixedCount++;
        }

        FixFaceSDF();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Dessi material texture fix complete. Updated {fixedCount} materials, skipped {missingCount}.");
    }

    private static void ConfigureFaceSDFImporter()
    {
        TextureImporter importer = AssetImporter.GetAtPath(FaceSDFPath) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;

        if (importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            changed = true;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static void FixFaceSDF()
    {
        Material faceMaterial = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
        Texture faceSDF = AssetDatabase.LoadAssetAtPath<Texture>(FaceSDFPath);

        if (faceMaterial == null || faceSDF == null)
        {
            Debug.LogWarning($"Dessi face SDF fix skipped: {FaceMaterialPath} / {FaceSDFPath}");
            return;
        }

        if (faceMaterial.HasProperty("_FaceSDFMap"))
        {
            faceMaterial.SetTexture("_FaceSDFMap", faceSDF);
        }

        if (faceMaterial.HasProperty("_UseFaceSDF"))
        {
            faceMaterial.SetFloat("_UseFaceSDF", 1.0f);
        }

        EditorUtility.SetDirty(faceMaterial);
    }
}
#endif
