Shader "Snow/SnowSurface_RTDeform"
{
    Properties
    {
        [Header(Base Snow)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 3)) = 1

        [Header(Snow Deform RT)]
        _FootstepTex ("Snow Deform RT R Sink G Rim A Mask", 2D) = "black" {}
        _FootstepRect ("Footstep Rect", Vector) = (0,0,1,1)
        _EnableFootstep ("Enable Footstep", Float) = 0

        [Header(Vertex Displacement)]
        _MaxSnowSink ("Max Snow Sink", Range(0, 2)) = 0.35
        _RimHeight ("Rim Height", Range(0, 1)) = 0.08
        _SnowDeformStrength ("Snow Deform Strength", Range(0, 2)) = 1

        [Header(Snow Normal From RT)]
        _SnowNormalStrength ("Snow Normal Strength", Range(0, 3)) = 1
        _SnowNormalSmoothMin ("Normal Smooth Min", Range(0, 1)) = 0.01
        _SnowNormalSmoothMax ("Normal Smooth Max", Range(0, 1)) = 0.5

        [Header(Snow Color Response)]
        _SnowAOStrength ("Depression AO Strength", Range(0, 1)) = 0.25
        _SnowRimLightStrength ("Rim Light Strength", Range(0, 0.8)) = 0.12
        _SnowMaskSmoothMin ("Mask Smooth Min", Range(0, 1)) = 0.02
        _SnowMaskSmoothMax ("Mask Smooth Max", Range(0, 1)) = 0.5

        [Header(Shadow)]
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.9
        _MinShadow ("Min Shadow Light", Range(0,1)) = 0.15

        [Header(Blinn Phong)]
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.25
        _SpecPower ("Spec Power", Range(1,256)) = 64
        _DepressionSpecOcclusion ("Depression Spec Occlusion", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardSnow"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_FootstepTex);
            SAMPLER(sampler_FootstepTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Brightness;

                float4 _FootstepRect;
                half _EnableFootstep;

                half _MaxSnowSink;
                half _RimHeight;
                half _SnowDeformStrength;

                half _SnowNormalStrength;
                half _SnowNormalSmoothMin;
                half _SnowNormalSmoothMax;

                half _SnowAOStrength;
                half _SnowRimLightStrength;
                half _SnowMaskSmoothMin;
                half _SnowMaskSmoothMax;

                half _ShadowStrength;
                half _MinShadow;

                half4 _SpecColor;
                half _SpecStrength;
                half _SpecPower;
                half _DepressionSpecOcclusion;
            CBUFFER_END

            // Unity 会自动提供纹理尺寸：
            // x = 1 / width, y = 1 / height, z = width, w = height。
            float4 _FootstepTex_TexelSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float2 footUV      : TEXCOORD3;
            };

            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            float2 WorldXZToFootUV(float3 positionWS)
            {
                float2 rectSize = max(_FootstepRect.zw - _FootstepRect.xy, float2(0.0001, 0.0001));

                float2 footUV;
                footUV.x = (positionWS.x - _FootstepRect.x) / rectSize.x;
                footUV.y = (positionWS.z - _FootstepRect.y) / rectSize.y;
                return footUV;
            }

            half FootUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            half4 SampleSnowRTLOD(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D_LOD(_FootstepTex, sampler_FootstepTex, uv, 0);
                return data * inside;
            }

            half4 SampleSnowRT(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D(_FootstepTex, sampler_FootstepTex, uv);
                return data * inside;
            }
            
            half4 DecodeSnowData(half4 data)
            {
                // RT 协议：
                // R = sink，下陷
                // G = rim，凸起
                // A = mask，有效区域

                half mask = saturate(data.a);

                half sink = saturate(data.r) * mask;
                half rim  = saturate(data.g) * mask;

                return half4(sink, rim, 0.0h, mask);
            }

            // Snow RT 协议：
            // R = sink，雪被压下去的强度。
            // G = rim，雪边鼓起强度。
            // A = mask，brush 覆盖范围。
            half SnowDisplacementFromData(half4 data)
            {
                half4 snow = DecodeSnowData(data);

                half sink = snow.r;
                half rim  = snow.g;

                // sink 往下，rim 往上。
                return (rim * _RimHeight - sink * _MaxSnowSink) * _SnowDeformStrength;
            }

            half SampleSnowHeight(float2 uv)
            {
                half4 data = SampleSnowRT(uv);
                return SnowDisplacementFromData(data);
            }

            half3 ReconstructSnowNormalWS(float2 uv)
            {
                float2 texel = _FootstepTex_TexelSize.xy;

                float worldSizeX = max(_FootstepRect.z - _FootstepRect.x, 0.0001);
                float worldSizeZ = max(_FootstepRect.w - _FootstepRect.y, 0.0001);

                float worldTexelX = texel.x * worldSizeX;
                float worldTexelZ = texel.y * worldSizeZ;

                half hL = SampleSnowHeight(uv - float2(texel.x, 0.0));
                half hR = SampleSnowHeight(uv + float2(texel.x, 0.0));
                half hD = SampleSnowHeight(uv - float2(0.0, texel.y));
                half hU = SampleSnowHeight(uv + float2(0.0, texel.y));

                float3 tangentX = float3(worldTexelX * 2.0, hR - hL, 0.0);
                float3 tangentZ = float3(0.0, hU - hD, worldTexelZ * 2.0);

                // 对平面来说 cross(tangentZ, tangentX) = (0,1,0)。
                return normalize((half3)cross(tangentZ, tangentX));
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                half3 normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));

                float3 positionWS = posInputs.positionWS;
                float2 footUV = WorldXZToFootUV(positionWS);

                half4 snowData = SampleSnowRTLOD(footUV);
                half displacement = SnowDisplacementFromData(snowData);

                // 这里按世界 Y 方向位移，适合水平雪面。
                // 如果你的雪面是大斜坡，可以改成 positionWS += normalWS * displacement。
                positionWS += normalWS * displacement;

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.footUV = footUV;

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb * _Brightness;

                half4 snowData = DecodeSnowData(SampleSnowRT(IN.footUV));

                half sink = snowData.r;
                half rim  = snowData.g;
                half mask = snowData.a;

                half depressionMask = SafeSmoothStep(_SnowMaskSmoothMin, _SnowMaskSmoothMax, sink);
                half rimMask = SafeSmoothStep(_SnowMaskSmoothMin, _SnowMaskSmoothMax, rim);
                half normalMask = SafeSmoothStep(_SnowNormalSmoothMin, _SnowNormalSmoothMax, mask);
                normalMask = saturate(normalMask * _SnowNormalStrength);

                half3 baseNormalWS = normalize(IN.normalWS);
                half3 snowNormalWS = ReconstructSnowNormalWS(IN.footUV);
                half3 finalNormalWS = normalize(lerp(baseNormalWS, snowNormalWS, normalMask));

                // 压下去的雪更暗一点，雪边略微提亮。
                albedo *= 1.0h - depressionMask * _SnowAOStrength;
                albedo = lerp(albedo, albedo * 1.25h, rimMask * _SnowRimLightStrength);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 lightDirWS = normalize(mainLight.direction);
                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);

                half ndotl = saturate(dot(finalNormalWS, lightDirWS));

                half shadowAtten = mainLight.shadowAttenuation;
                shadowAtten = lerp(1.0h, max(_MinShadow, shadowAtten), _ShadowStrength);
                half lightAtten = mainLight.distanceAttenuation * shadowAtten;

                half3 ambient = SampleSH(finalNormalWS);
                half3 direct = mainLight.color * ndotl * lightAtten;

                half3 color = albedo * (ambient + direct);

                half3 halfDir = normalize(lightDirWS + viewDirWS);
                half ndoth = saturate(dot(finalNormalWS, halfDir));
                half specTerm = pow(ndoth, _SpecPower) * _SpecStrength;
                specTerm *= step(0.001h, ndotl);
                specTerm *= lightAtten;
                specTerm *= 1.0h - depressionMask * _DepressionSpecOcclusion;

                color += specTerm * _SpecColor.rgb * mainLight.color;

                return half4(color, 1.0h);
            }

            ENDHLSL
        }
    }
}
