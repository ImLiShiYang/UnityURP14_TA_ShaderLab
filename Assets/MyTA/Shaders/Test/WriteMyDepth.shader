Shader "Unlit/WriteMyDepth"
{

    SubShader
    {
        // 渲染不透明物体
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float depthVS       : TEXCOORD0; // 视图空间深度
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 1. 常规坐标转换
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // 2. 手算视图空间 (View Space) 坐标
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 positionVS = TransformWorldToView(positionWS);

                // 3. Unity 中相机看向 -Z 方向，所以物体在相机前方的深度等于 -positionVS.z
                output.depthVS = -positionVS.z;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // _ProjectionParams.z 是相机的远裁剪面 (Far Plane)
                // 我们自己把深度除以远裁剪面，映射到 0 ~ 1 的线性范围
                float linearDepth01 = input.depthVS / _ProjectionParams.z;

                // 也可以加个 saturate 防溢出
                linearDepth01 = saturate(linearDepth01);

                // 直接把我们自己算出的深度值当做颜色输出！
                return half4(linearDepth01, linearDepth01, linearDepth01, 1.0);
            }
            ENDHLSL
        }
    }

}
