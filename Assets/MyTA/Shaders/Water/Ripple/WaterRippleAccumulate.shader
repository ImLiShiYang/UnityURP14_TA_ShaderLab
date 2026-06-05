Shader "Hidden/WaterRipple/WaterRippleAccumulate"
{
    Properties
    {
        _MainTex ("Current Brush RT", 2D) = "black" {}
        _LastTex ("Last Accum RT", 2D) = "black" {}
        _Offset ("Offset", Vector) = (0,0,0,0)
        _ReduceVal ("Fade Reduce", Float) = 0.001
        _EdgeSoftness ("Edge Softness", Float) = 25
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
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

            half3 DecodeNormalRGB(half3 rgb)
            {
                return normalize(rgb * 2.0h - 1.0h);
            }

            half3 EncodeNormalRGB(half3 n)
            {
                return n * 0.5h + 0.5h;
            }

            // Whiteout 法线混合
            // 用来把当前帧水波法线和上一帧累积法线叠加
            half3 WhiteoutBlend(half3 baseNormal, half3 addNormal)
            {
                return normalize(half3(
                    baseNormal.xy + addNormal.xy,
                    baseNormal.z * addNormal.z
                ));
            }
            
            float2 SnapUVToTexel(float2 uv, float4 texelSize)
            {
                float2 pixel = floor(uv * texelSize.zw) + 0.5;
                return pixel * texelSize.xy;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                const half EPS = 0.001h;

                half3 defaultNormal = half3(0.0h, 0.0h, 1.0h);

                // =====================================================
                // 1. 当前帧 Brush
                // =====================================================
                half4 cur = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // A 通道解码：
                // A = 0.5 表示无变化
                // A < 0.5 表示下陷
                // A > 0.5 表示隆起
                half curSigned = (cur.a - 0.5h) * 2.0h;
                half curInfluence = abs(curSigned);

                half3 curNormal = defaultNormal;

                if (curInfluence > EPS)
                {
                    curNormal = DecodeNormalRGB(cur.rgb);
                }


                // =====================================================
                // 2. 上一帧累积结果
                // =====================================================
                float2 lastUV = uv - _Offset;

                float inside =
                    step(0.0, lastUV.x) *
                    step(0.0, lastUV.y) *
                    step(lastUV.x, 1.0) *
                    step(lastUV.y, 1.0);

                lastUV = SnapUVToTexel(lastUV, _LastTex_TexelSize);

                half4 lastSample = SAMPLE_TEXTURE2D(_LastTex, sampler_LastTex, lastUV);

                half lastSigned = (lastSample.a - 0.5h) * 2.0h;

                // 超出范围就当作无高度变化
                lastSigned *= inside;

                // 淡出 signed height：
                // 不管是下陷还是隆起，都往 0 衰减。
                half lastAbs = abs(lastSigned);
                lastAbs = saturate(lastAbs - _ReduceVal);
                lastSigned = sign(lastSigned) * lastAbs;

                half lastInfluence = abs(lastSigned);

                half3 lastNormal = defaultNormal;

                if (lastInfluence > EPS)
                {
                    lastNormal = DecodeNormalRGB(lastSample.rgb);
                }


                // =====================================================
                // 3. 合并 signed height
                // =====================================================
                // 谁的绝对高度变化更强，就保留谁。
                half outSigned = lastSigned;
                half3 mixedNormal = lastNormal;

                if (curInfluence > lastInfluence)
                {
                    outSigned = curSigned;
                    mixedNormal = curNormal;
                }


                // =====================================================
                // 4. 边缘渐隐
                // =====================================================
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = saturate(min(edgeX, edgeY) * _EdgeSoftness);

                // signed height 往 0 衰减
                outSigned *= edge;

                half outInfluence = abs(outSigned);

                if (outInfluence <= EPS)
                {
                    mixedNormal = defaultNormal;
                    outSigned = 0.0h;
                }

                mixedNormal = normalize(mixedNormal);

                half3 outRGB = EncodeNormalRGB(mixedNormal);

                // 编码回 A:
                // -1 -> 0
                //  0 -> 0.5
                // +1 -> 1
                half outA = outSigned * 0.5h + 0.5h;

                return half4(outRGB, outA);
            }

            ENDHLSL
        }
    }
}