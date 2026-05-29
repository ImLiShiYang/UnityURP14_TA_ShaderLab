Shader "Unlit/BlinnPhone"
{
    Properties
    {
        _MainTex("MainTex",2D)="white"{}
        _Color("Color",Color)=(1,1,1,1)
        _Specual("Specual",Color)=(1,1,1,1)
        _Gloss("Gloss",Float)=1
    }

    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}

        Pass
        {
            Tags{"LightMode"="UniversalForward"}

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Specual;
                float _Gloss;
                float4 _MainTex_ST;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            struct Attributes
            {
                float4 positionOS:POSITION;
                float3 normalOS:NORMAL;
                float2 uv:TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float2 uv:TEXCOORD0;
                float3 normalWS:TEXCOORD1;
                float3 positionWS:TEXCOORD2;
                // float4 shadowCoord : TEXCOORD3;
            };

            Varyings vert(Attributes att)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(att.positionOS.xyz);
                o.positionWS=TransformObjectToWorld(att.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(att.normalOS);
                o.uv=TRANSFORM_TEX(att.uv,_MainTex);

                

                return o;
            }

            float4 frag(Varyings i):SV_Target
            {
                float3 texColor=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).xyz;
                float3 albedo=_Color.rgb*texColor;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight=GetMainLight(shadowCoord);
                float3 lightDirWS=normalize(mainLight.direction);
                float3 lightColor=mainLight.color;

                // 阴影衰减系数
                half shadowAttenuation = mainLight.shadowAttenuation;

                float3 normalWS=normalize(i.normalWS);

                float3 ambient=SampleSH(normalWS)*albedo;

                float diff=saturate(dot(lightDirWS,normalWS));
                float3 diffuse=lightColor*diff*albedo;
                
                float3 viewDirWS=normalize(GetCameraPositionWS()-i.positionWS);
                float3 halfdir=normalize(viewDirWS+lightDirWS);
                float spec=pow(saturate(dot(halfdir,normalWS)),_Gloss);
                float3 specual=_Specual.rgb*lightColor*spec;

                return float4(ambient+(diffuse+specual)*shadowAttenuation,1);
                // return float4(specual,1);

            }


            ENDHLSL
        }

         // 复用URP内置的ShadowCaster Pass，让物体能投射阴影
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        
        UsePass "Unlit/DepthNormal/DEPTHNORMALSPASS"
        
    }

}

