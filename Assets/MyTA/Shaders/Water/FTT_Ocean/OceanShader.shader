Shader "Unlit/NewUnlit"
{
    Properties
    {
        [Header(Wave)]
        _WaterColor ("Water Color", Color) = (1,1,1,1)
        _SSSColor ("SSS Color", Color) = (1,1,1,1)
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamScale ("Foam Scale", Float) = 1
        _Length ("Length", Float) = 100
        
        [Header(Optic)]
        _SSSStrength ("SSS Strength", Float) = 5
        _Gloss ("Gloss", Float) = 256
        _Absorption ("Absorption", Float) = 0.5
        _FresnelScale ("FresnelScale", Float) = 0.5
        _RefractionStrength ("Refraction Strength", Float) = 0.5
        _SceneColorStrength ("Scene Color Strength", Range(0, 1)) = 0.85
        _SurfaceTintStrength ("Surface Tint Strength", Range(0, 1)) = 0.2

        _Reflection_Tex("Reflection Tex", CUBE) = "white" {}
        [HideInInspector]_Displacement("Displacement", 2D) = "black" {}
        [HideInInspector]_Derivatives("Derivatives", 2D) = "black" {}
        [HideInInspector]_Turbulence("Turbulence", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionVS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 viewDir : TEXCOORD3;
                float2 screenUV : TEXCOORD4;
            };

            TEXTURECUBE(_Reflection_Tex);
            SAMPLER(sampler_Reflection_Tex);
            TEXTURE2D(_Displacement);
            SAMPLER(sampler_Displacement);
            TEXTURE2D(_Derivatives);
            SAMPLER(sampler_Derivatives);
            TEXTURE2D(_Turbulence);
            SAMPLER(sampler_Turbulence);
            
            float4 _WaterColor;
            float4 _SSSColor;
            float4 _SpecularColor;
            float4 _FoamColor;
            float _SSSStrength;
            float _FoamScale;
            float _Gloss;
            float _Length;
            float _Absorption;
            float _FresnelScale;
            float _RefractionStrength;
            float _SceneColorStrength;
            float _SurfaceTintStrength;


            Varyings vert (Attributes i)
            {
                float3 positionWS = TransformObjectToWorld(i.positionOS).xyz; 

                float2 uv = positionWS.xz / _Length;
                float3 displacement = SAMPLE_TEXTURE2D_LOD(_Displacement, sampler_Displacement, uv, 0).xzy;

                positionWS += displacement;
                Varyings o;
                float4 positionCS = TransformWorldToHClip(positionWS);
                o.positionHCS = positionCS;
                o.positionWS = positionWS;
                o.positionVS = TransformWorldToView(positionWS);
                o.uv = uv;
                o.viewDir = _WorldSpaceCameraPos.xyz - positionWS;
                
                #if UNITY_UV_STARTS_AT_TOP
                positionCS.y = -positionCS.y;
                #endif
                positionCS *= rcp(positionCS.w);
                o.screenUV = positionCS.xy * 0.5 + 0.5;
                return o;
            }
            
            float4 frag (Varyings i) : SV_Target
            {
                float4 derivatives = SAMPLE_TEXTURE2D_LOD(_Derivatives, sampler_Derivatives, i.uv, 0);
                float2 slope = float2(derivatives.x / (1 + derivatives.y), derivatives.z / (1 + derivatives.w));
                float3 normal = float3(-slope.x, 1, -slope.y);
                normal = normalize(normal);

                // 透射颜色计算
                // 重建世界坐标
                float bgDepth = LinearEyeDepth(SampleSceneDepth(i.screenUV), _ZBufferParams);
                float3 viewDir = normalize(i.viewDir);
                float2 refractionDir = -normalize(float2(dot(UNITY_MATRIX_V[0], normal), dot(UNITY_MATRIX_V[1], normal)));
                float2 distortDist = refractionDir * (1 - dot(viewDir, normal)) * _RefractionStrength
                    * (0.5 - length(i.screenUV - 0.5)) * saturate((bgDepth + i.positionVS.z) / _SSSStrength);
                float distortedDepth = LinearEyeDepth(SampleSceneDepth(i.screenUV + distortDist), _ZBufferParams);
                float depthDif;
                if (-i.positionVS.z < distortedDepth)
                {
                    depthDif = distortedDepth + i.positionVS.z;
                }
                else
                {
                    depthDif = max(0, bgDepth + i.positionVS.z);
                    distortDist = 0;
                }
                float extinction = exp(-_Absorption * depthDif);

                Light light = GetMainLight();
                float LDOTN = dot(light.direction, normal);
                // 反射颜色计算
                float3 reflectionColor = SAMPLE_TEXTURECUBE(_Reflection_Tex, sampler_Reflection_Tex, reflect(-viewDir, normal)).rgb;
                
                // 菲涅尔
                float fresnel = pow(saturate(1 - dot(normal, viewDir)), 5) * _FresnelScale;
                float3 specular = LightingSpecular(light.color, light.direction, normal, viewDir, _SpecularColor, _Gloss);
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;
                //泡沫颜色
                float jacobian = saturate(-SAMPLE_TEXTURE2D(_Turbulence, sampler_Turbulence, i.uv) * _FoamScale);
                // 次表面散射
                float scattering = pow(saturate(dot(light.direction, -viewDir)), 5);
                float3 baseWaterColor = lerp(_WaterColor.rgb, _SSSColor.rgb, scattering);
                float3 sceneColor = SampleSceneColor(i.screenUV + distortDist);
                float sceneBlend = saturate(extinction * _SceneColorStrength);
                float3 tintedSceneColor = lerp(sceneColor, sceneColor * baseWaterColor, _SurfaceTintStrength);
                float3 waterColor = lerp(baseWaterColor, tintedSceneColor, sceneBlend);
                //海洋颜色
                float3 oceanDiffuse = lerp(waterColor.rgb, _FoamColor.rgb, jacobian) * light.color * saturate(LDOTN);

                float3 col = ambient + lerp(oceanDiffuse, reflectionColor, fresnel) + saturate(specular);
                
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
