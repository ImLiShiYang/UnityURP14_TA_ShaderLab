Shader "Custom/ScreenSpaceDecal"
{
    Properties
    {
        _DecalTexture("Decal Texture", 2D) = "white" {}
        _DecalColor("Decal Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ScreenSpaceDecal"

            ZWrite Off
            ZTest Always
            Cull Off

            // 直接叠加到当前 Camera Color 上。
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DecalTexture);
            SAMPLER(sampler_DecalTexture);

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4x4 _DecalWorldToLocal;
            float4 _DecalColor;

            // x = opacity
            // y = edgeFade
            float4 _DecalParams;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV   : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // 使用 URP 提供的全屏三角形顶点函数。
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.screenUV = GetFullScreenTriangleTexCoord(input.vertexID);

                return output;
            }

            float3 ReconstructWorldPosition(float2 screenUV)
            {
                // 采样当前像素的深度。
                float rawDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture,sampler_CameraDepthTexture,screenUV).r;

                // 如果没有几何体，深度通常接近远平面。
                // 这种地方不应该显示 decal。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                    {
                        discard;
                    }
                #else
                    if (rawDepth >= 0.999999)
                    {
                        discard;
                    }
                #endif

                // 使用 URP Core.hlsl 提供的方法，从屏幕 UV + 深度还原世界坐标。
                float3 worldPos = ComputeWorldSpacePosition(screenUV,rawDepth,UNITY_MATRIX_I_VP);

                return worldPos;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 worldPos = ReconstructWorldPosition(input.screenUV);

                // 世界坐标转到 Decal 本地空间。
                float3 decalLocalPos =mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;
                    

                // 判断当前像素是否在 Decal Box 内。
                //
                // 我们约定：
                // local x/y/z 都在 -0.5 到 0.5 之间，
                // 表示这个点在贴花盒子内。
                float3 absLocal = abs(decalLocalPos);

                if (absLocal.x > 0.5 ||absLocal.y > 0.5 ||absLocal.z > 0.5)
                {
                    discard;
                }

                // local xy 转 decal uv。
                // float2 decalUV = decalLocalPos.xy + 0.5;
                
                // local xz 转 decal uv。
                // 适合投射到 Unity 常见的水平地面。
                float2 decalUV = decalLocalPos.xz + 0.5;

                half4 decalTex =SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, decalUV);
                    

                half4 color = decalTex * _DecalColor;

                // 盒子边缘淡出。
                //
                // distToBoxEdge 越接近 0，说明越靠近盒子边缘。
                float distToBoxEdge = min(min(0.5 - absLocal.x, 0.5 - absLocal.y),0.5 - absLocal.z);
                
                
                // 只根据 x/z 平面做贴花边缘淡出。
                // y 只是投射厚度，不参与贴图边缘。
                float distToPlaneEdge = min(0.5 - absLocal.x,0.5 - absLocal.z);

                float edgeFade = max(_DecalParams.y, 0.0001);
                float fade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                color.a *= _DecalParams.x;
                color.a *= fade;

                return color;
            }

            ENDHLSL
        }
    }
}