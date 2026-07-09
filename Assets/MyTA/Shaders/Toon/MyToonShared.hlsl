#ifndef MY_TOON_SHARED_INCLUDED
#define MY_TOON_SHARED_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseColor;
float _Cull;
float _UseAlphaClipping;
float _Cutoff;
float _DebugMode;
float _FeatureDebugMode;



float _IsFace;
float _ReceiveShadowStrength;
float _MinLight;
float _FaceMinLight;
float _FaceShadowColorStrength;
float _ReceiveShadowMappingPosOffset;

float _UseFaceSDF;
float _FaceSDFShadowThreshold;
float _FaceSDFShadowSoftness;
float _FaceSDFShadowStrength;
float _FaceSDFFrontOffset;
float _FaceSDFSideSwitchSoftness;
float _FaceSDFInvert;

float _UseFringeShadow;
float _IsFringeShadowCaster;
float _FringeShadowDistance;
float _FringeShadowStrength;
float _FringeShadowDepthBias;
float _FringeShadowColorStrength;
float _FringeShadowCameraFadeDistance;

float _UseToonRamp;
float _ToonRampStrength;
float _ToonRampOffset;
float _ToonRampContrast;
float _ToonRampInvert;

float _UseHeightGradient;
float4 _HeightGradientTopColor;
float4 _HeightGradientBottomColor;
float _HeightGradientMin;
float _HeightGradientMax;
float _HeightGradientStrength;

float _UseOcclusion;
float _OcclusionStrength;
float4 _OcclusionMapChannelMask;
float _OcclusionRemapStart;
float _OcclusionRemapEnd;

float _UseEmission;
float4 _EmissionColor;
float _EmissionMulByBaseColor;
float4 _EmissionMapChannelMask;

float _UseSpecular;
float4 _SpecularColor;
float _SpecularIntensity;
float _SpecularThreshold;
float _SpecularSoftness;

float _UseHairSpecular;
float _HairSpecularUseBitangent;
float _HairSpecularShiftMapStrength;
float _HairSpecularMaskStrength;
float _HairSpecularMaskPower;
float4 _HairSpecularColor;
float _HairSpecularIntensity;
float _HairSpecularPower;
float _HairSpecularThreshold;
float _HairSpecularSoftness;
float _HairSpecularShift;
float _HairSpecularDirectionAtten;

float _UseHairSecondarySpecular;
float4 _HairSecondarySpecularColor;
float _HairSecondarySpecularIntensity;
float _HairSecondarySpecularPower;
float _HairSecondarySpecularShift;

float _UseMatCap;
float4 _MatCapColor;
float _MatCapIntensity;
float _MatCapBlendBaseColor;

float _DirectLightMultiplier;
float _MainLightIgnoreCelShade;

float4 _IndirectLightMinColor;
float _IndirectLightMultiplier;

float4 _ShadowColor;
float _ShadowThreshold;
float _ShadowSoftness;

float _UseOutline;
float4 _OutlineColor;
float _OutlineWidth;
float _OutlineZOffset;
float _OutlineZOffsetMaskRemapStart;
float _OutlineZOffsetMaskRemapEnd;

float4 _RimColor;
float _RimIntensity;
float _RimThreshold;
float _RimSoftness;
CBUFFER_END

TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);

TEXTURE2D(_MatCapMap);
SAMPLER(sampler_MatCapMap);

TEXTURE2D(_ToonRampMap);
SAMPLER(sampler_ToonRampMap);

TEXTURE2D(_FaceSDFMap);
SAMPLER(sampler_FaceSDFMap);

TEXTURE2D(_HairSpecularShiftMap);
SAMPLER(sampler_HairSpecularShiftMap);

TEXTURE2D(_HairSpecularMaskMap);
SAMPLER(sampler_HairSpecularMaskMap);

TEXTURE2D(_OutlineZOffsetMaskTex);
SAMPLER(sampler_OutlineZOffsetMaskTex);

