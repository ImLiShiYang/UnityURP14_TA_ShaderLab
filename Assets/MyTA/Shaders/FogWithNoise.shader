Shader "Unlit/FogWithNoise"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _FogDensity ("Fog Density", Float) = 1.0
        _FogColor ("Fog Color", Color) = (1,1,1,1)
        _FogStart ("Fog Start", Float) = 0.0
        _FogEnd ("Fog End", Float) = 1.0
        _NoiseTexture ("Noise Texture", 2D) = "white" {}
        _FogxSpeed ("Fog X Speed", Float) = 0.0
        _FogySpeed ("Fog Y Speed", Float) = 0.0
        _NoiseAmount ("Noise Amount", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FogDensity;
                float4 _FogColor;
                float _FogStart;
                float _FogEnd;
                float _FogxSpeed;
                float _FogySpeed;
                float _NoiseAmount;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTexture);
            SAMPLER(sampler_NoiseTexture);

            float4x4 _FrustumCornersRay;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 interpolatedRay : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;

                // 根据屏幕 UV 选择射线（无需翻转，因为 URP 全屏 Quad 的 UV 已经符合屏幕空间）
                int index = 0;
                if (v.uv.x < 0.5 && v.uv.y < 0.5) index = 0;
                else if (v.uv.x > 0.5 && v.uv.y < 0.5) index = 1;
                else if (v.uv.x > 0.5 && v.uv.y > 0.5) index = 2;
                else index = 3;

                o.interpolatedRay = _FrustumCornersRay[index].xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float depth = SampleSceneDepth(i.uv);
                float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
                float3 normalWSSample = SampleSceneNormals(i.uv);

                float3 worldPos = _WorldSpaceCameraPos + linearDepth * i.interpolatedRay;

                float2 noiseUV = i.uv + float2(_FogxSpeed, _FogySpeed) * _Time.y;
                float noise = SAMPLE_TEXTURE2D(_NoiseTexture, sampler_NoiseTexture, noiseUV).r;
                noise = (noise - 0.5) * _NoiseAmount;

                float fogDensity = (_FogEnd - worldPos.y) / (_FogEnd - _FogStart);
                fogDensity = saturate(fogDensity * _FogDensity * (noise + 1.0));
                // fogDensity = saturate(fogDensity * _FogDensity );

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                col.rgb = lerp(col.rgb, _FogColor.rgb, fogDensity);
                col.a=1;
                return col;
                // return float4(depth,depth,depth,1);
                // return float4(normalWSSample,1);
            }
            
            ENDHLSL
        }
    }
}