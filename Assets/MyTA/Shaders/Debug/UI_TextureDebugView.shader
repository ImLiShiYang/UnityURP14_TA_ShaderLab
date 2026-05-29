Shader "Debug/UI_TextureDebugView"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}

        // 0  RGB
        // 1  Alpha
        // 2  R
        // 3  G
        // 4  B
        // 5  NormalDiff
        // 6  NormalEncoded
        // 7  RGBWithAlphaBackground
        // 8  SnowSinkR
        // 9  SnowRimG
        // 10 SnowMaskA
        // 11 SnowComposite
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

                // 0. RGB：普通彩色查看。
                if (_Mode < 0.5)
                    return half4(saturate(col.rgb * _Exposure), 1);

                // 1. Alpha：查看 A 通道。
                if (_Mode < 1.5)
                    return half4(saturate(col.aaa * _Exposure), 1);

                // 2. R：旧系统可看 R，新雪地系统中 R = sink 下陷深度。
                if (_Mode < 2.5)
                    return half4(saturate(col.rrr * _Exposure), 1);

                // 3. G：旧系统可看 G，新雪地系统中 G = rim 雪边凸起。
                if (_Mode < 3.5)
                    return half4(saturate(col.ggg * _Exposure), 1);

                // 4. B：旧系统可看 B，新雪地系统暂时预留。
                if (_Mode < 4.5)
                    return half4(saturate(col.bbb * _Exposure), 1);

                // 5. NormalDiff：旧法线 RT 调试用。
                // 新雪地 RT 是黑底数据图，不建议用这个模式判断雪下陷。
                if (_Mode < 5.5)
                {
                    half3 neutral = half3(0.5h, 0.5h, 1.0h);
                    half3 diff = abs(col.rgb - neutral) * _NormalDiffStrength;
                    half mask = step(0.001h, col.a);
                    return half4(saturate(diff) * mask, 1);
                }

                // 6. NormalEncoded：旧法线 RT 调试用。
                if (_Mode < 6.5)
                {
                    half3 n = normalize(col.rgb * 2.0h - 1.0h);
                    return half4(n * 0.5h + 0.5h, 1);
                }

                // 7. RGB + Alpha 背景：适合旧脚印 RT 看 alpha 覆盖区。
                if (_Mode < 7.5)
                {
                    half3 rgbOnBg = lerp(_BackgroundColor.rgb, col.rgb, col.a);
                    return half4(saturate(rgbOnBg * _Exposure), 1);
                }

                // 8. SnowSinkR：雪地下陷强度。黑=无下陷，白=最大下陷。
                if (_Mode < 8.5)
                {
                    half sink = saturate(col.r * _Exposure);
                    return half4(sink.xxx, 1);
                }

                // 9. SnowRimG：雪边凸起强度。第一阶段通常应该全黑或很弱。
                if (_Mode < 9.5)
                {
                    half rim = saturate(col.g * _Exposure);
                    return half4(rim.xxx, 1);
                }

                // 10. SnowMaskA：Brush 覆盖 mask。黑=无 brush，白=有 brush。
                if (_Mode < 10.5)
                {
                    half mask = saturate(col.a * _Exposure);
                    return half4(mask.xxx, 1);
                }

                // 11. SnowComposite：雪地专用合成调试。
                // R 通道显示下陷，G 通道显示雪边，B 通道显示 mask，方便一眼看数据是否串通道。
                half sinkC = saturate(col.r * _Exposure);
                half rimC = saturate(col.g * _Exposure);
                half maskC = saturate(col.a * _Exposure);
                half3 composite = half3(sinkC, rimC, maskC);
                composite = lerp(_BackgroundColor.rgb, composite, saturate(max(max(sinkC, rimC), maskC)));
                return half4(composite, 1);
            }

            ENDHLSL
        }
    }
}
