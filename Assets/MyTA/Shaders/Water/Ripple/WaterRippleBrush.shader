Shader "WaterRipple/WaterRippleBrush"
{
    Properties
    {
        // Brush 负责把一次水波输入写进 CurrentBrushRT。
        // RGB 存法线扰动，A 存 signed height：0.5 为无波动。
        _NormalTex ("WaterRipple Normal Tex", 2D) = "bump" {}
        _HeightTex ("WaterRipple Height Tex R", 2D) = "black" {}

        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _HeightStrength ("Height Strength", Range(0, 2)) = 1

        // 高度方向反了时再打开；默认认为黑色更低，白色更高。
        _InvertHeight ("Invert Height", Float) = 0
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

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
                // _NormalTex 必须按 Unity Normal Map 导入，UnpackNormalScale 才能正确解码。
                half4 packedNormal = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, IN.uvNormal);

                // 解码到 -1~1，再重新编码到 RT 使用的 0~1 范围。
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);
                half3 normalRGB = normalTS * 0.5h + 0.5h;

                // HeightTex.r 也转成 signed height：-1 下陷，0 无波动，+1 隆起。
                half heightR = SAMPLE_TEXTURE2D(_HeightTex, sampler_HeightTex, IN.uvHeight).r;
                half signedHeight = (heightR - 0.5h) * 2.0h;
                signedHeight = lerp(signedHeight, -signedHeight, saturate(_InvertHeight));
                signedHeight = clamp(signedHeight * _HeightStrength, -1.0h, 1.0h);

                half influence = abs(signedHeight);

                // 没有波动的透明区域直接丢弃，避免整张 Brush Quad 覆盖 CurrentBrushRT。
                clip(influence - 0.001h);

                // A 通道写回编码高度，后面的波动方程只读取这个通道。
                half encodedSignedHeight = signedHeight * 0.5h + 0.5h;

                return half4(normalRGB, encodedSignedHeight);
            }
            ENDHLSL
        }
    }
}
