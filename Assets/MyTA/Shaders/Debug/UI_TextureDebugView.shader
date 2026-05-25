Shader "Debug/UI_TextureDebugView"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}

        // 0 RGB
        // 1 Alpha
        // 2 R
        // 3 G
        // 4 B
        // 5 NormalDiff
        // 6 NormalEncoded
        // 7 RGBWithAlphaBackground
        _Mode ("Mode", Float) = 0

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
            Name "UITextureDebug"

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
                
                
                
                // RGB
                if (_Mode < 0.5)
                    return half4(saturate(col.rgb * _Exposure), 1);

                // Alpha
                if (_Mode < 1.5)
                {
                    // half a = col.a;
                    // // return half4(saturate(a * 50.0h).xxx, 1);
                    //
                    // half visible = step(0.005h, a);
                    // return half4(visible.xxx, 1);
                    
                    return half4(saturate(col.aaa * _Exposure), 1);
                }
                    

                // R
                if (_Mode < 2.5)
                    return half4(saturate(col.rrr * _Exposure), 1);

                // G
                if (_Mode < 3.5)
                    return half4(saturate(col.ggg * _Exposure), 1);

                // B
                if (_Mode < 4.5)
                    return half4(saturate(col.bbb * _Exposure), 1);

                // NormalDiff：看法线相对默认法线的变化
                // NormalDiff：只看 alpha 有效区域
                if (_Mode < 5.5)
                {
                    half3 neutral = half3(0.5h, 0.5h, 1.0h);
                    half3 diff = abs(col.rgb - neutral) * _NormalDiffStrength;

                    half mask = step(0.001h, col.a);

                    return half4(saturate(diff)*mask, 1);
                }

                // NormalEncoded：把 RGB 当 normalRGB 解码再编码显示
                if (_Mode < 6.5)
                {
                    half3 n = normalize(col.rgb * 2.0h - 1.0h);
                    return half4(n * 0.5h + 0.5h, 1);
                    // return half4(n,1);
                }

                // RGB + Alpha 背景
                half3 rgbOnBg = lerp(_BackgroundColor.rgb, col.rgb, col.a);
                return half4(saturate(rgbOnBg * _Exposure), 1);
            }

            ENDHLSL
        }
    }
}