Shader "Snow/SnowSurface_RTDeform"
{
    Properties
    {
        [Header(Base Snow)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 3)) = 1

        [Header(Snow Deform RT)]
        _FootstepTex ("Snow Deform RT R Sink G Rim A Mask", 2D) = "black" {}
        [HideInInspector] _SmoothFootstepTex ("Smoothed Snow Deform RT", 2D) = "black" {}
        _FootstepRect ("Footstep Rect", Vector) = (0,0,1,1)
        _EnableFootstep ("Enable Footstep", Float) = 0

        [Header(Vertex Displacement)]
        _MaxSnowSink ("Max Snow Sink", Range(0, 2)) = 0.35
        _RimHeight ("Rim Height", Range(0, 1)) = 0.08
        _SnowDeformStrength ("Snow Deform Strength", Range(0, 2)) = 1
        _SnowHeightBlurRadius ("Geometry Blur Radius (Texels)", Range(0, 8)) = 2.5
        _SnowHeightBlurStrength ("Geometry Blur Strength", Range(0, 1)) = 0.65

        [Header(Local Tessellation)]
        _TessellationFactor ("Tessellation Factor", Range(1, 16)) = 3
        _TessellationBorderFade ("Tessellation Border Fade", Range(0.1, 8)) = 2

        [Header(Snow Normal From RT)]
        _SnowNormalStrength ("Snow Normal Strength", Range(0, 3)) = 1
        _SnowNormalSampleRadius ("Normal Sample Radius (Texels)", Range(1, 12)) = 4
        _SnowNormalSmoothMin ("Normal Smooth Min", Range(0, 1)) = 0.01
        _SnowNormalSmoothMax ("Normal Smooth Max", Range(0, 1)) = 0.5

        [Header(Snow Color Response)]
        _SnowAOStrength ("Depression AO Strength", Range(0, 1)) = 0.25
        _SnowRimLightStrength ("Rim Light Strength", Range(0, 0.8)) = 0.12
        _SnowMaskSmoothMin ("Mask Smooth Min", Range(0, 1)) = 0.02
        _SnowMaskSmoothMax ("Mask Smooth Max", Range(0, 1)) = 0.5

        [Header(Shadow)]
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.9
        _MinShadow ("Min Shadow Light", Range(0,1)) = 0.15

        [Header(Blinn Phong)]
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.25
        _SpecPower ("Spec Power", Range(1,256)) = 64
        _DepressionSpecOcclusion ("Depression Spec Occlusion", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardSnow"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 4.6
            #pragma vertex Vert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_FootstepTex);
            SAMPLER(sampler_FootstepTex);
            TEXTURE2D(_SmoothFootstepTex);
            SAMPLER(sampler_SmoothFootstepTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Brightness;

                float4 _FootstepRect;
                half _EnableFootstep;

                half _MaxSnowSink;
                half _RimHeight;
                half _SnowDeformStrength;
                float _SnowHeightBlurRadius;
                half _SnowHeightBlurStrength;

                float _TessellationFactor;
                float _TessellationBorderFade;

                half _SnowNormalStrength;
                float _SnowNormalSampleRadius;
                half _SnowNormalSmoothMin;
                half _SnowNormalSmoothMax;

                half _SnowAOStrength;
                half _SnowRimLightStrength;
                half _SnowMaskSmoothMin;
                half _SnowMaskSmoothMax;

                half _ShadowStrength;
                half _MinShadow;

                half4 _SpecColor;
                half _SpecStrength;
                half _SpecPower;
                half _DepressionSpecOcclusion;
            CBUFFER_END

            // Unity 会自动提供纹理尺寸：
            // x = 1 / width, y = 1 / height, z = width, w = height。
            float4 _FootstepTex_TexelSize;
            float4 _SmoothFootstepTex_TexelSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float2 footUV      : TEXCOORD3;
            };

            struct TessellationControlPoint
            {
                float3 positionOS : INTERNALTESSPOS;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            float2 WorldXZToFootUV(float3 positionWS)
            {
                float2 rectSize = max(_FootstepRect.zw - _FootstepRect.xy, float2(0.0001, 0.0001));

                float2 footUV;
                footUV.x = (positionWS.x - _FootstepRect.x) / rectSize.x;
                footUV.y = (positionWS.z - _FootstepRect.y) / rectSize.y;
                return footUV;
            }

            half FootUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            half4 SampleSnowRTLOD(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D_LOD(_FootstepTex, sampler_FootstepTex, uv, 0);
                return data * inside;
            }

            half4 SampleSnowRT(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D(_FootstepTex, sampler_FootstepTex, uv);
                return data * inside;
            }

            half4 SampleSmoothSnowRTLOD(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D_LOD(
                    _SmoothFootstepTex,
                    sampler_SmoothFootstepTex,
                    uv,
                    0);
                return data * inside;
            }

            half4 SampleSmoothSnowRT(float2 uv)
            {
                half inside = FootUVInside(uv) * _EnableFootstep;
                half4 data = SAMPLE_TEXTURE2D(
                    _SmoothFootstepTex,
                    sampler_SmoothFootstepTex,
                    uv);
                return data * inside;
            }
            
            half4 DecodeSnowData(half4 data)
            {
                // RT 协议：
                // R = sink，下陷
                // G = rim，凸起
                // A = mask，有效区域

                half mask = saturate(data.a);

                half sink = saturate(data.r) * mask;
                half rim  = saturate(data.g) * mask;

                return half4(sink, rim, 0.0h, mask);
            }

            // Snow RT 协议：
            // R = sink，雪被压下去的强度。
            // G = rim，雪边鼓起强度。
            // A = mask，brush 覆盖范围。
            half SnowDisplacementFromData(half4 data)
            {
                half4 snow = DecodeSnowData(data);

                half sink = snow.r;
                half rim  = snow.g;

                // sink 往下，rim 往上。
                return (rim * _RimHeight - sink * _MaxSnowSink) * _SnowDeformStrength;
            }

            half SampleSnowHeight(float2 uv)
            {
                half4 data = SampleSmoothSnowRT(uv);
                return SnowDisplacementFromData(data);
            }

            half SampleSnowHeightLOD(float2 uv)
            {
                half4 data = SampleSmoothSnowRTLOD(uv);
                return SnowDisplacementFromData(data);
            }

            // Smooth only the height used for geometry. The accumulated RT stays raw,
            // so old tracks do not become wider or shallower after repeated updates.
            half SampleSmoothedSnowHeightLOD(float2 uv)
            {
                // The separable blur is generated once by SnowFootprintRTManager.
                // Sampling it directly keeps geometry and normal reconstruction aligned.
                return SampleSnowHeightLOD(uv);
            }

            half3 ReconstructSnowNormalWS(float2 uv)
            {
                float2 texel = _SmoothFootstepTex_TexelSize.xy;

                // A wider Sobel footprint suppresses high-frequency stamp seams while
                // preserving the large trench slope and footprint lighting detail.
                float radius = max(1.0, _SnowNormalSampleRadius);
                float2 sampleOffset = texel * radius;

                float worldSizeX = max(_FootstepRect.z - _FootstepRect.x, 0.0001);
                float worldSizeZ = max(_FootstepRect.w - _FootstepRect.y, 0.0001);

                float worldOffsetX = sampleOffset.x * worldSizeX;
                float worldOffsetZ = sampleOffset.y * worldSizeZ;

                half hTL = SampleSnowHeight(uv + float2(-sampleOffset.x,  sampleOffset.y));
                half hT  = SampleSnowHeight(uv + float2(0.0,             sampleOffset.y));
                half hTR = SampleSnowHeight(uv + float2( sampleOffset.x,  sampleOffset.y));
                half hL  = SampleSnowHeight(uv + float2(-sampleOffset.x,  0.0));
                half hR  = SampleSnowHeight(uv + float2( sampleOffset.x,  0.0));
                half hBL = SampleSnowHeight(uv + float2(-sampleOffset.x, -sampleOffset.y));
                half hB  = SampleSnowHeight(uv + float2(0.0,            -sampleOffset.y));
                half hBR = SampleSnowHeight(uv + float2( sampleOffset.x, -sampleOffset.y));

                half gradientX = ((hTR + 2.0h * hR + hBR) - (hTL + 2.0h * hL + hBL)) * 0.25h;
                half gradientZ = ((hTL + 2.0h * hT + hTR) - (hBL + 2.0h * hB + hBR)) * 0.25h;

                float3 tangentX = float3(worldOffsetX * 2.0, gradientX, 0.0);
                float3 tangentZ = float3(0.0, gradientZ, worldOffsetZ * 2.0);

                // 对平面来说 cross(tangentZ, tangentX) = (0,1,0)。
                return normalize((half3)cross(tangentZ, tangentX));
            }

            // The footprint RT already follows the player, so it is also a convenient
            // local tessellation window. Shared edge midpoints produce identical edge
            // factors in neighbouring patches and avoid visible cracks.
            float TessellationFactorAtPosition(float3 positionWS)
            {
                float2 rectCenter = (_FootstepRect.xy + _FootstepRect.zw) * 0.5;
                float2 rectHalfSize = max((_FootstepRect.zw - _FootstepRect.xy) * 0.5, 0.0);
                float2 outside = max(abs(positionWS.xz - rectCenter) - rectHalfSize, 0.0);
                float distanceOutside = length(outside);
                float localWeight = 1.0 - smoothstep(0.0, max(_TessellationBorderFade, 0.0001), distanceOutside);
                localWeight *= saturate(_EnableFootstep);

                return lerp(1.0, max(1.0, _TessellationFactor), localWeight);
            }

            TessellationControlPoint Vert(Attributes IN)
            {
                TessellationControlPoint OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                OUT.uv = IN.uv;
                return OUT;
            }

            TessellationFactors PatchConstantFunction(InputPatch<TessellationControlPoint, 3> patch)
            {
                TessellationFactors OUT;

                float3 positionWS0 = TransformObjectToWorld(patch[0].positionOS);
                float3 positionWS1 = TransformObjectToWorld(patch[1].positionOS);
                float3 positionWS2 = TransformObjectToWorld(patch[2].positionOS);

                // SV_TessFactor edge 0 is opposite control point 0, and so on.
                OUT.edge[0] = TessellationFactorAtPosition((positionWS1 + positionWS2) * 0.5);
                OUT.edge[1] = TessellationFactorAtPosition((positionWS2 + positionWS0) * 0.5);
                OUT.edge[2] = TessellationFactorAtPosition((positionWS0 + positionWS1) * 0.5);
                OUT.inside = (OUT.edge[0] + OUT.edge[1] + OUT.edge[2]) / 3.0;

                return OUT;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [outputcontrolpoints(3)]
            [maxtessfactor(16.0)]
            TessellationControlPoint Hull(
                InputPatch<TessellationControlPoint, 3> patch,
                uint controlPointID : SV_OutputControlPointID)
            {
                return patch[controlPointID];
            }

            [domain("tri")]
            Varyings Domain(
                TessellationFactors tessellationFactors,
                const OutputPatch<TessellationControlPoint, 3> patch,
                float3 barycentricCoordinates : SV_DomainLocation)
            {
                Varyings OUT;

                float3 positionOS =
                    patch[0].positionOS * barycentricCoordinates.x +
                    patch[1].positionOS * barycentricCoordinates.y +
                    patch[2].positionOS * barycentricCoordinates.z;

                float3 normalOS = normalize(
                    patch[0].normalOS * barycentricCoordinates.x +
                    patch[1].normalOS * barycentricCoordinates.y +
                    patch[2].normalOS * barycentricCoordinates.z);

                float2 uv =
                    patch[0].uv * barycentricCoordinates.x +
                    patch[1].uv * barycentricCoordinates.y +
                    patch[2].uv * barycentricCoordinates.z;

                float3 positionWS = TransformObjectToWorld(positionOS);
                half3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));

                float2 footUV = WorldXZToFootUV(positionWS);

                half displacement = SampleSmoothedSnowHeightLOD(footUV);

                // 这里按世界 Y 方向位移，适合水平雪面。
                // 如果你的雪面是大斜坡，可以改成 positionWS += normalWS * displacement。
                positionWS += normalWS * displacement;

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.uv = TRANSFORM_TEX(uv, _BaseMap);
                OUT.footUV = footUV;

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb * _Brightness;

                half4 snowData = DecodeSnowData(SampleSnowRT(IN.footUV));

                half sink = snowData.r;
                half rim  = snowData.g;
                half mask = snowData.a;

                half depressionMask = SafeSmoothStep(_SnowMaskSmoothMin, _SnowMaskSmoothMax, sink);
                half rimMask = SafeSmoothStep(_SnowMaskSmoothMin, _SnowMaskSmoothMax, rim);
                half normalMask = SafeSmoothStep(_SnowNormalSmoothMin, _SnowNormalSmoothMax, mask);
                normalMask = saturate(normalMask * _SnowNormalStrength);

                half3 baseNormalWS = normalize(IN.normalWS);
                half3 snowNormalWS = ReconstructSnowNormalWS(IN.footUV);
                half3 finalNormalWS = normalize(lerp(baseNormalWS, snowNormalWS, normalMask));

                // 压下去的雪更暗一点，雪边略微提亮。
                albedo *= 1.0h - depressionMask * _SnowAOStrength;
                albedo = lerp(albedo, albedo * 1.25h, rimMask * _SnowRimLightStrength);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 lightDirWS = normalize(mainLight.direction);
                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);

                half ndotl = saturate(dot(finalNormalWS, lightDirWS));

                half shadowAtten = mainLight.shadowAttenuation;
                shadowAtten = lerp(1.0h, max(_MinShadow, shadowAtten), _ShadowStrength);
                half lightAtten = mainLight.distanceAttenuation * shadowAtten;

                half3 ambient = SampleSH(finalNormalWS);
                half3 direct = mainLight.color * ndotl * lightAtten;

                half3 color = albedo * (ambient + direct);

                half3 halfDir = normalize(lightDirWS + viewDirWS);
                half ndoth = saturate(dot(finalNormalWS, halfDir));
                half specTerm = pow(ndoth, _SpecPower) * _SpecStrength;
                specTerm *= step(0.001h, ndotl);
                specTerm *= lightAtten;
                specTerm *= 1.0h - depressionMask * _DepressionSpecOcclusion;

                color += specTerm * _SpecColor.rgb * mainLight.color;

                return half4(color, 1.0h);
            }

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            // Uses the same tessellated RT displacement as ForwardSnow.
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma target 4.6
            #pragma vertex SnowAuxVert
            #pragma hull SnowAuxHull
            #pragma domain SnowAuxShadowDomain
            #pragma fragment SnowAuxShadowFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Assets/MyTA/Shaders/Snow/SnowSurface_RTDeformAuxPasses.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            // Keeps the camera depth texture aligned with the deformed surface.
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM

            #pragma target 4.6
            #pragma vertex SnowAuxVert
            #pragma hull SnowAuxHull
            #pragma domain SnowAuxDepthDomain
            #pragma fragment SnowAuxDepthFragment

            #include "Assets/MyTA/Shaders/Snow/SnowSurface_RTDeformAuxPasses.hlsl"

            ENDHLSL
        }
    }
}
