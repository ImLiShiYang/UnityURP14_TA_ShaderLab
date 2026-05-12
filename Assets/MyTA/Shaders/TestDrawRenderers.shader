Shader "Unlit/TestDrawRenderers"
{
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "FullscreenDraw"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float _gShadowBias;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;

            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;

            };
            
            // 将 [0, 1) 范围的浮点数编码到 RGBA 四个 8 位通道中，以在低精度渲染目标（如 RenderTextureFormat.ARGB32）中实现高精度存储[reference:2]。
            float4 EncodeFloatRGBA(float v)
            {
                // 1. 将输入值乘以系数，把不同部分提取到四个通道中。
                float4 kEncodeMul = float4(1.0, 255.0, 65025.0, 16581375.0); // 分别是 1, 255, 255^2, 255^3
                float4 enc = kEncodeMul * v;
                
                // 2. 取小数部分，保留各通道独立的信息。
                enc = frac(enc);
                
                // 3. 从前一个通道中减去后一个通道的信息，避免精度重叠。
                enc -= enc.yzww * float4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);
                
                return enc;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                
                
                #if UNITY_UV_STARTS_AT_TOP
                    output.positionCS.y = -output.positionCS.y;
                #endif
                output.uv = uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {

                // 明显可见的红绿渐变效果
                return half4(input.uv.x, input.uv.y, 1, 1.0);
                // return EncodeFloatRGBA(depth);
            }
            ENDHLSL
        }
    }


}
