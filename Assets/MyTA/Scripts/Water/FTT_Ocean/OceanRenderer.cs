using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public struct WaveData
{
    public float windDirectionX;
    public float windDirectionY;
    public float windSpeed;
    public float waveAmplitude;
    public float gravityAcceleration;
    public float fetch;
    public float depth;
}

public class OceanRenderer : MonoBehaviour
{
    [SerializeField] private Material oceanMaterial;
    [SerializeField] private ComputeShader spectrumShader;
    [SerializeField] private ComputeShader waveShader;
    [SerializeField] private ComputeShader fftShader;
    [SerializeField] [Range(128, 1024)] private int size;
    [SerializeField] private int length;
    [SerializeField] private float lambda;
    [SerializeField] private WaveData waveData;
    
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private FastFourierTransform fft;
    private Material oceanMaterial_Ins;
    private ReflectionProbe probe;

    private RenderTexture gaussNoise;
    
    private RenderTexture h0;
    private RenderTexture frequency;
    private RenderTexture dy_dxzSpec;
    private RenderTexture dx_dzSpec;
    private RenderTexture dxx_dzzSpec;
    private RenderTexture dyx_dyzSpec;
    private RenderTexture displacement;
    private RenderTexture derivatives;
    private RenderTexture turbulence;

    private RenderTexture reflection;
    
    private CommandBuffer cmd;

#if UNITY_EDITOR
    [SerializeField] private bool updateStaticSpectrum;

    public RenderTexture GaussNoise => gaussNoise;
    public RenderTexture H0 => h0;
    public RenderTexture Displacement => displacement;
    public RenderTexture Derivatives => derivatives;
#endif
    
    public static RenderTexture CreateRenderTexture(int width, int height, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(width, height, 0,
            format, RenderTextureReadWrite.Linear)
        {
            useMipMap = false,
            autoGenerateMips = false,
            anisoLevel = 6,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            enableRandomWrite = true
        };
        rt.Create();
        return rt;
    }

    void Start()
    {
        probe = GetComponent<ReflectionProbe>();
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
#if UNITY_EDITOR
#endif
        K_GAUSS_NOISE = spectrumShader.FindKernel("GenerateGaussNoise");
        K_STATIC_SPECTRUM = spectrumShader.FindKernel("GenerateStaticSpectrum");
        K_GENERATE_WAVE = waveShader.FindKernel("GenerateWave");
        K_MERGE_TEXTURES = waveShader.FindKernel("MergeTextures");
        
        cmd = new CommandBuffer();
        
        gaussNoise = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        h0 = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        frequency = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, TextureDimension.Tex2D);
        dy_dxzSpec = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        dx_dzSpec = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        dxx_dzzSpec = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        dyx_dyzSpec = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RGFloat, TextureDimension.Tex2D);
        displacement = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, TextureDimension.Tex2D);
        derivatives = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat, TextureDimension.Tex2D);
        turbulence = Utility.CreateRenderTexture(size, size, 0, RenderTextureFormat.RFloat, TextureDimension.Tex2D);
        reflection = Utility.CreateRenderTexture(probe.resolution, probe.resolution, 0, RenderTextureFormat.ARGB32,
            TextureDimension.Cube);
        
        cmd.SetComputeIntParam(waveShader, P_SIZE, size);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_H0, h0);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_FREQUENCY, frequency);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_DY_DXZ_SPEC, dy_dxzSpec);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_DX_DZ_SPEC, dx_dzSpec);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_DYZ_DZZ_SPEC, dxx_dzzSpec);
        cmd.SetComputeTextureParam(waveShader, K_GENERATE_WAVE, P_DYX_DXX_SPEC, dyx_dyzSpec);
        
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DY_DXZ_SPEC, dy_dxzSpec);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DX_DZ_SPEC, dx_dzSpec);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DYZ_DZZ_SPEC, dxx_dzzSpec);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DYX_DXX_SPEC, dyx_dyzSpec);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DISPLACEMENT, displacement);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_DERIVATIVES, derivatives);
        cmd.SetComputeTextureParam(waveShader, K_MERGE_TEXTURES, P_TURBULENCE, turbulence);
        
        GenerateNoise();
        GenerateStaticSpectrum();
        Graphics.ExecuteCommandBuffer(cmd);
        
        fft = new FastFourierTransform(fftShader, new Vector2Int(size, size));
        oceanMaterial_Ins = Material.Instantiate(oceanMaterial);
        meshFilter.mesh = Utility.CreatePlane(size, size, length, length);
        meshRenderer.material = oceanMaterial_Ins;

        probe.realtimeTexture = reflection;
        oceanMaterial_Ins.SetTexture(PROP_DISPLACEMENT, displacement);
        oceanMaterial_Ins.SetTexture(PROP_DERIVATIVES, derivatives);
        oceanMaterial_Ins.SetTexture(PROP_TURBULENCE, turbulence);
        oceanMaterial_Ins.SetTexture(PROP_REFLECTION_TEX, reflection);

#if !UNITY_EDITOR
        gaussNoise.Release();
