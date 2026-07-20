#ifndef MY_TOON_LIGHTING_INCLUDED
#define MY_TOON_LIGHTING_INCLUDED

#include "Assets/MyTA/Shaders/Toon/MyToonShared.hlsl"

float3 GetToonRampColor(float lightValue)
{
    if (_UseToonRamp < 0.5)
        return 1.0;

    float rampU = saturate((lightValue - 0.5) * _ToonRampContrast + 0.5 + _ToonRampOffset);

    if (_ToonRampInvert > 0.5)
        rampU = 1.0 - rampU;

    return SAMPLE_TEXTURE2D(_ToonRampMap, sampler_ToonRampMap, float2(0.5, rampU)).rgb;
}



float3 ApplyHeightGradient(float3 color, float3 positionWS)
{
    if (_UseHeightGradient < 0.5)
        return color;

    float height01 = InvLerpClamp(_HeightGradientMin, _HeightGradientMax, positionWS.y);
    float3 gradientColor = lerp(_HeightGradientBottomColor.rgb, _HeightGradientTopColor.rgb, height01);

    return lerp(color, color * gradientColor, _HeightGradientStrength);
}

float GetOcclusion(float2 uv)
{
    float occlusion = 1.0;

    if (_UseOcclusion > 0.5)
    {
        float4 occlusionMap = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv);
        float occlusionValue = dot(occlusionMap, _OcclusionMapChannelMask);
        occlusionValue = lerp(1.0, occlusionValue, _OcclusionStrength);
        occlusion = InvLerpClamp(_OcclusionRemapStart, _OcclusionRemapEnd, occlusionValue);
    }

    return occlusion;
}

float3 GetIndirectLight(float occlusion)
{
    float3 indirectLight = max(SampleSH(0), _IndirectLightMinColor.rgb);
    indirectLight *= _IndirectLightMultiplier;
    indirectLight *= lerp(1.0, occlusion, 0.5);
    return indirectLight;
}

float GetToonLight(float ndotl, float occlusion)
{
    float toonLight = smoothstep(
        _ShadowThreshold - _ShadowSoftness,
        _ShadowThreshold + _ShadowSoftness,
        ndotl
    );

    toonLight *= occlusion;
    toonLight = lerp(toonLight, 1.0, _MainLightIgnoreCelShade);

    return toonLight;
}

float GetSpecular(float3 normalWS, float3 lightDirWS, float3 viewDirWS)
{
    if (_UseSpecular < 0.5)
        return 0.0;

    float3 halfDirWS = normalize(lightDirWS + viewDirWS);
    float specRaw = saturate(dot(normalWS, halfDirWS));

    return smoothstep(
        _SpecularThreshold,
        _SpecularThreshold + max(_SpecularSoftness, 0.001),
        specRaw
    );
}

float3 ShiftHairTangent(float3 tangentWS, float3 normalWS, float shift)
{
    return normalize(tangentWS + normalWS * shift);
}

float3 GetUvVHairDirectionWS(float3 positionWS, float2 uv, float3 normalWS, float3 fallbackDirWS)
{
    float3 dpdx = ddx(positionWS);
    float3 dpdy = ddy(positionWS);
    float2 duvdx = ddx(uv);
    float2 duvdy = ddy(uv);

    float det = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
    float3 uvVDirWS = (duvdx.x * dpdy - duvdy.x * dpdx) / max(abs(det), 1e-5);
    uvVDirWS = uvVDirWS - normalWS * dot(uvVDirWS, normalWS);

    float uvVLenSq = dot(uvVDirWS, uvVDirWS);
    return (abs(det) > 1e-5 && uvVLenSq > 1e-6) ? (uvVDirWS * rsqrt(uvVLenSq)) : fallbackDirWS;
}

float GetHairStrandSpecular(
    float3 normalWS,
    float3 tangentWS,
    float3 lightDirWS,
    float3 viewDirWS,
    float power
)
{
    normalWS = normalize(normalWS);
    tangentWS = normalize(tangentWS);
    lightDirWS = normalize(lightDirWS);
    viewDirWS = normalize(viewDirWS);

    float3 halfDirWS = normalize(lightDirWS + viewDirWS);

    float tDotH = dot(tangentWS, halfDirWS);

    // Kajiya-Kay：H 和发丝方向越接近垂直，高光越强
    float sinTH = sqrt(saturate(1.0 - tDotH * tDotH));

    float rawSpec = pow(saturate(sinTH), max(power, 1.0));

    // 限制背光面乱亮
    float ndl = saturate(dot(normalWS, lightDirWS));
    float lightMask = smoothstep(0.05, 0.45, ndl);

    // 限制掠射角碎亮
    float ndv = saturate(dot(normalWS, viewDirWS));
    float viewMask = smoothstep(0.12, 0.55, ndv);

    rawSpec *= lightMask;
    rawSpec *= viewMask;

    // 方向衰减。你的模型 tangent 方向不一定稳定，建议参数先保持 0。
    float dirAtten = smoothstep(-1.0, 0.0, tDotH);
    rawSpec *= lerp(1.0, dirAtten, _HairSpecularDirectionAtten);

    return rawSpec;
}

