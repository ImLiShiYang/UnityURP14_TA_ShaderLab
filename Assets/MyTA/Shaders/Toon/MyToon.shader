Shader "MySimpleToon"
{
    Properties
    {
        [MainTexture]_BaseMap ("基础贴图", 2D) = "white" {}
        [MainColor]_BaseColor ("基础颜色", Color) = (1, 1, 1, 1)

        [Header(Render Setting)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除模式", Float) = 2
        [Toggle]_UseAlphaClipping ("启用透明裁剪", Float) = 0
        _Cutoff ("透明裁剪阈值", Range(0, 1)) = 0.5
        [Enum(Final,0,BaseMap,1,WorldNormal,2,NdotL,3,ToonLight,4,RealtimeShadow,5,AlphaClipMask,6)] _DebugMode ("调试模式", Float) = 0

        [Header(High Level Setting)]
        [Toggle]_IsFace ("是否脸部材质", Float) = 0
        _ReceiveShadowStrength ("接收阴影强度", Range(0, 1)) = 1
        _MinLight ("最小亮度", Range(0, 1)) = 0.08
        _FaceMinLight ("脸部最小亮度", Range(0, 1)) = 0.65
        _FaceShadowColorStrength ("脸部阴影颜色强度", Range(0, 1)) = 0.3
        _ReceiveShadowMappingPosOffset ("接收阴影位置偏移", Range(-0.2, 0.2)) = 0

        [Header(Indirect Light)]
        _IndirectLightMinColor ("环境光最低颜色", Color) = (0.05, 0.05, 0.05, 1)
        _IndirectLightMultiplier ("环境光强度", Range(0, 1)) = 1

        [Header(Toon Lighting)]
        _ShadowColor ("暗面颜色", Color) = (0.55, 0.55, 0.65, 1)
        _ShadowThreshold ("明暗分界位置", Range(-1, 1)) = -0.5
        _ShadowSoftness ("明暗过渡柔和度", Range(0.001, 1)) = 0.05

        [Header(Outline)]
        [Toggle]_UseOutline ("启用描边", Float) = 1
        _OutlineColor ("描边颜色", Color) = (0.02, 0.02, 0.025, 1)
        _OutlineWidth ("描边宽度", Range(0, 0.05)) = 0.005
        _OutlineZOffset ("描边深度偏移", Range(0, 1)) = 0.0001

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

            #include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

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

                positionWS = ApplyOutlineWidth(positionWS, normalWS);
                // positionWS += normalize(normalWS) * _OutlineWidth;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionCS = ApplyOutlineZOffset(output.positionCS, _OutlineZOffset + 0.03 * _IsFace);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_UseOutline < 0.5)
                {
                    clip(-1);
                }

                float4 baseMap = SampleBaseMap(input.uv);
                
                DoAlphaClip(baseMap.a);

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

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
                float4 baseMap = SampleBaseMap(input.uv);
                
                if (_DebugMode == 6)
                {
                    float alpha = baseMap.a * _BaseColor.a;
                    float clipMask = (_UseAlphaClipping > 0.5) ? step(_Cutoff, alpha) : 1.0;
                    return half4(clipMask, clipMask, clipMask, 1);
                }
                DoAlphaClip(baseMap.a);

                float3 baseColor = baseMap.rgb * _BaseColor.rgb;
                float3 indirectLight = max(SampleSH(0), _IndirectLightMinColor.rgb);
                indirectLight *= _IndirectLightMultiplier;

                float3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                float ndotl = dot(normalWS, lightDirWS);

                float toonLight = smoothstep(
                    _ShadowThreshold - _ShadowSoftness,
                    _ShadowThreshold + _ShadowSoftness,
                    ndotl
                );

                // 当前像素在主光阴影图里的坐标
                float3 shadowTestPosWS = input.positionWS + lightDirWS * _ReceiveShadowMappingPosOffset;
                float4 shadowCoord = TransformWorldToShadowCoord(shadowTestPosWS);
                float realtimeShadow = MainLightRealtimeShadow(shadowCoord);
                
                // 控制角色吃多少实时阴影，脸部材质可以把这个调低
                float shadowFactor = lerp(1.0, realtimeShadow, _ReceiveShadowStrength);
                float finalLight = toonLight * shadowFactor;

                // 防止暗部死黑
                finalLight = max(finalLight, _MinLight);

                // 脸部不要像衣服一样被 NdotL 和 shadow map 压黑
                if (_IsFace > 0.5)
                {
                    finalLight = max(finalLight, _FaceMinLight);
                }
                
                float3 litColor = baseColor;
                float3 bodyShadowColor = baseColor * _ShadowColor.rgb;
                float3 faceShadowColor = lerp(baseColor, bodyShadowColor, _FaceShadowColorStrength);

                float3 shadowColor = (_IsFace > 0.5) ? faceShadowColor : bodyShadowColor;
                float3 finalColor = lerp(shadowColor, litColor, finalLight);
                finalColor = max(finalColor, baseColor * indirectLight);
                
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

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            #include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif
            

            float3 _LightDirection;
            float3 _LightPosition;

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

            float4 GetShadowPositionHClip(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS)
                );

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = GetShadowPositionHClip(input);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                float4 baseMap = SampleBaseMap(input.uv);

                DoAlphaClip(baseMap.a);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif

                return 0;
            }

            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

  

            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            #include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half DepthOnlyFragment(Varyings input) : SV_Target
            {
                float4 baseMap = SampleBaseMap(input.uv);
                
                DoAlphaClip(baseMap.a);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif

                return input.positionCS.z;
            }

            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask RGBA

            HLSLPROGRAM

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            

            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            #include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            void DepthNormalsFragment(Varyings input,out half4 outNormalWS : SV_Target0)
            {
                float4 baseMap = SampleBaseMap(input.uv);
                
                
                DoAlphaClip(baseMap.a);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif

                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    outNormalWS = half4(packedNormalWS, 0.0);
                #else
                    outNormalWS = half4(normalWS, 0.0);
                #endif
            }

            ENDHLSL
        }

    }
}
