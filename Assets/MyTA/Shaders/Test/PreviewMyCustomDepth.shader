Shader "Unlit/PreviewMyCustomDepth"
{
    Properties
    {
        _Intensity("Intensity", Range(0.1, 10)) = 1
        _Invert("Invert", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MyCustomDepthTexture);
            SAMPLER(sampler_MyCustomDepthTexture);

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Invert;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs pos =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = pos.positionCS;
                output.uv = input.uv;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = SAMPLE_TEXTURE2D(
                    _MyCustomDepthTexture,
                    sampler_MyCustomDepthTexture,
                    input.uv
                ).r;

                if (_Invert > 0.5)
                {
                    d = 1.0 - d;
                }

                d = saturate(d * _Intensity);

                return half4(d.xxx, 1);
            }

            ENDHLSL
        }
    }
}