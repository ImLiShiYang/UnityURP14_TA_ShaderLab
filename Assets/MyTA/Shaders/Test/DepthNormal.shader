Shader "Unlit/DepthNormal"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "DepthNormalsPass"
            Tags{"LightMode" = "DepthNormals"}

            ZWrite On          // 开启深度写入
            ZTest LEqual       // 标准深度测试

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 包含必要的HLSL库
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
                Varyings output = (Varyings)0;
                // 计算裁剪空间位置
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 将法线转换到世界空间
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_TARGET
            {
                // 法线编码：从范围[-1, 1]映射到[0, 1]
                half3 normalWS = normalize(input.normalWS);
                return half4(normalWS * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}
