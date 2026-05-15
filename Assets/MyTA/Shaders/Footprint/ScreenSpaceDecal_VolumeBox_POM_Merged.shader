Shader "Custom/ScreenSpaceDecal_VolumeBox_POM_Merged"
{
    Properties
    {
        _DecalTexture("Base / Decal Texture RGB + Alpha", 2D) = "white" {}
        _DecalColor("Decal Color", Color) = (1,1,1,1)
        _UseBaseRGB("Use Base RGB 0 ColorOnly 1 BaseRGB", Range(0, 1)) = 1

        [Normal] _DecalNormalTexture("Normal Texture", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 3)) = 1.0

        _DecalHeightTexture("Height Texture", 2D) = "gray" {}
        _HeightGround("Height Ground Level", Range(0, 1)) = 0.5
        _HeightContrast("Height Contrast", Range(0, 8)) = 3.0
        _InvertHeight("Invert Height", Range(0, 1)) = 0.0

        _ParallaxStrength("Parallax Strength", Range(-0.2, 0.2)) = 0.06
        _POMMinSteps("POM Min Steps", Range(1, 32)) = 12
        _POMMaxSteps("POM Max Steps", Range(1, 96)) = 64

        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25
        _DiffuseStrength("Diffuse Strength", Range(0, 2)) = 1.0

        _AlphaFromPOM("Alpha From POM 0 Stable 1 POM", Range(0, 1)) = 1
        _DebugView("Debug View 0 Final 1 HeightDepth 2 Offset", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ScreenSpaceDecalVolumeBox_POM_Merged"

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
            TEXTURE2D(_DecalNormalTexture); SAMPLER(sampler_DecalNormalTexture);
            TEXTURE2D(_DecalHeightTexture); SAMPLER(sampler_DecalHeightTexture);
            TEXTURE2D_X_FLOAT(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);

            float4x4 _DecalWorldToLocal;

            float4 _DecalColor;
            float _UseBaseRGB;

            // x = opacity, y = edgeFade, z = cos(angleEnd), w = cos(angleStart)
            float4 _DecalParams;

            // xy = tiling, zw = offset
            float4 _DecalTilingOffset;

            float4 _DecalBackwardWS;
            float4 _DecalDistanceFade;

            float4 _DecalTangentWS;
            float4 _DecalBitangentWS;
            float4 _DecalNormalWS;

            float _NormalStrength;

            float _HeightGround;
            float _HeightContrast;
            float _InvertHeight;

            float _ParallaxStrength;
            float _POMMinSteps;
            float _POMMaxSteps;

            float _AmbientStrength;
            float _DiffuseStrength;

            float _AlphaFromPOM;
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

                if (dot(normalWS, viewDirWS) < 0.0)
                    normalWS = -normalWS;

                return normalWS;
            }

            bool IsUVOutside01(float2 uv)
            {
                return uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
            }

            half4 SampleBaseSafe(float2 uv)
            {
                if (IsUVOutside01(uv))
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);

                return SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, uv);
            }

            half4 SampleNormalSafe(float2 uv)
            {
                if (IsUVOutside01(uv))
                    return half4(0.5h, 0.5h, 1.0h, 1.0h);

                return SAMPLE_TEXTURE2D(_DecalNormalTexture, sampler_DecalNormalTexture, uv);
            }

            half SampleHeightRawSafe(float2 uv)
            {
                if (IsUVOutside01(uv))
                    return _HeightGround;

                half h = SAMPLE_TEXTURE2D(_DecalHeightTexture, sampler_DecalHeightTexture, uv).r;
                h = lerp(h, 1.0h - h, _InvertHeight);
                return h;
            }

            half SampleDepthForPOM(float2 uv)
            {
                half h = SampleHeightRawSafe(uv);
                half depth = saturate((_HeightGround - h) / max(_HeightGround, 0.0001h));
                depth = saturate(depth * _HeightContrast * 0.35h);
                return depth;
            }

            float2 ParallaxOcclusionMapping(float2 uv, float3 viewDirTS, out float2 uvOffset)
            {
                uvOffset = 0.0;

                if (abs(_ParallaxStrength) < 0.00001)
                    return uv;

                viewDirTS = normalize(viewDirTS);

                float viewZ = max(abs(viewDirTS.z), 0.08);
                float ndotv = saturate(abs(viewDirTS.z));

                int stepCount = (int)round(lerp(_POMMaxSteps, _POMMinSteps, ndotv));
                stepCount = clamp(stepCount, 1, 96);

                float layerDepth = 1.0 / stepCount;
                float currentLayerDepth = 0.0;

                float2 parallaxDir = viewDirTS.xy / viewZ;
                float2 deltaUV = parallaxDir * _ParallaxStrength / stepCount;

                float2 currentUV = uv;
                float2 previousUV = uv;

                half currentDepth = SampleDepthForPOM(currentUV);
                half previousDepth = currentDepth;

                float previousLayerDepth = 0.0;

                [loop]
                for (int i = 0; i < 96; i++)
                {
                    if (i >= stepCount)
                        break;

                    if (currentLayerDepth >= currentDepth)
                        break;

                    previousUV = currentUV;
                    previousDepth = currentDepth;
                    previousLayerDepth = currentLayerDepth;

                    currentUV -= deltaUV;
                    currentLayerDepth += layerDepth;
                    currentDepth = SampleDepthForPOM(currentUV);
                }

                float afterDepth = currentDepth - currentLayerDepth;
                float beforeDepth = previousDepth - previousLayerDepth;
                float denom = afterDepth - beforeDepth;

                float weight = 0.0;
                if (abs(denom) > 0.00001)
                    weight = saturate(afterDepth / denom);

                float2 finalUV = lerp(currentUV, previousUV, weight);
                uvOffset = finalUV - uv;

                return finalUV;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // ============================================================
                // 1. Screen UV + depth reconstruct
                // ============================================================

                float2 screenUV = GetScreenUV(input.positionCS);

                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0)
                    discard;

                float3 worldPos = ReconstructWorldPosition(screenUV);

                // ============================================================
                // 2. Decal box clipping
                // ============================================================

                float3 decalLocalPos = mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;
                float3 absLocal = abs(decalLocalPos);

                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
                    discard;

                // ============================================================
                // 3. Decal UV
                // ============================================================

                float2 decalUV = decalLocalPos.xy + 0.5;
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                // ============================================================
                // 4. Decal TBN + view direction
                // ============================================================

                half3 tangentWS = normalize(_DecalTangentWS.xyz);
                half3 bitangentWS = normalize(_DecalBitangentWS.xyz);
                half3 decalNormalWS = normalize(_DecalNormalWS.xyz);

                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 viewDirTS = float3(
                    dot(viewDirWS, tangentWS),
                    dot(viewDirWS, bitangentWS),
                    dot(viewDirWS, decalNormalWS)
                );

                // ============================================================
                // 5. POM
                // ============================================================

                float2 uvOffset;
                float2 pomUV = ParallaxOcclusionMapping(decalUV, viewDirTS, uvOffset);

                half4 basePOM = SampleBaseSafe(pomUV);
                half4 baseStable = SampleBaseSafe(decalUV);

                // ============================================================
                // 6. Debug
                // ============================================================

                if (_DebugView > 0.5 && _DebugView < 1.5)
                {
                    half d = SampleDepthForPOM(decalUV);
                    return half4(d, d, d, 1.0h);
                }

                if (_DebugView >= 1.5)
                {
                    float2 o = abs(uvOffset) * 80.0;
                    return half4(o.x, o.y, 0.0h, 1.0h);
                }

                // ============================================================
                // 7. Normal lighting
                // ============================================================

                half4 packedNormal = SampleNormalSafe(pomUV);
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);

                half3 bumpNormalWS = normalize(
                    normalTS.x * tangentWS +
                    normalTS.y * bitangentWS +
                    normalTS.z * decalNormalWS
                );

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(bumpNormalWS, normalize(mainLight.direction)));

                half lighting = _AmbientStrength + ndotl * _DiffuseStrength;
                lighting = saturate(lighting);

                // ============================================================
                // 8. Base color + alpha
                // ============================================================

                half3 baseRGB = lerp(half3(1.0h, 1.0h, 1.0h), basePOM.rgb, _UseBaseRGB);
                baseRGB = saturate((baseRGB - 0.03h) * 1.8h);

                half4 color;
                half depthShade = SampleDepthForPOM(pomUV);
                half heightDarken = lerp(1.0h, 0.62h, depthShade);
                color.rgb = baseRGB * _DecalColor.rgb * lighting * heightDarken;
                // color.rgb = baseRGB * _DecalColor.rgb * lighting;

                // _AlphaFromPOM = 1：alpha 也跟着 POM，视差最明显，但边缘可能锯齿。
                // _AlphaFromPOM = 0：alpha 用原始 decalUV，轮廓更稳定，但视差边缘弱一点。
                half alphaStable = baseStable.a;
                half alphaPOM = basePOM.a;
                color.a = lerp(alphaStable, alphaPOM, _AlphaFromPOM) * _DecalColor.a;

                // ============================================================
                // 9. Box Fade / Angle Fade / Distance Fade
                // ============================================================

                float distToPlaneEdge = min(0.5 - absLocal.x, 0.5 - absLocal.y);
                float edgeFade = max(_DecalParams.y, 0.0001);
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                float3 sceneNormalWS = ReconstructWorldNormalFromDepth(worldPos);
                float3 decalBackwardWS = normalize(_DecalBackwardWS.xyz);
                float facing = saturate(dot(sceneNormalWS, decalBackwardWS));
                float angleFade = smoothstep(_DecalParams.z, _DecalParams.w, facing);

                color.a *= _DecalParams.x;
                color.a *= _DecalDistanceFade.x;
                color.a *= angleFade;
                color.a *= boxFade;

                return color;
            }

            ENDHLSL
        }
    }
}