float3 GetAnisotropicHairSpecular(
    float3 normalWS,
    float3 hairDirWS,
    float3 lightDirWS,
    float3 viewDirWS,
    float lightAtten,
    float2 uv
)
{
    if (_UseHairSpecular < 0.5)
        return 0.0;

    normalWS = normalize(normalWS);
    hairDirWS = normalize(hairDirWS);
    lightDirWS = normalize(lightDirWS);
    viewDirWS = normalize(viewDirWS);

    float3 result = 0.0;

    // -----------------------------
    // 1. Shift Map：控制高光位置偏移
    // gray 0.5 = 不偏移
    // 小于 0.5 = 负向偏移
    // 大于 0.5 = 正向偏移
    // -----------------------------
    float shiftTex = SAMPLE_TEXTURE2D(
        _HairSpecularShiftMap,
        sampler_HairSpecularShiftMap,
        uv
    ).r - 0.5;

    float primaryShift = _HairSpecularShift + shiftTex * _HairSpecularShiftMapStrength;
    float secondaryShift = _HairSecondarySpecularShift + shiftTex * _HairSpecularShiftMapStrength;

    // -----------------------------
    // 2. Spec Mask：控制哪里允许出现动态高光
    // 黑 = 无高光
    // 灰 = 弱高光
    // 白 = 强高光
    // -----------------------------
    float hairSpecMask = SAMPLE_TEXTURE2D(
        _HairSpecularMaskMap,
        sampler_HairSpecularMaskMap,
        uv
    ).r;

    hairSpecMask = saturate(hairSpecMask);
    hairSpecMask = pow(hairSpecMask, max(_HairSpecularMaskPower, 0.001));
    hairSpecMask = lerp(1.0, hairSpecMask, _HairSpecularMaskStrength);

    // -----------------------------
    // 3. 主高光
    // -----------------------------
    float3 primaryTangentWS = ShiftHairTangent(
        hairDirWS,
        normalWS,
        primaryShift
    );

    float primaryRaw = GetHairStrandSpecular(
        normalWS,
        primaryTangentWS,
        lightDirWS,
        viewDirWS,
        _HairSpecularPower
    );

    float primaryToon = smoothstep(
        _HairSpecularThreshold,
        _HairSpecularThreshold + max(_HairSpecularSoftness, 0.001),
        primaryRaw
    );

    result += _HairSpecularColor.rgb
            * primaryToon
            * _HairSpecularIntensity;

    // -----------------------------
    // 4. 副高光
    // 第一版建议先关掉，等主高光稳定后再开
    // -----------------------------
    if (_UseHairSecondarySpecular > 0.5)
    {
        float3 secondaryTangentWS = ShiftHairTangent(
            hairDirWS,
            normalWS,
            secondaryShift
        );

        float secondaryRaw = GetHairStrandSpecular(
            normalWS,
            secondaryTangentWS,
            lightDirWS,
            viewDirWS,
            _HairSecondarySpecularPower
        );

        float secondaryToon = smoothstep(
            _HairSpecularThreshold,
            _HairSpecularThreshold + max(_HairSpecularSoftness, 0.001),
            secondaryRaw
        );

        result += _HairSecondarySpecularColor.rgb
                * secondaryToon
                * _HairSecondarySpecularIntensity;
    }

    // -----------------------------
    // 5. 最终遮罩和光照衰减
    // -----------------------------
    result *= hairSpecMask;
    result *= saturate(lightAtten);

    return result;
}

float GetRim(float3 normalWS, float3 viewDirWS)
{
    float rimRaw = 1.0 - saturate(dot(normalWS, viewDirWS));

    return smoothstep(
        _RimThreshold,
        _RimThreshold + max(_RimSoftness, 0.001),
        rimRaw
    );
}

