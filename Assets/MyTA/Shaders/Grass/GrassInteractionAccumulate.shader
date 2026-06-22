Shader "Hidden/Grass/InteractionAccumulate"
{
    Properties
    {
        _MainTex ("Current Brush RT", 2D) = "black" {}
        _LastTex ("Last Accum RT", 2D) = "black" {}
        _Offset ("History UV Offset", Vector) = (0, 0, 0, 0)
        _DecayAmount ("Recovery Amount Per Frame", Float) = 0
        _EdgeSoftness ("Edge Softness", Float) = 25
        _InteractionRect ("Interaction Rect", Vector) = (0, 0, 1, 1)
        _PressCenter0WS ("Left Foot Center WS", Vector) = (0, 0, 0, 0)
        _PressCenter1WS ("Right Foot Center WS", Vector) = (0, 0, 0, 0)
        _EnablePressCenter0 ("Enable Left Foot", Float) = 0
        _EnablePressCenter1 ("Enable Right Foot", Float) = 0
        _PressRadius0 ("Left Foot Radius", Float) = 0.45
        _PressRadius1 ("Right Foot Radius", Float) = 0.45
        _EnableRadialPress ("Enable Radial Press", Float) = 1
        _RadialMaskPower ("Radial Mask Power", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

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
                float _DecayAmount;
                float _EdgeSoftness;
                float4 _InteractionRect;
                float4 _PressCenter0WS;
                float4 _PressCenter1WS;
                float _EnablePressCenter0;
                float _EnablePressCenter1;
                float _PressRadius0;
                float _PressRadius1;
                float _EnableRadialPress;
                float _RadialMaskPower;
                float4 _LastTex_TexelSize;
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

            float2 SnapUVToTexel(float2 uv)
            {
                float2 pixel = floor(uv * _LastTex_TexelSize.zw) + 0.5;
                return pixel * _LastTex_TexelSize.xy;
            }

            float FootMask(float2 worldXZ, float3 centerWS, float radius, float enabled)
            {
                float safeRadius = max(radius, 0.0001);
                float mask = 1.0 - saturate(distance(worldXZ, centerWS.xz) / safeRadius);
                mask = smoothstep(0.0, 1.0, mask);
                mask = pow(mask, max(_RadialMaskPower, 0.0001));
                return mask * saturate(enabled) * saturate(_EnableRadialPress);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float currentMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

                float2 historyUV = uv - _Offset;
                float inside =
                    step(0.0, historyUV.x) *
                    step(0.0, historyUV.y) *
                    step(historyUV.x, 1.0) *
                    step(historyUV.y, 1.0);

                historyUV = SnapUVToTexel(historyUV);
                float historyMask = SAMPLE_TEXTURE2D(_LastTex, sampler_LastTex, historyUV).r;
                historyMask = saturate(historyMask - max(_DecayAmount, 0.0)) * inside;

                float2 worldXZ = lerp(_InteractionRect.xy, _InteractionRect.zw, uv);
                float leftFootMask = FootMask(
                    worldXZ,
                    _PressCenter0WS.xyz,
                    _PressRadius0,
                    _EnablePressCenter0
                );
                float rightFootMask = FootMask(
                    worldXZ,
                    _PressCenter1WS.xyz,
                    _PressRadius1,
                    _EnablePressCenter1
                );

                float liveFootMask = max(leftFootMask, rightFootMask);
                float resultMask = max(currentMask, max(historyMask, liveFootMask));

                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = saturate(min(edgeX, edgeY) * _EdgeSoftness);
                resultMask = saturate(resultMask * edge);

                return half4(resultMask, resultMask, resultMask, 1.0);
            }
            ENDHLSL
        }
    }
}
