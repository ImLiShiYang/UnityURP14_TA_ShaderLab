Shader "Unlit/BumpSpecual"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _BumpMap ("Bump Map", 2D) = "bump" {}
        _BumpScale ("Bump Scale", Float) = 1.0
        _Specual ("_Specual", Color) = (1,1,1,1)
        _Gloss ("Gloss", Range(8.0, 256)) = 20.0
    }

    SubShader
    {
        Tags{"RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline"}

        Pass
        {
            NAME "ForwardLit"
            Tags{"LightMode"="UniversalForward"}
            ZWrite On          // 强制写入深度
            Blend Off          // 关闭混合（不透明物体）
            Cull Back          // 可选，背面剔除

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

             // 启用阴影变体
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // 启用额外光源（点光源/聚光灯）
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            // 启用光照贴图
            #pragma multi_compile _ _LIGHT_LAYERS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _BumpScale;
                float4 _Specual;
                float _Gloss;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            struct a2v
            {
                float4 vertex:POSITION;
                float3 normal:NORMAL;
                float2 uv:TEXCOORD0;
                float4 tangent:TANGENT;
            };

            struct v2f
            {
                float4 pos:POSITION;
                float3 worldPos:TEXCOORD0;
                float3 worldNormal:TEXCOORD1;
                float4 uv:TEXCOORD2;
                float3 worldTangent:TEXCOORD3;
                float3 worldBinormal:TEXCOORD4;
                
            };

            v2f vert(a2v v)
            {
                v2f o;
                
                VertexPositionInputs posInput= GetVertexPositionInputs(v.vertex.xyz);
                o.pos=posInput.positionCS;
                o.worldPos=posInput.positionWS;

                VertexNormalInputs normalInput=GetVertexNormalInputs(v.normal, v.tangent);
                o.worldNormal=normalInput.normalWS;
                o.worldTangent=normalInput.tangentWS;
                o.worldBinormal=normalInput.bitangentWS;

                o.uv.xy=TRANSFORM_TEX(v.uv,_MainTex);
                o.uv.zw=TRANSFORM_TEX(v.uv,_BumpMap);

                return o;
            }

            float4 frag(v2f i):SV_Target
            {
                float4 sample_normal=SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv.zw);
                float3 tangentNormal=UnpackNormalScale(sample_normal, _BumpScale);

                float3x3 tangentMatrix=float3x3(i.worldTangent,i.worldBinormal,i.worldNormal);
                float3 normalWS=TransformTangentToWorld(tangentNormal,tangentMatrix);
                normalWS=normalize(normalWS);

                float3 sampleColor=SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb;
                float3 albedo=sampleColor*_Color.rgb;
                
                float3 ambient=SampleSH(normalWS)*albedo;
                
                float4 shadowCoord = TransformWorldToShadowCoord(i.worldPos);
                Light mainLight=GetMainLight(shadowCoord);
                float3 lightDirWS=normalize(mainLight.direction);
                float3 lightColor=mainLight.color;
                //阴影
                float shadowAttenuation=mainLight.shadowAttenuation;

                float diff=saturate(dot(lightDirWS,normalWS));
                float3 diffuse=lightColor*diff*albedo;

                float3 viewDirWS=normalize(GetCameraPositionWS()-i.worldPos);
                float3 halfdir=normalize(viewDirWS+lightDirWS);
                float spec=pow(saturate(dot(halfdir,normalWS)),_Gloss);
                float3 specual=_Specual.rgb*lightColor*spec;

                //加阴影
                float3 mainLightColor=ambient+(diffuse+specual)*shadowAttenuation;

                float3 additionLightColor=0;

                #ifdef _ADDITIONAL_LIGHTS
                    uint addLightCount=GetAdditionalLightsCount();
                    for(int index=0;index<addLightCount;index++)
                    {
                        Light addlight=GetAdditionalLight(index,i.worldPos,shadowAttenuation);
                        float3 addlightdir=normalize(addlight.direction);
                        float3 addlightcolor=addlight.color;
                        float shadowatten=addlight.shadowAttenuation*addlight.distanceAttenuation;

                        float adddiff=saturate(dot(addlightdir,normalWS));
                        float3 adddiffuse=addlightcolor*adddiff*albedo;

                        float3 addhalfdir=normalize(viewDirWS+addlightdir);
                        float addspec=pow(saturate(dot(addhalfdir,normalWS)),_Gloss);
                        float3 addspecual=_Specual.rgb*addlightcolor*addspec;   

                        //加阴影
                        additionLightColor+=(adddiffuse+addspecual)*shadowatten;
                    }
                #endif

                return float4(mainLightColor+additionLightColor,1);
            }

            ENDHLSL
        }


        // 复用URP内置的ShadowCaster Pass，使物体能投射阴影
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        
        UsePass "Unlit/DepthNormal/DEPTHNORMALSPASS"
        
        
        
    }

}

