Shader "Unlit/ReadMyCustomDepth_Shadow_Fixed"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
        _DiffuseStrength("Diffuse Strength", Range(0, 1)) = 0.8

        [Enum(SampledDepth,0,LightUV,1,ReceiverDepth,2,DepthDifference,3,ShadowCompare,4)]
        _DebugMode("Debug Mode", Float) = 4

        _DepthBias("Depth Bias", Range(0, 0.05)) = 0.002
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.6
        
//        _FlipShadowUVX("Flip Shadow UV X", Float) = 0
//        _FlipShadowUVY("Flip Shadow UV Y", Float) = 0
        
        _PCFRadius("PCF Radius", Range(0, 3)) = 1
        [Toggle] _UsePCF("Use PCF", Float) = 1
        
        _NormalBias("Normal Bias World", Range(0, 0.1)) = 0.005
        _SlopeDepthBias("Slope Depth Bias", Range(0, 0.01)) = 0.001
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MyCustomDepthTexture);
            SAMPLER(sampler_MyCustomDepthTexture);

            float4x4 _WorldToLightUVMatrix;
            float4x4 _WorldToLightViewMatrix;
            float4 _CustomLightDepthParams;
            float4 _MyCustomDepthTexture_TexelSize;
            float3 _CustomLightDirectionWS;

            CBUFFER_START(UnityPerMaterial)
                float _AmbientStrength;
                float _DiffuseStrength;
            
                half4 _BaseColor;
                float _DebugMode;
                float _DepthBias;
                float _ShadowStrength;
            
                float _PCFRadius;
                float _UsePCF;
            
                float _NormalBias;
                float _SlopeDepthBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs vertexInput =GetVertexPositionInputs(input.positionOS.xyz);
                    
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                
                VertexNormalInputs normalInput =GetVertexNormalInputs(input.normalOS);
                output.normalWS = normalInput.normalWS;

                return output;
            }
            
            float SampleShadowTap(float2 uv, float receiverDepth, float bias)
            {
                if (uv.x < 0.0 || uv.x > 1.0 ||uv.y < 0.0 || uv.y > 1.0)
                {
                    return 0.0;
                }

                float d = SAMPLE_TEXTURE2D(_MyCustomDepthTexture,sampler_MyCustomDepthTexture,uv).r;
                
                // sampledDepth 接近 1 代表没有 caster，视为亮
                if (d >= 0.999)
                {
                    return 0.0;
                }

                return receiverDepth > d + bias ? 1.0 : 0.0;
            }

            float SampleShadowPCF(float2 uv, float receiverDepth, float bias)
            {
                float2 texelSize = _MyCustomDepthTexture_TexelSize.xy * max(_PCFRadius, 0.01);
                float shadow = 0.0;
                float weightSum = 0.0;

                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float weight = (3.0 - abs((float)x)) * (3.0 - abs((float)y));
                        shadow += SampleShadowTap(uv + texelSize * float2(x, y), receiverDepth, bias) * weight;
                        weightSum += weight;
                    }
                }

                return shadow / weightSum;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                
                float3 normalWS = normalize(input.normalWS);
                
                // 光线从光源射向场景。
                // 反方向就是“指向光源”的方向。
                float3 lightDirWS = normalize(_CustomLightDirectionWS);
                float3 toLightDirWS = -lightDirWS;
                
                // 沿法线把接收点稍微推出去，减少自身表面和 shadow map 深度几乎相等导致的 acne。
                float3 receiverPositionWS =input.positionWS + normalWS * _NormalBias;
                    
                
                // 世界坐标 -> 光源 UV
                float4 lightUVH = mul(_WorldToLightUVMatrix,float4(receiverPositionWS, 1.0));
                

                float3 lightUVW = lightUVH.xyz / lightUVH.w;
                float2 lightUV = lightUVW.xy;


                // 当前像素在光源 View Space 中的深度
                float3 lightViewPos = mul(_WorldToLightViewMatrix,float4(receiverPositionWS, 1.0)).xyz;


                float viewDepth = -lightViewPos.z;

                float nearPlane = _CustomLightDepthParams.x;
                float farPlane  = _CustomLightDepthParams.y;

                // 注意：这里用 viewDepth 判断 near/far，不再用 lightUVW.z
                bool outside =
                    lightUV.x < 0.0 || lightUV.x > 1.0 ||
                    lightUV.y < 0.0 || lightUV.y > 1.0 ||
                    viewDepth < nearPlane ||
                    viewDepth > farPlane;

                // 临时调试：超出光源相机范围直接显示蓝色
                if (outside)
                {
                    return half4(0, 0, 1, 1);
                }

                float sampledDepth = SAMPLE_TEXTURE2D(_MyCustomDepthTexture,sampler_MyCustomDepthTexture,lightUV).r;

                float receiverDepth = saturate((viewDepth - nearPlane) * _CustomLightDepthParams.z );
                
                // sampledDepth 接近 1 说明这个位置基本是清屏区域，没有 caster
                float hasCaster = sampledDepth < 0.999;

                // 0: 采样到的 shadow map 深度
                if (_DebugMode < 0.5)
                {
                    return half4(sampledDepth.xxx, 1);
                }

                // 1: 光源 UV
                if (_DebugMode < 1.5)
                {
                    return half4(lightUV.x, lightUV.y, 0, 1);
                }

                // 2: 当前像素自身在光源视角下的线性深度
                if (_DebugMode < 2.5)
                {
                    return half4(receiverDepth.xxx, 1);
                }

                float diff = receiverDepth - sampledDepth;

                // 3: 深度差
                if (_DebugMode < 3.5)
                {
                    // 如果没有 caster，直接显示黑色
                    // 这样你就不会被 clear=1 的区域干扰
                    if (!hasCaster)
                    {
                        return half4(1, 0, 0, 1);
                    }

                    // 越亮表示 receiverDepth 比 sampledDepth 更远
                    // 也就是越可能在阴影里
                    float vis = saturate(diff * 50.0);
                    return half4(vis.xxx, 1);
                }

                // 4: Shadow Compare

                float NoL = saturate(dot(normalWS, toLightDirWS));
                
                // 面越斜，bias 越大。
                // 正对光源时 NoL 接近 1，bias 小。
                // 掠射角时 NoL 接近 0，bias 大。
                float slopeBias = _SlopeDepthBias * (1.0 - NoL);

                float totalBias = _DepthBias + slopeBias;
                
                float shadow;
                if (_UsePCF > 0.5 && _PCFRadius > 0.0)
                {
                    shadow = SampleShadowPCF(lightUV,receiverDepth,totalBias);
                }
                else
                {
                    // 关闭 PCF：只做一次普通 shadow map 深度比较
                    shadow = SampleShadowTap(lightUV,receiverDepth,totalBias);
                }


                float shadowFactor = lerp(1.0,1.0 - _ShadowStrength,shadow);
                
                float3 ambient=SampleSH(normalWS)*_AmbientStrength*_BaseColor.rgb;
                float3 diffuse = NoL * _DiffuseStrength*_BaseColor.rgb;

                float3 finalColor =saturate(ambient + diffuse * shadowFactor);

                return float4(finalColor, _BaseColor.a);
            }

            ENDHLSL
        }
    }
}