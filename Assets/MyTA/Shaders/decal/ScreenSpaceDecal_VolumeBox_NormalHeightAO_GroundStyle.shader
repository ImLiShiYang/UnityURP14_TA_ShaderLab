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
        // 当前代码用 UnpackNormalScale，所以这张图建议按 Unity Normal Map 导入。
        _DecalNormalTexture("Footprint Normal RGB Encoded", 2D) = "bump" {}

        // 脚印高度图。
        // 协议固定为：
        //      0.5 = 原地面
        //    < 0.5 = 凹陷
        //    > 0.5 = 泥边隆起
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

        // ============================================================
        // Rim Only POM
        //
        // 只给 A > 0.5 的泥边区域做 POM。
        // 不处理 A < 0.5 的下陷区域。
        //
        // 注意：
        // POM 只影响法线贴图采样 UV。
        // 不影响 height mask。
        // 不影响 alpha。
        // 不影响 depressionMask / rimMask 的计算。
        // ============================================================
        [Header(Rim Only POM)]
        _RimPOMStrength("Rim POM Strength", Range(0,0.12)) = 0.035
        _RimPOMSteps("Rim POM Steps", Range(4,24)) = 10
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

            TEXTURE2D(_DecalTexture);
            SAMPLER(sampler_DecalTexture);

            TEXTURE2D(_DecalNormalTexture);
            SAMPLER(sampler_DecalNormalTexture);

            TEXTURE2D(_DecalHeightTexture);
            SAMPLER(sampler_DecalHeightTexture);

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

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

            half _RimPOMStrength;
            half _RimPOMSteps;

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

            // ============================================================
            // RGB 0~1 编码还原成 -1~1 法线。
            //
            // 当前 Frag 里实际使用的是 UnpackNormalScale，
            // 这个函数保留着，方便你之后切回普通 RGB 解码。
            // ============================================================
            half3 DecodeNormalRGB(half3 normalRGB)
            {
                return normalize(normalRGB * 2.0h - 1.0h);
            }

            // ============================================================
            // 安全 smoothstep。
            // 防止 maxVal <= minVal 时出现异常。
            // ============================================================
            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            // ============================================================
            // signed height 死区处理。
            //
            // signedValue:
            //      < 0 = 下陷
            //      = 0 = 原始地面
            //      > 0 = 泥边隆起
            //
            // deadZone 用来消除 0 附近的小误差。
            // ============================================================
            half ApplySignedDeadZone(half signedValue, half deadZone)
            {
                half absValue = abs(signedValue);

                if (absValue <= deadZone)
                    return 0.0h;

                half remapped = (absValue - deadZone) / max(0.0001h, 1.0h - deadZone);
                return sign(signedValue) * saturate(remapped);
            }

            // ============================================================
            // 判断 decalUV 是否在 0~1 范围内。
            //
            // 返回：
            //      1 = 在贴图范围内
            //      0 = 超出范围
            // ============================================================
            half DecalUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            // ============================================================
            // 只采样泥边正高度。
            //
            // Height 协议：
            //      height = 0.5  原始地面
            //      height < 0.5  下陷
            //      height > 0.5  泥边隆起
            //
            // 这个函数只返回 height > 0.5 的部分。
            // 下陷区域会返回 0。
            // ============================================================
            half SampleRimHeight01(float2 uv)
            {
                half inside = DecalUVInside(uv);

                half height = SAMPLE_TEXTURE2D(
                    _DecalHeightTexture,
                    sampler_DecalHeightTexture,
                    uv
                ).r;

                half signedValue = (height - 0.5h) * 2.0h;
                signedValue = ApplySignedDeadZone(
                    signedValue,
                    _FootprintSignedDeadZone
                );

                // 只保留正数，也就是泥边隆起。
                // 下陷 signedValue < 0 会被 saturate 变成 0。
                return saturate(signedValue) * inside;
            }

            // ============================================================
            // 只给泥边做 POM。
            //
            // 输入：
            //      uv        : 原始 decalUV
            //      viewDirWS : 当前像素指向摄像机的世界空间方向
            //
            // 输出：
            //      POM 偏移后的 UV
            //
            // 重要：
            //      这个 UV 只用于采样法线贴图。
            //      不用于重新采样 height。
            //      不用于重新计算 alpha。
            //      不用于重新计算 mask。
            //
            // 这样可以保证：
            //      下陷区域不会被 POM 拉动。
            //      脚印透明范围不会被 POM 拉歪。
            //      泥边 mask 仍然来自原始 height。
            // ============================================================
            float2 ApplyRimOnlyPOM(float2 uv, float3 viewDirWS)
            {
                half startRimHeight = SampleRimHeight01(uv);

                // 当前位置不是泥边，直接返回原始 UV。
                if (startRimHeight <= 0.001h || _RimPOMStrength <= 0.0001h)
                    return uv;

                // 把世界空间视线方向转换到 decal local 空间。
                //
                // decalUV 来自 decalLocalPos.xy + 0.5，
                // 所以 POM 偏移也应该沿 local xy 做。
                float3 viewDirLocal = mul(
                    (float3x3)_DecalWorldToLocal,
                    viewDirWS
                );

                // 防止视线方向几乎平行于 decal 投影平面时偏移爆炸。
                float viewZ = max(abs(viewDirLocal.z), 0.2);

                // local xy 方向的视差方向。
                float2 viewParallaxDir = viewDirLocal.xy / viewZ;

                // 最大 UV 偏移。
                //
                // 如果你发现泥边偏移方向反了，
                // 只需要把这里的负号删掉：
                //
                //      float2 maxOffset = viewParallaxDir * _RimPOMStrength * startRimHeight;
                //
                float2 maxOffset = -viewParallaxDir * _RimPOMStrength * startRimHeight;

                int steps = (int)_RimPOMSteps;
                steps = min(max(steps, 4), 24);

                float layerDepth = 1.0 / steps;
                float2 deltaUV = maxOffset / steps;

                float2 curUV = uv;
                half curLayerDepth = 0.0h;
                half curHeight = startRimHeight;

                // 简化 POM：
                // 沿视线方向一层一层推进。
                // 当当前层深度超过采样到的泥边高度时停止。
                [loop]
                for (int i = 0; i < 24; i++)
                {
                    if (i >= steps)
                        break;

                    curLayerDepth += layerDepth;
                    curUV += deltaUV;

                    curHeight = SampleRimHeight01(curUV);

                    if (curLayerDepth >= curHeight)
                        break;
                }

                // 如果偏移后跑出贴图范围，回退到原始 UV。
                half finalInside = DecalUVInside(curUV);
                curUV = lerp(uv, curUV, finalInside);

                return curUV;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // =====================================================
                // 1. 屏幕坐标 -> 世界坐标
                // =====================================================
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;

                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0)
                    discard;

                float rawDepth = SAMPLE_TEXTURE2D_X(
                    _CameraDepthTexture,
                    sampler_CameraDepthTexture,
                    screenUV
                ).r;

                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                        discard;
                #else
                    if (rawDepth >= 0.999999)
                        discard;
                #endif

                float3 worldPos = ComputeWorldSpacePosition(
                    screenUV,
                    rawDepth,
                    UNITY_MATRIX_I_VP
                );

                // =====================================================
                // 2. 判断当前世界点是否落在 decal box 内
                // =====================================================
                float3 decalLocalPos = mul(
                    _DecalWorldToLocal,
                    float4(worldPos, 1.0)
                ).xyz;

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
                // 4. 采样高度，并按 signed height 协议拆 mask
                // =====================================================
                half height = SAMPLE_TEXTURE2D(
                    _DecalHeightTexture,
                    sampler_DecalHeightTexture,
                    decalUV
                ).r;

                // 固定协议：
                //      0.5 = 原始地面
                //    < 0.5 = 下陷
                //    > 0.5 = 泥边
                half signedFoot = (height - 0.5h) * 2.0h;
                signedFoot = ApplySignedDeadZone(
                    signedFoot,
                    _FootprintSignedDeadZone
                );

                // 下陷强度。
                half depressionRaw = saturate(-signedFoot);

                // 泥边强度。
                half rimRaw = saturate(signedFoot);

                // 总脚印影响区域。
                half influenceRaw = saturate(abs(signedFoot));

                half depressionMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    depressionRaw
                );

                half rimMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    rimRaw
                );

                half influenceMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    influenceRaw
                );

                depressionMask = saturate(depressionMask * _FootprintStrength);
                rimMask        = saturate(rimMask        * _FootprintStrength);
                influenceMask  = saturate(influenceMask  * _FootprintStrength);

                // 关键：
                // 透明度只由原始 height 的影响范围控制。
                // POM 不参与 alpha。
                clip(influenceMask - 0.001h);

                // =====================================================
                // 5. 只给泥边区域做 POM，然后采样法线
                // =====================================================

                // 当前像素指向摄像机的方向。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // POM 后的 UV。
                // 注意：
                //      只用于采样法线贴图。
                //      不用于采样 height。
                //      不用于重新计算 mask。
                float2 rimPOMUV = ApplyRimOnlyPOM(decalUV, viewDirWS);

                // 非泥边区域：
                //      使用原始 UV 的法线。
                //
                // 泥边区域：
                //      使用 POM 后 UV 的法线。
                //
                // 这样下陷区域不会被 POM 影响。
                half4 packedNormal = SAMPLE_TEXTURE2D(
                    _DecalNormalTexture,
                    sampler_DecalNormalTexture,
                    rimPOMUV
                );

                // Normal Map 导入类型使用 UnpackNormalScale。
                // _FootprintNormalStrength 在这里生效。
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
                // 6. 采样 Base 颜色，并叠加脚印局部变化
                // =====================================================
                half4 baseSample = SAMPLE_TEXTURE2D(
                    _DecalTexture,
                    sampler_DecalTexture,
                    decalUV
                );

                half3 baseRGB = baseSample.rgb * _DecalColor.rgb;

                // 当前版本不采样 _CameraOpaqueTexture。
                // Decal 颜色来自自己的 Base 贴图。
                Light mainLight = GetMainLight();
                half3 lightDirWS = normalize(mainLight.direction);

                half flatNdotL = saturate(dot(decalNormalWS, lightDirWS));
                half footprintNdotL = saturate(dot(footprintNormalWS, lightDirWS));

                // 下陷区域 AO 压暗。
                half footprintAO = 1.0h - depressionMask * _FootprintAOStrength;

                // 泥边轻微提亮。
                half rimLight = lerp(
                    1.0h,
                    1.25h,
                    rimMask * _FootprintRimLightStrength
                );

                half4 color;

                // 用脚印法线产生明暗变化。
                color.rgb = saturate(
                    baseRGB *
                    footprintNdotL *
                    footprintAO *
                    rimLight *
                    mainLight.color
                );

                // Alpha 仍然由原始 influenceMask 控制。
                // POM 不影响 alpha，避免脚印边界漂移。
                color.a = baseSample.a  * _DecalColor.a;

                // =====================================================
                // 7. decal box 边缘淡出 + projector 透明度
                // =====================================================
                float distToPlaneEdge = min(
                    0.5 - absLocal.x,
                    0.5 - absLocal.y
                );

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