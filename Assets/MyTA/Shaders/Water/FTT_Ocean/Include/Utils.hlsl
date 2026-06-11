#ifndef UTILITY_H
#define UTILITY_H

float remap(float x, float low1, float high1, float low2, float high2){
    return low2 + (x - low1) * (high2 - low2) / (high1 - low1);
}

float2 remap(float2 x, float2 low1, float2 high1, float2 low2, float2 high2){
    return low2 + (x - low1) * (high2 - low2) / (high1 - low1);
}

float3 remap(float3 x, float3 low1, float3 high1, float3 low2, float3 high2){
    return low2 + (x - low1) * (high2 - low2) / (high1 - low1);
}

float4 remap(float4 x, float4 low1, float4 high1, float4 low2, float4 high2){
    return low2 + (x - low1) * (high2 - low2) / (high1 - low1);
}

float fade(float t) {
    // 6t^5 - 15t^4 + 10t^3
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float2 fade(float2 t) {
    // 6t^5 - 15t^4 + 10t^3
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float3 fade(float3 t) {
    // 6t^5 - 15t^4 + 10t^3
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// assume a and b is normalized
float SinOf(const float3 a, const float3 b)
{
    return length(cross(a, b));
}

float Square(float x)
{
    return x * x;
}

float2 AABBRayIntersect(float3 rayOrigin, float3 rayDir, float3 aabbMin, float3 aabbMax)
{
    float3 invDir = 1.0 / rayDir;
    float3 t0 = (aabbMin - rayOrigin) * invDir;
    float3 t1 = (aabbMax - rayOrigin) * invDir;
    float3 tmin3 = min(t0, t1);
    float3 tmax3 = max(t0, t1);
    return float2(max(max(tmin3.x, tmin3.y), tmin3.z), min(tmax3.x, min(tmax3.y, tmax3.z)));
}

float2 RaySphereDst(float3 sphereCenter, float sphereRadius, float3 pos, float3 rayDir)
{
    float3 oc = pos - sphereCenter;
    float b = dot(rayDir, oc);
    float c = dot(oc, oc) - sphereRadius * sphereRadius;
    float t = b * b - c;//t > 0有两个交点, = 0 相切， < 0 不相交
    
    float delta = sqrt(max(t, 0));
    float dstToSphere = max(-b - delta, 0);
    float dstInSphere = max(-b + delta - dstToSphere, 0);
    return float2(dstToSphere, dstInSphere);
}

#endif