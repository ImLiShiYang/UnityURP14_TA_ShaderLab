Shader "WaterRipple/WaterRippleCircleStampBrush"
{
    Properties
    {
        // 为了兼容现有 WaterRippleBrushSpawner，保留这几个旧属性。
        _NormalTex ("Compatibility Normal Tex", 2D) = "bump" {}
        _HeightTex ("Compatibility Height Tex", 2D) = "gray" {}

        _NormalStrength ("Normal Strength", Range(0, 4)) = 1.0
        _HeightStrength ("Height Strength", Range(0, 2)) = 1.0
        _InvertHeight ("Invert Height", Float) = 0

        _CenterStrength ("Center Strength", Range(-1, 1)) = -0.35
        _RingStrength ("Ring Strength", Range(-1, 1)) = 0.18
        _InnerRadius ("Inner Radius 01", Range(0.001, 0.5)) = 0.14
        _OuterRadius ("Outer Radius 01", Range(0.001, 0.707)) = 0.42
        _EdgeSoftness ("Edge Softness", Range(0.0001, 0.25)) = 0.02
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
                half _CenterStrength;
                half _RingStrength;
                half _InnerRadius;
                half _OuterRadius;
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

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half EvaluateSignedHeight(float2 uv)
            {
                float2 p = uv - 0.5;
                half d = length(p);

                half outerFade = 1.0h - smoothstep(_OuterRadius - _EdgeSoftness, _OuterRadius, d);
                if (outerFade <= 0.0001h)
                    return 0.0h;

                half signedHeight = 0.0h;

                if (d <= _InnerRadius)
                {
                    half innerT = 1.0h - saturate(d / max(_InnerRadius, 0.0001h));
                    // 中心下陷：中心最强，向外过渡回 0。
                    signedHeight = _CenterStrength * innerT;
                }
                else if (d <= _OuterRadius)
                {
                    half ringT = saturate((d - _InnerRadius) / max(_OuterRadius - _InnerRadius, 0.0001h));
                    // 外环隆起：中间最强，两边变弱。
                    half ringShape = sin(ringT * HALF_PI) * sin((1.0h - ringT) * HALF_PI) * 2.0h;
                    signedHeight = _RingStrength * ringShape;
                }

                signedHeight *= outerFade;
                signedHeight = lerp(signedHeight, -signedHeight, saturate(_InvertHeight));
                signedHeight = clamp(signedHeight * _HeightStrength, -1.0h, 1.0h);
                return signedHeight;
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

            half4 Frag(Varyings IN) : SV_Target
            {
                half signedHeight = EvaluateSignedHeight(IN.uv);
                half influence = abs(signedHeight);
                clip(influence - 0.001h);

                half3 normalRGB = EncodeNormalFromHeight(IN.uv);
                half encodedSignedHeight = signedHeight * 0.5h + 0.5h;
                return half4(normalRGB, encodedSignedHeight);
            }
            ENDHLSL
        }
    }
}
