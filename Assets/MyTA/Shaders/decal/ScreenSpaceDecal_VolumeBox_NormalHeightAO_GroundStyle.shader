Shader "Custom/ScreenSpaceDecal_VolumeBox_GroundStyle_BaseSample"
{
    Properties
    {
        // Base 颜色贴图。
        // 这个版本不再采样 _CameraOpaqueTexture，最终颜色来自这里。
        // Alpha 不强制使用，脚印显示范围仍然由 HeightTex 生成的 influenceMask 控制。
        _DecalTexture("Base / Decal Texture RGB", 2D) = "white" {}

        // 颜色乘子。
        // RGB 用来给 Base 贴图整体调色，A 控制整体透明度。
        _DecalColor("Decal Color", Color) = (1,1,1,1)

        // 脚印法线图。
        // 这个版本严格参考地面 shader 的思路：直接采样 RGB，然后 normalRGB * 2 - 1 解码。
        // 因此建议 NormalTex 导入为 Default / sRGB Off，而不是 Unity Normal Map。
        _DecalNormalTexture("Footprint Normal RGB Encoded", 2D) = "bump" {}

        // 脚印高度图。
        // 协议固定为：0.5 = 原地面；< 0.5 = 凹陷；> 0.5 = 泥边隆起。
        _DecalHeightTexture("Footprint Height 0.5 Ground", 2D) = "gray" {}

        // 和地面 shader 对齐的脚印参数。
        _FootprintStrength("Footprint Strength", Range(0,2)) = 1
        _FootprintSignedDeadZone("Signed Height Dead Zone", Range(0,0.2)) = 0.01

        _FootprintNormalStrength("Footprint Normal Strength", Range(0,3)) = 1
        [Toggle] _FlipFootprintNormalY("Flip Footprint Normal Y", Float) = 0

        _FootprintAOStrength("Footprint AO Strength", Range(0,1)) = 0.25
        _FootprintAOSmoothMin("Footprint AO Smooth Min", Range(0,1)) = 0.02
        _FootprintAOSmoothMax("Footprint AO Smooth Max", Range(0,1)) = 0.45

        _FootprintRimLightStrength("Footprint Rim Light Strength", Range(0,0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ScreenSpaceDecalGroundStyleMinimal"

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

            TEXTURE2D(_DecalTexture);        SAMPLER(sampler_DecalTexture);
            TEXTURE2D(_DecalNormalTexture);  SAMPLER(sampler_DecalNormalTexture);
            TEXTURE2D(_DecalHeightTexture);  SAMPLER(sampler_DecalHeightTexture);

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);   SAMPLER(sampler_CameraDepthTexture);

            float4x4 _DecalWorldToLocal;

            float4 _DecalColor;
            float4 _DecalParams;        // x = opacity, y = box edge fade
            float4 _DecalTilingOffset;  // xy = tiling, zw = offset
            float4 _DecalDistanceFade;  // x = distance fade

            float4 _DecalTangentWS;
            float4 _DecalBitangentWS;
            float4 _DecalNormalWS;

            half _FootprintStrength;
            half _FootprintSignedDeadZone;

            half _FootprintNormalStrength;
            half _FlipFootprintNormalY;

            half _FootprintAOStrength;
            half _FootprintAOSmoothMin;
            half _FootprintAOSmoothMax;
            half _FootprintRimLightStrength;

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

            // 和地面 shader 一致：RGB 0~1 编码还原成 -1~1 法线。
            half3 DecodeNormalRGB(half3 normalRGB)
            {
                return normalize(normalRGB * 2.0h - 1.0h);
            }

            // 和地面 shader 一致：避免 smoothstep 的 max <= min 出错。
            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            // 和地面 shader 一致：清掉 0 附近的小误差。
            half ApplySignedDeadZone(half signedValue, half deadZone)
            {
                half absValue = abs(signedValue);

                if (absValue <= deadZone)
                    return 0.0h;

                half remapped = (absValue - deadZone) / max(0.0001h, 1.0h - deadZone);
                return sign(signedValue) * saturate(remapped);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // =====================================================
                // 1. 屏幕坐标 -> 世界坐标
                // =====================================================
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;

                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0)
                    discard;

                float rawDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV).r;

                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001) discard;
                #else
                    if (rawDepth >= 0.999999) discard;
                #endif

                float3 worldPos = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);

                // =====================================================
                // 2. 判断当前世界点是否落在 decal box 内
                // =====================================================
                float3 decalLocalPos = mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;
                float3 absLocal = abs(decalLocalPos);

                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
                    discard;

                // =====================================================
                // 3. decal local xy -> 脚印贴图 UV
                // =====================================================
                float2 decalUV = decalLocalPos.xy + 0.5;
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                // 不在脚印贴图范围内直接丢弃，避免 Wrap 采样造成重复脚印。
                if (decalUV.x < 0.0 || decalUV.x > 1.0 || decalUV.y < 0.0 || decalUV.y > 1.0)
                    discard;

                // =====================================================
                // 4. 采样高度，并按地面 shader 的 signed height 协议拆 mask
                // =====================================================
                half height = SAMPLE_TEXTURE2D(_DecalHeightTexture, sampler_DecalHeightTexture, decalUV).r;

                // 固定协议：0.5 = 原始地面；<0.5 = 下陷；>0.5 = 泥边。
                half signedFoot = (height - 0.5h) * 2.0h;
                signedFoot = ApplySignedDeadZone(signedFoot, _FootprintSignedDeadZone);

                half depressionRaw = saturate(-signedFoot);
                half rimRaw        = saturate( signedFoot);
                half influenceRaw  = saturate(abs(signedFoot));

                half depressionMask = SafeSmoothStep(_FootprintAOSmoothMin, _FootprintAOSmoothMax, depressionRaw);
                half rimMask        = SafeSmoothStep(_FootprintAOSmoothMin, _FootprintAOSmoothMax, rimRaw);
                half influenceMask  = SafeSmoothStep(_FootprintAOSmoothMin, _FootprintAOSmoothMax, influenceRaw);

                depressionMask = saturate(depressionMask * _FootprintStrength);
                rimMask        = saturate(rimMask        * _FootprintStrength);
                influenceMask  = saturate(influenceMask  * _FootprintStrength);

                // 关键：透明度只由高度影响范围控制。
                // 没有脚印的地方直接裁掉，避免出现 decal 方块。
                clip(influenceMask - 0.001h);

                // =====================================================
                // 5. 直接采样脚印法线，并转换到世界空间
                // =====================================================
                half4 packedNormal = SAMPLE_TEXTURE2D(
                    _DecalNormalTexture,
                    sampler_DecalNormalTexture,
                    decalUV
                );

                // Normal Map 导入类型必须用 UnpackNormalScale。
                // _FootprintNormalStrength 已经在这里生效，后面不要再 footprintNormal.xy *= strength。
                half3 footprintNormal = UnpackNormalScale(
                    packedNormal,
                    _FootprintNormalStrength
                );

                if (_FlipFootprintNormalY > 0.5h)
                {
                    footprintNormal.y = -footprintNormal.y;
                }

                footprintNormal = normalize(footprintNormal);

                half3 tangentWS = normalize(_DecalTangentWS.xyz);
                half3 bitangentWS = normalize(_DecalBitangentWS.xyz);
                half3 decalNormalWS = normalize(_DecalNormalWS.xyz);

                half3 footprintNormalWS = normalize(
                    footprintNormal.x * tangentWS +
                    footprintNormal.y * bitangentWS +
                    footprintNormal.z * decalNormalWS
                );

                // =====================================================
                // 6. 采样 Base 颜色，并叠加脚印造成的局部变化
                // =====================================================
                half4 baseSample = SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, decalUV);
                half3 baseRGB = baseSample.rgb * _DecalColor.rgb;

                // 这里不采样 _CameraOpaqueTexture。
                // 因此 decal 不再读取地面已经渲染好的颜色，而是使用自己的 Base 颜色。
                // 法线只用来产生相对明暗变化，避免直接乘 footprintNdotL 导致全黑。
                Light mainLight = GetMainLight();
                half3 lightDirWS = normalize(mainLight.direction);

                half flatNdotL = saturate(dot(decalNormalWS, lightDirWS));
                half footprintNdotL = saturate(dot(footprintNormalWS, lightDirWS));

                // 和地面 shader 一致：下陷区域做 AO 压暗。
                half footprintAO = 1.0h - depressionMask * _FootprintAOStrength;

                // 和地面 shader 一致：泥边区域只轻微提亮，不叠加额外泥色。
                half rimLight = lerp(1.0h, 1.25h, rimMask * _FootprintRimLightStrength);

                half4 color;
                color.rgb = saturate(baseRGB * footprintNdotL * footprintAO * rimLight*mainLight.color);

                // Alpha 仍然由高度图的 influenceMask 控制。
                // 这里不乘 baseSample.a，是为了避免 Base 透明边缘把 height 的泥边裁掉。
                // 如果你确定 Base alpha 覆盖了完整脚印，可以改成：
                color.a = influenceMask * baseSample.a * _DecalColor.a;
                // color.a = baseSample.a * _DecalColor.a;

                // =====================================================
                // 7. decal box 边缘淡出 + projector 透明度
                // =====================================================
                float distToPlaneEdge = min(0.5 - absLocal.x, 0.5 - absLocal.y);
                float edgeFade = max(_DecalParams.y, 0.0001);
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                color.a *= _DecalParams.x;        // projector opacity
                color.a *= _DecalDistanceFade.x;  // distance fade
                color.a *= boxFade;               // box edge fade

                return color;
            }
            ENDHLSL
        }
    }
}
