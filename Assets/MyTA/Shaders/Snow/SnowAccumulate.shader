Shader "Hidden/Snow/SnowAccumulate"
{
    Properties
    {
        _MainTex ("Current Brush RT", 2D) = "black" {}
        _LastTex ("Last Accum RT", 2D) = "black" {}
        _Offset ("Offset", Vector) = (0,0,0,0)
        _ReduceVal ("Fade Reduce", Float) = 0
        _EdgeSoftness ("Edge Softness", Float) = 25
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "SnowAccumulate"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_LastTex);
            SAMPLER(sampler_LastTex);

            CBUFFER_START(UnityPerMaterial)
                float2 _Offset;
                float _ReduceVal;
                float _EdgeSoftness;
                float4 _LastTex_TexelSize;
                float4 _MainTex_TexelSize;
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

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float2 SnapUVToTexel(float2 uv, float4 texelSize)
            {
                float2 pixel = floor(uv * texelSize.zw) + 0.5;
                return pixel * texelSize.xy;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // =====================================================
                // Snow RT 数据协议
                // =====================================================
                // R = sink：雪被压下去的深度，0 表示没有压陷，1 表示最大压陷。
                // G = rim ：雪边凸起强度，第一阶段可以一直为 0，后续做雪边时再写入。
                // B = 预留。
                // A = mask：brush 覆盖范围，主要用于调试、混合和后续扩展。
                //
                // 注意：
                // 这里不再处理 encoded normal，也不再使用 A=0.5 的 signed height。
                // 雪地版本的默认背景就是 float4(0,0,0,0)。
                // =====================================================

                // =====================================================
                // 1. 当前帧 Brush
                // =====================================================
                half4 cur = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                half curSink = saturate(cur.r);
                half curRim  = saturate(cur.g);
                half curMask = saturate(cur.a);

                // =====================================================
                // 2. 上一帧累积结果
                // =====================================================
                // _Offset 用来补偿 FootstepCamera 中心移动。
                // manager 中的约定是：
                //     _Offset = (lastCenter - currentCenter) / diameter
                //
                // 所以当前 uv 对应的旧历史采样位置为：
                //     lastUV = uv - _Offset
                float2 lastUV = uv - _Offset;

                float inside =
                    step(0.0, lastUV.x) *
                    step(0.0, lastUV.y) *
                    step(lastUV.x, 1.0) *
                    step(lastUV.y, 1.0);

                // 历史 RT 会随着角色移动不断滚动。
                // 采样时吸附到 texel 中心，可以减少小数 UV 重采样带来的模糊和抖动。
                lastUV = SnapUVToTexel(lastUV, _LastTex_TexelSize);

                half4 lastSample = SAMPLE_TEXTURE2D(_LastTex, sampler_LastTex, lastUV) * inside;

                half lastSink = saturate(lastSample.r);
                half lastRim  = saturate(lastSample.g);
                half lastMask = saturate(lastSample.a);

                // =====================================================
                // 3. 淡出
                // =====================================================
                // 如果希望雪痕永久保留，把 _ReduceVal 设为 0。
                // 如果希望雪痕慢慢恢复，把 _ReduceVal 设为一个很小的值。
                if (_ReduceVal > 0.0)
                {
                    lastSink = saturate(lastSink - _ReduceVal);
                    lastRim  = saturate(lastRim  - _ReduceVal);
                    lastMask = saturate(lastMask - _ReduceVal);
                }

                // =====================================================
                // 4. 合并当前 Brush 和历史累积
                // =====================================================
                // 第一阶段使用 max：
                // - 已经压下去的地方不会被较弱 brush 抹平。
                // - 新 brush 更强时会覆盖成更深的压陷。
                //
                // 后续如果要做“多次踩踏越来越深”，可以把 sink 改成：
                //     saturate(lastSink + curSink * _AddStrength)
                half outSink = max(lastSink, curSink);
                half outRim  = max(lastRim,  curRim);
                half outMask = max(lastMask, curMask);

                // =====================================================
                // 5. RT 边缘渐隐
                // =====================================================
                // 当角色移动时，FootstepCamera 覆盖范围也会移动。
                // 历史痕迹接近 RT 边缘时渐隐，避免出现硬切边。
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = saturate(min(edgeX, edgeY) * _EdgeSoftness);

                outSink *= edge;
                outRim  *= edge;
                outMask *= edge;

                return half4(outSink, outRim, 0.0h, outMask);
            }

            ENDHLSL
        }
    }
}
