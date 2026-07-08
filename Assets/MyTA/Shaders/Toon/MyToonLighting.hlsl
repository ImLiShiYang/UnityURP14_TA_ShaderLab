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

float GetFaceSDFLight(float2 uv, float3 normalWS,float3 lightDirWS)
{
    if (_UseFaceSDF < 0.5 || _IsFace < 0.5)
        return 1.0;

    float4 faceSDFMap = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, uv);
    float faceMask = faceSDFMap.g;

    // 非脸部 SDF 区域，比如耳朵，不要直接全亮
    // 改为普通法线卡通阴影
    if (faceMask <= 0.001)
        return GetNormalToonLight(normalWS, lightDirWS);

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

    // NoiRC256：RdotL > 0 时用翻转贴图
    float2 sdfUV = uv;
    if (side > 0.0)
        sdfUV.x = 1.0 - sdfUV.x;

    faceSDFMap = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, sdfUV);

    float ilm = faceSDFMap.r;
    faceMask = faceSDFMap.g;
    float noseMask = faceSDFMap.b;

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
    float sdfLit = smoothstep(
        ctrl - _FaceSDFShadowSoftness,
        ctrl + _FaceSDFShadowSoftness,
        ilm
    );

    float faceLight = lerp(
        1.0 - _FaceSDFShadowStrength,
        1.0,
        sdfLit
    );

    // 鼻影建议先弱化，避免干扰主脸阴影调试
    float noseShadow = noseMask * _FaceSDFShadowStrength;
    float noseLight = 1.0 - noseShadow;

    // faceLight = min(faceLight, noseLight);

    return lerp(1.0, faceLight, saturate(faceMask));
}

#endif
