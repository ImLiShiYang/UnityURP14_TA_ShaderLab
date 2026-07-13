Shader "MyTA/VFX/Water Splash Transparent"
{
    Properties
    {
        _MainTex("Packed Water Shape (RGBA)", 2D) = "white" {}
        _Tint("Water Tint", Color) = (0.72, 0.88, 1.0, 1.0)
        _FoamColor("Edge Highlight", Color) = (0.90, 0.97, 1.0, 1.0)
        _BodyOpacity("Body Opacity", Range(0, 1)) = 0.24
        _EdgeOpacity("Edge Opacity", Range(0, 1)) = 0.62
        _RefractionStrength("Refraction Strength", Range(0, 0.02)) = 0.0035
        _RefractionMix("Refraction Mix", Range(0, 1)) = 0.28
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WaterSplashForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _Tint;
                float4 _FoamColor;
                float _BodyOpacity;
                float _EdgeOpacity;
                float _RefractionStrength;
                float _RefractionMix;
                float _FresnelPower;
                float _FresnelStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half shapeAlpha = saturate(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a);
                clip(shapeAlpha - 0.015h);
                half crest = smoothstep(0.30h, 0.82h, shapeAlpha);
                half alpha = saturate(shapeAlpha * input.color.a * (_BodyOpacity + crest * _EdgeOpacity));
                half3 waterColor = lerp(_Tint.rgb, _FoamColor.rgb, crest) * input.color.rgb;
                return half4(waterColor * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
