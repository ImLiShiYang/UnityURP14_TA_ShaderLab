Shader "MySimpleToon"
{
    Properties
    {
        [MainTexture]_BaseMap ("基础贴图", 2D) = "white" {}
        [MainColor]_BaseColor ("基础颜色", Color) = (1, 1, 1, 1)

        [Header(Render Setting)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Toggle]_UseAlphaClipping ("Use Alpha Clipping", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(Final,0,BaseMap,1,WorldNormal,2,NdotL,3,ToonLight,4,RealtimeShadow,5)] _DebugMode ("Debug Mode", Float) = 0

        [Header(Toon Lighting)]
        _ShadowColor ("暗面颜色", Color) = (0.55, 0.55, 0.65, 1)
        _ShadowThreshold ("明暗分界位置", Range(0, 1)) = 0.5
        _ShadowSoftness ("明暗过渡柔和度", Range(0.001, 0.5)) = 0.05
        
        [Header(Outline)]
        [Toggle]_UseOutline ("Use Outline", Float) = 1
        _OutlineColor ("描边颜色", Color) = (0.02, 0.02, 0.025, 1)
        _OutlineWidth ("描边宽度", Range(0, 0.05)) = 0.005
        
        [Header(Rim Light)]
        _RimColor ("边缘光颜色", Color) = (1, 1, 1, 1)
        _RimIntensity ("边缘光强度", Range(0, 5)) = 0.4
        _RimThreshold ("边缘光范围", Range(0, 1)) = 0.65
        _RimSoftness ("边缘光柔和度", Range(0.001, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Cull;
                float _UseAlphaClipping;
                float _Cutoff;
                float _DebugMode;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float _UseOutline;
                float4 _OutlineColor;
                float _OutlineWidth;
            
                float4 _RimColor;
                float _RimIntensity;
                float _RimThreshold;
                float _RimSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                positionWS += normalize(normalWS) * _OutlineWidth;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_UseOutline < 0.5)
                {
                    clip(-1);
                }

                float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (_UseAlphaClipping > 0.5)
                {
                    clip(baseMap.a * _BaseColor.a - _Cutoff);
                }

                return _OutlineColor;
            }

            ENDHLSL
        }
        
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Cull;
                float _UseAlphaClipping;
                float _Cutoff;
                float _DebugMode;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
            
                float _UseOutline;
                float4 _OutlineColor;
                float _OutlineWidth;
            
                float4 _RimColor;
                float _RimIntensity;
                float _RimThreshold;
                float _RimSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (_UseAlphaClipping > 0.5)
                {
                    clip(baseMap.a * _BaseColor.a - _Cutoff);
                }

                float3 baseColor = baseMap.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                float ndotl = saturate(dot(normalWS, lightDirWS));

                float toonLight = smoothstep(
                    _ShadowThreshold,
                    _ShadowThreshold + _ShadowSoftness,
                    ndotl
                );

                // 当前像素在主光阴影图里的坐标
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                // 实时主光阴影，1 = 没被挡住，0 = 完全在阴影里
                float realtimeShadow = MainLightRealtimeShadow(shadowCoord);
                
                // 把卡通明暗和真实阴影结合
                float finalLight = toonLight * realtimeShadow;
                
                float3 litColor = baseColor;
                float3 shadowColor = baseColor * _ShadowColor.rgb;

                float3 finalColor = lerp(shadowColor, litColor, finalLight);
                
                // 观察方向：当前像素指向摄像机的方向
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // rimRaw：越靠近模型边缘，值越大
                float rimRaw = 1.0 - saturate(dot(normalWS, viewDirWS));

                // 用 smoothstep 控制边缘光范围和软硬
                float rim = smoothstep(
                    _RimThreshold,
                    _RimThreshold + max(_RimSoftness, 0.001),
                    rimRaw
                );

                // 把边缘光加到最终颜色上
                finalColor += _RimColor.rgb * rim * _RimIntensity;

                if (_DebugMode == 1)
                    return half4(baseMap.rgb, 1);
                if (_DebugMode == 2)
                    return half4(normalWS * 0.5 + 0.5, 1);
                if (_DebugMode == 3)
                    return half4(ndotl, ndotl, ndotl, 1);
                if (_DebugMode == 4)
                    return half4(toonLight, toonLight, toonLight, 1);
                if (_DebugMode == 5)
                    return half4(realtimeShadow, realtimeShadow, realtimeShadow, 1);
                
                return half4(finalColor, baseMap.a * _BaseColor.a);
            }

            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"

    }
}
