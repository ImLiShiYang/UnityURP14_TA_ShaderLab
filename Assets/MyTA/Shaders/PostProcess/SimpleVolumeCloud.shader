Shader "MyTA/Volumetric/SimpleVolumeCloud"
{
    Properties
    {
        [Header(Cloud Shape)]
        _CloudBoundsMin ("云范围最小点", Vector) = (-20, 20, -20, 0)
        _CloudBoundsMax ("云范围最大点", Vector) = (20, 60, 20, 0)
        _CloudScale ("云噪声缩放", Range(0.01, 1)) = 0.08
        _CloudCoverage ("云裁剪量", Range(0, 1)) = 0.45
        _CloudDensity ("云密度", Range(0, 5)) = 1.2
        _EdgeFalloff ("云边缘柔和度", Range(0.01, 1)) = 0.35
        _PhaseG ("云前向散射强度", Range(-0.5, 0.8)) = 0.35

        [Header(Lighting)]
        _CloudColor ("云颜色", Color) = (1, 1, 1, 1)
        _CloudAmbient ("云环境光", Range(0, 1)) = 0.25
        _SunIntensity ("太阳光强度", Range(0, 5)) = 1.5
        _ExtinctionCoeff ("消光系数", Range(0.1, 10)) = 1.0

        [Header(Marching)]
        _StepCount ("主步进次数", Range(8, 96)) = 32
        _LightStepCount ("光照步进次数", Range(1, 12)) = 4
        _StepJitter ("步进抖动", Range(0, 1)) = 0.5

        [Header(Animation)]
        _WindDirection ("云移动方向", Vector) = (1, 0, 0, 0)
        _WindSpeed ("云移动速度", Range(0, 10)) = 1
        
        [Header(Upsample)]
        _BlurDepthFalloff ("模糊深度保护强度", Range(0, 10)) = 2.0
        _UpsampleDepthThreshold ("上采样深度阈值", Range(0.0001, 0.1)) = 0.01
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

        TEXTURE2D_X(_DownsampledCloudDepthTexture);
        SAMPLER(sampler_DownsampledCloudDepthTexture);
        float4 _DownsampledCloudDepthTexture_TexelSize;

        TEXTURE2D_X(_VolumeCloudTexture);
        SAMPLER(sampler_VolumeCloudTexture);
        float4 _VolumeCloudTexture_TexelSize;

        float4 _CameraDepthTexture_TexelSize;
        float4 _BlitTexture_TexelSize;
        
        

        CBUFFER_START(UnityPerMaterial)
            float _BlurDepthFalloff;
            float _UpsampleDepthThreshold;
        
            float4 _CloudBoundsMin;
            float4 _CloudBoundsMax;

            float _CloudScale;
            float _CloudCoverage;
            float _CloudDensity;
            float _EdgeFalloff;
            float _PhaseG;

            float4 _CloudColor;
            float _CloudAmbient;
            float _SunIntensity;
            float _ExtinctionCoeff;

            float _StepCount;
            float _LightStepCount;
            float _StepJitter;

            float4 _WindDirection;
            float _WindSpeed;
            
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

        float SampleDownsampledCloudDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_DownsampledCloudDepthTexture, sampler_PointClamp, uv).r;
        }

        
        
        float Hash(float n)
        {
            return frac(sin(n) * 753.5453123);
        }

        float Noise3D(float3 x)
        {
            float3 p = floor(x);
            float3 f = frac(x);

            f = f * f * (3.0 - 2.0 * f);

            float n = p.x + p.y * 157.0 + 113.0 * p.z;

            float res =
                lerp(
                    lerp(
                        lerp(Hash(n + 0.0f), Hash(n + 1.0f), f.x),lerp(Hash(n + 157.0f), Hash(n + 158.0f), f.x),f.y),
                    lerp(
                        lerp(Hash(n + 113.0f), Hash(n + 114.0f), f.x),lerp(Hash(n + 270.0f), Hash(n + 271.0f), f.x),f.y),
                    f.z
                );

            return res;
        }

        float FBM(float3 p)
        {
            float f = 0.5 * Noise3D(p);
            p *= 2.02;

            f += 0.25 * Noise3D(p);
            p *= 2.03;

            f += 0.125 * Noise3D(p);

            return f;
        }
        
        //与盒子求交
        bool IntersectBox(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax, out float tNear, out float tFar)
        {
            float3 invDir = 1.0 / rayDir;

            float3 t0 = (boxMin - rayOrigin) * invDir;
            float3 t1 = (boxMax - rayOrigin) * invDir;

            float3 tMin = min(t0, t1);
            float3 tMax = max(t0, t1);

            tNear = max(max(tMin.x, tMin.y), tMin.z);
            tFar = min(min(tMax.x, tMax.y), tMax.z);

            return tFar >= max(tNear, 0.0);
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
        
        float GetCloudHeightMask(float3 posWS)
        {
            float height01 = saturate((posWS.y - _CloudBoundsMin.y) / (_CloudBoundsMax.y - _CloudBoundsMin.y));

            // 底部渐入，顶部渐出。
            float bottomFade = smoothstep(0.0, 0.15, height01);
            float topFade = 1.0 - smoothstep(0.75, 1.0, height01);

            return bottomFade * topFade;
        }
        
        float GetCloudDensity(float3 posWS)
        {
            // 取出云盒子的世界空间范围。
            float3 boundsMin = _CloudBoundsMin.xyz;
            float3 boundsMax = _CloudBoundsMax.xyz;

            // 把当前世界坐标转换到云盒子内部的 0~1 坐标。
            // 盒子最小点是 0，最大点是 1。
            float3 local01 = saturate((posWS - boundsMin) / (boundsMax - boundsMin));

            // X 方向边缘淡出，避免云在盒子左右边界突然截断。
            float edgeX = smoothstep(0.0, _EdgeFalloff, local01.x) * 
                          (1.0 - smoothstep(1.0 - _EdgeFalloff, 1.0, local01.x));

            // Y 方向边缘淡出，控制云在底部和顶部逐渐消失。
            float edgeY = smoothstep(0.0, _EdgeFalloff, local01.y) * 
                          (1.0 - smoothstep(1.0 - _EdgeFalloff, 1.0, local01.y));

            // Z 方向边缘淡出，避免云在前后边界突然截断。
            float edgeZ = smoothstep(0.0, _EdgeFalloff, local01.z) * 
                          (1.0 - smoothstep(1.0 - _EdgeFalloff, 1.0, local01.z));

            // 三个方向的边缘遮罩相乘。
            // 只要靠近任意一个边缘，最终密度都会降低。
            float edgeMask = edgeX * edgeY * edgeZ;

            // 根据风向、时间和速度，让噪声坐标随时间移动。
            // 实际移动的是云的噪声形状，不是云盒子本身。
            float3 windOffset = _WindDirection.xyz * _Time.y * _WindSpeed;

            // 使用世界坐标采样 FBM 噪声，
            float noise = FBM(posWS * _CloudScale + windOffset);
            
            // 高频噪声用来侵蚀云边缘，让云洞和边缘更自然。
            float detailNoise = FBM(posWS * _CloudScale * 3.0 + windOffset * 1.7);
            noise -= detailNoise * 0.25;

            // 用云量参数裁剪噪声，再用云密度放大结果。
            // _CloudCoverage 越小，云越多；越大，孔洞越多
            // _CloudDensity 越大，云越厚。
            float density = saturate((noise - _CloudCoverage) * _CloudDensity);

            float heightMask = GetCloudHeightMask(posWS);
            
            // 最终密度 = 噪声密度 * 边缘淡出。
            return density * edgeMask *heightMask;
        }
        
        float CloudInterleavedGradientNoise(float2 pixelPos)
        {
            return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
        }
        
        float HenyeyGreensteinPhase(float g, float cosTheta)
        {
            float g2 = g * g;
            float denom = pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.0001), 1.5);
            return (1.0 - g2) / max(0.0001, denom);
        }
        
        float GetCloudLightTransmittance(float3 posWS, float3 lightDir)
        {
            float3 rayStart = posWS + lightDir * 0.1;

            float tNear;
            float tFar;

            if (!IntersectBox(rayStart, lightDir, _CloudBoundsMin.xyz, _CloudBoundsMax.xyz, tNear, tFar))
                return 1.0;

            tNear = max(tNear, 0.0);

            float lightDistance = max(0.0, tFar - tNear);

            int lightStepCount = (int)clamp(_LightStepCount, 1.0, 12.0);
            float lightStepSize = lightDistance / max(1, lightStepCount);

            float transmittance = 1.0;

            for (int i = 0; i < 12; i++)
            {
                if (i >= lightStepCount)
                    break;

                float lightT = tNear + (i + 0.5) * lightStepSize;
                float3 samplePosWS = rayStart + lightDir * lightT;

                float density = GetCloudDensity(samplePosWS);

                transmittance *= exp(-density * _ExtinctionCoeff * lightStepSize);

                if (transmittance < 0.05)
                    break;
            }

            return transmittance;
        }

        float4 RayMarchCloud(float2 uv)
        {
            float rawDepth = SampleDownsampledCloudDepth(uv);

            bool hasSceneDepth = true;

            #if UNITY_REVERSED_Z
                hasSceneDepth = rawDepth > 0.0001;
            #else
                hasSceneDepth = rawDepth < 0.9999;
            #endif

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

            if (!hasSceneDepth)
                sceneDistance = 1e20;

            // tNear = 射线进入云盒子的距离
            // tFar  = 射线离开云盒子的距离
            float tNear;
            float tFar;

            if (!IntersectBox(cameraPosWS, rayDir, _CloudBoundsMin.xyz, _CloudBoundsMax.xyz, tNear, tFar))
            {
                // 当前像素射线没有穿过云盒子。
                // a = 1 表示完全透过，Composite 时不影响原画面。
                return float4(0, 0, 0, 1);
            }

            tNear = max(tNear, 0.0);
            
            tFar = min(tFar, sceneDistance);

            if (tFar <= tNear)
                return float4(0, 0, 0, 1);

            int stepCount = (int)clamp(_StepCount, 8.0, 96.0);

            float marchDistance = tFar - tNear;
            float stepSize = marchDistance / max(1, stepCount);

            float jitter = CloudInterleavedGradientNoise(uv * _ScreenParams.xy);
            jitter = lerp(0.5, jitter, saturate(_StepJitter));

            Light mainLight = GetMainLight();

            float3 lightDir = normalize(mainLight.direction);
            float3 sunColor = mainLight.color * _SunIntensity;

            float transmittance = 1.0;
            float3 cloudColorAccum = 0.0;

            for (int i = 0; i < 96; i++)
            {
                if (i >= stepCount)
                    break;

                float t = tNear + (i + jitter) * stepSize;
                float3 samplePosWS = cameraPosWS + rayDir * t;

                float density = GetCloudDensity(samplePosWS);

                if (density > 0.001)
                {
                    float opticalDepth = density * _ExtinctionCoeff * stepSize;
                    float stepAlpha = 1.0 - exp(-opticalDepth);

                    float lightTransmittance = GetCloudLightTransmittance(samplePosWS, lightDir);
                    
                    float cosTheta = dot(rayDir, lightDir);
                    float phase = HenyeyGreensteinPhase(_PhaseG, cosTheta);
                    
                    float3 ambientLighting = _CloudColor.rgb * _CloudAmbient;
                    float3 directLighting = _CloudColor.rgb * sunColor * lightTransmittance*phase;
                                        

                    float3 sampleLighting = ambientLighting + directLighting;

                    cloudColorAccum += transmittance * stepAlpha * sampleLighting;

                    transmittance *= exp(-opticalDepth);

                    if (transmittance < 0.01)
                        break;
                }
            }

            // 注意：这里 a 存的是 transmittance，不是 alpha。
            // 因为你当前 Composite 是 sceneColor.rgb * fog.a + fog.rgb。
            return float4(cloudColorAccum, transmittance);
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

        float4 VolumeCloudRenderFrag(Varyings input) : SV_Target
        {
            return RayMarchCloud(input.texcoord);
        }

        float4 DepthAwareBlur(float2 uv, float2 dir)
        {
            static const float weights[5] = { 0.2026, 0.1790, 0.1240, 0.0672, 0.0285 };

            float4 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            float centerDepth = SampleDownsampledCloudDepth(uv);
            float centerEyeDepth = LinearEyeDepthConsiderProjection(centerDepth);

            float3 rgb = center.rgb * weights[0];
            float totalWeight = weights[0];

            float2 stepUV = _BlitTexture_TexelSize.xy * dir;

            UNITY_UNROLL
            for (int i = 1; i <= 4; i++)
            {
                float2 uvA = uv - stepUV * i;
                float2 uvB = uv + stepUV * i;

                float depthA = SampleDownsampledCloudDepth(uvA);
                float depthB = SampleDownsampledCloudDepth(uvB);

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

        float4 DepthAwareUpsampleCloud(float2 uv)
        {
            float fullDepth = SampleSceneDepth(uv);
            float fullEyeDepth = LinearEyeDepthConsiderProjection(fullDepth);
            float threshold = max(0.01, fullEyeDepth * _UpsampleDepthThreshold);

            float2 halfTexel = _DownsampledCloudDepthTexture_TexelSize.xy * 0.5;
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
                float d = SampleDownsampledCloudDepth(uvs[i]);
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
                return SAMPLE_TEXTURE2D_X(_VolumeCloudTexture, sampler_LinearClamp, uv);

            return SAMPLE_TEXTURE2D_X(_VolumeCloudTexture, sampler_PointClamp, nearestUv);
        }

        float4 CompositeFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;

            float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float4 cloud = DepthAwareUpsampleCloud(uv);

            float3 finalColor = sceneColor.rgb * cloud.a + cloud.rgb;
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
            Name "VolumeCloudRender"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VolumeCloudRenderFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeCloudHorizontalBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment HorizontalBlurFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeCloudVerticalBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VerticalBlurFrag
            ENDHLSL
        }

        Pass
        {
            Name "VolumeCloudComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag
            ENDHLSL
        }
    }
}
