Shader "Snow/SnowCircleBrush"
{
    Properties
    {
        _SinkStrength ("Sink Strength", Range(0, 1)) = 1
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "SnowCircleBrush"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            // 多个 brush 同一帧重叠时，取更强的那个
            BlendOp Max
            Blend One One

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SinkStrength;
                float _Softness;
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

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;

                float d = length(p);

                float inner = saturate(1.0 - _Softness);

                float mask = 1.0 - smoothstep(inner, 1.0, d);

                float sink = mask * _SinkStrength;

                return half4(sink, 0.0, 0.0, mask);
            }

            ENDHLSL
        }
    }
}