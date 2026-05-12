Shader "Unlit/SimpleOverrideShader"
{

    Properties
    {
        _BaseColor ("Color", Color) = (1, 0, 0, 0.5) // 默认半透明红
        _NearEnhance ("Near Enhance", Range(0.1, 10)) = 1.0
    }

    SubShader
    {
        // 这个标签是必须的，让 RendererList 能够找到这个 Pass
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardPass"
            Tags { "LightMode" = "UniversalForward" } // 关键标签！
            
            ZTest LEqual
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _NearEnhance;
            CBUFFER_END
            
            float4 _LightCameraZBufferParams;
            float4x4 _WorldToLightClipMatrix;
            float4x4 _WorldToLightViewMatrix;
            float _CameraFarPlane;

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
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInput.positionCS;
                output.positionWS = posInput.positionWS;
                output.positionOS=input.positionOS;
                return output;
            }
            
            
            half4 frag(Varyings input) : SV_Target
            {
                // 统一深度定义：光源相机 View Space 的正距离
                float lightViewZ = mul(_WorldToLightViewMatrix, float4(input.positionWS, 1.0)).z;
                // float depth = -lightViewZ;
                
                // 1. 获取绝对世界坐标 (解决 URP 相机偏移问题)
                float3 absoluteWS = GetAbsolutePositionWS(input.positionWS);
                
                // 2. 计算在光源相机下的 View Space 坐标
                // 注意：这里建议直接使用 UNITY_MATRIX_V，因为它在渲染时就是光源相机的视图矩阵
                float4 viewPos = mul(UNITY_MATRIX_V, float4(absoluteWS, 1.0));
                float depth = -viewPos.z;
                
                // 归一化到 0~1（需外部传入远平面）
                float normalizedDepth = depth / _CameraFarPlane;
                
                return half4(depth, 0, 0, 1);
                // return half4(normalizedDepth, 0, 0, 1);
                // return half4(normalizedDepth, normalizedDepth, normalizedDepth, 1);
                // return half4(1, 1.0, 1.0, 1.0); 
            }
            ENDHLSL
        }
    }

}
