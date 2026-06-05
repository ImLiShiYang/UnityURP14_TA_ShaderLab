Shader "WaterRipple/WaterRippleWaveSurface"
{
    Properties
    {
        [Header(Water Ripple RT)]
        _WaterRippleTex ("Water Ripple RT A Height", 2D) = "gray" {}
        _WaterRippleRect ("Water Ripple Rect", Vector) = (0,0,1,1)
        _EnableWaterRipple ("Enable Water Ripple", Float) = 0
        _WaterRippleSignedDeadZone ("Signed Height Dead Zone", Range(0, 0.2)) = 0.002

        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.88, 1.00, 0.45)
        _DeepColor ("Deep Color", Color) = (0.02, 0.24, 0.42, 0.68)
        _ColorBlend ("Color Blend", Range(0, 1)) = 0.72
        _Alpha ("Base Alpha", Range(0, 1)) = 0.52

        [Header(Wave Response)]
        _WaveHeight ("Vertex Wave Height", Range(0, 0.3)) = 0.035
        _NormalStrength ("Normal Strength", Range(0.01, 12)) = 4.0
        _RippleColorStrength ("Ripple Color Strength", Range(0, 1)) = 0.08

        [Header(Refraction)]
        _RefractionStrength ("Refraction Strength", Range(0, 0.08)) = 0.016
        _RefractionWaveStrength ("Refraction Wave Strength", Range(0, 4)) = 1.2
        _RefractionTintStrength ("Refraction Tint Strength", Range(0, 1)) = 0.28

        [Header(Fresnel)]
        _FresnelColor ("Fresnel Color", Color) = (0.65, 0.95, 1.0, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.2
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.6
        _FresnelAlpha ("Fresnel Alpha", Range(0, 1)) = 0.24

        [Header(Specular)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularStrength ("Specular Strength", Range(0, 5)) = 1.2
        _SpecularPower ("Specular Power", Range(8, 256)) = 96

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.14
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.32
        _FoamSoftness ("Foam Softness", Range(0.001, 1)) = 0.22
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "WaterRippleSurfaceForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
                float4 screenPos   : TEXCOORD2;
            };

            TEXTURE2D(_WaterRippleTex);
            SAMPLER(sampler_WaterRippleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterRippleRect;
                float _EnableWaterRipple;
                float _WaterRippleSignedDeadZone;

                float4 _ShallowColor;
                float4 _DeepColor;
                float _ColorBlend;
                float _Alpha;

                float _WaveHeight;
                float _NormalStrength;
                float _RippleColorStrength;

                float _RefractionStrength;
                float _RefractionWaveStrength;
                float _RefractionTintStrength;

                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelStrength;
                float _FresnelAlpha;

                float4 _SpecularColor;
                float _SpecularStrength;
                float _SpecularPower;

                float4 _FoamColor;
                float _FoamStrength;
                float _FoamThreshold;
                float _FoamSoftness;

                float4 _WaterRippleTex_TexelSize;
            CBUFFER_END

            float SafeRcp(float value)
            {
                return rcp(max(abs(value), 0.0001));
            }

            float2 WorldXZToWaterRippleUV(float3 positionWS)
            {
                float2 rectSize = _WaterRippleRect.zw - _WaterRippleRect.xy;

                return float2(
                    (positionWS.x - _WaterRippleRect.x) * SafeRcp(rectSize.x),
                    (positionWS.z - _WaterRippleRect.y) * SafeRcp(rectSize.y)
                );
            }

            float WaterRippleUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            float ApplySignedDeadZone(float signedValue)
            {
                float absValue = abs(signedValue);

                if (absValue <= _WaterRippleSignedDeadZone)
                    return 0.0;

                float remapped = (absValue - _WaterRippleSignedDeadZone) /
                    max(0.0001, 1.0 - _WaterRippleSignedDeadZone);

                return sign(signedValue) * saturate(remapped);
            }

            float ReadRippleHeightUV(float2 uv)
            {
                float encodedHeight = SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv, 0).a;
                return ApplySignedDeadZone(encodedHeight * 2.0 - 1.0);
            }

            float ReadRippleHeightWS(float3 positionWS)
            {
                float enable = saturate(_EnableWaterRipple);
                float2 uv = WorldXZToWaterRippleUV(positionWS);
                float inside = WaterRippleUVInside(uv);

                return ReadRippleHeightUV(uv) * enable * inside;
            }

            float2 ReadRippleSlopeWS(float3 positionWS)
            {
                float enable = saturate(_EnableWaterRipple);
                float2 uv = WorldXZToWaterRippleUV(positionWS);
                float inside = WaterRippleUVInside(uv);
                float2 texel = max(_WaterRippleTex_TexelSize.xy, float2(0.0001, 0.0001));

                float left = ReadRippleHeightUV(uv + float2(-texel.x, 0.0));
                float right = ReadRippleHeightUV(uv + float2(texel.x, 0.0));
                float back = ReadRippleHeightUV(uv + float2(0.0, -texel.y));
                float front = ReadRippleHeightUV(uv + float2(0.0, texel.y));

                return float2(left - right, back - front) * enable * inside;
            }

            float3 NormalizeSafe(float3 value)
            {
                return value * rsqrt(max(dot(value, value), 1e-6));
            }

            float3 BuildRippleNormalWS(float3 baseNormalWS, float2 slope)
            {
                float3 n = NormalizeSafe(baseNormalWS);

                float3 worldX = float3(1.0, 0.0, 0.0);
                float3 worldZ = float3(0.0, 0.0, 1.0);

                float3 tangentX = NormalizeSafe(worldX - n * dot(worldX, n));
                float3 tangentZ = NormalizeSafe(worldZ - n * dot(worldZ, n));

                float3 rippleNormal =
                    n +
                    tangentX * slope.x * _NormalStrength +
                    tangentZ * slope.y * _NormalStrength;

                return NormalizeSafe(rippleNormal);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = NormalizeSafe(TransformObjectToWorldNormal(IN.normalOS));
                float height = ReadRippleHeightWS(positionWS);

                positionWS += normalWS * height * _WaveHeight;

                OUT.positionWS = positionWS;
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = normalWS;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float height = ReadRippleHeightWS(IN.positionWS);
                float2 slope = ReadRippleSlopeWS(IN.positionWS);
                float3 normalWS = BuildRippleNormalWS(IN.normalWS, slope);
                float3 viewDirWS = NormalizeSafe(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 lightDirWS = NormalizeSafe(mainLight.direction);
                float ndotl = saturate(dot(normalWS, lightDirWS));

                float fresnel = pow(
                    1.0 - saturate(dot(normalWS, viewDirWS)),
                    _FresnelPower
                );

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 refractionOffset =
                    slope *
                    _RefractionStrength *
                    _RefractionWaveStrength *
                    lerp(0.75, 1.35, fresnel);

                half3 sceneColor = SampleSceneColor(clamp(screenUV + refractionOffset, 0.001, 0.999));
                half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, _ColorBlend);

                float waveAmount = saturate(abs(height));
                float slopeAmount = saturate(length(slope) * 4.0);
                float rippleAmount = saturate(max(waveAmount, slopeAmount));

                waterTint += _FresnelColor.rgb * rippleAmount * _RippleColorStrength;

                half3 color = lerp(sceneColor, waterTint, _RefractionTintStrength);
                color += _FresnelColor.rgb * fresnel * _FresnelStrength;

                float3 halfDir = NormalizeSafe(lightDirWS + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecularPower);
                spec *= _SpecularStrength;
                spec *= mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                color += _SpecularColor.rgb * spec * mainLight.color;

                float foamMask = smoothstep(
                    _FoamThreshold,
                    _FoamThreshold + _FoamSoftness,
                    rippleAmount
                );

                color = lerp(color, _FoamColor.rgb, foamMask * _FoamStrength);

                half3 ambient = SampleSH(normalWS);
                half3 direct = mainLight.color *
                    (ndotl * 0.35 + 0.65) *
                    mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation;

                color *= ambient + direct;

                float alpha = _Alpha;
                alpha += fresnel * _FresnelAlpha;
                alpha += foamMask * 0.06;
                alpha = saturate(alpha);

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }
}
