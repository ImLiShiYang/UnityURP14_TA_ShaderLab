Shader "Unlit/Chapter8-AlphaBlend"   // 可自行重命名为 URP 分类，如 "URP/Chapter8-AlphaBlend"
{
    Properties
    {
        _Color ("Main Tint", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _AlphaScale ("Alpha Scale", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 texcoord   : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float _AlphaScale;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.texcoord, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half3 albedo = texColor.rgb * _Color.rgb;

                // 环境光 (使用球谐或全局环境色，这里用 SampleSH 获得漫反射环境项)
                half3 ambient = SampleSH(IN.worldNormal) * albedo;

                // 主平行光
                Light mainLight = GetMainLight();
                half3 worldNormal = normalize(IN.worldNormal);
                half3 worldLightDir = normalize(mainLight.direction);
                half NdotL = saturate(dot(worldNormal, worldLightDir));
                half3 diffuse = mainLight.color * albedo * NdotL;

                half3 finalRGB = diffuse + ambient;
                half alpha = texColor.a * _AlphaScale;

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }
    // URP 下通常无需 Fallback，可保留或移除
    // Fallback "Universal Render Pipeline/Unlit"
}