#ifndef SNOW_SURFACE_RT_DEFORM_AUX_PASSES_INCLUDED
#define SNOW_SURFACE_RT_DEFORM_AUX_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

TEXTURE2D(_SmoothFootstepTex);
SAMPLER(sampler_SmoothFootstepTex);

// Keep this layout identical to the forward pass so the material remains
// compatible with the SRP Batcher in every pass.
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

// Set by URP while rendering the shadow map.
float3 _LightDirection;
float3 _LightPosition;

struct SnowAuxAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct SnowAuxControlPoint
{
    float3 positionOS : INTERNALTESSPOS;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
};

struct SnowAuxTessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside  : SV_InsideTessFactor;
};

struct SnowAuxDepthVaryings
{
    float4 positionHCS : SV_POSITION;
};

struct SnowAuxShadowVaryings
{
    float4 positionHCS : SV_POSITION;
};

float2 SnowAuxWorldXZToFootUV(float3 positionWS)
{
    float2 rectSize = max(
        _FootstepRect.zw - _FootstepRect.xy,
        float2(0.0001, 0.0001));

    return (positionWS.xz - _FootstepRect.xy) / rectSize;
}

half SnowAuxFootUVInside(float2 uv)
{
    return
        step(0.0, uv.x) *
        step(0.0, uv.y) *
        step(uv.x, 1.0) *
        step(uv.y, 1.0);
}

half SnowAuxSampleDisplacementLOD(float2 uv)
{
    half inside = SnowAuxFootUVInside(uv) * _EnableFootstep;
    half4 data = SAMPLE_TEXTURE2D_LOD(
        _SmoothFootstepTex,
        sampler_SmoothFootstepTex,
        uv,
        0) * inside;

    half mask = saturate(data.a);
    half sink = saturate(data.r) * mask;
    half rim = saturate(data.g) * mask;

    return (rim * _RimHeight - sink * _MaxSnowSink) *
        _SnowDeformStrength;
}

float SnowAuxTessellationFactorAtPosition(float3 positionWS)
{
    float2 rectCenter = (_FootstepRect.xy + _FootstepRect.zw) * 0.5;
    float2 rectHalfSize = max(
        (_FootstepRect.zw - _FootstepRect.xy) * 0.5,
        0.0);
    float2 outside = max(
        abs(positionWS.xz - rectCenter) - rectHalfSize,
        0.0);
    float distanceOutside = length(outside);
    float localWeight = 1.0 - smoothstep(
        0.0,
        max(_TessellationBorderFade, 0.0001),
        distanceOutside);
    localWeight *= saturate(_EnableFootstep);

    return lerp(
        1.0,
        max(1.0, _TessellationFactor),
        localWeight);
}

SnowAuxControlPoint SnowAuxVert(SnowAuxAttributes input)
{
    SnowAuxControlPoint output;
    output.positionOS = input.positionOS.xyz;
    output.normalOS = input.normalOS;
    output.uv = input.uv;
    return output;
}

SnowAuxTessellationFactors SnowAuxPatchConstantFunction(
    InputPatch<SnowAuxControlPoint, 3> patch)
{
    SnowAuxTessellationFactors output;

    float3 positionWS0 = TransformObjectToWorld(patch[0].positionOS);
    float3 positionWS1 = TransformObjectToWorld(patch[1].positionOS);
    float3 positionWS2 = TransformObjectToWorld(patch[2].positionOS);

    output.edge[0] = SnowAuxTessellationFactorAtPosition(
        (positionWS1 + positionWS2) * 0.5);
    output.edge[1] = SnowAuxTessellationFactorAtPosition(
        (positionWS2 + positionWS0) * 0.5);
    output.edge[2] = SnowAuxTessellationFactorAtPosition(
        (positionWS0 + positionWS1) * 0.5);
    output.inside =
        (output.edge[0] + output.edge[1] + output.edge[2]) / 3.0;

    return output;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("SnowAuxPatchConstantFunction")]
[outputcontrolpoints(3)]
[maxtessfactor(16.0)]
SnowAuxControlPoint SnowAuxHull(
    InputPatch<SnowAuxControlPoint, 3> patch,
    uint controlPointID : SV_OutputControlPointID)
{
    return patch[controlPointID];
}

void SnowAuxInterpolatePatch(
    const OutputPatch<SnowAuxControlPoint, 3> patch,
    float3 barycentricCoordinates,
    out float3 positionOS,
    out float3 normalOS)
{
    positionOS =
        patch[0].positionOS * barycentricCoordinates.x +
        patch[1].positionOS * barycentricCoordinates.y +
        patch[2].positionOS * barycentricCoordinates.z;

    normalOS = normalize(
        patch[0].normalOS * barycentricCoordinates.x +
        patch[1].normalOS * barycentricCoordinates.y +
        patch[2].normalOS * barycentricCoordinates.z);
}

float3 SnowAuxGetDisplacedPositionWS(
    float3 positionOS,
    float3 normalOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    half3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
    float2 footUV = SnowAuxWorldXZToFootUV(positionWS);
    half displacement = SnowAuxSampleDisplacementLOD(footUV);
    return positionWS + normalWS * displacement;
}

[domain("tri")]
SnowAuxDepthVaryings SnowAuxDepthDomain(
    SnowAuxTessellationFactors tessellationFactors,
    const OutputPatch<SnowAuxControlPoint, 3> patch,
    float3 barycentricCoordinates : SV_DomainLocation)
{
    SnowAuxDepthVaryings output;
    float3 positionOS;
    float3 normalOS;
    SnowAuxInterpolatePatch(
        patch,
        barycentricCoordinates,
        positionOS,
        normalOS);

    float3 positionWS = SnowAuxGetDisplacedPositionWS(
        positionOS,
        normalOS);
    output.positionHCS = TransformWorldToHClip(positionWS);
    return output;
}

half SnowAuxDepthFragment(
    SnowAuxDepthVaryings input) : SV_TARGET
{
    return input.positionHCS.z;
}

[domain("tri")]
SnowAuxShadowVaryings SnowAuxShadowDomain(
    SnowAuxTessellationFactors tessellationFactors,
    const OutputPatch<SnowAuxControlPoint, 3> patch,
    float3 barycentricCoordinates : SV_DomainLocation)
{
    SnowAuxShadowVaryings output;
    float3 positionOS;
    float3 normalOS;
    SnowAuxInterpolatePatch(
        patch,
        barycentricCoordinates,
        positionOS,
        normalOS);

    float3 positionWS = SnowAuxGetDisplacedPositionWS(
        positionOS,
        normalOS);
    float3 normalWS = normalize(
        TransformObjectToWorldNormal(normalOS));

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 positionHCS = TransformWorldToHClip(
        ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
    positionHCS.z = min(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionHCS.z = max(positionHCS.z, UNITY_NEAR_CLIP_VALUE);
#endif

    output.positionHCS = positionHCS;
    return output;
}

half4 SnowAuxShadowFragment(
    SnowAuxShadowVaryings input) : SV_TARGET
{
    return 0;
}

#endif
