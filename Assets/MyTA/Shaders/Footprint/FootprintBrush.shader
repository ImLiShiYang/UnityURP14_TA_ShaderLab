Shader "Footprints/URP_FootprintBrush_NormalHeightSeparate"
{
    Properties
    {
        _NormalTex ("Footprint Normal Tex", 2D) = "bump" {}
        _HeightTex ("Footprint Height Tex R", 2D) = "black" {}

        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _HeightStrength ("Height Strength", Range(0, 2)) = 1

        // 你的图是：鞋印黑、背景白，所以默认反转。
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
                // 1. 采样 Unity Normal Map。
                // 注意：这里 _NormalTex 的 Texture Type 必须是 Normal Map。
                half4 packedNormal = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, IN.uvNormal);

                // 2. 用 Unity/URP 的方式解包 normal。
                // normalTS 范围是 -1~1。
                // _NormalStrength 会增强/减弱 xy 法线强度。
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);

                // 3. 重新编码回 0~1。
                // 因为你的 CurrentBrushRT / AccumA 协议是：
                // RGB = encoded normal
                // A   = mask / depression
                half3 normalRGB = normalTS * 0.5h + 0.5h;

                half3 neutralNormal = half3(0.5h, 0.5h, 1.0h);

                // 4. 采样高度图。
                // HeightTex 新语义：
                // 0.5 = 原始地面
                // <0.5 = 下陷
                // >0.5 = 泥边隆起
                half heightR = SAMPLE_TEXTURE2D(_HeightTex, sampler_HeightTex, IN.uvHeight).r;

                // 转成 signed height:
                // -1 = 最深下陷
                //  0 = 原始地面
                // +1 = 最高隆起
                half signedHeight = (heightR - 0.5h) * 2.0h;

                // 如果你的高度图黑色是下陷、白色是隆起，_InvertHeight 应该设为 0。
                // 如果方向反了，再设为 1。
                signedHeight = lerp(signedHeight, -signedHeight, saturate(_InvertHeight));

                // 强度缩放
                signedHeight = clamp(signedHeight * _HeightStrength, -1.0h, 1.0h);

                // 影响强度。
                // 下陷和隆起都算有效影响。
                half influence = abs(signedHeight);

                // 没有高度变化的地方不写入 CurrentBrushRT。
                clip(influence - 0.001h);

                // 重新编码到 A 通道：
                // signedHeight = -1 -> A = 0
                // signedHeight =  0 -> A = 0.5
                // signedHeight = +1 -> A = 1
                half encodedSignedHeight = signedHeight * 0.5h + 0.5h;

                return half4(normalRGB, encodedSignedHeight);
            }
            ENDHLSL
        }
    }
}