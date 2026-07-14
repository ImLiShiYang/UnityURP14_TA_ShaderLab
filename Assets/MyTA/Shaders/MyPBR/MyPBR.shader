Shader "MyTA/MyPBR"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _NormalMap("Normal Map", 2D) = "bump" {}
        _ParallaxMap("Height Map", 2D) = "black" {}
        _Parallax("Parallax Strength", Range(0, 0.08)) = 0.02
        
        _EmissionMap("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (1, 1, 1, 1)

        // ARM：R = AO，G = Roughness，B = Metallic
        _ARMMap("ARM Map (R:AO G:Roughness B:Metallic)", 2D) = "white" {}
        _Metallic("Metallic Strength", Range(0, 1)) = 1
        _Smoothness("Smoothness Strength", Range(0, 1)) = 1
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 主方向光阴影
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            // Reflection Probe 混合与盒投影
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            
            // 静态 Lightmap
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            
            TEXTURE2D(_ParallaxMap);
            SAMPLER(sampler_ParallaxMap);
            
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);
            
            TEXTURE2D(_ARMMap);
            SAMPLER(sampler_ARMMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _OcclusionStrength;
                float _Parallax;
                float4 _EmissionColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            // 模型顶点数据
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                
                // 模型的第二套 UV，用于采样 Lightmap
                float2 lightmapUV : TEXCOORD1;
            };

            // 顶点阶段传递给片元阶段的数据
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                
                // LIGHTMAP_ON 时传递 Lightmap UV
                // 否则传递 Light Probe 的球谐光照
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                // 获取裁剪空间和世界空间位置
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;

                // 将模型空间法线和切线转换到世界空间
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);

                // 处理模型存在负缩放时的切线空间方向
                float tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * tangentSign;

                // 应用 BaseMap 的 Tiling 和 Offset
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                // 计算当前顶点在主光阴影贴图中的坐标
                output.shadowCoord = GetShadowCoord(positionInputs);
                
                // 静态物体：转换并输出 Lightmap UV
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);

                // 动态物体：计算 Light Probe / SH
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            // 将世界空间观察方向转换到切线空间，供视差计算使用
            float3 GetViewDirectionTS(Varyings input, float3 viewDirWS)
            {
                return float3(
                    dot(viewDirWS, normalize(input.tangentWS)),
                    dot(viewDirWS, normalize(input.bitangentWS)),
                    dot(viewDirWS, normalize(input.normalWS))
                );
            }
            
            // 将法线贴图中的切线空间法线转换到世界空间
            float3 GetNormalWS(Varyings input, float2 uv)
            {
                float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));

                float3x3 TBN = float3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    normalize(input.normalWS)
                );

                return normalize(mul(normalTS, TBN));
            }

            // Fresnel-Schlick：观察角度越倾斜，表面反射越强
            float3 FresnelSchlick(float HdotV, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(1.0 - HdotV, 5.0);
            }

            // GGX 法线分布函数 D：描述朝向半程向量的微表面数量
            float DistributionGGX(float NdotH, float roughness)
            {
                // 将感知粗糙度转换为 GGX 使用的 alpha
                float a = roughness * roughness;
                float a2 = a * a;

                float denominator = NdotH * NdotH * (a2 - 1.0) + 1.0;
                denominator = PI * denominator * denominator;

                return a2 / max(denominator, 0.0001);
            }

            // Schlick-GGX：计算单个方向上的微表面遮挡
            float GeometrySchlickGGX(float NdotX, float roughness)
            {
                // 直接光使用的 k 值
                float r = roughness + 1.0;
                float k = r * r / 8.0;

                return NdotX / max(NdotX * (1.0 - k) + k, 0.0001);
            }

            // Smith 几何遮挡函数 G：同时计算观察方向和光照方向的遮挡
            float GeometrySmith(float NdotV, float NdotL, float roughness)
            {
                float geometryView = GeometrySchlickGGX(NdotV, roughness);
                float geometryLight = GeometrySchlickGGX(NdotL, roughness);

                return geometryView * geometryLight;
            }

            // 采样 Reflection Probe，并计算环境镜面反射
            float3 GetEnvironmentSpecular(float3 normalWS,float3 viewDir,float3 positionWS,float4 positionCS,float3 F0,float smoothness,float roughness)
            {
                float3 reflectDir = reflect(-viewDir, normalWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);

                float3 environment = GlossyEnvironmentReflection(
                    reflectDir,
                    positionWS,
                    roughness,
                    1.0,
                    screenUV
                );

                float NdotV = saturate(dot(normalWS, viewDir));

                float fresnelTerm = 1.0 - NdotV;
                fresnelTerm *= fresnelTerm;
                fresnelTerm *= fresnelTerm;

                float reflectivity = max(max(F0.r, F0.g), F0.b);
                float grazingTerm = saturate(smoothness + reflectivity);

                float roughness2 = roughness * roughness;
                float surfaceReduction = 1.0 / (roughness2 * roughness2 + 1.0);

                float3 environmentBRDF = surfaceReduction * lerp(
                    F0,
                    float3(grazingTerm, grazingTerm, grazingTerm),
                    fresnelTerm
                );

                return environment * environmentBRDF;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 viewDirTS = GetViewDirectionTS(input, viewDir);

                // 根据高度贴图和观察方向偏移 UV
                float2 uv = input.uv;
                uv += ParallaxMapping(TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap),viewDirTS,_Parallax, uv);

                // 后续所有材质贴图统一使用偏移后的 UV
                float3 normalWS = GetNormalWS(input, uv);
                
                float4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                float3 albedo = baseColor.rgb * _BaseColor.rgb;

                // 自发光贴图也使用视差偏移后的 UV
                float3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb;
                float3 emission = emissionMap * _EmissionColor.rgb;

                // ARM：R = AO，G = Roughness，B = Metallic
                float4 armMap = SAMPLE_TEXTURE2D(_ARMMap, sampler_ARMMap, uv);
                float occlusionMap = armMap.r;
                float roughnessMap = armMap.g;
                float metallicMap = armMap.b;

                float metallic = saturate(metallicMap * _Metallic);

                float smoothness = saturate((1.0 - roughnessMap) * _Smoothness);
                float roughness = max(1.0 - smoothness, 0.04);

                float occlusion = lerp(1.0, occlusionMap, _OcclusionStrength);
                // 静态物体读取 Lightmap，动态物体读取 Light Probe / SH
                float3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);

                // 当前只计算主方向光
                Light mainLight = GetMainLight(input.shadowCoord);

                float3 lightDir = normalize(mainLight.direction);
                float3 halfDir = normalize(lightDir + viewDir);

                float NdotL = saturate(dot(normalWS, lightDir));
                float NdotV = saturate(dot(normalWS, viewDir));
                float NdotH = saturate(dot(normalWS, halfDir));
                float HdotV = saturate(dot(halfDir, viewDir));

                float attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // 非金属使用约 0.04 的基础反射率，金属使用自身颜色
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

                float D = DistributionGGX(NdotH, roughness);
                float G = GeometrySmith(NdotV, NdotL, roughness);
                float3 F = FresnelSchlick(HdotV, F0);

                // Cook-Torrance 镜面反射
                float3 specularBRDF = D * G * F;
                specularBRDF /= max(4.0 * NdotV * NdotL, 0.0001);

                // 金属没有漫反射，Fresnel 反射的能量不能重复参与漫反射
                float3 kD = (1.0 - F) * (1.0 - metallic);
                float3 diffuseBRDF = kD * albedo / PI;

                float3 radiance = mainLight.color * attenuation;
                float3 directLighting = (diffuseBRDF + specularBRDF) * radiance * NdotL;

                // AO 只影响间接光
                float3 indirectSpecular = GetEnvironmentSpecular(normalWS,viewDir,input.positionWS,input.positionCS,F0,smoothness,roughness) * occlusion;

                float3 indirectDiffuse = albedo * (1.0 - metallic) * bakedGI  * occlusion;

                float3 color = directLighting + indirectDiffuse + indirectSpecular+emission;

                return float4(color, 1.0);
            }

            ENDHLSL
        }

        // 将当前物体写入主方向光的阴影贴图
        Pass
        {
            Name "ShadowCaster"

            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // URP 传入的主方向光方向
            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;

                // 获取顶点的世界空间位置和法线
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // 应用阴影偏移，减少阴影痤疮
                float3 biasedPositionWS = ApplyShadowBias(
                    positionWS,
                    normalWS,
                    _LightDirection
                );

                output.positionCS = TransformWorldToHClip(biasedPositionWS);

                return output;
            }

            // ShadowCaster 只写入深度，不需要输出颜色
            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }

        // 向 Lightmapper 输出材质的 Albedo 和 Emission
        Pass
        {
            Name "Meta"

            Tags
            {
                "LightMode" = "Meta"
            }

            Cull Off

            HLSLPROGRAM

            #pragma vertex UniversalVertexMeta
            #pragma fragment MyPBRMetaFragment
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            TEXTURE2D(_ARMMap);
            SAMPLER(sampler_ARMMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float _Metallic;
                float _Smoothness;
                float4 _BaseMap_ST;
            CBUFFER_END

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

            half4 MyPBRMetaFragment(Varyings input) : SV_Target
            {
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;

                // ARM：R = AO，G = Roughness，B = Metallic
                float4 armMap = SAMPLE_TEXTURE2D(_ARMMap, sampler_ARMMap, input.uv);

                float metallic = saturate(armMap.b * _Metallic);
                float smoothness = saturate((1.0 - armMap.g) * _Smoothness);

                float perceptualRoughness = max(1.0 - smoothness, 0.04);
                float roughness = perceptualRoughness * perceptualRoughness;

                // 接近 URP Lit Meta Pass 的材质输出
                float oneMinusReflectivity = 0.96 * (1.0 - metallic);
                float3 diffuse = albedo * oneMinusReflectivity;
                float3 specular = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = diffuse + specular * roughness * 0.5;
                metaInput.Emission = emission;

                return UniversalFragmentMeta(input, metaInput);
            }

            ENDHLSL
        }
    
        // 写入相机深度纹理
        Pass
        {
            Name "DepthOnly"

            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R

            HLSLPROGRAM

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                return output;
            }

            half DepthOnlyFragment(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }

            ENDHLSL
        }

        // 写入相机深度和法线纹理
        Pass
        {
            Name "DepthNormals"

            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On

            HLSLPROGRAM

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // 支持 URP 的八面体法线编码格式
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_ParallaxMap);
            SAMPLER(sampler_ParallaxMap);

            // 和 ForwardLit 保持完全相同的顺序，避免破坏 SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _OcclusionStrength;
                float _Parallax;
                float4 _EmissionColor;
                float4 _BaseMap_ST;
            CBUFFER_END

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
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 viewDirTS : TEXCOORD4;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);

                float tangentSign = input.tangentOS.w * GetOddNegativeScale();
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * tangentSign;

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(positionInputs.positionWS));

                output.viewDirTS = float3(
                    dot(viewDirWS, normalize(output.tangentWS)),
                    dot(viewDirWS, normalize(output.bitangentWS)),
                    dot(viewDirWS, normalize(output.normalWS))
                );

                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // 与 ForwardLit 保持相同的视差偏移
                uv += ParallaxMapping(
                    TEXTURE2D_ARGS(_ParallaxMap, sampler_ParallaxMap),
                    normalize(input.viewDirTS),
                    _Parallax,
                    uv
                );

                float3 normalTS = UnpackNormal(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv)
                );

                float3x3 TBN = float3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    normalize(input.normalWS)
                );

                float3 normalWS = normalize(mul(normalTS, TBN));

                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);

                return half4(packedNormalWS, 0.0);
                 #else
                return half4(normalWS, 0.0);
                #endif
            }

            ENDHLSL
        }

    }
}