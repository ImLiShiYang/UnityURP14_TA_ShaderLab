Shader "Footprints/InteractiveGround"
{
    Properties
    {
        [Header(Base)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 3)) = 1

        [Header(Shadow)]
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.9
        _MinShadow ("Min Shadow Light", Range(0,1)) = 0.15

        [Header(Footprint RT Signed Height)]
        _FootstepTex ("Footstep RT RGB Normal A Signed Height", 2D) = "gray" {}
        _FootstepRect ("Footstep Rect", Vector) = (0,0,1,1)
        _EnableFootstep ("Enable Footstep", Float) = 0

        [Header(Footprint Mask)]
        _FootprintStrength ("Footprint Strength", Range(0,2)) = 1
        _FootprintSignedDeadZone ("Signed Height Dead Zone", Range(0,0.2)) = 0.005

        [Header(Footprint Normal)]
        _FootprintNormalStrength ("Footprint Normal Strength", Range(0,3)) = 1.5
        [Toggle] _FlipFootprintNormalY ("Flip Footprint Normal Y", Float) = 0

        [Header(Footprint AO)]
        _FootprintAOStrength ("Footprint AO Strength", Range(0,1)) = 0.25
        _FootprintAOSmoothMin ("Footprint AO Smooth Min", Range(0,1)) = 0.02
        _FootprintAOSmoothMax ("Footprint AO Smooth Max", Range(0,1)) = 0.45
        _FootprintSpecOcclusion ("Footprint Spec Occlusion", Range(0,1)) = 0.35

        [Header(Footprint Rim)]
        _FootprintRimLightStrength ("Footprint Rim Light Strength", Range(0,0.5)) = 0.08

        [Header(Blinn Phong)]
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.35
        _SpecPower ("Spec Power", Range(1,256)) = 48
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
            Name "ForwardBlinnPhong"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.0
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

                half _ShadowStrength;
                half _MinShadow;

                float4 _FootstepRect;
                half _EnableFootstep;

                half _FootprintStrength;
                half _FootprintSignedDeadZone;

                half _FootprintNormalStrength;
                half _FlipFootprintNormalY;

                half _FootprintAOStrength;
                half _FootprintAOSmoothMin;
                half _FootprintAOSmoothMax;
                half _FootprintSpecOcclusion;

                half _FootprintRimLightStrength;

                half4 _SpecColor;
                half _SpecStrength;
                half _SpecPower;
            CBUFFER_END

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
            };

            half3 DecodeNormalRGB(half3 normalRGB)
            {
                return normalize(normalRGB * 2.0h - 1.0h);
            }

            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            half ApplySignedDeadZone(half signedValue, half deadZone)
            {
                half absValue = abs(signedValue);

                if (absValue <= deadZone)
                    return 0.0h;

                half remapped = (absValue - deadZone) / max(0.0001h, 1.0h - deadZone);
                return sign(signedValue) * saturate(remapped);
            }

            float2 WorldXZToFootUV(float3 positionWS)
            {
                float2 footUV;
                footUV.x = (positionWS.x - _FootstepRect.x) / (_FootstepRect.z - _FootstepRect.x);
                footUV.y = (positionWS.z - _FootstepRect.y) / (_FootstepRect.w - _FootstepRect.y);
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

            float3 NormalizeSafeCustom(float3 v)
            {
                return v * rsqrt(max(dot(v, v), 1e-6));
            }

            half3 FootprintNormalToWorld(half3 footprintNormal, half3 baseNormalWS)
            {
                float3 N = NormalizeSafeCustom(baseNormalWS);

                float3 worldX = float3(1.0, 0.0, 0.0);
                float3 worldZ = float3(0.0, 0.0, 1.0);

                float3 tangentX = worldX - N * dot(worldX, N);
                float3 tangentZ = worldZ - N * dot(worldZ, N);

                tangentX = NormalizeSafeCustom(tangentX);
                tangentZ = NormalizeSafeCustom(tangentZ);

                float3 nWS =
                    tangentX * footprintNormal.x +
                    tangentZ * footprintNormal.y +
                    N        * footprintNormal.z;

                return normalize((half3)nWS);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // =====================================================
                // 1. Base
                // =====================================================
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb * _Brightness;

                half3 baseNormalWS = normalize(IN.normalWS);
                half3 finalNormalWS = baseNormalWS;

                // =====================================================
                // 2. Footprint RT Signed Height
                // =====================================================
                float2 footUV = WorldXZToFootUV(IN.positionWS);
                half inside = FootUVInside(footUV);

                half4 foot = SAMPLE_TEXTURE2D(_FootstepTex, sampler_FootstepTex, footUV);

                // 新协议：
                // A = 0.5  表示原始地面
                // A < 0.5  表示下陷
                // A > 0.5  表示泥边隆起
                half signedFoot = (foot.a - 0.5h) * 2.0h;

                signedFoot = ApplySignedDeadZone(signedFoot, _FootprintSignedDeadZone);

                signedFoot *= inside * _EnableFootstep;

                // 下陷区域
                half depressionRaw = saturate(-signedFoot);

                // 隆起泥边区域
                half rimRaw = saturate(signedFoot);

                // 总影响区域，给法线混合用
                half influenceRaw = saturate(abs(signedFoot));

                half influenceMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    influenceRaw
                );

                influenceMask = saturate(influenceMask * _FootprintStrength);

                half depressionMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    depressionRaw
                );

                depressionMask = saturate(depressionMask * _FootprintStrength);

                half rimMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    rimRaw
                );

                rimMask = saturate(rimMask * _FootprintStrength);

                // =====================================================
                // 3. Footprint Normal Blend
                // =====================================================
                half3 footprintNormal = DecodeNormalRGB(foot.rgb);

                if (_FlipFootprintNormalY > 0.5h)
                {
                    footprintNormal.y = -footprintNormal.y;
                }

                half3 footprintNormalWS = FootprintNormalToWorld(
                    footprintNormal,
                    baseNormalWS
                );

                half normalBlend = saturate(influenceMask * _FootprintNormalStrength);

                finalNormalWS = normalize(lerp(
                    finalNormalWS,
                    footprintNormalWS,
                    normalBlend
                ));

                // =====================================================
                // 4. AO / Rim Color Blend
                // =====================================================
                // AO 主要只压暗下陷区域，不应该把隆起泥边也整体压黑。
                half footprintAO = 1.0h - depressionMask * _FootprintAOStrength;
                albedo *= footprintAO;

                // 泥边是高于地面的区域，可以非常轻微提亮一点。
                // 数值不要太大，否则会像白描边。
                albedo = lerp(
                    albedo,
                    albedo * 1.06h,
                    rimMask * _FootprintRimLightStrength
                );

                // =====================================================
                // 5. Lighting
                // =====================================================
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

                // =====================================================
                // 6. Blinn Phong Specular
                // =====================================================
                half3 halfDir = normalize(lightDirWS + viewDirWS);
                half ndoth = saturate(dot(finalNormalWS, halfDir));

                half specTerm = pow(ndoth, _SpecPower);
                specTerm *= _SpecStrength;
                specTerm *= step(0.001h, ndotl);
                specTerm *= lightAtten;

                // 下陷处高光稍微弱一点，泥边不压太多。
                specTerm *= 1.0h - depressionMask * _FootprintSpecOcclusion;

                color += specTerm * _SpecColor.rgb * mainLight.color;

                return half4(color, 1.0h);
            }

            ENDHLSL
        }
    }
}