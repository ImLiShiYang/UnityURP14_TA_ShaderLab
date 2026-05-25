Shader "Footprints/InteractiveGround"
{
    Properties
    {
        [Header(Base)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 3)) = 1

        [Header(Shadow)]
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.9
        _MinShadow ("Min Shadow Light", Range(0,1)) = 0.15

        [Header(Footprint RT)]
        _FootstepTex ("Footstep RT RGB Normal A Mask", 2D) = "black" {}
        _FootstepRect ("Footstep Rect", Vector) = (0,0,1,1)
        _EnableFootstep ("Enable Footstep", Float) = 0

        [Header(Footprint Normal)]
        _FootprintStrength ("Footprint Mask Strength", Range(0,2)) = 1
        _FootprintNormalStrength ("Footprint Normal Strength", Range(0,3)) = 1
        [Toggle] _FlipFootprintNormalY ("Flip Footprint Normal Y", Float) = 0

        [Header(Blinn Phong)]
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.35
        _SpecPower ("Spec Power", Range(1,256)) = 48
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
            Name "ForwardBlinnPhong"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Brightness;

                half _ShadowStrength;
                half _MinShadow;

                float4 _FootstepRect;
                half _EnableFootstep;

                half _FootprintStrength;
                half _FootprintNormalStrength;
                half _FlipFootprintNormalY;

                half4 _SpecColor;
                half _SpecStrength;
                half _SpecPower;
            CBUFFER_END

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
            };

            half3 DecodeNormalRGB(half3 normalRGB)
            {
                return normalize(normalRGB * 2.0h - 1.0h);
            }

            float2 WorldXZToFootUV(float3 positionWS)
            {
                float2 footUV;
                footUV.x = (positionWS.x - _FootstepRect.x) / (_FootstepRect.z - _FootstepRect.x);
                footUV.y = (positionWS.z - _FootstepRect.y) / (_FootstepRect.w - _FootstepRect.y);
                return footUV;
            }

            float FootUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            // 这里构造的是“脚印投影 UV”的切线空间：
            // T 对应 footUV.x，也就是世界 X 方向；
            // B 对应 footUV.y，也就是世界 Z 方向；
            // N 是当前地表法线。
            half3 FootprintNormalTangentToWorld(half3 normalTS, half3 baseNormalWS)
            {
                half3 N = normalize(baseNormalWS);

                half3 worldX = half3(1.0h, 0.0h, 0.0h);
                half3 worldZ = half3(0.0h, 0.0h, 1.0h);

                // 把世界 X 投影到当前地表切平面上，作为 T。
                half3 T = worldX - N * dot(worldX, N);

                // 极端情况下，如果法线几乎平行 worldX，就改用 worldZ。
                if (dot(T, T) < 0.0001h)
                {
                    T = worldZ - N * dot(worldZ, N);
                }

                T = normalize(T);

                // 为了让 normalTS.y 在水平地面上对应世界 +Z，
                // 这里使用 cross(T, N)。
                half3 B = normalize(cross(T, N));

                return normalize(
                    normalTS.x * T +
                    normalTS.y * B +
                    normalTS.z * N
                );
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInput = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = posInput.positionCS;
                OUT.positionWS = posInput.positionWS;
                OUT.normalWS = normalInput.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // =====================================================
                // 1. 基础颜色
                // =====================================================
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                // =====================================================
                // 2. 采样脚印 RT
                // =====================================================
                float2 footUV = WorldXZToFootUV(IN.positionWS);
                float inside = FootUVInside(footUV);

                half4 foot = SAMPLE_TEXTURE2D(_FootstepTex, sampler_FootstepTex, footUV);

                half footprintMask = saturate(
                    foot.a *
                    inside *
                    _EnableFootstep *
                    _FootprintStrength
                );

                // =====================================================
                // 3. 切线空间脚印法线 -> 世界空间法线
                // =====================================================
                half3 baseNormalWS = normalize(IN.normalWS);

                half3 footNormalTS = DecodeNormalRGB(foot.rgb);

                // 不同 normal 贴图来源可能 Y 方向相反。
                // 如果脚印凹凸方向看起来反了，就打开这个开关。
                if (_FlipFootprintNormalY > 0.5h)
                {
                    footNormalTS.y = -footNormalTS.y;
                }

                // 用 mask 控制法线强度。
                // mask = 0 时 normalTS 约等于 (0,0,1)，不会影响地面。
                half normalStrength = footprintMask * _FootprintNormalStrength;
                half3 finalNormalTS = normalize(half3(
                    footNormalTS.xy * normalStrength,
                    max(footNormalTS.z, 0.001h)
                ));

                half3 normalWS = FootprintNormalTangentToWorld(finalNormalTS, baseNormalWS);

                // =====================================================
                // 4. Blinn-Phong 光照
                // =====================================================
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 lightDirWS = normalize(mainLight.direction);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                half3 halfDirWS = normalize(lightDirWS + viewDirWS);

                half ndotl = saturate(dot(normalWS, lightDirWS));
                half ndoth = saturate(dot(normalWS, halfDirWS));

                half rawShadow = mainLight.shadowAttenuation;
                half shadow = lerp(1.0h, rawShadow, _ShadowStrength);
                shadow = max(shadow, _MinShadow);

                half lightAtten = mainLight.distanceAttenuation * shadow;

                half3 ambient = SampleSH(normalWS);
                ambient = max(ambient, half3(0.05h, 0.05h, 0.05h));

                half3 diffuse = mainLight.color * ndotl * lightAtten;

                half specTerm = pow(ndoth, _SpecPower);
                specTerm *= _SpecStrength;
                specTerm *= step(0.001h, ndotl);
                specTerm *= lightAtten;

                half3 specular = mainLight.color * _SpecColor.rgb * specTerm;

                half3 finalColor = albedo * (ambient + diffuse) + specular;
                finalColor *= _Brightness;

                return half4(finalColor, 1.0h);
            }

            ENDHLSL
        }
    }
}