Shader "WaterRipple/WaterRippleSlashStampBrush"
{
    Properties
    {
        _NormalTex ("Compatibility Normal Tex", 2D) = "bump" {}
        _HeightTex ("Compatibility Height Tex", 2D) = "gray" {}

        _NormalStrength ("Normal Strength", Range(0, 4)) = 2.2
        _HeightStrength ("Height Strength", Range(0, 2)) = 1.4
        _InvertHeight ("Invert Height", Float) = 0

        _CutStrength ("Cut Strength", Range(-1, 0)) = -0.62
        _SideLiftStrength ("Side Lift Strength", Range(0, 1)) = 0.22
        _CutWidth ("Cut Width 01", Range(0.01, 0.4)) = 0.09
        _SideWidth ("Side Width 01", Range(0.01, 0.45)) = 0.18
        _LengthSoftness ("Length Softness", Range(0.001, 0.5)) = 0.16
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.2)) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _NormalStrength;
                half _HeightStrength;
                half _InvertHeight;
                half _CutStrength;
                half _SideLiftStrength;
                half _CutWidth;
                half _SideWidth;
                half _LengthSoftness;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half EvaluateSignedHeight(float2 uv)
            {
                float2 p = uv - 0.5;
                half x = abs(p.x);
                half y = abs(p.y);

                half lengthFade = 1.0h - smoothstep(0.5h - _LengthSoftness, 0.5h, y);
                half sideFade = 1.0h - smoothstep(_SideWidth, _SideWidth + _EdgeSoftness, x);
                half mask = saturate(lengthFade * sideFade);

                if (mask <= 0.0001h)
                    return 0.0h;

                half cut = _CutStrength * (1.0h - smoothstep(0.0h, _CutWidth, x));

                half sideCenter = _CutWidth + (_SideWidth - _CutWidth) * 0.45h;
                half sideSigma = max((_SideWidth - _CutWidth) * 0.35h, 0.001h);
                half sideT = saturate(1.0h - abs(x - sideCenter) / sideSigma);
                half sideLift = _SideLiftStrength * sideT * sideT;

                half signedHeight = (cut + sideLift) * mask;
                signedHeight = lerp(signedHeight, -signedHeight, saturate(_InvertHeight));
                return clamp(signedHeight * _HeightStrength, -1.0h, 1.0h);
            }

            half3 EncodeNormalFromHeight(float2 uv)
            {
                half eps = 0.0025h;
                half hL = EvaluateSignedHeight(uv + float2(-eps, 0));
                half hR = EvaluateSignedHeight(uv + float2( eps, 0));
                half hD = EvaluateSignedHeight(uv + float2(0, -eps));
                half hU = EvaluateSignedHeight(uv + float2(0,  eps));

                half dx = hR - hL;
                half dy = hU - hD;

                half3 n = normalize(half3(-dx * _NormalStrength, -dy * _NormalStrength, 1.0h));
                return n * 0.5h + 0.5h;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half signedHeight = EvaluateSignedHeight(input.uv);
                clip(abs(signedHeight) - 0.001h);

                half3 normalRGB = EncodeNormalFromHeight(input.uv);
                half encodedSignedHeight = signedHeight * 0.5h + 0.5h;
                return half4(normalRGB, encodedSignedHeight);
            }
            ENDHLSL
        }
    }
}
