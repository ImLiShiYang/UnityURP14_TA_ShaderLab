Shader "Hidden/Snow/SnowHeightBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _BlurRadius ("Blur Radius", Range(0, 8)) = 2.5
        _BlurStrength ("Blur Strength", Range(0, 1)) = 0.75
        _AutoRimStrength ("Auto Rim Strength", Range(0, 2)) = 0.65
        _AutoRimRadius ("Auto Rim Radius (Pixels)", Range(1, 256)) = 32
        _AutoRimAsymmetry ("Auto Rim Asymmetry", Range(0, 1)) = 0.35
        _AutoRimNoiseScale ("Auto Rim Noise Scale", Float) = 0.8
        _RawDepressionTex ("Raw Depression", 2D) = "black" {}
        _BaseSmoothedTex ("Base Smoothed Height", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_RawDepressionTex);
        SAMPLER(sampler_RawDepressionTex);
        TEXTURE2D(_BaseSmoothedTex);
        SAMPLER(sampler_BaseSmoothedTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_TexelSize;
            float _BlurRadius;
            half _BlurStrength;
            half _AutoRimStrength;
            float _AutoRimRadius;
            half _AutoRimAsymmetry;
            float _AutoRimNoiseScale;
            float4 _FootstepWorldRect;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        half4 GaussianBlur(float2 uv, float2 direction)
        {
            half4 center = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

            if (_BlurRadius <= 0.001 || _BlurStrength <= 0.001h)
                return center;

            float2 offset1 = direction * _MainTex_TexelSize.xy * (_BlurRadius * 0.5);
            float2 offset2 = direction * _MainTex_TexelSize.xy * _BlurRadius;

            // Five-tap separable Gaussian. Clamp sampling keeps the rolling RT border stable.
            half4 filtered = center * 0.375h;
            filtered += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv - offset1)) * 0.25h;
            filtered += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv + offset1)) * 0.25h;
            filtered += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv - offset2)) * 0.0625h;
            filtered += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv + offset2)) * 0.0625h;

            return lerp(center, filtered, saturate(_BlurStrength));
        }

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 local = frac(p);
            float2 blend = local * local * (3.0 - 2.0 * local);
            float a = Hash21(cell);
            float b = Hash21(cell + float2(1.0, 0.0));
            float c = Hash21(cell + float2(0.0, 1.0));
            float d = Hash21(cell + float2(1.0, 1.0));
            return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
        }

        half DecodeMainSink(float2 uv)
        {
            half4 sampleValue = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv));
            return sampleValue.r * sampleValue.a;
        }

        half GaussianMainRed(float2 uv, float2 direction, float radius)
        {
            float2 offset1 = direction * _MainTex_TexelSize.xy * (radius * 0.5);
            float2 offset2 = direction * _MainTex_TexelSize.xy * radius;

            half filtered = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r * 0.375h;
            filtered += SAMPLE_TEXTURE2D(
                _MainTex, sampler_MainTex, saturate(uv - offset1)).r * 0.25h;
            filtered += SAMPLE_TEXTURE2D(
                _MainTex, sampler_MainTex, saturate(uv + offset1)).r * 0.25h;
            filtered += SAMPLE_TEXTURE2D(
                _MainTex, sampler_MainTex, saturate(uv - offset2)).r * 0.0625h;
            filtered += SAMPLE_TEXTURE2D(
                _MainTex, sampler_MainTex, saturate(uv + offset2)).r * 0.0625h;
            return filtered;
        }
        ENDHLSL

        Pass
        {
            Name "Horizontal"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragHorizontal

            half4 FragHorizontal(Varyings input) : SV_Target
            {
                return GaussianBlur(input.uv, float2(1.0, 0.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Vertical"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragVertical

            half4 FragVertical(Varyings input) : SV_Target
            {
                return GaussianBlur(input.uv, float2(0.0, 1.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "RimGaussianHorizontal"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragRimHorizontal

            half FragRimHorizontal(Varyings input) : SV_Target
            {
                float radius = max(_AutoRimRadius, 1.0);
                float2 offset1 = float2(1.0, 0.0) *
                    _MainTex_TexelSize.xy * (radius * 0.5);
                float2 offset2 = float2(1.0, 0.0) *
                    _MainTex_TexelSize.xy * radius;

                half filtered = DecodeMainSink(input.uv) * 0.375h;
                filtered += DecodeMainSink(input.uv - offset1) * 0.25h;
                filtered += DecodeMainSink(input.uv + offset1) * 0.25h;
                filtered += DecodeMainSink(input.uv - offset2) * 0.0625h;
                filtered += DecodeMainSink(input.uv + offset2) * 0.0625h;
                return filtered;
            }
            ENDHLSL
        }

        Pass
        {
            Name "RimGaussianVerticalComposite"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragRimVerticalComposite

            half4 FragRimVerticalComposite(Varyings input) : SV_Target
            {
                half4 result = SAMPLE_TEXTURE2D(
                    _BaseSmoothedTex,
                    sampler_BaseSmoothedTex,
                    input.uv);

                half blurredDepression = GaussianMainRed(
                    input.uv,
                    float2(0.0, 1.0),
                    max(_AutoRimRadius, 1.0));

                half4 raw = SAMPLE_TEXTURE2D(
                    _RawDepressionTex,
                    sampler_RawDepressionTex,
                    input.uv);
                half depression = raw.r * raw.a;

                // This is the method shown in the reference:
                // keep only the mass which the Gaussian moves outside the pit.
                half moundPotential = max(blurredDepression - depression, 0.0h);

                float2 worldXZ = lerp(
                    _FootstepWorldRect.xy,
                    _FootstepWorldRect.zw,
                    input.uv);
                half noise = (half)(
                    ValueNoise(worldXZ * max(_AutoRimNoiseScale, 0.01)) *
                    2.0 - 1.0);
                half asymmetry = max(
                    0.25h,
                    1.0h + noise * _AutoRimAsymmetry);
                half generatedRim = saturate(
                    moundPotential * _AutoRimStrength * asymmetry);

                result.g = max(result.g, generatedRim);
                // DecodeSnowData multiplies rim by A, so extend the valid mask
                // over the generated outer mound without widening the sink.
                result.a = max(result.a, saturate(generatedRim * 8.0h));
                return result;
            }
            ENDHLSL
        }
    }
}
