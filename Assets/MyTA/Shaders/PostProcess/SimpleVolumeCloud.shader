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
        
        _CloudBottomDarkness ("云底压暗", Range(0, 1)) = 0.35
        _PowderStrength ("边缘透光增强", Range(0, 2)) = 0.5
        _SilverLiningStrength ("银边光强度", Range(0, 5)) = 0.8
        _SilverLiningPower ("银边光集中度", Range(1, 32)) = 8

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

        [Header(Temporal)]
        _TemporalBlendFactor ("上一帧混合权重", Range(0, 0.95)) = 0.75
        _TemporalDepthThreshold ("历史深度拒绝阈值", Float) = 2.0
        _TemporalCloudChangeThreshold ("云变化拒绝阈值", Range(0.01, 2)) = 0.35
        _TemporalMinBlendOnCloudChange ("云变化时最小历史权重", Range(0, 0.5)) = 0.1
        
        [Header(Cloud Noise Texture)]
        [NoScaleOffset]_CloudShapeNoiseTex ("云形状噪声 3D", 3D) = "white" {}
        [NoScaleOffset]_CloudErosionNoiseTex ("云侵蚀噪声 3D", 3D) = "white" {}
        _ErosionNoiseScale ("侵蚀噪声缩放", Range(1, 20)) = 4.0
        _ErosionStrength ("侵蚀强度", Range(0, 1)) = 0.35
        
        [Header(Cloud Map)]
        [NoScaleOffset]_CloudMapTex ("云分布图 2D", 2D) = "white" {}
        _CloudMapScale ("云分布图缩放", Range(0.001, 0.05)) = 0.008
        _CloudMapOffset ("云分布图偏移", Vector) = (0, 0, 0, 0)
        _CloudMapStrength ("云分布图影响强度", Range(0, 1)) = 0.8
        
        [Header(Cloud Shadow)]
        _CloudShadowStrength ("云影强度", Range(0, 1)) = 0.25
        _CloudShadowThreshold ("云影阈值", Range(0, 1)) = 0.35
        _CloudShadowSoftness ("云影边缘柔和度", Range(0.001, 1)) = 0.25
        _CloudShadowHeight ("云影采样高度", Float) = 55
 
        [Header(Debug)]
        _DebugMode ("调试模式", Range(0, 4)) = 0
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

        TEXTURE2D_X(_CloudHistoryTexture);
        SAMPLER(sampler_CloudHistoryTexture);

        TEXTURE2D_X(_CloudHistoryDepthTexture);
        SAMPLER(sampler_CloudHistoryDepthTexture);

        float4 _CameraDepthTexture_TexelSize;
        float4 _BlitTexture_TexelSize;
        
        TEXTURE3D(_CloudShapeNoiseTex);
        SAMPLER(sampler_CloudShapeNoiseTex);

        TEXTURE3D(_CloudErosionNoiseTex);
        SAMPLER(sampler_CloudErosionNoiseTex);
        
        TEXTURE2D(_CloudMapTex);
        SAMPLER(sampler_CloudMapTex);

        CBUFFER_START(UnityPerMaterial)
            float _BlurDepthFalloff;
            float _UpsampleDepthThreshold;
            float _TemporalBlendFactor;
            float _TemporalDepthThreshold;
            float _TemporalCloudChangeThreshold;
            float _TemporalMinBlendOnCloudChange;
            float4x4 _PreviousViewProjectionMatrix;
        
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
        
            float _CloudBottomDarkness;
            float _PowderStrength;
            float _SilverLiningStrength;
            float _SilverLiningPower;

            float _StepCount;
            float _LightStepCount;
            float _StepJitter;

            float4 _WindDirection;
            float _WindSpeed;
            
            float _ErosionNoiseScale;
            float _ErosionStrength;
        
            float _CloudMapScale;
            float4 _CloudMapOffset;
            float _CloudMapStrength;
        
            float _CloudShadowStrength;
            float _CloudShadowThreshold;
            float _CloudShadowSoftness;
            float _CloudShadowHeight;
        
            float _DebugMode;
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
        
        float3 GetCloudWindDirection()
        {
            float3 windDir = _WindDirection.xyz;
            float windLength = max(length(windDir), 0.001);
            return windDir / windLength;
        }

        float3 GetCloudAnimatedPosition(float3 posWS, float speedMultiplier)
        {
            // 统一用“世界空间位移”控制云移动。
            // 这样 3D 噪声、Cloud Map、云影会保持同一套移动逻辑。
            float3 windDir = GetCloudWindDirection();
            return posWS + windDir * _Time.y * _WindSpeed * speedMultiplier;
        }

        float2 GetCloudMapUV(float3 posWS)
        {
            float3 animatedPosWS = GetCloudAnimatedPosition(posWS, 1.0);
            return animatedPosWS.xz * _CloudMapScale + _CloudMapOffset.xy;
        }

        float SampleCloudMapValue(float3 posWS)
        {
            float2 cloudMapUV = GetCloudMapUV(posWS);

            float cloudMap = SAMPLE_TEXTURE2D_LOD(
                _CloudMapTex,
                sampler_CloudMapTex,
                cloudMapUV,
                0
            ).r;

            return smoothstep(0.05, 0.95, cloudMap);
        }
        
        // 它生成一个云盒 XZ 范围遮罩，让云影只出现在云盒投影范围内，并且在边缘柔和淡出。
        float GetCloudBoundsXZMask(float3 posWS)
        {
            float2 boundsMinXZ = _CloudBoundsMin.xz;
            float2 boundsMaxXZ = _CloudBoundsMax.xz;

            float2 localXZ = (posWS.xz - boundsMinXZ) / max(boundsMaxXZ - boundsMinXZ, 0.001);

            float fadeX =
                smoothstep(0.0, _EdgeFalloff, localXZ.x) *
                (1.0 - smoothstep(1.0 - _EdgeFalloff, 1.0, localXZ.x));

            float fadeZ =
                smoothstep(0.0, _EdgeFalloff, localXZ.y) *
                (1.0 - smoothstep(1.0 - _EdgeFalloff, 1.0, localXZ.y));

            return fadeX * fadeZ;
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
            float3 shapeSamplePosWS = GetCloudAnimatedPosition(posWS, 1.0);
            float3 erosionSamplePosWS = GetCloudAnimatedPosition(posWS, 1.7);

            float3 shapeUV = shapeSamplePosWS * _CloudScale;
            float shapeNoise = SAMPLE_TEXTURE3D_LOD(
                _CloudShapeNoiseTex,
                sampler_CloudShapeNoiseTex,
                shapeUV,
                0
            ).r;

            float3 erosionUV = erosionSamplePosWS * _CloudScale * _ErosionNoiseScale;
            float erosionNoise = SAMPLE_TEXTURE3D_LOD(
                _CloudErosionNoiseTex,
                sampler_CloudErosionNoiseTex,
                erosionUV,
                0
            ).r;

            // 参照 jiaozi 项目的思路：大形状噪声决定云块，高频噪声侵蚀边缘。
            float noise = shapeNoise;
            noise -= (1.0 - erosionNoise) * _ErosionStrength;

            // 用云量参数裁剪噪声，再用云密度放大结果。
            // _CloudCoverage 越小，云越多；越大，孔洞越多。
            // _CloudDensity 越大，云越厚。
            float density = saturate((noise - _CloudCoverage) * _CloudDensity);

            float cloudMap = SampleCloudMapValue(posWS);

            // _CloudMapStrength = 0 时，不使用云分布图。
            // _CloudMapStrength = 1 时，完全由云分布图控制大范围分布。
            float cloudMapMask = lerp(1.0, cloudMap, _CloudMapStrength);

            float heightMask = GetCloudHeightMask(posWS);

            // 最终密度 = 3D 噪声密度 * 云分布图 * 边缘淡出 * 高度遮罩。
            return density * cloudMapMask * edgeMask * heightMask;
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

            [loop]
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
            
            // DebugMode = 3：只看当前像素射线是否穿过云盒子。
            // 蓝色区域表示这个像素参与了云的 raymarch。
            if (_DebugMode > 2.5)
            {
                return float4(0.05, 0.35, 1.0, 0.0);
            }

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
            
            // DebugMode = 1 用来观察密度累计。
            // densityAccum 越大，说明这条视线穿过的云越厚。
            float densityAccum = 0.0;

            [loop]
            for (int i = 0; i < 96; i++)
            {
                if (i >= stepCount)
                    break;

                float t = tNear + (i + jitter) * stepSize;
                float3 samplePosWS = cameraPosWS + rayDir * t;

                float density = GetCloudDensity(samplePosWS);
                densityAccum += density;

                if (density > 0.001)
                {
                    float opticalDepth = density * _ExtinctionCoeff * stepSize;
                    float stepAlpha = 1.0 - exp(-opticalDepth);

                    float lightTransmittance = GetCloudLightTransmittance(samplePosWS, lightDir);
                    
                    float cosTheta = dot(rayDir, lightDir);
                    float phase = HenyeyGreensteinPhase(_PhaseG, cosTheta);

                    // 根据当前采样点在云盒子里的高度，压暗云底。
                    // height01 越低，说明越靠近云底。
                    float height01 = saturate(
                        (samplePosWS.y - _CloudBoundsMin.y) /
                        max(0.001, _CloudBoundsMax.y - _CloudBoundsMin.y)
                    );

                    float bottomMask = smoothstep(0.15, 0.75, height01);
                    float bottomLighting = lerp(1.0 - _CloudBottomDarkness, 1.0, bottomMask);

                    // 简化版 Powder 效果。
                    // 云内部被遮挡越多，给一点额外散射，避免暗部死黑。
                    float powder = 1.0 + (1.0 - lightTransmittance) * _PowderStrength;
                    powder = min(powder, 2.0);

                    // 银边光。
                    // 视线越接近太阳方向越强，同时只让它主要出现在低密度边缘。
                    float edgeMask = 1.0 - saturate(density * 4.0);
                    float silver = pow(saturate(cosTheta), _SilverLiningPower);
                    silver *= edgeMask * _SilverLiningStrength;

                    float3 ambientLighting = _CloudColor.rgb * _CloudAmbient * bottomLighting;
                    float3 directLighting = _CloudColor.rgb * sunColor * lightTransmittance * phase * powder * bottomLighting;
                    float3 silverLighting = _CloudColor.rgb * sunColor * lightTransmittance * silver;

                    float3 sampleLighting = ambientLighting + directLighting + silverLighting;

                    cloudColorAccum += transmittance * stepAlpha * sampleLighting;

                    transmittance *= exp(-opticalDepth);

                    if (transmittance < 0.01)
                        break;
                }
            }
            
            // DebugMode = 1：只看云密度。
            // 越白表示密度越高，越黑表示几乎没有云。
            if (_DebugMode > 0.5 && _DebugMode < 1.5)
            {
                float densityView = saturate(densityAccum / max(1.0, stepCount) * 4.0);
                return float4(densityView.xxx, 0.0);
            }

            // DebugMode = 2：只看透射率。
            // 越白表示光线基本没被云吸收，越黑表示云越厚。
            if (_DebugMode > 1.5 && _DebugMode < 2.5)
            {
                return float4(transmittance.xxx, 0.0);
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

        float SampleHistoryDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_CloudHistoryDepthTexture, sampler_PointClamp, uv).r;
        }

        bool TryGetCloudHistoryUV(float2 uv, out float2 historyUV, out float currentEyeDepth)
        {
            historyUV = uv;

            float rawDepth = SampleDownsampledCloudDepth(uv);
            currentEyeDepth = LinearEyeDepthConsiderProjection(rawDepth);

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

            float tNear;
            float tFar;

            if (!IntersectBox(cameraPosWS, rayDir, _CloudBoundsMin.xyz, _CloudBoundsMax.xyz, tNear, tFar))
                return false;

            tNear = max(tNear, 0.0);
            tFar = min(tFar, sceneDistance);

            if (tFar <= tNear)
                return false;

            // 当前云图没有单独保存“最浓云点深度”，这里先用云盒内射线中点近似。
            float historyT = lerp(tNear, tFar, 0.5);
            float3 cloudPosWS = cameraPosWS + rayDir * historyT;

            float4 previousClipPos = mul(_PreviousViewProjectionMatrix, float4(cloudPosWS, 1.0));

            if (previousClipPos.w <= 0.0001)
                return false;

            #if UNITY_UV_STARTS_AT_TOP
                previousClipPos.y = -previousClipPos.y;
            #endif

            historyUV = previousClipPos.xy / previousClipPos.w;
            historyUV = historyUV * 0.5 + 0.5;

            bool insideHistory =
                historyUV.x >= 0.0 && historyUV.x <= 1.0 &&
                historyUV.y >= 0.0 && historyUV.y <= 1.0;

            return insideHistory;
        }

        float4 TemporalBlendFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;

            // 当前帧刚 raymarch 得到的体积云结果。
            float4 currentCloud = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

            float2 historyUV;
            float currentEyeDepth;

            // 通过重投影找到上一帧对应的屏幕位置。
            // 如果找不到可靠的历史位置，就直接使用当前帧。
            if (!TryGetCloudHistoryUV(uv, historyUV, currentEyeDepth))
                return currentCloud;

            // 采样上一帧保存的体积云结果。
            float4 historyCloud = SAMPLE_TEXTURE2D_X(_CloudHistoryTexture, sampler_LinearClamp, historyUV);

            // 用深度差判断历史帧是否可靠。
            // 深度差越大，说明遮挡或重投影可能不准，历史权重越低。
            float historyRawDepth = SampleHistoryDepth(historyUV);
            float historyEyeDepth = LinearEyeDepthConsiderProjection(historyRawDepth);
            float depthDiff = abs(currentEyeDepth - historyEyeDepth);
            float depthAccept = 1.0 - smoothstep(_TemporalDepthThreshold, _TemporalDepthThreshold * 2.0, depthDiff);

            // 比较当前帧和上一帧的云颜色/透射率差异。
            // 差异越大，说明云变化越明显，历史权重越低。
            float cloudDiff =
                length(currentCloud.rgb - historyCloud.rgb) +
                abs(currentCloud.a - historyCloud.a);

            float cloudAccept = 1.0 - smoothstep(
                _TemporalCloudChangeThreshold * 0.5,
                _TemporalCloudChangeThreshold,
                cloudDiff
            );

            // 云变化很大时，也保留一点最小历史权重，避免画面突然跳变。
            float cloudChangeWeight = lerp(
                saturate(_TemporalMinBlendOnCloudChange),
                1.0,
                cloudAccept
            );

            // 最终历史帧权重 = 基础权重 * 深度可信度 * 云变化可信度。
            float historyWeight = saturate(_TemporalBlendFactor) * depthAccept * cloudChangeWeight;

            // 当前帧和上一帧混合，减少体积云闪烁和噪点。
            return lerp(currentCloud, historyCloud, historyWeight);
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

        
        float GetScreenSpaceCloudShadow(float3 scenePosWS)
        {
            Light mainLight = GetMainLight();

            // 这里沿用你云光照里使用的 mainLight.direction。
            // 在当前项目里，它表示从当前点指向太阳的方向。
            float3 lightDir = normalize(mainLight.direction);

            // 太阳方向几乎水平或者向下时，先不计算云影。
            // 第一版避免出现投影距离过长导致的拉伸问题。
            if (lightDir.y <= 0.001)
                return 0.0;

            // 从当前场景点沿太阳方向，找到云层采样高度上的位置。
            float t = (_CloudShadowHeight - scenePosWS.y) / lightDir.y;

            // 当前点已经高于采样云层，或者投影方向不对时，不加云影。
            if (t <= 0.0)
                return 0.0;

            float3 shadowSamplePosWS = scenePosWS + lightDir * t;

            // 限制云影只出现在云盒子的 XZ 投影范围内。
            // 否则 CloudMap 的 Repeat 会让整个地面都出现重复云影。
            float boundsMask = GetCloudBoundsXZMask(shadowSamplePosWS);

            if (boundsMask <= 0.001)
                return 0.0;

            float cloudAmount = SampleCloudMapValue(shadowSamplePosWS);

            float shadow = smoothstep(
                _CloudShadowThreshold,
                _CloudShadowThreshold + max(_CloudShadowSoftness, 0.001),
                cloudAmount
            );

            return shadow * boundsMask;
        }
        
        
        float4 CompositeFrag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;

            float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

            float rawDepth = SampleSceneDepth(uv);

            bool hasSceneDepth = true;

            #if UNITY_REVERSED_Z
                hasSceneDepth = rawDepth > 0.0001;
            #else
                hasSceneDepth = rawDepth < 0.9999;
            #endif

            float cloudShadow = 0.0;

            // 只给真实场景物体计算云影，不给天空盒加。
            if (hasSceneDepth)
            {
                float3 scenePosWS = GetWorldPositionFromDepth(uv, rawDepth);
                cloudShadow = GetScreenSpaceCloudShadow(scenePosWS);
            }

            // DebugMode = 4：只看云影。
            // 白色 = 云影强，黑色 = 没有云影。
            // 天空没有场景深度，所以显示黑色。
            if (_DebugMode > 3.5 && _DebugMode < 4.5)
            {
                return float4(cloudShadow.xxx, 1.0);
            }

            if (hasSceneDepth)
            {
                // cloudShadow = 0，不变暗。
                // cloudShadow = 1，最多压暗 _CloudShadowStrength。
                float shadowFactor = lerp(1.0, 1.0 - _CloudShadowStrength, cloudShadow);
                sceneColor.rgb *= shadowFactor;
            }

            float4 cloud = DepthAwareUpsampleCloud(uv);

            // 先让场景受云影影响，再把体积云合成上去。
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
            Name "VolumeCloudTemporalBlend"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TemporalBlendFrag
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
