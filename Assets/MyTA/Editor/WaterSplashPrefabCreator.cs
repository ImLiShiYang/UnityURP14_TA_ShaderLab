// Put this file under: Assets/Editor/WaterSplashPrefabCreator.cs
// Unity menu: Tools/VFX/Create Low Side Water Splash Prefab
// It creates: Assets/VFX/GeneratedWaterSplash/PF_LowSideWaterSplash.prefab

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterSplashPrefabCreator
{
    private const string RootFolder = "Assets/VFX/GeneratedWaterSplash";
    private const string PrefabPath = RootFolder + "/PF_LowSideWaterSplash.prefab";
    private const string TexturePath = RootFolder + "/T_SoftWaterDroplet.png";
    private const string MaterialPath = RootFolder + "/M_WaterSplash_Particle.mat";

    [MenuItem("Tools/VFX/Create Low Side Water Splash Prefab")]
    public static void CreatePrefab()
    {
        EnsureFolder(RootFolder);

        CreateSoftDropletTexture(TexturePath);
        Material waterMat = CreateWaterParticleMaterial(MaterialPath, TexturePath);

        GameObject root = new GameObject("PF_LowSideWaterSplash");
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        ConfigureRootTimer(root.AddComponent<ParticleSystem>());

        // Local axis convention:
        // +Z = character forward / splash forward
        // +X = right side
        // +Y = up
        ConfigureSideSheet(CreateChild(root.transform, "01_Left_Dense_Sheet", new Vector3(-0.18f, 0.02f, 0.10f)), waterMat, true);
        ConfigureSideSheet(CreateChild(root.transform, "01_Right_Dense_Sheet", new Vector3(0.18f, 0.02f, 0.10f)), waterMat, false);
        ConfigureFineDroplets(CreateChild(root.transform, "02_Fine_Droplets", new Vector3(0.0f, 0.03f, 0.12f)), waterMat);
        ConfigureMist(CreateChild(root.transform, "03_Soft_Mist", new Vector3(0.0f, 0.02f, 0.15f)), waterMat);
        ConfigureSurfaceFoam(CreateChild(root.transform, "04_Surface_Foam", new Vector3(0.0f, 0.01f, 0.16f)), waterMat);

        AssetDatabase.DeleteAsset(PrefabPath);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Debug.Log($"Created water splash prefab: {PrefabPath}\nLocal +Z is forward. Instantiate it with Quaternion.LookRotation(characterForward, Vector3.up).");
    }

    private static ParticleSystem CreateChild(Transform parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.AddComponent<ParticleSystem>();
    }

    private static void ConfigureRootTimer(ParticleSystem ps)
    {
        var main = ps.main;
        main.duration = 1.15f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 0.01f;
        main.startSpeed = 0f;
        main.startSize = 0.01f;
        main.maxParticles = 1;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.enabled = false;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.enabled = false;
    }

    private static void ConfigureSideSheet(ParticleSystem ps, Material mat, bool left)
    {
        var main = ps.main;
        main.duration = 0.45f;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.52f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.65f, 0.65f);
        main.gravityModifier = 0.22f;
        main.maxParticles = 160;
        main.startColor = Color.white;

        SetBurst(ps, 0f, 50, 75);
        SetBoxShape(ps, new Vector3(0.18f, 0.02f, 0.10f));

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = left ? new ParticleSystem.MinMaxCurve(-2.0f, -0.85f) : new ParticleSystem.MinMaxCurve(0.85f, 2.0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.12f, 0.62f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.30f, 1.05f);

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = MakeAlphaGradient(0.95f, 0.78f, 0.0f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, MakeCurve(0f, 0.55f, 0.18f, 1.25f, 1f, 0.0f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.14f);
        noise.frequency = 2.2f;
        noise.octaveCount = 1;

        SetRenderer(ps, mat, ParticleSystemRenderMode.Stretch, 1.65f, 0.12f, 0.7f);
    }

    private static void ConfigureFineDroplets(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration = 0.55f;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.75f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
        main.gravityModifier = 1.15f;
        main.maxParticles = 260;
        main.startColor = Color.white;

        SetBurst(ps, 0.02f, 120, 170);
        SetBoxShape(ps, new Vector3(0.72f, 0.02f, 0.14f));

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-2.35f, 2.35f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 1.55f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.05f, 1.25f); // positive Z keeps most droplets in front

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = MakeAlphaGradient(1.0f, 0.9f, 0.0f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, MakeCurve(0f, 0.9f, 0.55f, 0.7f, 1f, 0.0f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.08f);
        noise.frequency = 4.0f;
        noise.octaveCount = 1;

        SetRenderer(ps, mat, ParticleSystemRenderMode.Stretch, 0.55f, 0.18f, 1.0f);
    }

    private static void ConfigureMist(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration = 0.65f;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.28f);
        main.gravityModifier = 0.02f;
        main.maxParticles = 90;
        main.startColor = new Color(1f, 1f, 1f, 0.28f);

        SetBurst(ps, 0.04f, 35, 55);
        SetBoxShape(ps, new Vector3(0.65f, 0.02f, 0.14f));

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.70f, 0.70f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.28f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.05f, 0.45f);

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = MakeAlphaGradient(0.22f, 0.16f, 0.0f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, MakeCurve(0f, 0.2f, 0.45f, 1.25f, 1f, 0.0f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.22f);
        noise.frequency = 1.15f;
        noise.octaveCount = 1;

        SetRenderer(ps, mat, ParticleSystemRenderMode.Billboard, 1f, 0f, 0.2f);
    }

    private static void ConfigureSurfaceFoam(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.duration = 0.50f;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-3.14f, 3.14f);
        main.gravityModifier = 0f;
        main.maxParticles = 120;
        main.startColor = new Color(1f, 1f, 1f, 0.85f);

        SetBurst(ps, 0.00f, 45, 70);
        SetBoxShape(ps, new Vector3(0.78f, 0.01f, 0.18f));

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.95f, 0.95f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.00f, 0.05f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.02f, 0.55f);

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = MakeAlphaGradient(0.65f, 0.38f, 0.0f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, MakeCurve(0f, 0.45f, 0.35f, 1.10f, 1f, 0.0f));

        SetRenderer(ps, mat, ParticleSystemRenderMode.Billboard, 1f, 0f, 0.5f);
    }

    private static void SetBurst(ParticleSystem ps, float time, short minCount, short maxCount)
    {
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(time, minCount, maxCount) });
    }

    private static void SetBoxShape(ParticleSystem ps, Vector3 scale)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = scale;
    }

    private static void SetRenderer(ParticleSystem ps, Material mat, ParticleSystemRenderMode mode, float lengthScale, float velocityScale, float sortingFudge)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.lengthScale = lengthScale;
        renderer.velocityScale = velocityScale;
        renderer.sortingFudge = sortingFudge;
        renderer.minParticleSize = 0.001f;
        renderer.maxParticleSize = 0.5f;
    }

    private static Gradient MakeAlphaGradient(float a0, float aMid, float a1)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.82f, 0.92f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(a0, 0f),
                new GradientAlphaKey(aMid, 0.25f),
                new GradientAlphaKey(a1, 1f)
            }
        );
        return g;
    }

    private static AnimationCurve MakeCurve(float t0, float v0, float t1, float v1, float t2, float v2)
    {
        AnimationCurve c = new AnimationCurve(
            new Keyframe(t0, v0),
            new Keyframe(t1, v1),
            new Keyframe(t2, v2)
        );
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Auto);
        }
        return c;
    }

    private static Material CreateWaterParticleMaterial(string path, string texPath)
    {
        AssetDatabase.DeleteAsset(path);

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.name = "M_WaterSplash_Particle";

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

        // URP transparent alpha blend setup. Safe even if some properties do not exist.
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void CreateSoftDropletTexture(string path)
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float d = Vector2.Distance(uv, new Vector2(0.5f, 0.5f)) * 2f;
                float core = Mathf.SmoothStep(1f, 0f, d);
                float rim = Mathf.SmoothStep(0.95f, 0.55f, d) * 0.25f;
                float noise = Mathf.PerlinNoise(uv.x * 12.0f, uv.y * 12.0f) * 0.18f + 0.82f;
                float alpha = Mathf.Clamp01((core + rim) * noise);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);

        AssetDatabase.DeleteAsset(path);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }

    private static void EnsureFolder(string fullPath)
    {
        fullPath = fullPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(fullPath)) return;
        if (fullPath == "Assets") return;

        string parent = Path.GetDirectoryName(fullPath)?.Replace("\\", "/");
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent)) parent = "Assets";
        EnsureFolder(parent);
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
