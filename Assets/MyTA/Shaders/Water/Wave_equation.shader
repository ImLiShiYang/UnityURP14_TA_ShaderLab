Shader "Custom/URP/wave_equation"
{
    Properties
    {
        _InputTex ("Input", 2D) = "gray" {}
        _PrevTex ("Prev", 2D) = "gray" {}
        _PrevPrevTex ("PrevPrev", 2D) = "gray" {}
        _Param ("Factor", Vector) = (0.25, 0.995, 0, 0)
        _RoundAdjuster ("Round Adjuster", Float) = 0
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

            TEXTURE2D(_InputTex);
            SAMPLER(sampler_InputTex);

            TEXTURE2D(_PrevTex);
            SAMPLER(sampler_PrevTex);

            TEXTURE2D(_PrevPrevTex);
            SAMPLER(sampler_PrevPrevTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Param;
                float2 _Stride;
                float _RoundAdjuster;
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
                return SAMPLE_TEXTURE2D(_InputTex, sampler_InputTex, uv).r * 2.0h - 1.0h;
            }

            half ReadHeight_Prev(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_PrevTex, sampler_PrevTex, uv).r * 2.0h - 1.0h;
            }

            half ReadHeight_PrevPrev(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_PrevPrevTex, sampler_PrevPrevTex, uv).r * 2.0h - 1.0h;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                half prev = ReadHeight_Prev(uv);

                half prevL = ReadHeight_Prev(uv + float2(-_Stride.x, 0));
                half prevR = ReadHeight_Prev(uv + float2( _Stride.x, 0));
                half prevT = ReadHeight_Prev(uv + float2(0,  _Stride.y));
                half prevB = ReadHeight_Prev(uv + float2(0, -_Stride.y));

                half prevPrev = ReadHeight_PrevPrev(uv);

                half value =
                    prev * 2.0h
                    + (prevL + prevR + prevT + prevB - prev * 4.0h) * _Param.x
                    - prevPrev;

                value += ReadHeight_Input(uv);

                value *= _Param.y;

                value = (value + 1.0h) * 0.5h;

                value += _RoundAdjuster;

                return half4(value, value, value, 1);
            }
            ENDHLSL
        }
    }
}