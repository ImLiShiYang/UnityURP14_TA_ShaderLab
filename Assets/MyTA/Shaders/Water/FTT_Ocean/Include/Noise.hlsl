#ifndef NOISE_H
#define NOISE_H
#define PI 3.14159265359

#include "Utils.hlsl"

// Hash without sine
// https://www.shadertoy.com/view/4djSRW
float hash11(float p)
{
    p = frac(p * .1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float hash12(float2 p)
{
    float3 p3 = frac(p.xyx * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float hash13(float3 p)
{
    p = frac(p * .1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float hash14(float4 p)
{
    p = frac(p * float4(.1031, .1030, .0973, .1099));
    p += dot(p, p.wzxy + 33.33);
    return frac((p.x + p.y) * (p.z + p.w));
}

float2 hash21(float p)
{
    float3 p3 = frac(p * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 hash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 hash23(float3 p)
{
    p = frac(p * float3(.1031, .1030, .0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.xx + p.yz) * p.zy);
}

float3 hash31(float p)
{
    float3 p3 = frac(p * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float3 hash32(float2 p)
{
    float3 p3 = frac(p.xyx * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float3 hash33(float3 p)
{
    p = frac(p * float3(.1031, .1030, .0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float4 hash41(float p)
{
    float4 p4 = frac(p * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash42(float2 p)
{
    float4 p4 = frac(p.xyxy * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash43(float3 p)
{
    float4 p4 = frac(p.xyzx * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash44(float4 p)
{
    p = frac(p * float4(.1031, .1030, .0973, .1099));
    p += dot(p, p.wzxy + 33.33);
    return frac((p.xxyz + p.yzzw) * p.zywx);
}

// https://www.iliyan.com/publications/DitheredSampling
// float BlueNoise()

uint MurmurHash (uint p)
{
    uint m = 0x5bd1e995;
    p *= m;
    p ^= p >> 24;
    p *= m;
    uint h = 0x9747b28c;
    h ^= p.x;
    h ^= h >> 15;
    h *= m;
    h ^= h >> 13;
    return h;
}

uint MurmurHash (uint p, uint seed)
{
    uint m = 0x5bd1e995;
    uint h = seed;
    p *= m;
    p ^= p >> 24;
    p *= m;
    h *= m;
    h ^= p.x;
    h ^= h >> 15;
    h *= m;
    h ^= h >> 13;
    return h;
}

uint MurmurHash (uint2 p, uint seed)
{
    uint m = 0x5bd1e995;
    uint h = seed;
    p *= m;
    p ^= p >> 24;
    p *= m;
    h *= m;
    h ^= p.x;
    h *= m;
    h ^= p.y;
    h ^= h >> 15;
    h *= m;
    h ^= h >> 13;
    return h;
}

uint MurmurHash (uint3 p, uint seed)
{
    uint m = 0x5bd1e995;
    uint h = seed;
    p *= m;
    p ^= p >> 24;
    p *= m;
    
    h *= m;
    h ^= p.x;
    h *= m;
    h ^= p.y;
    h *= m;
    h ^= p.z;
    
    h ^= h >> 16;
    h *= m;
    h ^= h >> 11;
    return h;
}

float3 Grad (int hash)
{
    switch (hash & 15)
    {
        case 0: return float3(1, 1, 0);
        case 1: return float3(-1, 1, 0);
        case 2: return float3(1, -1, 0);
        case 3: return float3(-1, -1, 0);
        case 4: return float3(1, 0, 1);
        case 5: return float3(-1, 0, 1);
        case 6: return float3(1, 0, -1);
        case 7: return float3(-1, 0, -1);
        case 8: return float3(0, 1, 1);
        case 9: return float3(0, -1, 1);
        case 10: return float3(0, 1, -1);
        case 11: return float3(0, -1, -1);
        case 12: return float3(1, 1, 0);
        case 13: return float3(-1, 1, 0);
        case 14: return float3(0, -1, 1);
        default: return float3(0, -1, -1);
    }
}

float2 RandN (float root, float phase)
{
    return float2(cos(phase), sin(phase)) * root;                                       // Box-Muller transform
}

float PerlinNoise2(float2 uv, uint seed, uint2 wrap)
{
    float4 offSet = float4(frac(uv), 1, 1);
    offSet.zw = offSet.xy - 1;
    uint4 gps = uint4(uv, uint2(1 + uv) % wrap);

    float2 grad00 = Grad(MurmurHash(gps.xy, seed)).xy;
    float2 grad01 = Grad(MurmurHash(gps.xw, seed)).xy;
    float2 grad10 = Grad(MurmurHash(gps.zy, seed)).xy;
    float2 grad11 = Grad(MurmurHash(gps.zw, seed)).xy;

    float4 dots = float4(
        dot(offSet.xy, grad00),
        dot(offSet.xw, grad01),
        dot(offSet.zy, grad10),
        dot(offSet.zw, grad11));

    offSet.xy = fade(offSet.xy);
    float2 lx = lerp(dots.xy, dots.zw, offSet.x);
    return lerp(lx.x, lx.y, offSet.y);
}

float PerlinNoise3(float3 uvw, uint seed, uint3 wrap)
{
    float3 offSet = frac(uvw);
    float3 negOffSet = offSet - 1;
    
    uint3 gp = floor(uvw);
    uint3 gp1 = (gp + 1) % wrap;

    float3 grad000 = Grad(MurmurHash(gp, seed));
    float3 grad001 = Grad(MurmurHash(uint3(gp.xy, gp1.z), seed));
    float3 grad010 = Grad(MurmurHash(uint3(gp.x, gp1.y, gp.z), seed));
    float3 grad011 = Grad(MurmurHash(uint3(gp.x, gp1.yz), seed));
    float3 grad100 = Grad(MurmurHash(uint3(gp1.x, gp.yz), seed));
    float3 grad101 = Grad(MurmurHash(uint3(gp1.x, gp.y, gp1.z), seed));
    float3 grad110 = Grad(MurmurHash(uint3(gp1.xy, gp.z), seed));
    float3 grad111 = Grad(MurmurHash(gp1, seed));

    float4 dots0 = float4(
        dot(offSet, grad000),
        dot(float3(offSet.xy, negOffSet.z), grad001),
        dot(float3(offSet.x, negOffSet.y, offSet.z), grad010),
        dot(float3(offSet.x, negOffSet.yz), grad011));
    float4 dots1 = float4(
        dot(float3(negOffSet.x, offSet.yz), grad100),
        dot(float3(negOffSet.x, offSet.y, negOffSet.z), grad101),
        dot(float3(negOffSet.xy, offSet.z), grad110),
        dot(negOffSet, grad111));

    offSet = fade(offSet);
    float4 lx = lerp(dots0, dots1, offSet.x);
    float2 ly = lerp(lx.xy, lx.zw, offSet.y);
    return lerp(ly.x, ly.y, offSet.z);
}

float WorleyNoise2(float2 uv, uint seed, uint2 wrap)
{
    uint2 gp = floor(uv);        // 晶格坐标
    float minDistance = 2.0f;
    int2 offsetN = -1;
    for(; offsetN.x <= 1; offsetN.x++)
    {
        for(offsetN.y = -1; offsetN.y <= 1; offsetN.y++)
        {
            int2 neighbor = gp + offsetN;
            float d = length(neighbor - uv + hash33(float3(neighbor % wrap, seed / float(0xffffffff))).xy);
            minDistance = min(minDistance, d);
        }
    }

    return minDistance;
}

float WorleyNoise3(float3 uvw, uint seed, int3 wrap)
{
    uint3 gp = floor(uvw);
    float minDistance = 1.0f;
    int3 offsetN = -1;
    for(; offsetN.x <= 1; offsetN.x++)
    {
        for(offsetN.y = -1; offsetN.y <= 1; offsetN.y++)
        {
            for(offsetN.z = -1; offsetN.z <= 1; offsetN.z++)
            {
                uint3 neighbor = gp + offsetN;
                float d = length(neighbor - uvw + hash44(float4(neighbor % wrap, seed / float(0xffffffff))).xyz);
                minDistance = min(minDistance, d);
            }
        }
    }

    return minDistance;
}

float SimplexNoise2(float2 uv, uint seed)
{
    uint2 s_uv0 = floor(uv + (uv.x + uv.y) * 0.366025404f);         // 所在网格坐标
    float2 d0 = uv - s_uv0 + (s_uv0.x + s_uv0.y) * 0.211324865f;    // 将三角晶格坐标s_uv0进行逆skew变换并计算标准坐标系下uv相对它的偏移
    float s = step(d0.y, d0.x);
    uint2 s_uv0Tuv1 = int2(s, 1 - s);                               // 确定坐标在哪个三角形，得到三角晶格坐标下s_o_uv1 = 第二个晶格点坐标 - 第一个晶格点坐标
    
    // 相对第二和第三个的顶点的偏移, 等于第二个顶点和第三个顶点相对晶格原点的偏移减去坐标相对晶格原点的偏移
    // 第二个顶点坐标通过对s_o_uv1应用Skew逆变换获得
    float2 d1 = d0 - s_uv0Tuv1 + 0.2113248654f;                     // d0 - (s_o_uv1 - (s_o_uv1.x + s_o_uv1.y) * simplexFac2) = d0 - uv1 + 1 * simplexFac2
    float2 d2 = d0 - 0.5773502692f;                                 // d0 - (s_o_uv2 - (s_o_uv2.x + s_o_uv2.y) * simplexFac2) = d0 - 1 + 2 * simplexFac2    (s_o_uv2 = (1, 1))
    float3 t = max(0, 0.6f - float3(dot(d0, d0), dot(d1, d1), dot(d2, d2)));
    float3 dots = float3(
        dot(Grad(MurmurHash(s_uv0, seed)), d0),
        dot(Grad(MurmurHash(s_uv0 + s_uv0Tuv1, seed)), d1),
        dot(Grad(MurmurHash(s_uv0 + 1, seed)), d2));
    
    t *= t;
    return dot(24 * t * t, dots);                                   // 计算三个顶点的贡献, max(0, (0.6 - |dist|^2))^4 * dot(grad, dist)
}


#endif