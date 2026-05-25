Shader "Footprints/URP_FootprintBrush_NormalHeightSeparate"
{
    Properties
    {
        _NormalTex ("Footprint Normal Tex", 2D) = "bump" {}
        _HeightTex ("Footprint Height Tex R", 2D) = "black" {}

        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _HeightStrength ("Height Strength", Range(0, 2)) = 1

        // 你的图是：鞋印黑、背景白，所以默认反转。
        _InvertHeight ("Invert Height", Float) = 1
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

            TEXTURE2D(_NormalTex);
            SAMPLER(sampler_NormalTex);

            TEXTURE2D(_HeightTex);
            SAMPLER(sampler_HeightTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NormalTex_ST;
                float4 _HeightTex_ST;
                half _NormalStrength;
                half _HeightStrength;
                half _InvertHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvNormal : TEXCOORD0;
                float2 uvHeight : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uvNormal = TRANSFORM_TEX(IN.uv, _NormalTex);
                OUT.uvHeight = TRANSFORM_TEX(IN.uv, _HeightTex);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 normalSample = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, IN.uvNormal);
                // return normalSample;
                
                half3 normalRGB = normalSample.rgb;

                half3 neutralNormal = half3(0.5h, 0.5h, 1.0h);

                // Default 纹理，不用 UnpackNormal。
                // 但为了调 NormalStrength，先 decode，调 xy，再 encode。
                half3 n = normalize(normalRGB * 2.0h - 1.0h);
                n.xy *= _NormalStrength;
                n = normalize(n);
                normalRGB = n * 0.5h + 0.5h;

                half heightR = SAMPLE_TEXTURE2D(_HeightTex, sampler_HeightTex, IN.uvHeight).r;
                half depression = lerp(heightR, 1.0h - heightR, saturate(_InvertHeight));
                depression = saturate(depression * _HeightStrength);

                // 没脚印的地方回默认法线
                half writeMask = step(0.001h, depression);
                // alpha = 0 的区域，RGB 保持默认法线
                // alpha > 0 的区域，RGB 写入真实法线
                normalRGB = lerp(neutralNormal, normalRGB, writeMask);

                return half4(normalRGB, depression);
            }
            ENDHLSL
        }
    }
}