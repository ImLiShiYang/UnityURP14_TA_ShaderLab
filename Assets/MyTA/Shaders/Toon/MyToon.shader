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
        _DebugMode ("调试模式", Range(0, 10)) = 0
        [Enum(Off,0,Occlusion,1,Specular,2,Rim,3,MatCap,4,Emission,5,HairSpecular,6)] _FeatureDebugMode ("功能调试模式", Float) = 0

        [Header(High Level Setting)]
        [Toggle]_IsFace ("是否脸部材质", Float) = 0
        _ReceiveShadowStrength ("接收阴影强度", Range(0, 1)) = 1
        _MinLight ("最小亮度", Range(0, 1)) = 0.08
        _FaceMinLight ("脸部最小亮度", Range(0, 1)) = 0.65
        _FaceShadowColorStrength ("脸部阴影颜色强度", Range(0, 1)) = 0.3
        _ReceiveShadowMappingPosOffset ("接收阴影位置偏移", Range(-0.2, 0.2)) = 0

        [Header(Face SDF)]
        [Toggle]_UseFaceSDF ("启用脸部 SDF 阴影", Float) = 0
        [NoScaleOffset]_FaceSDFMap ("脸部 SDF 贴图", 2D) = "gray" {}
        _FaceSDFShadowThreshold ("脸部 SDF 阴影分界", Range(0, 1)) = 0.5
        _FaceSDFShadowSoftness ("脸部 SDF 阴影柔和度", Range(0.0001, 0.5)) = 0.08
        _FaceSDFShadowStrength ("脸部 SDF 阴影强度", Range(0, 1)) = 1
        _FaceSDFFrontOffset ("脸部受光偏移", Range(-1, 1)) = 0
        [Toggle]_FaceSDFInvert ("脸部 SDF 反向", Float) = 0

        [Header(Fringe Shadow)]
        [Toggle]_UseFringeShadow ("启用刘海投影", Float) = 0
        [Toggle]_IsFringeShadowCaster ("作为刘海投影遮挡物", Float) = 0
        _FringeShadowDistance ("刘海投影偏移", Range(-0.2, 0.2)) = 0.035
        _FringeShadowStrength ("刘海投影强度", Range(0, 1)) = 0.55
        _FringeShadowDepthBias ("刘海深度偏移", Range(-0.05, 0.05)) = 0.01
        _FringeShadowColorStrength ("刘海阴影颜色强度", Range(0, 1)) = 0.45
        _FringeShadowCameraFadeDistance ("刘海远处淡出距离", Range(0.1, 20)) = 5

        [Header(Toon Ramp)]
        [Toggle]_UseToonRamp ("启用 Toon Ramp", Float) = 0
        [NoScaleOffset]_ToonRampMap ("Toon Ramp 贴图", 2D) = "white" {}
        _ToonRampStrength ("Toon Ramp 强度", Range(0, 1)) = 1
        _ToonRampOffset ("Toon Ramp 偏移", Range(-1, 1)) = 0
        _ToonRampContrast ("Toon Ramp 对比度", Range(0.1, 4)) = 1
        [Toggle]_ToonRampInvert ("Toon Ramp 反向", Float) = 0

        [Header(Occlusion)]
        [Toggle]_UseOcclusion ("启用遮蔽", Float) = 0
        _OcclusionStrength ("遮蔽强度", Range(0, 1)) = 1
        [NoScaleOffset]_OcclusionMap ("遮蔽贴图", 2D) = "white" {}
        _OcclusionMapChannelMask ("遮蔽通道遮罩", Vector) = (1, 0, 0, 0)
        _OcclusionRemapStart ("遮蔽重映射起点", Range(0, 1)) = 0
        _OcclusionRemapEnd ("遮蔽重映射终点", Range(0, 1)) = 1

        [Header(Height Gradient)]
        [Toggle]_UseHeightGradient ("启用高度渐变", Float) = 0
        _HeightGradientTopColor ("顶部颜色", Color) = (1, 1, 1, 1)
        _HeightGradientBottomColor ("底部颜色", Color) = (1, 0.85, 0.85, 1)
        _HeightGradientMin ("渐变最低高度", Float) = 0
        _HeightGradientMax ("渐变最高高度", Float) = 2
        _HeightGradientStrength ("渐变强度", Range(0, 1)) = 0.5

        [Header(MatCap)]
        [Toggle]_UseMatCap ("启用 MatCap", Float) = 0
        [NoScaleOffset]_MatCapMap ("MatCap 贴图", 2D) = "black" {}
        _MatCapColor ("MatCap 颜色", Color) = (1, 1, 1, 1)
        _MatCapIntensity ("MatCap 强度", Range(0, 2)) = 0.5
        _MatCapBlendBaseColor ("MatCap 混合基础色", Range(0, 1)) = 0

        [Header(Specular)]
        [Toggle]_UseSpecular ("启用高光", Float) = 0
        _SpecularColor ("高光颜色", Color) = (1, 1, 1, 1)
        _SpecularIntensity ("高光强度", Range(0, 3)) = 0.5
        _SpecularThreshold ("高光范围", Range(0, 1)) = 0.85
        _SpecularSoftness ("高光柔和度", Range(0.001, 0.5)) = 0.05


        [Header(Anisotropic Hair Specular)]
        [Toggle]_UseHairSpecular ("启用头发各向异性高光", Float) = 0
        [NoScaleOffset]_HairSpecularShiftMap ("头发高光偏移贴图", 2D) = "gray" {}
        _HairSpecularShiftMapStrength ("头发高光偏移贴图强度", Range(0, 1)) = 0.15

        [NoScaleOffset]_HairSpecularMaskMap ("头发高光遮罩贴图", 2D) = "white" {}
        _HairSpecularMaskStrength ("头发高光遮罩强度", Range(0, 1)) = 1
        _HairSpecularMaskPower ("头发高光遮罩对比度", Range(0.2, 4)) = 1
        [Toggle]_HairSpecularUseBitangent ("使用副切线作为发丝方向", Float) = 1
        _HairSpecularColor ("头发主高光颜色", Color) = (1, 1, 1, 1)
        _HairSpecularIntensity ("头发主高光强度", Range(0, 5)) = 1.0
        _HairSpecularPower ("头发主高光锐度", Range(1, 256)) = 80
        _HairSpecularThreshold ("头发主高光范围", Range(0, 1)) = 0.55
        _HairSpecularSoftness ("头发主高光柔和度", Range(0.001, 0.5)) = 0.08
        _HairSpecularShift ("头发主高光偏移", Range(-1, 1)) = 0.05
        _HairSpecularDirectionAtten ("头发高光方向衰减", Range(0, 1)) = 0

        [Toggle]_UseHairSecondarySpecular ("启用头发副高光", Float) = 0
        _HairSecondarySpecularColor ("头发副高光颜色", Color) = (0.5, 0.65, 1, 1)
        _HairSecondarySpecularIntensity ("头发副高光强度", Range(0, 5)) = 0.35
        _HairSecondarySpecularPower ("头发副高光锐度", Range(1, 256)) = 40
        _HairSecondarySpecularShift ("头发副高光偏移", Range(-1, 1)) = -0.25

        [Header(Emission)]
        [Toggle]_UseEmission ("启用自发光", Float) = 0
        [HDR]_EmissionColor ("自发光颜色", Color) = (0, 0, 0, 1)
        _EmissionMulByBaseColor ("自发光混合基础色", Range(0, 1)) = 0
        [NoScaleOffset]_EmissionMap ("自发光贴图", 2D) = "white" {}
        _EmissionMapChannelMask ("自发光通道遮罩", Vector) = (1, 1, 1, 0)

        [Header(Direct Light)]
        _DirectLightMultiplier ("主光亮度", Range(0, 1)) = 1
        _MainLightIgnoreCelShade ("主光去除卡通阴影", Range(0, 1)) = 0

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
        [NoScaleOffset]_OutlineZOffsetMaskTex ("描边深度偏移遮罩 黑色应用", 2D) = "black" {}
        _OutlineZOffsetMaskRemapStart ("描边深度遮罩重映射起点", Range(0, 1)) = 0
        _OutlineZOffsetMaskRemapEnd ("描边深度遮罩重映射终点", Range(0, 1)) = 1

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
                float outlineZOffsetMask = SAMPLE_TEXTURE2D_LOD(
                    _OutlineZOffsetMaskTex,
                    sampler_OutlineZOffsetMaskTex,
                    input.uv,
                    0
                ).r;
                outlineZOffsetMask = 1.0 - outlineZOffsetMask;
                outlineZOffsetMask = InvLerpClamp(
                    _OutlineZOffsetMaskRemapStart,
                    _OutlineZOffsetMaskRemapEnd,
                    outlineZOffsetMask
                );
                output.positionCS = ApplyOutlineZOffset(output.positionCS, _OutlineZOffset * outlineZOffsetMask + 0.03 * _IsFace);
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
            #include "Assets/MyTA/Shaders/Toon/MyToonLighting.hlsl"

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
                float3 positionWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;

                float4 positionSS : TEXCOORD4;
                float posNDCw : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.positionSS = ComputeScreenPos(positionInputs.positionCS);
                output.posNDCw = positionInputs.positionNDC.w;

                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                float3 tangentWS = normalize(TransformObjectToWorldDir(input.tangentOS.xyz));
                float tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = float4(tangentWS, tangentSign);

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

                float occlusion = GetOcclusion(input.uv);

                float3 indirectLight = GetIndirectLight(occlusion);

                float3 normalWS = normalize(input.normalWS);

                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                float ndotl = dot(normalWS, lightDirWS);

                float toonLight = GetToonLight(ndotl, occlusion);

                // 当前像素在主光阴影图里的坐标
                float3 shadowTestPosWS = input.positionWS + lightDirWS * _ReceiveShadowMappingPosOffset;
                float4 shadowCoord = TransformWorldToShadowCoord(shadowTestPosWS);
                float realtimeShadow = MainLightRealtimeShadow(shadowCoord);

                // 控制角色吃多少实时阴影，脸部材质可以把这个调低
                float shadowFactor = lerp(1.0, realtimeShadow, _ReceiveShadowStrength);
                float finalLight = toonLight * shadowFactor*_DirectLightMultiplier;
                float fringeShadow = 1.0;
                float fringeShadowMask = 0.0;

                if (_IsFace > 0.5)
                {
                    fringeShadowMask = GetFringeShadowMask(
                        input.positionCS,
                        input.positionSS,
                        input.posNDCw,
                        lightDirWS
                    );
                    fringeShadow = lerp(1.0, 1.0 - _FringeShadowStrength, fringeShadowMask);
                }

                if (_IsFace > 0.5 && _UseFaceSDF > 0.5)
                {
                    float faceSDFLight = GetFaceSDFLight(input.uv, normalWS,lightDirWS);
                    float faceSDFLitMask = smoothstep(0.98, 1.0, faceSDFLight);
                    fringeShadowMask *= faceSDFLitMask;
                    fringeShadow = lerp(1.0, 1.0 - _FringeShadowStrength, fringeShadowMask);
                    finalLight = faceSDFLight * shadowFactor * _DirectLightMultiplier;
                }

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
                float3 rampColor = GetToonRampColor(finalLight);
                float3 rampFinalColor = baseColor * rampColor;
                finalColor = lerp(finalColor, rampFinalColor, _UseToonRamp * _ToonRampStrength);

                finalColor = max(finalColor, baseColor * indirectLight);

                if (_IsFace > 0.5)
                {
                    float3 fringeShadowColor = lerp(
                        baseColor,
                        baseColor * _ShadowColor.rgb,
                        _FringeShadowColorStrength
                    );

                    finalColor = lerp(fringeShadowColor, finalColor, fringeShadow);
                }

                float specularDebug = 0;
                float rimDebug = 0;
                float3 matCapDebug = 0;
                float3 emissionDebug = 0;

                // 观察方向：当前像素指向摄像机的方向
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float specular = GetSpecular(normalWS, lightDirWS, viewDirWS);

                float rim = GetRim(normalWS, viewDirWS);

                float3 matCapColor = GetMatCap(normalWS, baseColor);

                float3 emissionColor = GetEmission(input.uv, baseColor);

                // tangentWS 是模型切线。
                // bitangentWS 是副切线。很多头发卡片的“发丝方向”更接近 UV 的 V 方向，所以默认用 bitangent。
                float3 tangentWS = normalize(input.tangentWS.xyz);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
                float3 uvVHairDirWS = GetUvVHairDirectionWS(input.positionWS, input.uv, normalWS, bitangentWS);
                float3 hairDirWS = (_HairSpecularUseBitangent > 0.5) ? uvVHairDirWS : tangentWS;

                // 只乘 shadowFactor，不乘 toonLight。
                // 这样头发高光不会被普通明暗分界切得太死，但仍然会受实时阴影影响。
                float hairSpecularLightAtten = shadowFactor * _DirectLightMultiplier;

                float3 hairSpecularColor = GetAnisotropicHairSpecular(
                    normalWS,
                    hairDirWS,
                    lightDirWS,
                    viewDirWS,
                    hairSpecularLightAtten,
                    input.uv
                );

                float hairSpecularDebug = max(max(hairSpecularColor.r, hairSpecularColor.g), hairSpecularColor.b);

                specularDebug = specular;
                rimDebug = rim;
                matCapDebug = matCapColor;
                emissionDebug = emissionColor;

                finalColor += _SpecularColor.rgb * specular * _SpecularIntensity;
                finalColor += hairSpecularColor;
                finalColor += _RimColor.rgb * rim * _RimIntensity;
                finalColor += matCapColor;
                finalColor += emissionColor;
                finalColor = ApplyHeightGradient(finalColor, input.positionWS);

                if (_FeatureDebugMode == 1)
                    return half4(occlusion, occlusion, occlusion, 1);
                if (_FeatureDebugMode == 2)
                    return half4(specularDebug, specularDebug, specularDebug, 1);
                if (_FeatureDebugMode == 3)
                    return half4(rimDebug, rimDebug, rimDebug, 1);
                if (_FeatureDebugMode == 4)
                    return half4(matCapDebug, 1);
                if (_FeatureDebugMode == 5)
                    return half4(emissionDebug, 1);
                if (_FeatureDebugMode == 6)
                return half4(hairSpecularDebug, hairSpecularDebug, hairSpecularDebug, 1);

                if (_DebugMode == 1)
                    return half4(baseMap.rgb, 1);
                if (_DebugMode == 2)
                    return half4(normalWS * 0.5 + 0.5, 1);
                if (_DebugMode == 3)
                {
                    return half4(ndotl, ndotl, ndotl, 1);
                }
                if (_DebugMode == 4)
                    return half4(toonLight, toonLight, toonLight, 1);
                if (_DebugMode == 5)
                    return half4(realtimeShadow, realtimeShadow, realtimeShadow, 1);
                if (_DebugMode == 7)
                {
                    float4 faceSDFMap = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, input.uv);
                    return half4(faceSDFMap.r, faceSDFMap.r, faceSDFMap.r, 1);
                }

                if (_DebugMode == 8)
                {
                    float4 faceSDFMap = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, input.uv);
                    return half4(faceSDFMap.g, faceSDFMap.g, faceSDFMap.g, 1);
                }

                if (_DebugMode == 9)
                {
                    float faceSDFLight = GetFaceSDFLight(input.uv, normalWS,lightDirWS);
                    return half4(faceSDFLight, faceSDFLight, faceSDFLight, 1);
                }

                // if (_DebugMode == 10)
                // {
                //     float3 faceForwardWS = normalize(TransformObjectToWorldDir(float3(0.0, 0.0, _FaceSDFForwardSign)));
                //     float3 lightFlatWS = float3(lightDirWS.x, 0.0, lightDirWS.z);
                //     lightFlatWS = normalize(lightFlatWS + faceForwardWS * 0.0001);
                //     float ctrl = 1.0 - saturate(dot(lightFlatWS, faceForwardWS) * 0.5 + 0.5 + _FaceSDFFrontOffset);
                //     ctrl = saturate(ctrl + _FaceSDFShadowThreshold - 0.5);
                //     return half4(ctrl, ctrl, ctrl, 1);
                // }

                return half4(finalColor, baseMap.a * _BaseColor.a);
            }

            ENDHLSL
        }

        Pass
        {
            Name "FringeShadowDepth"
            Tags { "LightMode" = "MyToonFringeShadow" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask RGBA

            HLSLPROGRAM

            #pragma vertex FringeShadowDepthVertex
            #pragma fragment FringeShadowDepthFragment

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

            Varyings FringeShadowDepthVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 FringeShadowDepthFragment(Varyings input) : SV_Target
            {
                // 不是头发材质的子网格，直接丢弃，不写入刘海投影图
                clip(_IsFringeShadowCaster - 0.5);

                float4 baseMap = SampleBaseMap(input.uv);
                DoAlphaClip(baseMap.a);

                float rawDepth = input.positionCS.z;

                // G 通道存头发深度。
                return half4(0, rawDepth, 0, 1);
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
