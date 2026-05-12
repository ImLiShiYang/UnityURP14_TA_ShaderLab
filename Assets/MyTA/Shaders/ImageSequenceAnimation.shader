Shader "Unlit/ImageSequenceAnimation"
{

    Properties
    {
        _Color ("Color Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Image Sequence", 2D) = "white" {}
        _HorizontalAmount ("Horizontal Amount", Float) = 4
        _VerticalAmount ("Vertical Amount", Float) = 4
        _Speed ("Speed", Range(1, 100)) = 30
        _VerticalBillboarding ("Vertical Restraints", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 常量缓冲区（SRP Batcher）
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _HorizontalAmount;
                float _VerticalAmount;
                float _Speed;
                float4 _Color;
                float _VerticalBillboarding;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                // 广告牌技术：使物体始终面向相机（类似粒子公告牌）
                float3 center = float3(0, 0, 0);
                // 获取模型空间下的相机位置
                float3 cameraPos = TransformWorldToObject(_WorldSpaceCameraPos.xyz);
                float3 normalDir = cameraPos - center;
                normalDir.y *= _VerticalBillboarding;
                normalDir = normalize(normalDir);

                // 计算向上方向（避免与法线共线）
                float3 upDir = abs(normalDir.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 rightDir = normalize(cross(upDir, normalDir));
                upDir = normalize(cross(normalDir, rightDir));

                // 重新计算顶点位置（局部空间）
                float3 centerOffset = v.vertex.xyz - center;
                float3 localPos = center + rightDir * centerOffset.x + upDir * centerOffset.y + normalDir * centerOffset.z;

                o.vertex = TransformObjectToHClip(localPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 序列帧动画：计算当前帧的行列索引
                float time = floor(_Time.y * _Speed);
                float row = floor(time / _HorizontalAmount);
                float column = time - row * _HorizontalAmount;   // 等效于 fmod(time, _HorizontalAmount)

                // 计算缩放后的 UV（取其中一帧）
                float2 uv = i.uv + float2(column, -row);
                uv.x /= _HorizontalAmount;
                uv.y /= _VerticalAmount;

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                col.rgb *= _Color.rgb;
                return col;
            }
            ENDHLSL
        }
    }
}

