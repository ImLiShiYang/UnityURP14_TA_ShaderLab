Shader "Unlit/EdgeDetect"
{

    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _EdgeOnly ("Edge Only", Range(0,1)) = 0
        _EdgeColor ("Edge Color", Color) = (0,0,0,1)
        _BackgroundColor ("Background Color", Color) = (1,1,1,1)
        _SampleDistance ("Sample Distance", Range(0.1,3)) = 1
        _Sensitivity ("Sensitivity (Normal, Depth)", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "EdgeDetectPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 注意：不包含 Blit.hlsl 以避免结构体重名

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            TEXTURE2D_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            TEXTURE2D(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);

            float _EdgeOnly;
            float4 _EdgeColor;
            float4 _BackgroundColor;
            float _SampleDistance;
            float4 _Sensitivity;   // x: normal sensitivity, y: depth sensitivity

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv[5] : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float2 uv = input.uv;

                output.uv[0] = uv;

                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    uv.y = 1 - uv.y;
                #endif

                float2 offset = _MainTex_TexelSize.xy * _SampleDistance;
                output.uv[1] = uv + offset * float2( 1, 1);
                output.uv[2] = uv + offset * float2(-1,-1);
                output.uv[3] = uv + offset * float2(-1, 1);
                output.uv[4] = uv + offset * float2( 1,-1);

                return output;
            }

            // 比较两个像素的法线和深度是否相似
            half CheckSame(float2 uvCenter, float2 uvSample)
            {
                // 获取法线 (视角空间，范围 [-1,1] 映射到 [0,1])
                float3 normalCenter = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, uvCenter).xyz * 2.0 - 1.0;
                float3 normalSample = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, uvSample).xyz * 2.0 - 1.0;

                // 获取线性深度 (Eye depth)
                float depthCenter = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvCenter);
                float depthSample = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uvSample);
                depthCenter = LinearEyeDepth(depthCenter, _ZBufferParams);
                depthSample = LinearEyeDepth(depthSample, _ZBufferParams);

                // 法线差异
                float3 diffNormal = abs(normalCenter - normalSample) * _Sensitivity.x;
                bool isSameNormal = (diffNormal.x + diffNormal.y + diffNormal.z) < 0.1;

                // 深度差异，并按距离缩放阈值
                float diffDepth = abs(depthCenter - depthSample) * _Sensitivity.y;
                bool isSameDepth = diffDepth < 0.1 * depthCenter;

                return (isSameNormal && isSameDepth) ? 1.0 : 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 采样四个方向的像素（直接从法线纹理获取，因为只需法线和深度信息）
                half4 sample1 = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, input.uv[1]);
                half4 sample2 = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, input.uv[2]);
                half4 sample3 = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, input.uv[3]);
                half4 sample4 = SAMPLE_TEXTURE2D(_CameraNormalsTexture, sampler_CameraNormalsTexture, input.uv[4]);

                half edge = 1.0;
                edge *= CheckSame(input.uv[1], input.uv[2]);
                edge *= CheckSame(input.uv[3], input.uv[4]);

                float4 originalColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv[0]);
                float4 withEdgeColor = lerp(_EdgeColor, originalColor, edge);
                float4 onlyEdgeColor = lerp(_EdgeColor, _BackgroundColor, edge);

                return lerp(withEdgeColor, onlyEdgeColor, _EdgeOnly);
                // return  sample1;
            }
            
            ENDHLSL
        }
    }
    FallBack Off

}
