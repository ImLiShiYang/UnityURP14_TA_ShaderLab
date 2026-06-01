Shader "Snow/SnowFootprintBrush"
{
    Properties
    {
        _FootprintHeight ("Footprint Height", 2D) = "gray" {}

        _SinkStrength ("Sink Strength", Range(0, 1)) = 1
        _RaiseStrength ("Raise Strength", Range(0, 1)) = 1

        _NeutralHeight ("Neutral Height", Range(0, 1)) = 0.5
        _Threshold ("Dead Zone", Range(0, 0.2)) = 0.02
        _Softness ("Mask Softness", Range(0.001, 0.3)) = 0.05
        _Power ("Shape Power", Range(0.2, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "SnowFootprintHeightBrush"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            // 多个脚印重叠时，每个通道取最大值
            BlendOp Max
            Blend One One

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FootprintHeight);
            SAMPLER(sampler_FootprintHeight);

            CBUFFER_START(UnityPerMaterial)
                float4 _FootprintHeight_ST;
                float _SinkStrength;
                float _RaiseStrength;
                float _NeutralHeight;
                float _Threshold;
                float _Softness;
                float _Power;
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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _FootprintHeight);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float heightValue = SAMPLE_TEXTURE2D(
                    _FootprintHeight,
                    sampler_FootprintHeight,
                    input.uv
                ).r;

                // 0.5 是中性高度
                // < 0.5 = 凹陷
                // > 0.5 = 凸起
                float depression01 = saturate((_NeutralHeight - heightValue) / max(_NeutralHeight, 0.0001));
                float raise01 = saturate((heightValue - _NeutralHeight) / max(1.0 - _NeutralHeight, 0.0001));

                // mask 只负责去掉 0.5 附近的灰色背景误差
                float depressionMask = smoothstep(_Threshold, _Threshold + _Softness, depression01);
                float raiseMask = smoothstep(_Threshold, _Threshold + _Softness, raise01);

                // 真正写入 RT 的强度，保留高度图本身的深浅变化
                float depression = pow(depression01, _Power) * depressionMask * _SinkStrength;
                float raise = pow(raise01, _Power) * raiseMask * _RaiseStrength;

                // A 通道不要乘强度，只表示这个区域是否有效
                float mask = saturate(max(depressionMask, raiseMask));

                return half4(depression, raise, 0.0, mask);
            }

            ENDHLSL
        }
    }
}