Shader "Unlit/ReadMyCustomDepth_Shadow_Fixed"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
        _DiffuseStrength("Diffuse Strength", Range(0, 1)) = 0.8

        // ================= CSM 改动：DebugMode 新增 CascadeIndex 调试模式 =================
        [Enum(SampledDepth,0,LightUV,1,ReceiverDepth,2,DepthDifference,3,ShadowCompare,4,CascadeIndex,5)]
        _DebugMode("Debug Mode", Float) = 4
        
        // ================= CSM 改动：Cascade 距离分割 =================
        // 小于这个距离的像素使用 Cascade 0，大于这个距离的像素使用 Cascade 1
        _CascadeSplit0("Cascade Split 0", Float) = 15
        
        // 超过这个距离就不再计算自定义阴影
        _CascadeMaxDistance("Cascade Max Distance", Float) = 1000

        _DepthBias("Depth Bias", Range(0, 0.05)) = 0.002
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.6
        
//        _FlipShadowUVX("Flip Shadow UV X", Float) = 0
//        _FlipShadowUVY("Flip Shadow UV Y", Float) = 0
        
        _PCFRadius("PCF Radius", Range(0, 3)) = 1
        [Toggle] _UsePCF("Use PCF", Float) = 1
        
        _NormalBias("Normal Bias World", Range(0, 0.1)) = 0.005
        _SlopeDepthBias("Slope Depth Bias", Range(0, 0.01)) = 0.001
        
        // ================= CSM 改动：是否启用级联阴影 =================
        [Toggle] _UseCSM("Use Cascade Shadow", Float) = 1
        
        [Toggle] _UsePCSS("Use PCSS", Float) = 0
        _PCSSLightSize("PCSS Light Size", Range(0, 40)) = 6
        _PCSSBlockerSearchRadius("PCSS Blocker Search Radius", Range(1, 16)) = 4
        _PCSSMaxFilterRadius("PCSS Max Filter Radius", Range(1, 32)) = 12
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

            // ================= CSM 改动：每个 Cascade 一张 ShadowMap =================
            TEXTURE2D(_MyCustomDepthTexture0);
            SAMPLER(sampler_MyCustomDepthTexture0);

            TEXTURE2D(_MyCustomDepthTexture1);
            SAMPLER(sampler_MyCustomDepthTexture1);

            // ================= CSM 改动：每个 Cascade 一套矩阵 =================
            float4x4 _WorldToLightUVMatrix0;
            float4x4 _WorldToLightUVMatrix1;
            
            float4x4 _WorldToLightViewMatrix0;
            float4x4 _WorldToLightViewMatrix1;

            // ================= CSM 改动：每个 Cascade 一套 near/far/depthRange 参数 =================
            float4 _CustomLightDepthParams0;
            float4 _CustomLightDepthParams1;
            
            // ================= CSM 改动：每个 Cascade 一套 TexelSize，方便不同分辨率 =================
            float4 _MyCustomDepthTexture0_TexelSize;
            float4 _MyCustomDepthTexture1_TexelSize;
            
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
            
                // Use PCSS：是否开启 PCSS。
                float _UsePCSS;
                // PCSS Light Size：模拟光源尺寸，越大阴影越软。
                float _PCSSLightSize;
                // PCSS Blocker Search Radius：找遮挡物时搜索范围。
                float _PCSSBlockerSearchRadius;
                // PCSS Max Filter Radius：动态 PCF 最大半径，防止阴影糊成一片。
                float _PCSSMaxFilterRadius;
            
                float _NormalBias;
                float _SlopeDepthBias;
            
                // ================= CSM 改动：级联阴影开关 =================
                float _UseCSM;
            
                // ================= CSM 改动：材质面板控制 Cascade 分割距离 =================
                float _CascadeSplit0;
                float _CascadeMaxDistance;
            CBUFFER_END

            // ================= CSM 改动：保存当前像素使用的 Cascade 阴影数据 =================
            struct CascadeShadowData
            {
                int cascadeIndex;
                float2 lightUV;
                float viewDepth;
                float nearPlane;
                float farPlane;
                float invDepthRange;
                bool outside;
            };
            
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
            
            // ================= CSM 改动：根据当前像素到主相机的距离选择 Cascade =================
            // _UseCSM 关闭时，永远使用 Cascade1，相当于单张 ShadowMap 模式
            int SelectCascadeIndex(float3 positionWS)
            {
                if (_UseCSM < 0.5)
                {
                    return 1;
                }

                // 把世界坐标转换到当前渲染相机的 View Space。
                // 在 Unity 里，相机前方通常是 view space 的 -Z。
                float3 viewPos = TransformWorldToView(positionWS);

                // 所以前方深度要取 -viewPos.z，变成正数。
                float cameraViewDepth = -viewPos.z;

                return cameraViewDepth < _CascadeSplit0 ? 0 : 1;
            }
            
            
            // ================= CSM 改动：根据 CascadeIndex 采样对应 ShadowMap =================
            float SampleCascadeDepth(int cascadeIndex, float2 uv)
            {
                if (cascadeIndex == 0)
                {
                    return SAMPLE_TEXTURE2D(_MyCustomDepthTexture0, sampler_MyCustomDepthTexture0, uv).r;
                }
                else
                {
                    return SAMPLE_TEXTURE2D(_MyCustomDepthTexture1, sampler_MyCustomDepthTexture1, uv).r;
                }
            }
            
            // ================= CSM 改动：根据 CascadeIndex 获取对应 ShadowMap 的 texelSize =================
            float2 GetCascadeTexelSize(int cascadeIndex)
            {
                if (cascadeIndex == 0)
                {
                    return _MyCustomDepthTexture0_TexelSize.xy;
                }
                else
                {
                    return _MyCustomDepthTexture1_TexelSize.xy;
                }
            }
            
            
            // ================= CSM 改动：根据 CascadeIndex 使用对应矩阵，把世界坐标转到对应 ShadowMap 空间 =================
            CascadeShadowData GetCascadeShadowData(float3 originalPositionWS,float3 receiverPositionWS)
            {
                CascadeShadowData data;

                data.cascadeIndex = SelectCascadeIndex(originalPositionWS);

                float4 lightUVH;
                float3 lightViewPos;
                float4 depthParams;

                if (data.cascadeIndex == 0)
                {
                    lightUVH = mul(_WorldToLightUVMatrix0, float4(receiverPositionWS, 1.0));
                    lightViewPos = mul(_WorldToLightViewMatrix0, float4(receiverPositionWS, 1.0)).xyz;
                    depthParams = _CustomLightDepthParams0;
                }
                else
                {
                    lightUVH = mul(_WorldToLightUVMatrix1, float4(receiverPositionWS, 1.0));
                    lightViewPos = mul(_WorldToLightViewMatrix1, float4(receiverPositionWS, 1.0)).xyz;
                    depthParams = _CustomLightDepthParams1;
                }

                float3 lightUVW = lightUVH.xyz / lightUVH.w;

                data.lightUV = lightUVW.xy;
                data.viewDepth = -lightViewPos.z;
                data.nearPlane = depthParams.x;
                data.farPlane = depthParams.y;
                data.invDepthRange = depthParams.z;

                data.outside = data.lightUV.x < 0.0 || data.lightUV.x > 1.0 || data.lightUV.y < 0.0 || data.lightUV.y > 1.0 || data.viewDepth < data.nearPlane || data.viewDepth > data.farPlane;

                return data;
            }
            
            // 单点阴影比较。
            // 返回 0 = 不在阴影里。
            // 返回 1 = 在阴影里。
            float SampleShadowTap(int cascadeIndex, float2 uv, float receiverDepth, float bias)
            {
                // 超出 shadow map 范围，认为没有阴影。
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                {
                    return 0.0;
                }

                // 从自定义 shadow map 里读取深度。
                float d = SampleCascadeDepth(cascadeIndex, uv);

                // d 接近 1 表示这个 texel 是清屏值，没有 caster。
                // 没有 caster 就不产生阴影。
                if (d >= 0.999)
                {
                    return 0.0;
                }

                // receiverDepth 更远，说明光源先看到了 blocker，当前点在阴影里。
                return receiverDepth > d + bias ? 1.0 : 0.0;
            }
            
            // 带指定半径的 5x5 PCF。
            // radius 的单位是 shadow map texel 数量。
            // radius 越大，阴影边缘越软。
            float SampleShadowPCFRadius(int cascadeIndex,float2 uv, float receiverDepth, float bias, float radius)
            {
                float2 texelSize = GetCascadeTexelSize(cascadeIndex) * max(radius, 0.01);

                float shadow = 0.0;
                float weightSum = 0.0;

                // 5x5 tent filter。
                // 中心权重大，边缘权重小，比普通平均 5x5 更自然。
                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float wx = 3.0 - abs((float)x);
                        float wy = 3.0 - abs((float)y);
                        float weight = wx * wy;

                        float2 sampleUV = uv + texelSize * float2(x, y);
                        shadow += SampleShadowTap(cascadeIndex,sampleUV, receiverDepth, bias) * weight;
                        weightSum += weight;
                    }
                }

                return shadow / weightSum;
            }

            // 普通 PCF。
            // 使用材质里的 _PCFRadius。
            float SampleShadowPCF(int cascadeIndex, float2 uv, float receiverDepth, float bias)
            {
                return SampleShadowPCFRadius(cascadeIndex,uv, receiverDepth, bias, _PCFRadius);
            }

            // PCSS 第一步：搜索 blocker。
            // blocker 是比 receiver 更靠近光源的深度。
            // 返回平均 blocker 深度，输出 blockerCount。
            float FindAverageBlockerDepth(int cascadeIndex, float2 uv, float receiverDepth, float bias, out float blockerCount)
            {
                float2 texelSize = GetCascadeTexelSize(cascadeIndex) * _PCSSBlockerSearchRadius;

                float blockerDepthSum = 0.0;
                blockerCount = 0.0;

                // 这里用 5x5 搜索 blocker。
                // 搜索范围由 _PCSSBlockerSearchRadius 控制。
                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 sampleUV = uv + texelSize * float2(x, y);

                        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                        {
                            continue;
                        }

                        float d = SampleCascadeDepth(cascadeIndex, sampleUV);

                        // d < 0.999：这个 texel 里有 caster。
                        // d + bias < receiverDepth：这个 caster 比当前点更靠近光源，说明它可能遮挡当前点。
                        if (d < 0.999 && d + bias < receiverDepth)
                        {
                            blockerDepthSum += d;
                            blockerCount += 1.0;
                        }
                    }
                }

                // 没找到 blocker 时返回 1。
                // 1 表示清屏深度，不会产生阴影。
                return blockerCount > 0.5 ? blockerDepthSum / blockerCount : 1.0;
            }


            // PCSS 完整采样。
            // receiverDepth 是 0~1 线性深度。
            // receiverViewDepth 是光源相机 view space 下的真实线性深度。
            // nearPlane / farPlane 用来把 blocker 的 0~1 深度还原回 view depth。
            float SampleShadowPCSS(int cascadeIndex,float2 uv, float receiverDepth, float receiverViewDepth, float nearPlane, float farPlane, float bias)
            {
                // 1. 搜索 blocker。
                float blockerCount = 0.0;
                float avgBlockerDepth01 = FindAverageBlockerDepth(cascadeIndex,uv, receiverDepth, bias, blockerCount);

                // 没找到 blocker，说明当前点没有被遮挡。
                if (blockerCount < 0.5)
                {
                    return 0.0;
                }

                // 2. 把 blocker 的 0~1 线性深度还原成 view space 深度。
                float blockerViewDepth = lerp(nearPlane, farPlane, avgBlockerDepth01);

                // 3. 根据 receiver 和 blocker 的距离估算半影大小。
                // receiver 离 blocker 越远，penumbraRatio 越大，阴影越软。
                float penumbraRatio = max(receiverViewDepth - blockerViewDepth, 0.0) / max(blockerViewDepth, 0.001);

                // 4. 把半影比例换成 shadow map 里的采样半径。
                // _PCSSLightSize 越大，软阴影越明显。
                float filterRadius = penumbraRatio * _PCSSLightSize;

                // 5. 限制半径，避免过小或过大。
                filterRadius = clamp(filterRadius, _PCFRadius, _PCSSMaxFilterRadius);

                // 6. 用动态半径做 PCF。
                return SampleShadowPCFRadius(cascadeIndex,uv, receiverDepth, bias, filterRadius);
            }
            
            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                float3 lightDirWS = normalize(_CustomLightDirectionWS);
                float3 toLightDirWS = -lightDirWS;

                float3 receiverPositionWS = input.positionWS + normalWS * _NormalBias;

                float NoL = saturate(dot(normalWS, toLightDirWS));

                float3 ambient = SampleSH(normalWS) * _AmbientStrength * _BaseColor.rgb;
                float3 diffuse = NoL * _DiffuseStrength * _BaseColor.rgb;
                float3 unshadowedColor = saturate(ambient + diffuse);

                // ================= CSM 改动：超过最大阴影距离，不采样阴影 =================
                float3 mainCameraViewPos = TransformWorldToView(input.positionWS);
                float mainCameraViewDepth = -mainCameraViewPos.z;

                if (mainCameraViewDepth > _CascadeMaxDistance)
                {
                    return float4(unshadowedColor, _BaseColor.a);
                }

                // ================= CSM 改动：获取当前像素所属 Cascade 的阴影数据 =================
                CascadeShadowData shadowData = GetCascadeShadowData(input.positionWS,receiverPositionWS);

                // ================= CSM 改动：调试当前像素使用哪个 Cascade =================
                if (_DebugMode > 4.5)
                {
                    if (shadowData.cascadeIndex == 0)
                    {
                        return float4(1, 0, 0, 1); // 红色 = Cascade 0
                    }
                    else
                    {
                        return float4(0, 1, 0, 1); // 绿色 = Cascade 1
                    }
                }

                // 超出当前 Cascade 的 light camera 范围，直接不加阴影
                if (shadowData.outside)
                {
                    return float4(unshadowedColor, _BaseColor.a);
                }

                float2 lightUV = shadowData.lightUV;

                float sampledDepth = SampleCascadeDepth(shadowData.cascadeIndex, lightUV);

                float receiverDepth = saturate((shadowData.viewDepth - shadowData.nearPlane) * shadowData.invDepthRange);

                bool hasCaster = sampledDepth < 0.999;

                // 0: 采样到的 shadow map 深度
                if (_DebugMode < 0.5)
                {
                    return float4(sampledDepth.xxx, 1);
                }

                // 1: 光源 UV
                if (_DebugMode < 1.5)
                {
                    return float4(lightUV.x, lightUV.y, 0, 1);
                }

                // 2: 当前像素自身在光源视角下的线性深度
                if (_DebugMode < 2.5)
                {
                    return float4(receiverDepth.xxx, 1);
                }

                float diff = receiverDepth - sampledDepth;

                // 3: 深度差
                if (_DebugMode < 3.5)
                {
                    if (!hasCaster)
                    {
                        return float4(1, 0, 0, 1);
                    }

                    float vis = saturate(diff * 50.0);
                    return float4(vis.xxx, 1);
                }

                // 4: Shadow Compare
                float slopeBias = _SlopeDepthBias * (1.0 - NoL);
                float totalBias = _DepthBias + slopeBias;

                float shadow;

                if (_UsePCSS > 0.5)
                {
                    shadow = SampleShadowPCSS(shadowData.cascadeIndex, lightUV, receiverDepth, shadowData.viewDepth, shadowData.nearPlane, shadowData.farPlane, totalBias);
                }
                else if (_UsePCF > 0.5 && _PCFRadius > 0.0)
                {
                    shadow = SampleShadowPCF(shadowData.cascadeIndex, lightUV, receiverDepth, totalBias);
                }
                else
                {
                    shadow = SampleShadowTap(shadowData.cascadeIndex, lightUV, receiverDepth, totalBias);
                }

                float shadowFactor = lerp(1.0, 1.0 - _ShadowStrength, shadow);
                float3 finalColor = saturate(ambient + diffuse * shadowFactor);

                return float4(finalColor, _BaseColor.a);
            }

            ENDHLSL
        }
    }
}