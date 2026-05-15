Shader "Custom/ScreenSpaceDecal_VolumeBox_DoubleLayer"
{
    Properties
    {
        _DecalTexture("Main Decal RGB + Alpha", 2D) = "white" {}
        _DecalColor("Main Decal Color", Color) = (1,1,1,1)
        _UseBaseRGB("Use Main RGB 0 ColorOnly 1 BaseRGB", Range(0, 1)) = 1
        _MainOpacity("Main Opacity", Range(0, 1)) = 1

        _ShadowTexture("Shadow Decal Alpha", 2D) = "black" {}
        _ShadowColor("Shadow Color", Color) = (0.06, 0.08, 0.055, 1)
        _ShadowOpacity("Shadow Opacity", Range(0, 1)) = 0.35
        _ShadowScale("Shadow Scale", Range(0.5, 2.0)) = 1.08
        _ShadowOffset("Shadow Offset XY", Vector) = (0.015, -0.015, 0, 0)

        [Normal] _DecalNormalTexture("Normal Texture", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 3)) = 0.8

        _DecalHeightTexture("Height Texture", 2D) = "gray" {}
        _HeightGround("Height Ground Level", Range(0, 1)) = 0.5
        _HeightContrast("Height Contrast", Range(0, 8)) = 2.0
        _HeightDarkenStrength("Height Darken Strength", Range(0, 1)) = 0.25

        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.45
        _DiffuseStrength("Diffuse Strength", Range(0, 2)) = 0.65

        _DebugView("Debug View 0 Final 1 MainA 2 ShadowA 3 Height 4 Normal", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ScreenSpaceDecalVolumeBox_DoubleLayer"

            ZWrite Off
            ZTest Always
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_DecalTexture); SAMPLER(sampler_DecalTexture);
            TEXTURE2D(_ShadowTexture); SAMPLER(sampler_ShadowTexture);
            TEXTURE2D(_DecalNormalTexture); SAMPLER(sampler_DecalNormalTexture);
            TEXTURE2D(_DecalHeightTexture); SAMPLER(sampler_DecalHeightTexture);
            TEXTURE2D_X_FLOAT(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);

            float4x4 _DecalWorldToLocal;

            float4 _DecalColor;
            float _UseBaseRGB;
            float _MainOpacity;

            float4 _ShadowColor;
            float _ShadowOpacity;
            float _ShadowScale;
            float4 _ShadowOffset;

            float4 _DecalParams;
            float4 _DecalTilingOffset;
            float4 _DecalBackwardWS;
            float4 _DecalDistanceFade;

            float4 _DecalTangentWS;
            float4 _DecalBitangentWS;
            float4 _DecalNormalWS;

            float _NormalStrength;

            float _HeightGround;
            float _HeightContrast;
            float _HeightDarkenStrength;

            float _AmbientStrength;
            float _DiffuseStrength;
            float _DebugView;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float2 GetScreenUV(float4 positionCS)
            {
                return positionCS.xy / _ScaledScreenParams.xy;
            }

            float SampleRawDepth(float2 screenUV)
            {
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV).r;
            }

            float3 ReconstructWorldPosition(float2 screenUV)
            {
                float rawDepth = SampleRawDepth(screenUV);
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001) discard;
                #else
                    if (rawDepth >= 0.999999) discard;
                #endif
                return ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
            }

            float3 ReconstructWorldNormalFromDepth(float3 worldPos)
            {
                float3 dx = ddx(worldPos);
                float3 dy = ddy(worldPos);
                float3 normalWS = normalize(cross(dy, dx));
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                if (dot(normalWS, viewDirWS) < 0.0) normalWS = -normalWS;
                return normalWS;
            }

            bool IsUVOutside01(float2 uv)
            {
                return uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
            }

            half4 SampleMainSafe(float2 uv)
            {
                if (IsUVOutside01(uv)) return half4(0.0h, 0.0h, 0.0h, 0.0h);
                return SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, uv);
            }

            half4 SampleShadowSafe(float2 uv)
            {
                if (IsUVOutside01(uv)) return half4(0.0h, 0.0h, 0.0h, 0.0h);
                return SAMPLE_TEXTURE2D(_ShadowTexture, sampler_ShadowTexture, uv);
            }

            half4 SampleNormalSafe(float2 uv)
            {
                if (IsUVOutside01(uv)) return half4(0.5h, 0.5h, 1.0h, 1.0h);
                return SAMPLE_TEXTURE2D(_DecalNormalTexture, sampler_DecalNormalTexture, uv);
            }

            half SampleHeightSafe(float2 uv)
            {
                if (IsUVOutside01(uv)) return _HeightGround;
                return SAMPLE_TEXTURE2D(_DecalHeightTexture, sampler_DecalHeightTexture, uv).r;
            }

            half SampleDepthForDarken(float2 uv)
            {
                half h = SampleHeightSafe(uv);
                half depth = saturate((_HeightGround - h) / max(_HeightGround, 0.0001h));
                depth = saturate(depth * _HeightContrast * 0.35h);
                return depth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetScreenUV(input.positionCS);
                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0) discard;

                float3 worldPos = ReconstructWorldPosition(screenUV);

                float3 decalLocalPos = mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;
                float3 absLocal = abs(decalLocalPos);
                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5) discard;

                float2 decalUV = decalLocalPos.xy + 0.5;
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                float2 shadowUV = (decalUV - 0.5) / max(_ShadowScale, 0.0001) + 0.5 + _ShadowOffset.xy;

                half4 mainTex = SampleMainSafe(decalUV);
                half4 shadowTex = SampleShadowSafe(shadowUV);

                half mainA = mainTex.a * _MainOpacity * _DecalColor.a;
                half shadowA = shadowTex.a * _ShadowOpacity * _ShadowColor.a;

                half3 tangentWS = normalize(_DecalTangentWS.xyz);
                half3 bitangentWS = normalize(_DecalBitangentWS.xyz);
                half3 decalNormalWS = normalize(_DecalNormalWS.xyz);

                half4 packedNormal = SampleNormalSafe(decalUV);
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);
                half3 bumpNormalWS = normalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * decalNormalWS);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(bumpNormalWS, normalize(mainLight.direction)));
                half lighting = saturate(_AmbientStrength + ndotl * _DiffuseStrength);

                half depth = SampleDepthForDarken(decalUV);
                half heightDarken = lerp(1.0h, 0.58h, depth * _HeightDarkenStrength);

                half3 mainBaseRGB = lerp(half3(1.0h, 1.0h, 1.0h), mainTex.rgb, _UseBaseRGB);
                half3 mainRGB = mainBaseRGB * _DecalColor.rgb * lighting * heightDarken;
                half3 shadowRGB = _ShadowColor.rgb;

                if (_DebugView > 0.5 && _DebugView < 1.5) return half4(mainA, mainA, mainA, 1.0h);
                if (_DebugView >= 1.5 && _DebugView < 2.5) return half4(shadowA, shadowA, shadowA, 1.0h);
                if (_DebugView >= 2.5 && _DebugView < 3.5) return half4(depth, depth, depth, 1.0h);
                if (_DebugView >= 3.5) return half4(normalTS * 0.5h + 0.5h, mainA);

                float distToPlaneEdge = min(0.5 - absLocal.x, 0.5 - absLocal.y);
                float edgeFade = max(_DecalParams.y, 0.0001);
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                float3 sceneNormalWS = ReconstructWorldNormalFromDepth(worldPos);
                float3 decalBackwardWS = normalize(_DecalBackwardWS.xyz);
                float facing = saturate(dot(sceneNormalWS, decalBackwardWS));
                float angleFade = smoothstep(_DecalParams.z, _DecalParams.w, facing);

                half fade = _DecalParams.x * _DecalDistanceFade.x * angleFade * boxFade;

                mainA *= fade;
                shadowA *= fade;

                half finalA = saturate(shadowA + mainA * (1.0h - shadowA));
                half3 premulRGB = shadowRGB * shadowA * (1.0h - mainA) + mainRGB * mainA;
                half3 finalRGB = premulRGB / max(finalA, 0.0001h);

                return half4(finalRGB, finalA);
            }

            ENDHLSL
        }
    }
}