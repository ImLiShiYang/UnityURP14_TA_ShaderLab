Shader "Custom/BaseHeightNormal_POM"
{
    Properties
    {
        _BaseMap("Base Map RGB + Alpha", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 3)) = 1.0

        _HeightMap("Height Map", 2D) = "gray" {}
        _HeightCenter("Height Center", Range(0, 1)) = 0.5
        _HeightContrast("Height Contrast", Range(0, 8)) = 2.0
        _InvertHeight("Invert Height", Range(0, 1)) = 0.0

        _ParallaxStrength("Parallax Strength", Range(-0.2, 0.2)) = 0.05
        _POMMinSteps("POM Min Steps", Range(1, 32)) = 8
        _POMMaxSteps("POM Max Steps", Range(1, 64)) = 32

        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
        _DiffuseStrength("Diffuse Strength", Range(0, 2)) = 1.0

        _DebugView("Debug View 0 Final 1 HeightDepth 2 Offset", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardLit_POM_Test"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;

                float _NormalStrength;

                float _HeightCenter;
                float _HeightContrast;
                float _InvertHeight;

                float _ParallaxStrength;
                float _POMMinSteps;
                float _POMMaxSteps;

                float _AmbientStrength;
                float _DiffuseStrength;

                float _DebugView;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;

                float tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = float4(normalInputs.tangentWS, tangentSign);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half SampleHeight01(float2 uv)
            {
                half h = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, uv).r;
                h = lerp(h, 1.0h - h, _InvertHeight);
                return h;
            }

            half SampleDepthForPOM(float2 uv)
            {
                half h = SampleHeight01(uv);

                // 约定：
                // HeightCenter = 0.5
                // h < 0.5 表示凹陷
                // POM 需要的是“深度”，所以把低于中心的部分转成 depth。
                return saturate((_HeightCenter - h) * _HeightContrast);
            }

            float2 ParallaxOcclusionMapping(float2 uv, float3 viewDirTS, out float2 uvOffset)
            {
                uvOffset = 0.0;

                if (abs(_ParallaxStrength) < 0.00001)
                    return uv;

                viewDirTS = normalize(viewDirTS);

                float viewZ = max(abs(viewDirTS.z), 0.08);
                float ndotv = saturate(abs(viewDirTS.z));

                int stepCount = (int)round(lerp(_POMMaxSteps, _POMMinSteps, ndotv));
                stepCount = clamp(stepCount, 1, 64);

                float layerDepth = 1.0 / stepCount;
                float currentLayerDepth = 0.0;

                float2 parallaxDir = viewDirTS.xy / viewZ;
                float2 deltaUV = parallaxDir * _ParallaxStrength / stepCount;

                float2 currentUV = uv;
                float2 previousUV = uv;

                half currentDepth = SampleDepthForPOM(currentUV);
                half previousDepth = currentDepth;

                float previousLayerDepth = 0.0;

                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= stepCount) break;
                    if (currentLayerDepth >= currentDepth) break;

                    previousUV = currentUV;
                    previousDepth = currentDepth;
                    previousLayerDepth = currentLayerDepth;

                    currentUV -= deltaUV;
                    currentLayerDepth += layerDepth;
                    currentDepth = SampleDepthForPOM(currentUV);
                }

                float afterDepth = currentDepth - currentLayerDepth;
                float beforeDepth = previousDepth - previousLayerDepth;
                float denom = afterDepth - beforeDepth;

                float weight = 0.0;
                if (abs(denom) > 0.00001)
                    weight = saturate(afterDepth / denom);

                float2 finalUV = lerp(currentUV, previousUV, weight);
                uvOffset = finalUV - uv;

                return finalUV;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 viewDirTS = float3(dot(viewDirWS, tangentWS), dot(viewDirWS, bitangentWS), dot(viewDirWS, normalWS));

                float2 uvOffset;
                float2 pomUV = ParallaxOcclusionMapping(input.uv, viewDirTS, uvOffset);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, pomUV);

                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, pomUV);
                half3 normalTS = UnpackNormalScale(normalSample, _NormalStrength);

                half3 finalNormalWS = normalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * normalWS);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(finalNormalWS, mainLight.direction));

                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half3 ambient = albedo * _AmbientStrength;
                half3 diffuse = albedo * mainLight.color * ndotl * _DiffuseStrength;

                half3 finalColor = ambient + diffuse;
                half alpha = baseSample.a * _BaseColor.a;

                if (_DebugView > 0.5 && _DebugView < 1.5)
                {
                    half d = SampleDepthForPOM(input.uv);
                    return half4(d, d, d, 1.0h);
                }

                if (_DebugView >= 1.5)
                {
                    float2 o = abs(uvOffset) * 80.0;
                    return half4(o.x, o.y, 0.0h, 1.0h);
                }

                return half4(finalColor, alpha);
            }

            ENDHLSL
        }
    }
}