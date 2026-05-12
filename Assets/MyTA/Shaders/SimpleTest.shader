Shader "Unlit/SimpleTest"
{

    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        X("X",Float)=1
        Y("Y",Float)=1
        Z("Z",Float)=1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
                        
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float X;
            float Y;
            float Z;
            
            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 直接返回红色
                float res=clamp(X,Y,Z);
                return half4(res.xxx, 1);
            }
            ENDHLSL
        }
    }

}
