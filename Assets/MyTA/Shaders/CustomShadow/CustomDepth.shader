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

                // 把顶点从模型空间转换到各个常用空间。
                // pos.positionCS：裁剪空间位置，最终用于屏幕光栅化。
                // pos.positionVS：观察空间位置，也就是以相机为原点的坐标。
                VertexPositionInputs pos =GetVertexPositionInputs(input.positionOS.xyz);                    

                // 输出顶点的裁剪空间位置。
                // positionCS 是顶点着色器必须输出的结果。
                output.positionCS = pos.positionCS;

                // 记录顶点到相机的观察空间深度。
                //
                // Unity 的观察空间中：
                // 相机前方通常是 Z 轴负方向。
                // 所以前方物体的 pos.positionVS.z 一般是负数。
                //
                // 加上负号后：
                // 相机前方 5 米的位置，深度就会变成正数 5。
                output.viewDepth = -pos.positionVS.z;

                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                float nearPlane = _ProjectionParams.y;
                float farPlane  = _ProjectionParams.z;

                // 把相机拍到的物体深度转换成线性的黑白深度图。
                float linear01Depth = saturate(
                    (input.viewDepth - nearPlane) / (farPlane - nearPlane)
                );

                return linear01Depth;
            }
            ENDHLSL
        }
    }


}
