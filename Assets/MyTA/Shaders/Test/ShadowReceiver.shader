Shader "Unlit/ShadowReceiver"
{
    
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _MainTex ("Main Texture", 2D) = "white" {}
        _ShadowIntensity ("Shadow Intensity", Range(0,1)) = 0.8
        _CustomShadowBias ("Shadow Bias", Range(0,0.1)) = 0.001
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 属性声明
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _MainTex_ST;
                float _ShadowIntensity;
                float _CustomShadowBias;
            CBUFFER_END

            // 全局深度纹理（由您的 Feature 生成）
            TEXTURE2D(_MyCustomDepthTexture);
            SAMPLER(sampler_MyCustomDepthTexture);
            TEXTURE2D(_MainTex);        
            SAMPLER(sampler_MainTex);

            // 需要从 C# 脚本设置的矩阵：将世界坐标转换到光源裁剪空间
            float4x4 _WorldToLightClipMatrix;
            float4 _LightCameraZBufferParams;
            float4x4 _WorldToLightViewMatrix;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 lightClipPos  : TEXCOORD3;   
            };

            // 解码函数（与您的编码函数对应）
            float DecodeFloatRGBA(float4 enc)
            {
                float4 kDecodeDot = float4(1.0, 1.0/255.0, 1.0/65025.0, 1.0/16581375.0);
                return dot(enc, kDecodeDot);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                // 世界空间位置
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = posInput.positionWS;
                output.positionCS = posInput.positionCS;

                // 法线
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                output.normalWS = normalInput.normalWS;

                // UV
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

               output.lightClipPos = mul(_WorldToLightClipMatrix, float4(output.positionWS, 1.0));

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 基础颜色
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 albedo = texColor.rgb * _BaseColor.rgb;

                // 简单主光源照明
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half3 lighting = albedo * mainLight.color * NdotL;

                // ========== 自定义阴影计算 ==========
                float shadowAtten = 1.0;
                
                // 1. 获取绝对世界坐标
                float3 absoluteWS = GetAbsolutePositionWS(input.positionWS);

                // 2. 计算当前片元在光源相机下的深度 (使用绝对坐标)
                float currentDepth = -mul(_WorldToLightViewMatrix, float4(absoluteWS, 1.0)).z;
                currentDepth -= _CustomShadowBias;

                // 3. 计算 UV
                float4 lightClipPos = mul(_WorldToLightClipMatrix, float4(absoluteWS, 1.0));
                float3 lightNDC = lightClipPos.xyz / lightClipPos.w;
                float2 shadowUV = lightNDC.xy * 0.5 + 0.5;

                // 处理不同平台的 Y 翻转问题
                #if UNITY_UV_STARTS_AT_TOP
                    shadowUV.y = 1.0 - shadowUV.y;
                #endif

                // 用同一套 View Space 深度定义
                // float currentDepth = -mul(_WorldToLightViewMatrix, float4(input.positionWS, 1.0)).z;
                // currentDepth -= _CustomShadowBias;
                //
                // // / input.lightClipPos.w
                // float3 lightNDC = input.lightClipPos.xyz;
                // float2 shadowUV = lightNDC.xy * 0.5 + 0.5;
                // shadowUV.y = 1.0 - shadowUV.y;
                
                float sampledDepth=0;
                // 5. 边界检查：超出纹理范围的不计算阴影（视为无阴影）
                if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && shadowUV.y >= 0.0 && shadowUV.y <= 1.0)
                {
                    // 采样编码后的深度纹理并解码
                    sampledDepth = SAMPLE_TEXTURE2D(_MyCustomDepthTexture, sampler_MyCustomDepthTexture, shadowUV).r;
                    // shadowMapDepth = DecodeFloatRGBA(encodedDepth);

                    
                    if (currentDepth > sampledDepth)
                    {
                        shadowAtten = 1.0 - _ShadowIntensity; // 处于阴影中
                    }
                }

                // 将阴影衰减应用到最终颜色
                half3 finalColor = lighting * shadowAtten;

                // 添加一点环境光避免全黑
                finalColor += albedo * 0.1;

                // return half4(finalColor, 1.0);
                // return half4(sampledDepth,sampledDepth,sampledDepth, 1.0);
                // return half4(currentDepth,currentDepth,currentDepth, 1.0);
                // return half4(currentDepth/20,currentDepth/20,currentDepth/20, 1.0);
                return half4(currentDepth-sampledDepth,currentDepth-sampledDepth,currentDepth-sampledDepth, 1.0);
                // return half4(shadowUV, 0.0, 1.0);
            }
            ENDHLSL
        }
    }


}