TEXTURE2D(_MyToonFringeShadowTex);
SAMPLER(sampler_MyToonFringeShadowTex);


float4 SampleBaseMap(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
}

void DoAlphaClip(float alpha)
{
    if (_UseAlphaClipping > 0.5)
    {
        clip(alpha * _BaseColor.a - _Cutoff);
    }
}

float InvLerpClamp(float from, float to, float value)
{
    return saturate((value - from) / max(to - from, 0.0001));
}

float4 ApplyOutlineZOffset(float4 positionCS, float zOffset)
{
    if (unity_OrthoParams.w == 0)
    {
        float2 projZ = UNITY_MATRIX_P[2].zw;
        float modifiedPositionVSZ = -positionCS.w - zOffset;
        float modifiedPositionCSZ = modifiedPositionVSZ * projZ.x + projZ.y;
        positionCS.z = modifiedPositionCSZ * positionCS.w / -modifiedPositionVSZ;
    }
    else
    {
        positionCS.z -= zOffset / _ProjectionParams.z;
    }
    return positionCS;
}

float GetOutlineZOffsetMask(float2 uv)
{
    float mask = SAMPLE_TEXTURE2D_LOD(_OutlineZOffsetMaskTex, sampler_OutlineZOffsetMaskTex, uv, 0).r;
    mask = 1.0 - mask;
    return InvLerpClamp(_OutlineZOffsetMaskRemapStart, _OutlineZOffsetMaskRemapEnd, mask);
}

// 根据投影矩阵反推出当前摄像机的垂直 FOV，单位是角度。
float GetCameraFOV()
{
    // _m11 对应投影矩阵 Y 方向缩放，和垂直 FOV 相关。
    float t = unity_CameraProjection._m11;

    // 弧度转角度。
    float rad2Deg = 180.0 / 3.14159265;

    // FOV = atan(1 / _m11) * 2
    return atan(1.0 / t) * 2.0 * rad2Deg;
}

// 计算描边宽度的摄像机修正倍率。
// 目的是让角色远近变化、FOV 变化时，描边在屏幕上的粗细更稳定。
float GetOutlineCameraFixMultiplier(float positionVS_Z)
{
    // 参考距离：距离摄像机 3 米时，描边宽度按原始 _OutlineWidth 使用。
    float referenceDistance = 3.0;

    // 参考 FOV：FOV 为 60 度时，描边宽度按原始 _OutlineWidth 使用。
    float referenceFOV = 60.0;

    // unity_OrthoParams.w == 0 表示当前是透视相机。
    if (unity_OrthoParams.w == 0)
    {
        // 透视相机下，物体越远，屏幕上越小，所以描边需要按距离放大。
        float distanceFix = abs(positionVS_Z) / referenceDistance;

        // FOV 越大，画面越广，物体看起来越小，所以描边也需要适当放大。
        float fovFix = GetCameraFOV() / referenceFOV;

        return distanceFix * fovFix;
    }
    else
    {
        // 正交相机没有近大远小，所以只根据正交相机尺寸修正描边。
        float referenceOrthoSize = 5.0;

        return abs(unity_OrthoParams.y) / referenceOrthoSize;
    }
}

// 根据摄像机修正后的描边宽度，沿世界空间法线外扩顶点。
float3 ApplyOutlineWidth(float3 positionWS, float3 normalWS)
{
    // 转到观察空间，主要是为了拿到 positionVS.z，也就是距离摄像机的大致远近。
    float3 positionVS = TransformWorldToView(positionWS);

    // 基础描边宽度乘以摄像机修正倍率。
    float outlineWidth = _OutlineWidth * GetOutlineCameraFixMultiplier(positionVS.z);

    // 沿法线方向外扩，形成 Inverted Hull 描边。
    return positionWS + normalize(normalWS) * outlineWidth;
}

#endif
