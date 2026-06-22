Shader "MyTA/Grass/InteractiveGrass_RT"
{
    Properties
    {
        [Header(Color)]
        _BaseColor ("Base Color", Color) = (0.15, 0.45, 0.12, 1)
        _TipColor ("Tip Color", Color) = (0.45, 0.85, 0.25, 1)

        [Header(Interaction)]
        [NoScaleOffset]_GrassInteractionTex ("Grass Interaction Tex", 2D) = "black" {}
        _GrassInteractionRect ("Grass Interaction Rect", Vector) = (0, 0, 1, 1)
        _GrassBendDirWS ("Grass Bend Dir WS", Vector) = (0, 0, 1, 0)
        _EnableGrassInteraction ("Enable Grass Interaction", Float) = 1

        [Header(Bend)]
        _BendStrength ("Bend Strength", Range(0, 3)) = 1.0
        _FlattenStrength ("Flatten Strength", Range(0, 1)) = 0.25
        _HeightMaskPower ("Height Mask Power", Range(0.2, 5)) = 1.5
        _GrassHeightAxisOS ("Grass Height Axis OS", Vector) = (0, 0, 1, 0)
        _GrassHeightMinOS ("Grass Height Min OS", Float) = -0.322
        _GrassHeightMaxOS ("Grass Height Max OS", Float) = 0.322

        [Header(Wind)]
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.08
        _WindSpeed ("Wind Speed", Range(0, 10)) = 2.0
        _WindFrequency ("Wind Frequency", Range(0, 5)) = 1.0
        _WindDirectionWS ("Wind Direction WS", Vector) = (1, 0, 0, 0)

        [Header(Debug)]
        _DebugInteractionMask ("Debug Interaction Mask", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_GrassInteractionTex);
            SAMPLER(sampler_GrassInteractionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;

                float4 _GrassInteractionRect;
                float4 _GrassBendDirWS;
                float _EnableGrassInteraction;

                float _BendStrength;
                float _FlattenStrength;
                float _HeightMaskPower;
                float4 _GrassHeightAxisOS;
                float _GrassHeightMinOS;
                float _GrassHeightMaxOS;

                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirectionWS;

                float _DebugInteractionMask;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float bendMask : TEXCOORD1;
                float heightMask : TEXCOORD2;
            };

            float2 GetGrassInteractionUV(float3 positionWS)
            {
                float2 rectMin = _GrassInteractionRect.xy;
                float2 rectMax = _GrassInteractionRect.zw;
                float2 rectSize = rectMax - rectMin;

                float2 uv = float2(
                    (positionWS.x - rectMin.x) / max(abs(rectSize.x), 0.0001),
                    (positionWS.z - rectMin.y) / max(abs(rectSize.y), 0.0001)
                );

                return uv;
            }

            float IsInside01(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            float SampleGrassInteraction(float3 positionWS)
            {
                float2 uv = GetGrassInteractionUV(positionWS);

                float inside = IsInside01(uv);

                float mask = SAMPLE_TEXTURE2D_LOD(
                    _GrassInteractionTex,
                    sampler_GrassInteractionTex,
                    uv,
                    0
                ).r;

                return mask * inside * saturate(_EnableGrassInteraction);
            }

            float2 SafeNormalize2(float2 v, float2 fallback)
            {
                float lenSq = dot(v, v);

                if (lenSq < 0.0001)
                    return normalize(fallback);

                return v * rsqrt(lenSq);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 草根不动，草尖动。
                // 要求草 Mesh 的 UV.y：底部为 0，顶部为 1。
                float heightOS = dot(input.positionOS.xyz, _GrassHeightAxisOS.xyz);
                float heightRange = max(_GrassHeightMaxOS - _GrassHeightMinOS, 0.0001);
                float heightMask = saturate((heightOS - _GrassHeightMinOS) / heightRange);
                heightMask = pow(heightMask, _HeightMaskPower);

                // 采样 RT，得到当前草顶点所在位置的压草强度。
                float bendMask = SampleGrassInteraction(positionWS);

                // 角色 / 脚步压草方向。
                float2 bendDir = SafeNormalize2(_GrassBendDirWS.xz, float2(0, 1));

                // 简单风吹方向。
                float2 windDir = SafeNormalize2(_WindDirectionWS.xz, float2(1, 0));

                float windNoise = sin(
                    _Time.y * _WindSpeed +
                    positionWS.x * _WindFrequency +
                    positionWS.z * _WindFrequency
                );

                float2 windOffset = windDir * windNoise * _WindStrength * heightMask;

                // 压草横向偏移。
                float2 interactionOffset =
                    bendDir *
                    bendMask *
                    _BendStrength *
                    heightMask;

                positionWS.xz += windOffset + interactionOffset;

                // 往下压一点，让草更像被踩倒。
                positionWS.y -= bendMask * _FlattenStrength * heightMask;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.bendMask = bendMask;
                output.heightMask = heightMask;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float height = saturate(input.uv.y);

                half4 color = lerp(_BaseColor, _TipColor, height);

                // 被压到的地方稍微变暗，方便观察。
                color.rgb *= lerp(1.0, 0.65, saturate(input.bendMask));

                // Debug 模式：直接显示 RT 采样结果。
                if (_DebugInteractionMask > 0.5)
                {
                    float m = saturate(input.bendMask);
                    return half4(m, m, m, 1);
                }

                return color;
            }

            ENDHLSL
        }
    }
}
