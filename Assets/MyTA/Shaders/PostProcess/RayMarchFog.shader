Shader "MyTA/Volumetric/SimpleRayMarchFog"
{
    Properties
    {
        [Header(Fog)]
        _FogColor ("Fog Color", Color) = (0.65, 0.75, 0.85, 1)
        _FogDensity ("Fog Density", Range(0, 0.2)) = 0.035
        _FogIntensity ("Fog Intensity", Range(0, 5)) = 1.0
        _Extinction ("Extinction", Range(0, 3)) = 1.0
        _MaxDistance ("Max Distance", Float) = 60
        _SampleCount ("Sample Count", Range(4, 64)) = 16
        _FogStartDistance ("Fog Start Distance", Float) = 3
        
        [Header(Local Fog)]
        _UseLocalFog ("使用局部雾", Float) = 0
        _LocalFogCenter ("局部雾中心", Vector) = (0, 0, 0, 0)
        _LocalFogSize ("局部雾尺寸", Vector) = (20, 5, 20, 0)
        _LocalFogSoftness ("局部雾柔软度", Float) = 2
        
        [Header(Noise)]
        _NoiseScale ("Noise Scale", Float) = 0.08
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.5
        _NoiseSpeed ("噪声速度", Float) = 0.1
        _NoiseDirection ("噪声方向 XZ", Vector) = (1, 0, 0, 0)

        [Header(Height Fog)]
        _UseHeightFog ("Use Height Fog", Float) = 1
        _FogBaseHeight ("Fog Base Height", Float) = 0
        _HeightFalloff ("Height Falloff", Range(0, 2)) = 0.25

        [Header(Light)]
        _LightScatter ("LightScatter", Range(0, 3)) = 1.0
        _LightPower ("LightPower", Range(1, 16)) = 6
        _VolumeLightIntensity ("VolumeLightIntensity", Range(0, 5)) = 1.5
        _AmbientFog ("AmbientFog", Range(0, 1)) = 0.35
        _ShadowStrength ("ShadowStrength", Range(0, 1)) = 1.0
        _SideScatter ("侧向散射保底", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Simple Volume Fog"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _FogDensity;
                float _FogIntensity;
                float _Extinction;
                float _MaxDistance;
                float _SampleCount;
                float _FogStartDistance;
            
                float _UseLocalFog;
                float4 _LocalFogCenter;
                float4 _LocalFogSize;
                float _LocalFogSoftness;
            
                float _NoiseScale;
                float _NoiseStrength;
                float _NoiseSpeed;
                float4 _NoiseDirection;

                float _UseHeightFog;
                float _FogBaseHeight;
                float _HeightFalloff;

                float _LightScatter;
                float _LightPower;
                float _VolumeLightIntensity;
                float _AmbientFog;
                float _ShadowStrength;
                float _SideScatter;
            CBUFFER_END


            
            float Hash31(float3 p)
            {
                p = frac(p * float3(123.34, 456.21, 789.12));
                p += dot(p, p + 45.32);
                return frac((p.x + p.y) * p.z);
            }

            //3D噪音扰动函数
            // 1. 找到 p 所在的 3D 格子
            // 2. 给这个格子的 8 个角各生成一个随机值
            // 3. 根据 p 在格子内部的位置，在 8 个角之间做三线性插值
            // 4. 得到一个平滑的 0~1 噪声值
            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));

                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float3 u = f * f * (3.0 - 2.0 * f);

                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);

                float nxy0 = lerp(nx00, nx10, u.y);
                float nxy1 = lerp(nx01, nx11, u.y);

                return lerp(nxy0, nxy1, u.z);
            }
            
            // worldPos 在 box 内部：返回 0~1
            // worldPos 在 box 外部：返回 0
            // 靠近 box 边缘：逐渐变淡
            // 靠近 box 中心：雾更完整
            float GetLocalBoxMask(float3 worldPos)
            {
                float3 halfSize = max(_LocalFogSize.xyz * 0.5, 0.001);
                float3 localPos = abs(worldPos - _LocalFogCenter.xyz);

                float3 edgeDistance = halfSize - localPos;
                float minEdgeDistance = min(edgeDistance.x, min(edgeDistance.y, edgeDistance.z));

                float inside = step(0.0, minEdgeDistance);

                float softness = max(0.001, _LocalFogSoftness);
                float edgeFade = saturate(minEdgeDistance / softness);

                return inside * edgeFade;
            }
            
            float InterleavedGradientNoise(float2 pixelPos)
            {
                return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
            }
            
            float3 GetWorldPositionFromDepth(float2 uv, float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    float depth = rawDepth;
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
            }

            Light GetMainLightWithShadow(float3 positionWS)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                return GetMainLight(shadowCoord);
            #else
                return GetMainLight();
            #endif
            }
            
            float GetFogDensity(float3 worldPos)
            {
                float density = max(0.0, _FogDensity);

                if (_UseHeightFog > 0.5)
                {
                    float heightAboveBase = max(0.0, worldPos.y - _FogBaseHeight);
                    float heightFactor = exp(-heightAboveBase * _HeightFalloff);
                    density *= heightFactor;
                }
                
                float3 noisePos = worldPos * _NoiseScale;

                float2 noiseDir = _NoiseDirection.xy;

                if (dot(noiseDir, noiseDir) < 0.0001)
                {
                    noiseDir = float2(1.0, 0.0);
                }
                else
                {
                    noiseDir = normalize(noiseDir);
                }

                // 让噪声沿世界 XZ 平面流动，但噪声本身是 3D 的
                noisePos.xz += noiseDir * _Time.y * _NoiseSpeed;

                float noise = ValueNoise3D(noisePos);

                // 先用温和写法，只让雾部分变淡，不让局部突然变得特别浓
                float noiseFactor = lerp(1.0, noise, _NoiseStrength);

                density *= noiseFactor;
                
                if (_UseLocalFog > 0.5)
                {
                    float localMask = GetLocalBoxMask(worldPos);
                    density *= localMask;
                }
                
                return density;
            }

            float3 RayMarchFog(float3 sceneColor, float3 rayOrigin, float3 rayDir, float marchDistance, float jitter)
            {
                float startDistance = max(0.0, _FogStartDistance);
                float effectiveDistance = max(0.0, marchDistance - startDistance);

                int sampleCount = (int)clamp(_SampleCount, 4.0, 64.0);
                float stepSize = effectiveDistance / sampleCount;

                float transmittance = 1.0;
                float3 fogColorAccum = 0.0;

                // Light mainLight = GetMainLight();

                for (int i = 0; i < 64; i++)
                {
                    if (i >= sampleCount)
                        break;

                    float dist = startDistance + (i + jitter) * stepSize;
                    float3 samplePosWS = rayOrigin + rayDir * dist;

                    float density = GetFogDensity(samplePosWS);

                    float stepDensity = density * stepSize;
                    float stepAlpha = 1.0 - exp(-stepDensity);

                    Light mainLight = GetMainLightWithShadow(samplePosWS);

                    float lightFacing = dot(rayDir, mainLight.direction) * 0.5 + 0.5;
                    float forwardScatter = pow(saturate(lightFacing), max(1.0, _LightPower));
                    
                    // 让非正对太阳的角度也能看到一点体积光
                    float scatterAmount = _SideScatter + forwardScatter * _LightScatter;

                    float shadowAttenuation = lerp(1.0, mainLight.shadowAttenuation, saturate(_ShadowStrength));

                    float3 ambientFog = _FogColor.rgb * _FogIntensity * _AmbientFog;

                    float3 directFog = _FogColor.rgb
                                     * mainLight.color
                                     * _FogIntensity
                                     * scatterAmount
                                     * _VolumeLightIntensity
                                     * shadowAttenuation;

                    float3 fogLighting = ambientFog + directFog;

                    fogColorAccum += transmittance * stepAlpha * fogLighting;

                    transmittance *= exp(-stepDensity * max(0.001, _Extinction));

                    if (transmittance < 0.01)
                        break;
                }

                return sceneColor * transmittance + fogColorAccum;
            }

            // 对当前屏幕像素，先读取原画面颜色和深度；
            // 然后用深度还原世界坐标；
            // 再从相机到这个世界坐标生成一条射线；
            // 最后沿这条射线 ray march，计算这条视线中有多少雾，并把雾混合到原画面上。
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                
                // 如果当前像素没有有效场景深度，比如天空或远平面，就不要强行 ray march。否则很容易整片发白。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.0001)
                        return sceneColor;
                #else
                    if (rawDepth >= 0.9999)
                        return sceneColor;
                #endif
                
                float3 worldPos = GetWorldPositionFromDepth(uv, rawDepth);

                float3 cameraPosWS = GetCameraPositionWS();
                float3 cameraToPixel = worldPos - cameraPosWS;

                float sceneDistance = length(cameraToPixel);

                float3 rayDir = float3(0, 0, 1);

                if (sceneDistance > 0.0001)
                {
                    rayDir = cameraToPixel / sceneDistance;
                }

                sceneDistance = min(sceneDistance, _MaxDistance);

                float jitter = InterleavedGradientNoise(uv * _ScreenParams.xy);
                float3 finalColor = RayMarchFog(sceneColor.rgb,cameraPosWS,rayDir,sceneDistance,jitter);
                
                return half4(finalColor, sceneColor.a);
            }

            ENDHLSL
        }
    }
}