float3 GetMatCap(float3 normalWS, float3 baseColor)
{
    if (_UseMatCap < 0.5)
        return 0;

    float3 normalVS = normalize(TransformWorldToViewDir(normalWS));
    float2 matCapUV = normalVS.xy * 0.5 + 0.5;

    float3 matCapColor = SAMPLE_TEXTURE2D(_MatCapMap, sampler_MatCapMap, matCapUV).rgb;
    matCapColor *= _MatCapColor.rgb * _MatCapIntensity;
    matCapColor = lerp(matCapColor, matCapColor * baseColor, _MatCapBlendBaseColor);

    return matCapColor;
}

float3 GetEmission(float2 uv, float3 baseColor)
{
    if (_UseEmission < 0.5)
        return 0;

    float4 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv);
    float emissionMask = dot(emissionMap, _EmissionMapChannelMask);

    float3 emissionColor = _EmissionColor.rgb * emissionMask;
    emissionColor = lerp(emissionColor, emissionColor * baseColor, _EmissionMulByBaseColor);

    return emissionColor;
}

float GetNormalToonLight(float3 normalWS, float3 lightDirWS)
{
    normalWS = normalize(normalWS);
    lightDirWS = normalize(lightDirWS);

    float ndl = saturate(dot(normalWS, lightDirWS));

    // 这里先用你脸部 SDF 的软硬参数，避免再新增面板参数
    float normalLit = smoothstep(
        _FaceSDFShadowThreshold - _FaceSDFShadowSoftness,
        _FaceSDFShadowThreshold + _FaceSDFShadowSoftness,
        ndl
    );

    return lerp(
        1.0 - _FaceSDFShadowStrength,
        1.0,
        normalLit
    );
}

float GetFringeShadowMask(
    float4 positionCS,
    float4 positionSS,
    float posNDCw,
    float3 positionWS,
    float2 uv,
    float3 lightDirWS
)
{
    if (_UseFringeShadow < 0.5 || _IsFace < 0.5)
        return 0.0;

    float2 screenUV = positionSS.xy / max(positionSS.w, 0.0001);

    float rawDepth = positionCS.z;
    float faceEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

    // 将脸部像素沿光照方向移动一小段世界距离，再投影回屏幕。
    // 这样偏移会随透视距离自然缩小；不能直接使用固定屏幕 UV 偏移，
    // 否则角色变远、脸部像素变少后，同样的偏移会覆盖整张脸。
    float3 offsetPositionWS = positionWS + normalize(lightDirWS) * _FringeShadowDistance;
    float4 offsetPositionCS = TransformWorldToHClip(offsetPositionWS);
    float4 offsetPositionSS = ComputeScreenPos(offsetPositionCS);
    float2 projectedUV = offsetPositionSS.xy / max(offsetPositionSS.w, 0.0001);

    // 距离内完整开启，超过设置距离后立即关闭，不做中间渐变。
    float cutoffDistance = max(_FringeShadowCameraFadeDistance, 0.0001);
    float distanceVisible = 1.0 - step(cutoffDistance, faceEyeDepth);
    float2 sampleUV = projectedUV;

    if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
        return 0.0;

    float hairRawDepth = SAMPLE_TEXTURE2D(
        _MyToonFringeShadowTex,
        sampler_MyToonFringeShadowTex,
        sampleUV
    ).g;

    // 黑色清屏区域没有头发。
    float hasHair = step(0.000001, hairRawDepth);

    float hairEyeDepth = LinearEyeDepth(hairRawDepth, _ZBufferParams);

    // 只有紧贴脸部前方的头发才允许投影。旧逻辑只判断“头发在脸前”，
    // 会把侧发、后发等离脸较远的深度也当作刘海，导致整张脸变暗。
    float depthGap = faceEyeDepth - hairEyeDepth;
    float depthSoftness = clamp(
        fwidth(faceEyeDepth) + fwidth(hairEyeDepth),
        0.0005,
        0.01
    );
    float frontPass = smoothstep(
        -_FringeShadowDepthBias - depthSoftness,
        -_FringeShadowDepthBias + depthSoftness,
        depthGap
    );
    float maxDepthGap = max(_FringeShadowMaxDepthGap, 0.01);
    float proximityPass = 1.0 - smoothstep(
        maxDepthGap - depthSoftness,
        maxDepthGap + depthSoftness,
        depthGap
    );

    // 刘海只应影响上半脸。即使远处头发与脸落入同一像素，
    // 也不会再把鼻子、嘴和下巴一起压暗。
    float boundarySoftness = max(_FringeShadowBoundarySoftness, 0.001);
    float upperFaceMask = smoothstep(
        _FringeShadowLowerBoundary - boundarySoftness,
        _FringeShadowLowerBoundary + boundarySoftness,
        uv.y
    );

    return hasHair * frontPass * proximityPass * upperFaceMask * distanceVisible;
}

