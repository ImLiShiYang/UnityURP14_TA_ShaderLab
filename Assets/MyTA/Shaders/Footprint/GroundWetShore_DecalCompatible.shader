Shader "MyTA/Ground Wet Shore Decal Compatible"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Wetland)]
        _WetTint ("Wet Tint", Color) = (0.42, 0.34, 0.22, 1)
        _WetStrength ("Wet Strength", Range(0, 1)) = 0.85

        _ShoreZ ("Shore Z", Float) = 41
        _WetWidth ("Wet Width", Float) = 4
        _LandSide ("Land Side", Float) = 1

        [Header(Wet Edge Noise)]
        _NoiseTex ("Noise Texture", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Float) = 0.2
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.2

        [Header(Lighting)]
        _DrySmoothness ("Dry Smoothness", Range(0, 1)) = 0.05
        _WetSmoothness ("Wet Smoothness", Range(0, 1)) = 0.45
        _SpecularColor ("Specular Color", Color) = (1,1,1,1)

        [Header(Debug)]
        [Toggle(_WET_DEBUG)] _WetDebug ("Debug Wet Mask", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 这些很重要：
            // Screen Space Decal 依赖场景深度。
            // 地面必须作为不透明物体正常写入深度。
            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #pragma shader_feature_local _WET_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;

                half4 _WetTint;
                half _WetStrength;

                float _ShoreZ;
                float _WetWidth;
                float _LandSide;

                float _NoiseScale;
                half _NoiseStrength;

                half _DrySmoothness;
                half _WetSmoothness;
                half4 _SpecularColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                output.fogCoord = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            /// <summary>
            /// 计算湿地遮罩。
            ///
            /// 返回值：
            /// 0 = 干地
            /// 1 = 最湿
            /// </summary>
            half CalculateWetMask(float3 positionWS)
            {
                // landSide 只取方向：
                //  1  = 陆地在 Z < ShoreZ
                // -1  = 陆地在 Z > ShoreZ
                float landSide = _LandSide >= 0.0 ? 1.0 : -1.0;

                // 计算当前像素到岸线的陆地方向距离。
                //
                // landDistance = 0：
                //     正好在岸线。
                //
                // landDistance > 0：
                //     在陆地一侧。
                //
                // landDistance < 0：
                //     在水的一侧。
                float landDistance = (_ShoreZ - positionWS.z) * landSide;

                // 水的一侧不生成湿地。
                float landMask = step(0.0, landDistance);

                // 世界空间噪声。
                // 用 XZ 采样，让噪声固定在世界中，不跟模型 UV 走。
                float2 noiseUV = positionWS.xz * _NoiseScale;
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;

                // 把噪声变成距离扰动。
                // 这样湿地边缘不是一条笔直的线，而是有自然破碎变化。
                float noiseOffset = (noise - 0.5) * _NoiseStrength * _WetWidth;

                float noisyDistance = landDistance + noiseOffset;

                // 防止除 0。
                float safeWetWidth = max(_WetWidth, 0.0001);

                // 0 = 岸线
                // 1 = 湿地边缘
                float t = saturate(noisyDistance / safeWetWidth);

                // smoothstep 公式。
                // 让湿地过渡更柔和。
                float smoothT = t * t * (3.0 - 2.0 * t);

                // 越靠近岸线越湿。
                half wetMask = 1.0 - smoothT;

                // 水的一侧强制为 0。
                wetMask *= landMask;

                // 总强度控制。
                wetMask *= _WetStrength;

                return saturate(wetMask);
            }

            half3 ApplyBlinnPhong(
                half3 albedo,
                half3 normalWS,
                float3 positionWS,
                half smoothness
            )
            {
                half3 finalColor = 0;

                float3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(positionWS));

                // 环境光
                half3 ambient = SampleSH(normalWS) * albedo;
                finalColor += ambient;

                // 主光源
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 lightDirWS = normalize(mainLight.direction);
                half3 lightColor = mainLight.color;

                half NdotL = saturate(dot(normalWS, lightDirWS));

                half3 diffuse = albedo * lightColor * NdotL;

                half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);

                // smoothness 越高，高光越集中。
                half glossPower = lerp(8.0h, 128.0h, smoothness);

                half spec = pow(saturate(dot(normalWS, halfDir)), glossPower);

                // 湿地更亮一点，干地高光弱一点。
                half specStrength = lerp(0.05h, 0.35h, smoothness);

                half3 specular = _SpecularColor.rgb * lightColor * spec * specStrength;

                finalColor += (diffuse + specular) * mainLight.shadowAttenuation;

                // 额外光源
                #ifdef _ADDITIONAL_LIGHTS
                uint additionalLightCount = GetAdditionalLightsCount();

                for (uint lightIndex = 0; lightIndex < additionalLightCount; lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, positionWS);

                    half3 addLightDirWS = normalize(light.direction);
                    half3 addLightColor = light.color;

                    half atten = light.distanceAttenuation * light.shadowAttenuation;

                    half addNdotL = saturate(dot(normalWS, addLightDirWS));

                    half3 addDiffuse = albedo * addLightColor * addNdotL;

                    half3 addHalfDir = SafeNormalize(addLightDirWS + viewDirWS);
                    half addSpec = pow(saturate(dot(normalWS, addHalfDir)), glossPower);

                    half3 addSpecular = _SpecularColor.rgb * addLightColor * addSpec * specStrength;

                    finalColor += (addDiffuse + addSpecular) * atten;
                }
                #endif

                return finalColor;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // 原始地面颜色。
                half3 dryColor = baseSample.rgb * _BaseColor.rgb;

                // 湿地遮罩。
                half wetMask = CalculateWetMask(input.positionWS);

                // Debug 模式：
                // 打开后可以直接看到湿地范围。
                // 蓝绿色 = 湿地
                // 暗色 = 干地
                #if defined(_WET_DEBUG)
                    half3 debugDry = half3(0.05, 0.08, 0.05);
                    half3 debugWet = half3(0.0, 0.85, 1.0);
                    return half4(lerp(debugDry, debugWet, wetMask), 1);
                #endif

                // 湿地颜色：
                // 不是直接替换颜色，而是让原地面颜色乘以 WetTint。
                // 这样可以保留原贴图明暗，同时变成湿泥 / 湿草色。
                half3 wetColor = dryColor * _WetTint.rgb;

                // 根据 wetMask 混合干地和湿地。
                half3 albedo = lerp(dryColor, wetColor, wetMask);

                // 湿地区域更光滑。
                half smoothness = lerp(_DrySmoothness, _WetSmoothness, wetMask);

                half3 color = ApplyBlinnPhong(
                    albedo,
                    normalWS,
                    input.positionWS,
                    smoothness
                );

                color = MixFog(color, input.fogCoord);

                return half4(color, 1);
            }

            ENDHLSL
        }

        // 显式 DepthOnly Pass。
        // 这个 Pass 用来保证地面能稳定写入相机深度纹理。
        // 你的屏幕空间脚印会依赖 _CameraDepthTexture。
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthOnlyVertex(DepthAttributes input)
            {
                DepthVaryings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;

                return output;
            }

            half4 DepthOnlyFragment(DepthVaryings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // 复用 URP Lit 的 ShadowCaster。
        // 让地面可以投射阴影。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}