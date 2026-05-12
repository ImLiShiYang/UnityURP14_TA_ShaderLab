Shader "Unlit/DebugWorldNormal"
{
    Properties
    {
        // 无属性，极简
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 顶点位置转换到裁剪空间
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 将法线从对象空间转换到世界空间
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 将法线从 [-1,1] 映射到 [0,1] 作为颜色输出
                half3 normalColor = input.normalWS * 0.5 + 0.5;
                return half4(normalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
