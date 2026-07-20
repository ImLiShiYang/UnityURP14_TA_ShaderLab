Shader "MyTA/Volumetric/SimpleRayMarchFogV2"
{
    Properties
    {
        [Header(Fog)]
        _FogColor ("Fog Color", Color) = (0.65, 0.75, 0.85, 1)
        _FogDensity ("Fog Density", Range(0, 0.2)) = 0.035
        _FogIntensity ("Fog Intensity", Range(0, 5)) = 1
        _Extinction ("Extinction", Range(0, 3)) = 1
        _MaxDistance ("Max Distance", Float) = 60
        _SampleCount ("Sample Count", Range(4, 64)) = 16
        _FogStartDistance ("Fog Start Distance", Float) = 3

        [Header(Local Fog)]
        _UseLocalFog ("Use Local Fog", Float) = 0
        _LocalFogCenter ("Local Fog Center", Vector) = (0, 0, 0, 0)
        _LocalFogSize ("Local Fog Size", Vector) = (20, 5, 20, 0)
        _LocalFogSoftness ("Local Fog Softness", Float) = 2

        [Header(Noise)]
        _NoiseScale ("Noise Scale", Float) = 0.08
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.5
        _NoiseSpeed ("Noise Speed", Float) = 0.1
        _NoiseDirection ("Noise Direction XZ", Vector) = (1, 0, 0, 0)

        [Header(Height Fog)]
        _UseHeightFog ("Use Height Fog", Float) = 1
        _FogBaseHeight ("Fog Base Height", Float) = 0
        _HeightFalloff ("Height Falloff", Range(0, 2)) = 0.25

        [Header(Light)]
        _LightScatter ("Light Scatter", Range(0, 3)) = 1
        _Anisotropy ("Anisotropy", Range(-0.85, 0.85)) = 0.45
        _VolumeLightIntensity ("Volume Light Intensity", Range(0, 5)) = 1.5
        _AmbientFog ("Ambient Fog", Range(0, 1)) = 0.15
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 1
        _SideScatter ("Side Scatter", Range(0, 1)) = 0.08

        [Header(V2 Filtering)]
        _BlurDepthFalloff ("Blur Depth Falloff", Float) = 0.5
        _UpsampleDepthThreshold ("Upsample Depth Threshold", Range(0.01, 0.5)) = 0.1
        
        [Header(Additional Lights)]
        _AdditionalLightIntensity ("Additional Light Intensity", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE

        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
        #pragma multi_compile_fragment _ _SHADOWS_SOFT
        
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
        #pragma multi_compile_fragment _ _LIGHT_COOKIES

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        TEXTURE2D_X(_DownsampledFogDepthTexture);
        SAMPLER(sampler_DownsampledFogDepthTexture);
        float4 _DownsampledFogDepthTexture_TexelSize;

        TEXTURE2D_X(_VolumeFogTexture);
        SAMPLER(sampler_VolumeFogTexture);
        float4 _VolumeFogTexture_TexelSize;

        float4 _CameraDepthTexture_TexelSize;
        float4 _BlitTexture_TexelSize;
        
        

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
            float _Anisotropy;
            float _VolumeLightIntensity;
            float _AmbientFog;
            float _ShadowStrength;
            float _SideScatter;

            float _BlurDepthFalloff;
            float _UpsampleDepthThreshold;
        
            float _AdditionalLightIntensity;
        CBUFFER_END
        
        #define MAX_VOLUMETRIC_ADDITIONAL_LIGHTS 32

        int _VolumetricAdditionalLightCount;
        float _VolumetricAdditionalAnisotropy[MAX_VOLUMETRIC_ADDITIONAL_LIGHTS];
        float _VolumetricAdditionalScattering[MAX_VOLUMETRIC_ADDITIONAL_LIGHTS];
        float _VolumetricAdditionalRadius[MAX_VOLUMETRIC_ADDITIONAL_LIGHTS];

        float LinearEyeDepthConsiderProjection(float rawDepth)
        {
            float perspectiveDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

            #if UNITY_REVERSED_Z
                float orthoDepth = lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
            #else
                float orthoDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
            #endif

            return lerp(perspectiveDepth, orthoDepth, unity_OrthoParams.w);
        }

        float SampleDownsampledFogDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_DownsampledFogDepthTexture, sampler_PointClamp, uv).r;
        }

        float Hash31(float3 p)
        {
            p = frac(p * float3(123.34, 456.21, 789.12));
            p += dot(p, p + 45.32);
            return frac((p.x + p.y) * p.z);
        }

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

        // Cornette-Shanks 相函数：根据光照方向和观察方向的夹角，计算雾的方向性散射强度。
        // g 控制散射方向偏向：g > 0 偏前向散射，g = 0 接近均匀散射，g < 0 偏后向散射。
        // cosTheta 是视线方向和光照方向的夹角余弦值。
        float CornetteShanksPhase(float g, float cosTheta)
        {
            const float MY_PI = 3.14159265;
            const float MY_FOUR_PI = 12.5663706;

            // 限制参数范围，避免 g 太极端导致体积光爆亮或数值不稳定。
            g = clamp(g, -0.85, 0.85);
            cosTheta = clamp(cosTheta, -1.0, 1.0);

            float g2 = g * g;

            // 相函数公式中的分母，max 用来防止分母过小。
            float denom = pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.0001), 1.5);

            // Cornette-Shanks 相函数主体。
            // phase 越大，说明当前角度下光被雾散射到摄像机的强度越高。
            float phase = (3.0 / (8.0 * MY_PI))
                        * ((1.0 - g2) / (2.0 + g2))
                        * ((1.0 + cosTheta * cosTheta) / denom);

            // 乘 4π，把标准相函数结果放大到更适合游戏调参的范围。
            return phase * MY_FOUR_PI;
        }
        
        float3 GetAdditionalLightsFog(float3 positionWS, float3 rayDir)
        {
            float3 result = 0.0;

        #if defined(_ADDITIONAL_LIGHTS)
            uint lightCount = min((uint)_VolumetricAdditionalLightCount, (uint)GetAdditionalLightsCount());
            lightCount = min(lightCount, (uint)MAX_VOLUMETRIC_ADDITIONAL_LIGHTS);

            for (uint lightIndex = 0; lightIndex < lightCount; lightIndex++)
            {
                float scattering = _VolumetricAdditionalScattering[lightIndex];

                if (scattering <= 0.0001)
                    continue;

                Light light = GetAdditionalLight(lightIndex, positionWS);

                float cosTheta = dot(rayDir, light.direction);
                float phase = CornetteShanksPhase(_VolumetricAdditionalAnisotropy[lightIndex], cosTheta);

                result += light.color
                        * light.distanceAttenuation
                        * light.shadowAttenuation
                        * phase
                        * scattering
                        * _AdditionalLightIntensity;
            }
        #endif

            return result;
        }
        
        float GetLocalBoxMask(float3 worldPos)
        {
            float3 halfSize = max(_LocalFogSize.xyz * 0.5, 0.001);
            float3 localPos = abs(worldPos - _LocalFogCenter.xyz);
            float3 edgeDistance = halfSize - localPos;
            float minEdgeDistance = min(edgeDistance.x, min(edgeDistance.y, edgeDistance.z));
            float inside = step(0.0, minEdgeDistance);
            float edgeFade = saturate(minEdgeDistance / max(0.001, _LocalFogSoftness));
            return inside * edgeFade;
        }

        float GetFogDensity(float3 worldPos)
        {
            float density = max(0.0, _FogDensity);

            if (_UseHeightFog > 0.5)
            {
                float heightAboveBase = max(0.0, worldPos.y - _FogBaseHeight);
                density *= exp(-heightAboveBase * _HeightFalloff);
            }

            float3 noisePos = worldPos * _NoiseScale;
            float2 noiseDir = _NoiseDirection.xz;

            if (dot(noiseDir, noiseDir) < 0.0001)
                noiseDir = float2(1.0, 0.0);
            else
                noiseDir = normalize(noiseDir);

            noisePos.xz += noiseDir * _Time.y * _NoiseSpeed;

            float noise = ValueNoise3D(noisePos);
            density *= lerp(1.0, noise, _NoiseStrength);

            if (_UseLocalFog > 0.5)
                density *= GetLocalBoxMask(worldPos);

            return density;
        }

        float4 RayMarchFogBuffer(float3 rayOrigin, float3 rayDir, float marchDistance, float jitter)
        {
            float startDistance = max(0.0, _FogStartDistance);
            float effectiveDistance = max(0.0, marchDistance - startDistance);

            int sampleCount = (int)clamp(_SampleCount, 4.0, 64.0);
            float stepSize = effectiveDistance / sampleCount;

            float transmittance = 1.0;
            float3 fogColorAccum = 0.0;

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

                float cosTheta = dot(rayDir, mainLight.direction);
                float phase = CornetteShanksPhase(_Anisotropy, cosTheta);

                float scatterAmount = _SideScatter + phase * _LightScatter;
                float shadowAttenuation = lerp(1.0, mainLight.shadowAttenuation, saturate(_ShadowStrength));

                float3 ambientFog = _FogColor.rgb * _FogIntensity * _AmbientFog;
                float3 directFog = _FogColor.rgb * mainLight.color * _FogIntensity * scatterAmount * _VolumeLightIntensity * shadowAttenuation;
                
                float3 additionalFog = _FogColor.rgb * _FogIntensity * GetAdditionalLightsFog(samplePosWS, rayDir);
                float3 fogLighting = ambientFog + directFog + additionalFog;

                fogColorAccum += transmittance * stepAlpha * fogLighting;
                transmittance *= exp(-stepDensity * max(0.001, _Extinction));

                if (transmittance < 0.01)
                    break;
            }

            return float4(fogColorAccum, transmittance);
        }

        // 降采样深度 Pass。
        // 从当前 uv 附近采样 4 个深度点，选出离摄像机最近的深度写入低分辨率深度图。
        // 这样可以减少体积雾在物体边缘穿帮、漏雾的问题。
        float4 DownsampleDepthFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;

            // 深度图中半个 texel 的 uv 偏移，用来在当前像素附近采样 4 个点。
            float2 texel = _CameraDepthTexture_TexelSize.xy * 0.5;

            // 当前 uv 周围的 4 个采样位置。
            float2 uvs[4] =
            {
                uv + float2(-texel.x, -texel.y),
                uv + float2( texel.x, -texel.y),
                uv + float2(-texel.x,  texel.y),
                uv + float2( texel.x,  texel.y)
            };

            // 先用第一个采样点作为当前最近深度。
            float bestRawDepth = SampleSceneDepth(uvs[0]);
            float bestEyeDepth = LinearEyeDepthConsiderProjection(bestRawDepth);

            // 遍历剩下 3 个采样点，找出离摄像机最近的那个深度。
            UNITY_UNROLL
            for (int i = 1; i < 4; i++)
            {
                float rawDepth = SampleSceneDepth(uvs[i]);
                float eyeDepth = LinearEyeDepthConsiderProjection(rawDepth);

                // eyeDepth 越小表示越靠近摄像机。
                if (eyeDepth < bestEyeDepth)
                {
                    bestEyeDepth = eyeDepth;
                    bestRawDepth = rawDepth;
                }
            }

            // 保存最近点对应的原始深度，后续体积雾 Pass 会用它重建世界坐标。
            return float4(bestRawDepth, 0, 0, 0);
        }

        float4 VolumeFogRenderFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float rawDepth = SampleDownsampledFogDepth(uv);

            bool hasSceneDepth = true;

            #if UNITY_REVERSED_Z
                hasSceneDepth = rawDepth > 0.0001;
            #else
                hasSceneDepth = rawDepth < 0.9999;
            #endif

            // 天空像素没有场景深度，但是它们仍然需要一条射线方向，
            // 这样远处的雾和太阳光束才能显示在天空背景上。
            float rayDepth = rawDepth;
            if (!hasSceneDepth)
            {
                #if UNITY_REVERSED_Z
                    rayDepth = 0.0001;
                #else
                    rayDepth = 0.9999;
                #endif
            }

            float3 worldPos = GetWorldPositionFromDepth(uv, rayDepth);
            float3 cameraPosWS = GetCameraPositionWS();
            float3 cameraToPixel = worldPos - cameraPosWS;

            float sceneDistance = length(cameraToPixel);
            float3 rayDir = sceneDistance > 0.0001 ? cameraToPixel / sceneDistance : float3(0, 0, 1);

            sceneDistance = hasSceneDepth ? min(sceneDistance, _MaxDistance) : _MaxDistance;

            float jitter = InterleavedGradientNoise(input.positionCS.xy);
            return RayMarchFogBuffer(cameraPosWS, rayDir, sceneDistance, jitter);
        }

        float4 DepthAwareBlur(float2 uv, float2 dir)
        {
            static const float weights[5] = { 0.2026, 0.1790, 0.1240, 0.0672, 0.0285 };

            float4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            float centerDepth = SampleDownsampledFogDepth(uv);
            float centerEyeDepth = LinearEyeDepthConsiderProjection(centerDepth);

            float3 rgb = center.rgb * weights[0];
            float totalWeight = weights[0];

            float2 stepUV = _BlitTexture_TexelSize.xy * dir;

            UNITY_UNROLL
            for (int i = 1; i <= 4; i++)
            {
                float2 uvA = uv - stepUV * i;
                float2 uvB = uv + stepUV * i;

                float depthA = SampleDownsampledFogDepth(uvA);
                float depthB = SampleDownsampledFogDepth(uvB);

                float eyeA = LinearEyeDepthConsiderProjection(depthA);
                float eyeB = LinearEyeDepthConsiderProjection(depthB);

                float wa = weights[i] * exp(-pow(abs(centerEyeDepth - eyeA) * _BlurDepthFalloff, 2.0));
                float wb = weights[i] * exp(-pow(abs(centerEyeDepth - eyeB) * _BlurDepthFalloff, 2.0));

                rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uvA).rgb * wa;
                rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uvB).rgb * wb;

                totalWeight += wa + wb;
            }

            return float4(rgb / max(totalWeight, 0.0001), center.a);
        }

        float4 HorizontalBlurFrag(Varyings input) : SV_Target
        {
            return DepthAwareBlur(input.texcoord, float2(1, 0));
        }

        float4 VerticalBlurFrag(Varyings input) : SV_Target
        {
            return DepthAwareBlur(input.texcoord, float2(0, 1));
        }

        float4 DepthAwareUpsampleFog(float2 uv)
        {
            float fullDepth = SampleSceneDepth(uv);
            float fullEyeDepth = LinearEyeDepthConsiderProjection(fullDepth);
            float threshold = max(0.01, fullEyeDepth * _UpsampleDepthThreshold);

            float2 halfTexel = _DownsampledFogDepthTexture_TexelSize.xy * 0.5;
            float2 uvs[4] =
            {
                uv + float2(-halfTexel.x, -halfTexel.y),
                uv + float2( halfTexel.x, -halfTexel.y),
                uv + float2(-halfTexel.x,  halfTexel.y),
                uv + float2( halfTexel.x,  halfTexel.y)
            };

            float nearestDiff = 1e20;
            float2 nearestUv = uv;
            int validCount = 0;

            UNITY_UNROLL
            for (int i = 0; i < 4; i++)
            {
                float d = SampleDownsampledFogDepth(uvs[i]);
                float eye = LinearEyeDepthConsiderProjection(d);
                float diff = abs(fullEyeDepth - eye);

                if (diff < threshold)
                    validCount++;

                if (diff < nearestDiff)
                {
                    nearestDiff = diff;
                    nearestUv = uvs[i];
                }
            }

            if (validCount == 4)
                return SAMPLE_TEXTURE2D_X(_VolumeFogTexture, sampler_LinearClamp, uv);

            return SAMPLE_TEXTURE2D_X(_VolumeFogTexture, sampler_PointClamp, nearestUv);
        }

        float4 CompositeFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;

            float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float4 fog = DepthAwareUpsampleFog(uv);

            float3 finalColor = sceneColor.rgb * fog.a + fog.rgb;
            return float4(finalColor, sceneColor.a);
        }

        ENDHLSL

        Pass
        {
            Name "DownsampleDepth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DownsampleDepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeFogRender"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VolumeFogRenderFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeFogHorizontalBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment HorizontalBlurFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeFogVerticalBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VerticalBlurFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeFogComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag
            ENDHLSL
        }
    }
}