float GetFringeShadow(
    float4 positionCS,
    float4 positionSS,
    float posNDCw,
    float3 positionWS,
    float2 uv,
    float3 lightDirWS
)
{
    float shadowMask = GetFringeShadowMask(positionCS, positionSS, posNDCw, positionWS, uv, lightDirWS);

    // 返回值：1 = 不受刘海影响；越小 = 越暗。
    return lerp(1.0, 1.0 - _FringeShadowStrength, shadowMask);
}


float GetFaceSDFLight(float2 uv, float3 normalWS,float3 lightDirWS)
{
    if (_UseFaceSDF < 0.5 || _IsFace < 0.5)
        return 1.0;

    // 非脸部 SDF 区域，比如耳朵，不要直接全亮
    // 改为普通法线卡通阴影
    // if (faceMask <= 0.001)
    //     return GetNormalToonLight(normalWS, lightDirWS);

    // 从 objectToWorld 矩阵里取模型自己的方向
    float3 upWS      = normalize(unity_ObjectToWorld._m01_m11_m21);
    float3 forwardWS = normalize(unity_ObjectToWorld._m02_m12_m22);
    float3 rightWS   = normalize(unity_ObjectToWorld._m00_m10_m20);

    // 只看水平面 XZ，不让光源上下角度影响脸部 SDF 阴影
    float2 lightXZ = lightDirWS.xz;
    float2 forwardXZ = forwardWS.xz;
    float2 rightXZ = rightWS.xz;

    lightXZ = normalize(lightXZ + forwardXZ * 0.0001);
    forwardXZ = normalize(forwardXZ);
    rightXZ = normalize(rightXZ);

    // 参照 NoiRC256 的倒置处理
    // 如果你确认模型永远是正常站立，也可以先改成 float isUpright = 1.0;
    float isUpright = (upWS.y - lightDirWS.y) < 0.0 ? 1.0 : -1.0;

    float frontDot = dot(forwardXZ, lightXZ) ;
    float side = dot(rightXZ, lightXZ) ;

    // 光线接近正前方时，side 会在 0 附近来回跨越。
    // 硬切镜像 UV 会让鼻影瞬间跳边，所以在 0 附近混合左右两次采样。
    float2 flippedSdfUV = float2(1.0 - uv.x, uv.y);
    float ilmOriginal = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, uv).r;
    float ilmFlipped = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, flippedSdfUV).r;
    float sideSwitch = smoothstep(
        -_FaceSDFSideSwitchSoftness,
        _FaceSDFSideSwitchSoftness,
        side
    );
    float ilm = lerp(ilmOriginal, ilmFlipped, sideSwitch);

    if (_FaceSDFInvert > 0.5)
        ilm = 1.0 - ilm;

    // NoiRC256 核心映射：
    // 光从正前方来：frontDot 接近 1，ctrl 接近 0
    // 光从侧面来：frontDot 接近 0，ctrl 接近 0.5
    // 光从背后方来：frontDot 接近 -1，ctrl 接近 1
    float ctrl = -0.5 * frontDot + 0.5;

    // 你的自定义偏移继续保留
    ctrl = saturate(ctrl + _FaceSDFFrontOffset + _FaceSDFShadowThreshold);

    // 原版是 step(ctrl, ilm)
    // 为了保留你的柔边，用 smoothstep 做软过渡
    // 至少保留一个像素足迹宽度的过渡。否则材质把 softness 调得很小时，
    // SDF 会随着相机距离改变采样覆盖范围，并在明暗阈值两侧反复跳变。
    float sdfSoftness = max(_FaceSDFShadowSoftness, fwidth(ilm));
    float sdfLit = smoothstep(
        ctrl - sdfSoftness,
        ctrl + sdfSoftness,
        ilm
    );
    
    // 光源接近脸背后时，强制把脸部 SDF 压暗
    float backLightFade = smoothstep(-0.65, -0.55, frontDot);
    sdfLit *= backLightFade;

    float faceLight = lerp(
        1.0 - _FaceSDFShadowStrength,
        1.0,
        sdfLit
    );

    
    return faceLight;
}

#endif
