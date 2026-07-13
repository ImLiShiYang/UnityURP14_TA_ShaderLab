#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterSplashPrefabCreator
{
    private const string RootFolder = "Assets/VFX/GeneratedWaterSplash";
    private const string PrefabPath = RootFolder + "/PF_LowSideWaterSplash.prefab";
    private const string SheetTexturePath = RootFolder + "/T_WaterSplash_SheetPacked.png";
    private const string DropletTexturePath = RootFolder + "/T_WaterSplash_DropletPacked.png";
    private const string SheetMaterialPath = RootFolder + "/M_WaterSplash_Particle.mat";
    private const string DropletMaterialPath = RootFolder + "/M_WaterSplash_Droplet.mat";
    private const string RingMaterialPath = RootFolder + "/M_WaterSplash_Ring.mat";
    private const string LeftSheetMeshPath = RootFolder + "/MESH_WaterSplash_Sheet_Left.asset";
    private const string RightSheetMeshPath = RootFolder + "/MESH_WaterSplash_Sheet_Right.asset";
    private const string RingMeshPath = RootFolder + "/MESH_WaterSplash_Ring.asset";
    private const string ShaderPath = "Assets/MyTA/Shaders/Water/WaterSplashTransparent.shader";
    private const string ShaderName = "MyTA/VFX/Water Splash Transparent";
    private const string PlayerPrefabPath = "Assets/MyTA/Prefabs/Player/Player.prefab";
    private const string AutoBuildRequestPath = "Temp/CodexBuildRealisticWaterSplash.request";

    [InitializeOnLoadMethod]
    private static void QueueRequestedBuild()
    {
        if (!File.Exists(AutoBuildRequestPath))
            return;

        EditorApplication.delayCall += RunRequestedBuild;
    }

    private static void RunRequestedBuild()
    {
        if (!File.Exists(AutoBuildRequestPath))
            return;

        File.Delete(AutoBuildRequestPath);

        try
        {
            CreatePrefab();
            Debug.Log("[WaterSplashPrefabCreator] Requested realistic splash build completed.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/VFX/Create Realistic Low Water Splash Prefab")]
    public static void CreatePrefab()
    {
        EnsureFolder(RootFolder);
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
            throw new InvalidOperationException($"Shader not found: {ShaderName}");

        Texture2D sheetTexture = CreatePackedTexture(SheetTexturePath, 256, true);
        Texture2D dropletTexture = CreatePackedTexture(DropletTexturePath, 128, false);

        Mesh leftSheetMesh = UpsertMesh(LeftSheetMeshPath, CreateSheetMesh(true));
        Mesh rightSheetMesh = UpsertMesh(RightSheetMeshPath, CreateSheetMesh(false));

        Material sheetMaterial = UpsertMaterial(
            SheetMaterialPath,
            shader,
            sheetTexture,
            new Color(0.72f, 0.88f, 1f, 1f),
            0.22f,
            0.78f,
            0f,
            0f,
            0f
        );

        Material dropletMaterial = UpsertMaterial(
            DropletMaterialPath,
            shader,
            dropletTexture,
            new Color(0.80f, 0.93f, 1f, 1f),
            0.20f,
            0.68f,
            0f,
            0f,
            0f
        );

        Material ringMaterial = UpsertMaterial(
            RingMaterialPath,
            shader,
            sheetTexture,
            new Color(0.76f, 0.91f, 1f, 1f),
            0.10f,
            0.44f,
            0f,
            0f,
            0.22f
        );

        GameObject root = new GameObject("PF_LowSideWaterSplash");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;

        ConfigureRootTimer(root.AddComponent<ParticleSystem>());
        ConfigureWaterSheet(
            CreateChild(root.transform, "01_Left_Transparent_WaterSheet", new Vector3(-0.03f, 0.01f, 0.05f)),
            sheetMaterial,
            leftSheetMesh,
            true
        );
        ConfigureWaterSheet(
            CreateChild(root.transform, "01_Right_Transparent_WaterSheet", new Vector3(0.03f, 0.01f, 0.05f)),
            sheetMaterial,
            rightSheetMesh,
            false
        );
        ConfigureDroplets(
            CreateChild(root.transform, "02_Coarse_Droplets", new Vector3(0f, 0.025f, 0.10f)),
            dropletMaterial,
            false
        );
        ConfigureDroplets(
            CreateChild(root.transform, "03_Fine_Droplets", new Vector3(0f, 0.03f, 0.12f)),
            dropletMaterial,
            true
        );

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        GameObject splashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        InstallOnPlayerPrefab(splashPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = splashPrefab;

        Debug.Log(
            $"[WaterSplashPrefabCreator] Created realistic splash: {PrefabPath}. " +
            "Local +Z is motion direction and local +Y is the water normal."
        );
    }

    private static ParticleSystem CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.AddComponent<ParticleSystem>();
    }

    private static void ConfigureRootTimer(ParticleSystem particleSystem)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        main.duration = 0.72f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 0.01f;
        main.startSpeed = 0f;
        main.startSize = 0.01f;
        main.maxParticles = 1;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = false;
        particleSystem.GetComponent<ParticleSystemRenderer>().enabled = false;
    }

    private static void ConfigureWaterSheet(
        ParticleSystem particleSystem,
        Material material,
        Mesh mesh,
        bool left)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        ConfigureOneShotMain(main, 0.32f, new ParticleSystem.MinMaxCurve(0.20f, 0.30f));
        main.startSize = new ParticleSystem.MinMaxCurve(0.86f, 1.04f);
        main.startColor = new Color(0.90f, 0.97f, 1f, 0.94f);
        main.gravityModifier = 0.68f;
        main.maxParticles = 2;

        SetBurst(particleSystem, 0f, 1, 1);
        SetBoxShape(particleSystem, new Vector3(0.05f, 0.01f, 0.04f));

        ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = left
            ? new ParticleSystem.MinMaxCurve(-0.28f, -0.16f)
            : new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.05f, 0.20f);

        SetColorOverLifetime(particleSystem, 0.32f, 0.86f, 0f);
        SetSizeOverLifetime(particleSystem, MakeCurve(0f, 0.55f, 0.22f, 1.08f, 1f, 0.78f));
        SetMeshRenderer(particleSystem, material, mesh, 0.15f);
    }

    private static void ConfigureDroplets(
        ParticleSystem particleSystem,
        Material material,
        bool fine)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        ConfigureOneShotMain(
            main,
            0.58f,
            fine
                ? new ParticleSystem.MinMaxCurve(0.20f, 0.42f)
                : new ParticleSystem.MinMaxCurve(0.22f, 0.40f)
        );
        main.startSize = fine
            ? new ParticleSystem.MinMaxCurve(0.005f, 0.012f)
            : new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        main.startColor = fine
            ? new Color(0.86f, 0.96f, 1f, 0.82f)
            : new Color(0.82f, 0.94f, 1f, 0.88f);
        main.gravityModifier = fine ? 1.5f : 1.25f;
        main.maxParticles = fine ? 18 : 10;

        SetBurst(particleSystem, fine ? 0.01f : 0f, fine ? (short)10 : (short)6, fine ? (short)16 : (short)9);
        SetBoxShape(particleSystem, fine
            ? new Vector3(0.58f, 0.015f, 0.12f)
            : new Vector3(0.42f, 0.015f, 0.10f));

        ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        float horizontal = fine ? 0.92f : 0.72f;
        velocity.x = new ParticleSystem.MinMaxCurve(-horizontal, horizontal);
        velocity.y = fine
            ? new ParticleSystem.MinMaxCurve(0.30f, 0.92f)
            : new ParticleSystem.MinMaxCurve(0.24f, 0.72f);
        velocity.z = fine
            ? new ParticleSystem.MinMaxCurve(-0.05f, 0.52f)
            : new ParticleSystem.MinMaxCurve(-0.04f, 0.42f);

        SetColorOverLifetime(particleSystem, 0.88f, 0.72f, 0f);
        SetSizeOverLifetime(particleSystem, MakeCurve(0f, 0.95f, 0.55f, 0.72f, 1f, 0.12f));
        SetBillboardRenderer(
            particleSystem,
            material,
            ParticleSystemRenderMode.Stretch,
            fine ? 0.34f : 0.42f,
            fine ? 0.08f : 0.10f,
            0.25f
        );
    }

    private static void ConfigureSurfaceRing(
        ParticleSystem particleSystem,
        Material material,
        Mesh mesh)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        ConfigureOneShotMain(main, 0.70f, new ParticleSystem.MinMaxCurve(0.48f, 0.66f));
        main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.58f);
        main.startColor = new Color(0.82f, 0.95f, 1f, 0.46f);
        main.gravityModifier = 0f;
        main.maxParticles = 2;

        SetBurst(particleSystem, 0f, 1, 1);
        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = false;
        SetColorOverLifetime(particleSystem, 0.42f, 0.30f, 0f);
        SetSizeOverLifetime(particleSystem, MakeCurve(0f, 0.40f, 0.35f, 1.25f, 1f, 1.85f));
        SetMeshRenderer(particleSystem, material, mesh, -0.1f);
    }

    private static void ConfigureMicroMist(ParticleSystem particleSystem, Material material)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        ConfigureOneShotMain(main, 0.55f, new ParticleSystem.MinMaxCurve(0.35f, 0.62f));
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new Color(0.86f, 0.96f, 1f, 0.055f);
        main.gravityModifier = 0.02f;
        main.maxParticles = 8;

        SetBurst(particleSystem, 0.025f, 3, 6);
        SetBoxShape(particleSystem, new Vector3(0.42f, 0.01f, 0.10f));

        ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.28f, 0.28f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.18f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.02f, 0.24f);

        SetColorOverLifetime(particleSystem, 0.05f, 0.04f, 0f);
        SetSizeOverLifetime(particleSystem, MakeCurve(0f, 0.35f, 0.50f, 1.05f, 1f, 1.35f));
        SetBillboardRenderer(particleSystem, material, ParticleSystemRenderMode.Billboard, 1f, 0f, -0.2f);
    }

    private static void ConfigureOneShotMain(
        ParticleSystem.MainModule main,
        float duration,
        ParticleSystem.MinMaxCurve lifetime)
    {
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
    }

    private static void SetBurst(ParticleSystem particleSystem, float time, short minCount, short maxCount)
    {
        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(time, minCount, maxCount) });
    }

    private static void SetBoxShape(ParticleSystem particleSystem, Vector3 scale)
    {
        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = scale;
    }

    private static void SetColorOverLifetime(
        ParticleSystem particleSystem,
        float startAlpha,
        float middleAlpha,
        float endAlpha)
    {
        ParticleSystem.ColorOverLifetimeModule color = particleSystem.colorOverLifetime;
        color.enabled = true;
        color.color = MakeAlphaGradient(startAlpha, middleAlpha, endAlpha);
    }

    private static void SetSizeOverLifetime(ParticleSystem particleSystem, AnimationCurve curve)
    {
        ParticleSystem.SizeOverLifetimeModule size = particleSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void SetMeshRenderer(
        ParticleSystem particleSystem,
        Material material,
        Mesh mesh,
        float sortingFudge)
    {
        ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        ConfigureRendererCommon(renderer, material, sortingFudge);
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
    }

    private static void SetBillboardRenderer(
        ParticleSystem particleSystem,
        Material material,
        ParticleSystemRenderMode mode,
        float lengthScale,
        float velocityScale,
        float sortingFudge)
    {
        ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        ConfigureRendererCommon(renderer, material, sortingFudge);
        renderer.renderMode = mode;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.lengthScale = lengthScale;
        renderer.velocityScale = velocityScale;
        renderer.minParticleSize = 0.001f;
        renderer.maxParticleSize = 0.35f;
    }

    private static void ConfigureRendererCommon(
        ParticleSystemRenderer renderer,
        Material material,
        float sortingFudge)
    {
        renderer.material = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.sortingFudge = sortingFudge;
    }

    private static Gradient MakeAlphaGradient(float startAlpha, float middleAlpha, float endAlpha)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.80f, 0.93f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(startAlpha, 0f),
                new GradientAlphaKey(middleAlpha, 0.22f),
                new GradientAlphaKey(endAlpha, 1f)
            }
        );
        return gradient;
    }

    private static AnimationCurve MakeCurve(float t0, float v0, float t1, float v1, float t2, float v2)
    {
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(t0, v0),
            new Keyframe(t1, v1),
            new Keyframe(t2, v2)
        );

        for (int index = 0; index < curve.length; index++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Auto);
        }

        return curve;
    }

    private static Material UpsertMaterial(
        string path,
        Shader shader,
        Texture2D texture,
        Color tint,
        float bodyOpacity,
        float edgeOpacity,
        float refractionStrength,
        float refractionMix,
        float fresnelStrength)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.name = Path.GetFileNameWithoutExtension(path);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_Tint", tint);
        material.SetColor("_FoamColor", new Color(0.91f, 0.98f, 1f, 1f));
        material.SetFloat("_BodyOpacity", bodyOpacity);
        material.SetFloat("_EdgeOpacity", edgeOpacity);
        material.SetFloat("_RefractionStrength", refractionStrength);
        material.SetFloat("_RefractionMix", refractionMix);
        material.SetFloat("_FresnelPower", 3f);
        material.SetFloat("_FresnelStrength", fresnelStrength);
        material.renderQueue = 3020;
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D CreatePackedTexture(string path, int size, bool sheet)
    {
        Texture2D generated = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                pixels[y * size + x] = sheet
                    ? EvaluateSheetPixel(uv)
                    : EvaluateDropletPixel(uv);
            }
        }

        generated.SetPixels(pixels);
        generated.Apply(false, false);
        File.WriteAllBytes(path, generated.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(generated);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color EvaluateSheetPixel(Vector2 uv)
    {
        float x = uv.x;
        float y = uv.y;
        float noise = Mathf.PerlinNoise(x * 13.7f + 1.3f, y * 10.9f + 4.1f);

        // A narrow broken crest, not a filled translucent panel.
        float crestY = 0.16f + 0.58f * Mathf.Pow(Mathf.Sin(x * Mathf.PI), 0.72f);
        float crest = 1f - Mathf.SmoothStep(0.035f, 0.105f, Mathf.Abs(y - crestY));
        crest *= Mathf.SmoothStep(0.02f, 0.10f, x) * (1f - Mathf.SmoothStep(0.88f, 0.99f, x));

        // Three short, irregular fingers connect the crest back to the surface.
        float jitter = (noise - 0.5f) * 0.025f;
        float fingers = Mathf.Max(
            EvaluateFinger(x, y, 0.24f + jitter, 0.035f, 0.28f),
            Mathf.Max(
                EvaluateFinger(x, y, 0.50f + jitter, 0.045f, 0.37f),
                EvaluateFinger(x, y, 0.75f + jitter, 0.035f, 0.46f)
            )
        );

        float breakup = Mathf.SmoothStep(0.30f, 0.58f, noise);
        // Texture generation runs in a linear Color texture. Boost the sparse mask
        // before PNG encoding so its stored alpha uses the full 8-bit range.
        float alpha = Mathf.Clamp01(
            Mathf.Max(crest, fingers * 0.72f) * Mathf.Lerp(0.45f, 1f, breakup) * 24f
        );
        return new Color(alpha, alpha, alpha, alpha);
    }

    private static float EvaluateFinger(float x, float y, float center, float width, float top)
    {
        float horizontal = 1f - Mathf.SmoothStep(width, width * 2.2f, Mathf.Abs(x - center));
        float vertical = Mathf.SmoothStep(0.03f, 0.10f, y) *
            (1f - Mathf.SmoothStep(top, top + 0.12f, y));
        return horizontal * vertical;
    }

    private static Color EvaluateDropletPixel(Vector2 uv)
    {
        Vector2 centered = uv * 2f - Vector2.one;
        centered.y *= 1.35f;
        float distance = centered.magnitude;
        float core = Mathf.Clamp01((1f - distance) * 5f);
        float noise = Mathf.PerlinNoise(uv.x * 10f + 3.2f, uv.y * 14f + 6.7f);
        float alpha = Mathf.Clamp01(core * Mathf.Lerp(0.82f, 1f, noise));
        float encodedX = Mathf.Clamp01(0.5f + centered.x * 0.22f);
        float encodedY = Mathf.Clamp01(0.5f + centered.y * 0.18f);
        return new Color(alpha, encodedX, encodedY, alpha);
    }

    private static Mesh CreateSheetMesh(bool left)
    {
        const int segments = 10;
        const int rows = 3;
        List<Vector3> vertices = new List<Vector3>((segments + 1) * rows);
        List<Vector2> uvs = new List<Vector2>((segments + 1) * rows);
        List<int> triangles = new List<int>(segments * (rows - 1) * 6);
        float sideSign = left ? -1f : 1f;

        for (int segment = 0; segment <= segments; segment++)
        {
            float t = segment / (float)segments;
            float side = sideSign * Mathf.Lerp(0.01f, 0.42f, t);
            float forward = 0.01f + 0.18f * t + Mathf.Sin(t * Mathf.PI) * 0.025f;
            float height = 0.035f + Mathf.Sin(t * Mathf.PI * 0.88f) * 0.18f + t * 0.04f;

            for (int row = 0; row < rows; row++)
            {
                float v = row / (float)(rows - 1);
                float curvedForward = forward + Mathf.Sin(v * Mathf.PI) * (0.025f + 0.045f * t);
                vertices.Add(new Vector3(side * Mathf.Lerp(0.94f, 1.04f, v), height * v, curvedForward));
                uvs.Add(new Vector2(t, v));
            }
        }

        for (int segment = 0; segment < segments; segment++)
        {
            for (int row = 0; row < rows - 1; row++)
            {
                int a = segment * rows + row;
                int b = (segment + 1) * rows + row;
                int c = b + 1;
                int d = a + 1;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        Mesh mesh = new Mesh { name = left ? "MESH_WaterSplash_Sheet_Left" : "MESH_WaterSplash_Sheet_Right" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateRingMesh(int segments, float innerRadius)
    {
        List<Vector3> vertices = new List<Vector3>((segments + 1) * 2);
        List<Vector2> uvs = new List<Vector2>((segments + 1) * 2);
        List<int> triangles = new List<int>(segments * 6);

        for (int index = 0; index <= segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(direction * innerRadius * 0.5f);
            vertices.Add(direction * 0.5f);
            uvs.Add(new Vector2(direction.x * innerRadius * 0.5f + 0.5f, direction.z * innerRadius * 0.5f + 0.5f));
            uvs.Add(new Vector2(direction.x * 0.5f + 0.5f, direction.z * 0.5f + 0.5f));
        }

        for (int index = 0; index < segments; index++)
        {
            int a = index * 2;
            int b = a + 1;
            int c = a + 3;
            int d = a + 2;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        Mesh mesh = new Mesh { name = "MESH_WaterSplash_Ring" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh UpsertMesh(string path, Mesh generated)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }

        generated.name = existing.name;
        EditorUtility.CopySerialized(generated, existing);
        UnityEngine.Object.DestroyImmediate(generated);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void InstallOnPlayerPrefab(GameObject splashPrefab)
    {
        if (splashPrefab == null)
            throw new InvalidOperationException($"Splash prefab was not created: {PrefabPath}");

        GameObject playerContents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            foreach (WaterRippleBrushSpawner spawner in playerContents.GetComponentsInChildren<WaterRippleBrushSpawner>(true))
            {
                spawner.footWaterSplashPrefab = splashPrefab;
                spawner.minFootWaterSplashScale = 0.50f;
                spawner.maxFootWaterSplashScale = 0.82f;
                EditorUtility.SetDirty(spawner);
            }

            foreach (WaterRippleWeaponEventTrail trail in playerContents.GetComponentsInChildren<WaterRippleWeaponEventTrail>(true))
            {
                trail.weaponWaterSplashPrefab = splashPrefab;
                trail.weaponWaterSplashScale = 0.90f;
                trail.weaponWaterSplashCooldown = 0.20f;
                trail.maxWeaponWaterSplashesPerAttack = 2;
                EditorUtility.SetDirty(trail);
            }

            PrefabUtility.SaveAsPrefabAsset(playerContents, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(playerContents);
        }
    }

    private static void EnsureFolder(string fullPath)
    {
        fullPath = fullPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(fullPath) || fullPath == "Assets")
            return;

        string parent = Path.GetDirectoryName(fullPath)?.Replace("\\", "/");
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent))
            parent = "Assets";

        EnsureFolder(parent);
        if (!AssetDatabase.IsValidFolder(fullPath))
            AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
