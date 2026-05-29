Shader "Snow/SnowTrailBrush"
{
    Properties
    {
        [Header(Trail Shape)]
        _SinkStrength ("Sink Strength", Range(0, 1)) = 0.65
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.25

        [Header(Width)]
        _CenterWidth ("Center Width", Range(0, 1)) = 0.45
        _EdgeWidth ("Edge Width", Range(0, 1)) = 0.78
        _OuterSoftness ("Outer Softness", Range(0.01, 1)) = 0.25

        [Header(Length)]
        _LengthSoftness ("Length Softness", Range(0.01, 1)) = 0.25

        [Header(Debug)]
        _DebugView ("Debug View", Float) = 0
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
            Name "SnowTrailBrush"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            // 多个 Brush 同一帧重叠时取更强值。
            // R/G/A 都会取 max，适合写数据 RT。
            BlendOp Max
            Blend One One

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _SinkStrength;
                half _RimStrength;

                half _CenterWidth;
                half _EdgeWidth;
                half _OuterSoftness;

                half _LengthSoftness;

                half _DebugView;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;

                return OUT;
            }

            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // 把 UV 从 0~1 转成 -1~1。
                //
                // p.x = 左右方向
                // p.y = 前后方向
                float2 p = IN.uv * 2.0 - 1.0;

                half side = abs((half)p.x);
                half forward = abs((half)p.y);

                // =====================================================
// Capsule Length Mask
//
// 让 Brush 前后端变成圆弧，而不是横向直线。
// p.x 控制左右宽度，p.y 控制前后长度。
// capsuleY 越大，前后圆头越明显。
// =====================================================
half capsuleY = saturate(1.0h - _LengthSoftness);

// 中间直段：abs(p.y) <= capsuleY
// 两端圆头：abs(p.y) > capsuleY
half2 capP = half2(
    side,
    max(0.0h, forward - capsuleY) / max(0.0001h, 1.0h - capsuleY)
);

// 胶囊距离。
// 中间区域主要由 side 控制；
// 前后圆头区域由 side + forward 共同控制。
half capsuleDist = length(capP);

// 胶囊整体 mask。
// capsuleDist <= 1 内有效，外侧渐隐。
half lengthMask = 1.0h - SafeSmoothStep(1.0h - _LengthSoftness, 1.0h, capsuleDist);

                // -----------------------------------------------------
                // Center Sink
                //
                // 中间区域写 R 通道，表示雪沟下陷。
                //
                // side <= _CenterWidth:
                //     基本是沟底，sink 接近 1。
                //
                // side 从 _CenterWidth 到 _EdgeWidth:
                //     从沟底逐渐过渡到边缘。
                // -----------------------------------------------------
                half sinkShape = 1.0h - SafeSmoothStep(_CenterWidth, _EdgeWidth, side);
                sinkShape *= lengthMask;

                // -----------------------------------------------------
                // Rim
                //
                // 两侧边缘写 G 通道，表示雪被挤起来的边。
                //
                // rimInner:
                //     从沟底外侧开始出现。
                //
                // rimOuter:
                //     到最外侧逐渐消失。
                //
                // 两者相乘后，会得到左右两条软边雪墙。
                // -----------------------------------------------------
                half rimInner = SafeSmoothStep(_CenterWidth, _EdgeWidth, side);

                half outerStart = saturate(_EdgeWidth);
                half outerEnd = saturate(_EdgeWidth + _OuterSoftness);

                half rimOuter = 1.0h - SafeSmoothStep(outerStart, outerEnd, side);

                half rimShape = rimInner * rimOuter;
                rimShape *= lengthMask;

                // 总 mask：sink 和 rim 有一个存在就认为这个 brush 有效。
                half mask = saturate(max(sinkShape, rimShape));

                half sink = saturate(sinkShape * _SinkStrength);
                half rim = saturate(rimShape * _RimStrength);

                // DebugView:
                // 0 = 正常输出数据
                // 1 = 只看 sink
                // 2 = 只看 rim
                // 3 = 只看 mask
                if (_DebugView > 0.5h && _DebugView < 1.5h)
                    return half4(sink.xxx, 1.0h);

                if (_DebugView > 1.5h && _DebugView < 2.5h)
                    return half4(rim.xxx, 1.0h);

                if (_DebugView > 2.5h)
                    return half4(mask.xxx, 1.0h);

                // Snow RT 协议：
                // R = sink，下陷
                // G = rim，雪边
                // B = 0，预留
                // A = mask，有效区域
                return half4(sink, rim, 0.0h, mask);
            }

            ENDHLSL
        }
    }
}