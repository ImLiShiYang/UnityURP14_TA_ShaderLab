Shader "Custom/URP/waterripple_wave_equation"
{
    Properties
    {
        // _MainTex 是当前帧 Brush 输入，Prev/PrevPrev 是上一帧和上上帧水面高度。
        // RT 协议：A = signed height 编码值，0.5 表示没有波动。
        _MainTex ("Input", 2D) = "gray" {}
        _PrevTex ("Prev", 2D) = "gray" {}
        _PrevPrevTex ("PrevPrev", 2D) = "gray" {}
        _PrevOffset ("Prev Offset", Vector) = (0,0,0,0)
        _PrevPrevOffset ("Prev Prev Offset", Vector) = (0,0,0,0)

        // x = wave factor，控制扩散速度；y = decay，控制能量衰减。
        _Param ("Factor", Vector) = (0.25, 0.995, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        ZTest Always
        Cull Off
        ZWrite Off

        Pass
        {
            Name "WaveEquation"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_PrevTex);
            SAMPLER(sampler_PrevTex);

            TEXTURE2D(_PrevPrevTex);
            SAMPLER(sampler_PrevPrevTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Param;
                float2 _Stride;
                float2 _PrevOffset;
                float2 _PrevPrevOffset;
            CBUFFER_END

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half ReadHeight_Input(float2 uv)
            {
                // Alpha 从 0~1 解回 -1~1，后面所有计算都使用 signed height。
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * 2.0h - 1.0h;
            }

            half UVInside(float2 uv)
            {
                return step(0.0h, uv.x) *
                       step(0.0h, uv.y) *
                       step(uv.x, 1.0h) *
                       step(uv.y, 1.0h);
            }

            half ReadHeight_Prev(float2 uv)
            {
                half inside = UVInside(uv);
                half height = SAMPLE_TEXTURE2D(_PrevTex, sampler_PrevTex, uv).a * 2.0h - 1.0h;
                return height * inside;
            }

            half ReadHeight_PrevPrev(float2 uv)
            {
                half inside = UVInside(uv);
                half height = SAMPLE_TEXTURE2D(_PrevPrevTex, sampler_PrevPrevTex, uv).a * 2.0h - 1.0h;
                return height * inside;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float2 prevUV = uv - _PrevOffset;
                float2 prevPrevUV = uv - _PrevPrevOffset;

                half prev = ReadHeight_Prev(prevUV);

                // 采样四邻域，用它们和中心点做一个简单 Laplacian。
                // _Stride 由 RTManager 设置为 1 / textureSize。
                half prevL = ReadHeight_Prev(prevUV + float2(-_Stride.x, 0));
                half prevR = ReadHeight_Prev(prevUV + float2( _Stride.x, 0));
                half prevT = ReadHeight_Prev(prevUV + float2(0,  _Stride.y));
                half prevB = ReadHeight_Prev(prevUV + float2(0, -_Stride.y));

                half prevPrev = ReadHeight_PrevPrev(prevPrevUV);

                half value =prev * 2.0h+ (prevL + prevR + prevT + prevB - prev * 4.0h) * _Param.x- prevPrev;
                
                // Brush 输入作为外力注入本帧高度。
                value += ReadHeight_Input(uv);

                // 衰减避免波一直保持能量。
                value *= _Param.y;

                value = clamp(value, -1.0h, 1.0h);
                
                // 输出继续沿用水波 RT 协议：RGB 保持默认法线，A 保存新高度。
                half encodedHeight = value * 0.5h + 0.5h;

                return half4(0.5h, 0.5h, 1.0h, encodedHeight);
            }
            ENDHLSL
        }
    }
}
