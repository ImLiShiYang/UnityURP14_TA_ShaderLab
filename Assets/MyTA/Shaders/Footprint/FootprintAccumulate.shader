Shader "Hidden/Footprints/FootprintAccumulate"
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
            // 用来把当前帧脚印法线和上一帧累积法线叠加
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

                half3 defaultNormal = half3(0.0h, 0.0h, 1.0h);
                half3 defaultRGB = half3(0.5h, 0.5h, 1.0h);

                // =====================================================
                // 1. 当前帧 Brush
                // =====================================================
                half4 cur = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                // return cur;

                // 当前帧高度 / mask
                half curHeight = saturate(cur.a);

                // 当前帧没有脚印的地方，不允许 cur.rgb 参与混合
                half3 curNormal = DecodeNormalRGB(cur.rgb);
                curNormal = normalize(lerp(defaultNormal, curNormal, step(0.0001h, curHeight)));


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

                // 超出范围就当作没有历史脚印
                half lastHeight = saturate(lastSample.a * inside);

                // 只对历史高度淡出
                lastHeight = saturate(lastHeight - _ReduceVal);

                // 历史没有脚印的地方，历史法线强制默认
                half3 lastNormal = DecodeNormalRGB(lastSample.rgb);
                lastNormal = normalize(lerp(defaultNormal, lastNormal, step(0.0001h, lastHeight)));


                // =====================================================
                // 3. 合并高度
                // =====================================================
                half outHeight = max(curHeight, lastHeight);


                // =====================================================
                // 4. 合并法线
                // =====================================================
                half3 mixedNormal = lastNormal;

                // 只有当前帧有脚印时，才 Whiteout 混合当前法线
                if (curHeight > lastHeight)
                {
                    mixedNormal = curNormal;
                }


                // 如果最终没有脚印，强制默认法线
                mixedNormal = normalize(lerp(defaultNormal, mixedNormal, step(0.0001h, outHeight)));


                // =====================================================
                // 5. 边缘渐隐
                // =====================================================
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = saturate(min(edgeX, edgeY) * _EdgeSoftness);

                outHeight *= edge;

                // 边缘没有脚印时，法线也回默认
                mixedNormal = normalize(lerp(defaultNormal, mixedNormal, step(0.0001h, outHeight)));

                half3 outRGB = EncodeNormalRGB(mixedNormal);

                return half4(outRGB, outHeight);
            }

            ENDHLSL
        }
    }
}