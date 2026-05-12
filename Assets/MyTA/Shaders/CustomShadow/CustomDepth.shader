Shader "Unlit/CustomDepth"
{
    
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        Pass
        {
            Name "LinearDepthOnly"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float viewDepth   : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.viewDepth = -pos.positionVS.z;

                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                float nearPlane = _ProjectionParams.y;
                float farPlane  = _ProjectionParams.z;

                float linear01Depth = saturate(
                    (input.viewDepth - nearPlane) / (farPlane - nearPlane)
                );

                return linear01Depth;
            }
            ENDHLSL
        }
    }


}
