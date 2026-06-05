Shader "WaterRipple/Debug/UI_WaterRippleTextureDebugView"
{
    Properties
    {
        _MainTex ("Water Ripple Texture", 2D) = "black" {}

        // 0  RGB
        // 1  Alpha
        // 2  R
        // 3  G
        // 4  B
        // 5  NormalDiff
        // 6  NormalEncoded
        // 7  RGBWithAlphaBackground
        // 8  WaterSignedHeightA
        // 9  WaterHeightMagnitudeA
        // 10 WaterMaskA
        // 11 WaterComposite
        _Mode ("Mode", Float) = 9

        _Exposure ("Exposure", Range(0, 10)) = 1
        _NormalDiffStrength ("Normal Diff Strength", Range(1, 100)) = 20
        _BackgroundColor ("Background Color", Color) = (0.15, 0.15, 0.15, 1)
        _FlipY ("Flip Y", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "UIWaterRippleTextureDebug"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Mode;
                float _Exposure;
                float _NormalDiffStrength;
                half4 _BackgroundColor;
                float _FlipY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                if (_FlipY > 0.5)
                    OUT.uv.y = 1.0 - OUT.uv.y;

                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                // 水波 signed height 协议：
                // A = 0.5 表示无变化；A < 0.5 表示下陷；A > 0.5 表示隆起。
                half signedHeight = (col.a - 0.5h) * 2.0h;
                half heightMagnitude = saturate(abs(signedHeight));

                // 0. RGB：普通彩色查看。
                if (_Mode < 0.5)
                    return half4(saturate(col.rgb * _Exposure), 1);

                // 1. Alpha：查看原始 A 通道。
                if (_Mode < 1.5)
                    return half4(saturate(col.aaa * _Exposure), 1);

                // 2. R：encoded normal X。
                if (_Mode < 2.5)
                    return half4(saturate(col.rrr * _Exposure), 1);

                // 3. G：encoded normal Y。
                if (_Mode < 3.5)
                    return half4(saturate(col.ggg * _Exposure), 1);

                // 4. B：encoded normal Z。
                if (_Mode < 4.5)
                    return half4(saturate(col.bbb * _Exposure), 1);

                // 5. NormalDiff：默认法线差异。适合看水波法线有没有写进去。
                if (_Mode < 5.5)
                {
                    half3 neutral = half3(0.5h, 0.5h, 1.0h);
                    half3 diff = abs(col.rgb - neutral) * _NormalDiffStrength;
                    return half4(saturate(diff) * heightMagnitude, 1);
                }

                // 6. NormalEncoded：把 encoded normal 解码再编码查看。
                if (_Mode < 6.5)
                {
                    half3 n = normalize(col.rgb * 2.0h - 1.0h);
                    return half4(n * 0.5h + 0.5h, 1);
                }

                // 7. RGB + Height 背景：用高度变化把水波叠到背景上。
                if (_Mode < 7.5)
                {
                    half3 rgbOnBg = lerp(_BackgroundColor.rgb, col.rgb, heightMagnitude);
                    return half4(saturate(rgbOnBg * _Exposure), 1);
                }

                // 8. WaterSignedHeightA：直接查看 signed height。中灰 = 无变化，黑 = 下陷，白 = 隆起。
                if (_Mode < 8.5)
                {
                    half signedView = saturate(signedHeight * _Exposure * 0.5h + 0.5h);
                    return half4(signedView.xxx, 1);
                }

                // 9. WaterHeightMagnitudeA：查看绝对波纹强度。黑 = 无变化，白 = 强变化。
                if (_Mode < 9.5)
                {
                    half mag = saturate(heightMagnitude * _Exposure);
                    return half4(mag.xxx, 1);
                }

                // 10. WaterMaskA：水波影响范围。当前协议下等同于 signed height 的绝对值。
                if (_Mode < 10.5)
                {
                    half mask = saturate(heightMagnitude * _Exposure);
                    return half4(mask.xxx, 1);
                }

                // 11. WaterComposite：水波综合调试。
                // R/G 显示 encoded normal 的 XY 偏移，B 显示高度强度。
                half2 normalOffset = abs(col.rg - half2(0.5h, 0.5h)) * 2.0h;
                half3 composite = half3(normalOffset.x, normalOffset.y, heightMagnitude);
                composite = lerp(_BackgroundColor.rgb, composite, saturate(max(max(composite.r, composite.g), composite.b)));
                return half4(saturate(composite * _Exposure), 1);
            }

            ENDHLSL
        }
    }
}