#endif
    }

    void Update()
    {
#if UNITY_EDITOR
        cmd.Clear();
        if (updateStaticSpectrum)
        {
            GenerateStaticSpectrum();
            Graphics.ExecuteCommandBuffer(cmd);
        }
#endif
        cmd.Clear();
#if UNITY_EDITOR
        cmd.SetComputeFloatParam(waveShader, P_LAMBDA, lambda);
#endif
        cmd.DispatchCompute(waveShader, K_GENERATE_WAVE, size >> 3, size >> 3, 1);
        fft.IFFT2D(dy_dxzSpec, cmd, true);
        fft.IFFT2D(dx_dzSpec, cmd, true);
        fft.IFFT2D(dxx_dzzSpec, cmd, true);
        fft.IFFT2D(dyx_dyzSpec, cmd, true);
        cmd.DispatchCompute(waveShader, K_MERGE_TEXTURES, size >> 3, size >> 3, 1);
        var fence = cmd.CreateAsyncGraphicsFence();
        cmd.WaitOnAsyncGraphicsFence(fence);
        Graphics.ExecuteCommandBuffer(cmd);
        
#if UNITY_EDITOR
        oceanMaterial_Ins.SetFloat(PROP_LENGTH, length);
#endif
    }

    private void OnDestroy()
    {
        gaussNoise?.Release();
        h0?.Release();
        dx_dzSpec?.Release();
        dy_dxzSpec?.Release();
        meshRenderer.material = null;
        cmd.Dispose();
        fft.Dispose();
    }

    private void GenerateStaticSpectrum()
    {
        cmd.SetComputeIntParam(spectrumShader, P_SIZE, size);
        cmd.SetComputeTextureParam(spectrumShader, K_STATIC_SPECTRUM, P_GAUSS_NOISE, gaussNoise);
        cmd.SetComputeTextureParam(spectrumShader, K_STATIC_SPECTRUM, P_H0, h0);
        cmd.SetComputeTextureParam(spectrumShader, K_STATIC_SPECTRUM, P_FREQUENCY, frequency);
        cmd.SetComputeFloatParams(spectrumShader, P_DIRECTION, waveData.windDirectionX, waveData.windDirectionY);
        cmd.SetComputeFloatParams(spectrumShader, P_LENGTH, length);
        cmd.SetComputeFloatParam(spectrumShader, P_WIND_SPEED, waveData.windSpeed);
        cmd.SetComputeFloatParam(spectrumShader, P_GRAVITY, waveData.gravityAcceleration);
        cmd.SetComputeFloatParam(spectrumShader, P_SQRT_AMPLITUDE, Mathf.Sqrt(waveData.waveAmplitude));
        cmd.SetComputeFloatParam(spectrumShader, P_FETCH, waveData.fetch);
        cmd.SetComputeFloatParam(spectrumShader, P_DEPTH, waveData.depth);
        cmd.DispatchCompute(spectrumShader, K_STATIC_SPECTRUM, size >> 3, size >> 3, 1);
    }
    
    private void GenerateNoise()
    {
        spectrumShader.SetTexture(K_GAUSS_NOISE, P_GAUSS_NOISE, gaussNoise);
        spectrumShader.SetInt(P_SIZE, size);
        spectrumShader.Dispatch(K_GAUSS_NOISE, size >> 3, size >> 3, 1);
    }
    
    private int K_STATIC_SPECTRUM;
    private int K_GAUSS_NOISE;
    private int K_GENERATE_WAVE;
    private int K_MERGE_TEXTURES;
    
    private static readonly int PROP_REFLECTION_TEX = Shader.PropertyToID("_Reflection_Tex");
    private static readonly int PROP_DISPLACEMENT = Shader.PropertyToID("_Displacement");
    private static readonly int PROP_DERIVATIVES = Shader.PropertyToID("_Derivatives");
    private static readonly int PROP_TURBULENCE = Shader.PropertyToID("_Turbulence");
    private static readonly int PROP_LENGTH = Shader.PropertyToID("_Length");
    
    private static readonly int P_GAUSS_NOISE = Shader.PropertyToID("GaussNoise");
    private static readonly int P_H0 = Shader.PropertyToID("H0");
    private static readonly int P_LAMBDA = Shader.PropertyToID("Lambda");
    private static readonly int P_FREQUENCY = Shader.PropertyToID("Frequency");
    private static readonly int P_DY_DXZ_SPEC = Shader.PropertyToID("DyDxzSpec");
    private static readonly int P_DX_DZ_SPEC = Shader.PropertyToID("DxDzSpec");
    private static readonly int P_DYZ_DZZ_SPEC = Shader.PropertyToID("DyzDzzSpec");
    private static readonly int P_DYX_DXX_SPEC = Shader.PropertyToID("DyxDxxSpec");
    private static readonly int P_DISPLACEMENT = Shader.PropertyToID("Displacement");
    private static readonly int P_DERIVATIVES = Shader.PropertyToID("Derivatives");
    private static readonly int P_TURBULENCE = Shader.PropertyToID("Turbulence");

    private static readonly int P_SIZE = Shader.PropertyToID("Size");
    private static readonly int P_WIND_SPEED = Shader.PropertyToID("WindSpeed");
    private static readonly int P_DIRECTION = Shader.PropertyToID("WindDirection");
    private static readonly int P_GRAVITY = Shader.PropertyToID("Gravity");
    private static readonly int P_SQRT_AMPLITUDE = Shader.PropertyToID("Amplitude");
    private static readonly int P_FETCH = Shader.PropertyToID("Fetch");
    private static readonly int P_DEPTH = Shader.PropertyToID("Depth");
    private static readonly int P_LENGTH = Shader.PropertyToID("Length");
}
