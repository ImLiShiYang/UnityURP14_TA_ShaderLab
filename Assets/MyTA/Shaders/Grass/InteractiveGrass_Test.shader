Shader "MyTA/Grass/InteractiveGrass_RT"
{
    Properties
    {
        [Header(Color)]
        // 草根部颜色和草尖颜色，最终会根据顶点在草叶上的高度做渐变。
        _BaseColor ("根部颜色", Color) = (0.15, 0.45, 0.12, 1)
        _TipColor ("草尖颜色", Color) = (0.45, 0.85, 0.25, 1)

        [Header(Lighting)]
        // 贴近地面的暗色，用来模拟 NiloCat 草 Shader 里的 ground tint。
        // 它会和根部 / 草尖渐变一起形成“暗根亮尖”的层次。
        _GroundColor ("地表暗色", Color) = (0.05, 0.12, 0.03, 1)

        // 假法线的随机侧向扰动。越大，每株草之间的受光差异越明显。
        _RandomNormal ("随机法线强度", Range(0, 0.8)) = 0.32

        // 假法线朝向摄像机的强度。越大，billboard 草越容易出现柔和亮面。
        _CameraNormalStrength ("相机方向法线强度", Range(0, 1)) = 0.55

        // 环境光和直射光倍率，方便在场景光照不变时单独调草的受光对比。
        _IndirectStrength ("环境光强度", Range(0, 2)) = 0.85
        _DirectStrength ("直射光强度", Range(0, 2)) = 1.15

        // 草尖高光强度。
        _SpecularStrength ("草尖高光强度", Range(0, 0.5)) = 0.12

        [Header(Interaction RT)]
        // 交互 RT，通常由脚步、角色或其他刷子写入。
        // 白色/高值区域代表草被压过或正在受到影响。
        [NoScaleOffset]_GrassInteractionTex ("草地交互贴图", 2D) = "black" {}

        // RT 覆盖的世界空间 XZ 范围：xy 是 minXZ，zw 是 maxXZ。
        // Shader 用它把草根的世界坐标转换成 RT 的 UV。
        _GrassInteractionRect ("交互区域 World XZ", Vector) = (0, 0, 1, 1)

        // 没有使用径向方向时的默认压弯方向，使用世界空间 xz。
        _GrassBendDirWS ("固定压弯方向 WS", Vector) = (0, 0, 1, 0)

        // 总开关，控制是否采样交互 RT。
        _EnableGrassInteraction ("启用交互 RT", Float) = 1

        // 采样 RT 梯度时的偏移距离，数值越大，估算方向越平滑，但细节更少。
        _GradientSampleDistance ("RT 梯度采样距离", Range(1, 8)) = 2

        [Header(Two Feet Radial Press)]
        // 是否启用实时脚部径向压草。
        // RT 负责历史压痕，两只脚参数负责当前帧脚附近的实时摊开。
        _EnableRadialPress ("启用实时脚部压草", Float) = 1

        // 两只脚的世界空间中心点，由 C# 每帧传入。
        _PressCenter0WS ("左脚压草中心 WS", Vector) = (0, 0, 0, 0)
        _PressCenter1WS ("右脚压草中心 WS", Vector) = (0, 0, 0, 0)

        // 两只脚各自的启用开关。
        _EnablePressCenter0 ("启用左脚压草", Float) = 0
        _EnablePressCenter1 ("启用右脚压草", Float) = 0

        // 两只脚各自的影响半径。
        _PressRadius0 ("左脚压草半径", Float) = 1.0
        _PressRadius1 ("右脚压草半径", Float) = 1.0

        // 是否使用“从脚中心向外散开”的方向。
        // 关闭时会退回到 _GrassBendDirWS。
        _UseRadialBendDir ("使用径向压弯方向", Float) = 1

        // 草向外摊开的强度。
        _SpreadStrength ("向外摊开强度", Range(0, 3)) = 1.2

        // 被压弯时中段向上拱起的高度，用于做类似塞尔达压草的弧形。
        _ArcLift ("弧形上拱高度", Range(0, 1)) = 0.25

        // 脚部影响范围的衰减曲线，越大越集中在脚附近。
        _RadialMaskPower ("径向衰减曲线", Range(0.2, 5)) = 1.2

        // 沿草高度方向的摊开权重，越大越偏向草尖移动。
        _SpreadHeightPower ("高度摊开曲线", Range(0.2, 5)) = 1.8

        [Header(Bend)]
        // 历史 RT 压弯强度。
        _BendStrength ("历史 RT 弯曲强度", Range(0, 3)) = 1.0

        // 草被压住后整体向下压平的强度。
        _FlattenStrength ("压低强度", Range(0, 1)) = 0.25

        // 高度遮罩曲线。越大，根部越稳定，草尖受影响越明显。
        _HeightMaskPower ("高度遮罩曲线", Range(0.2, 5)) = 1.5

        // 草叶在模型空间中的高度轴。
        // 当前默认是 Z 轴，因为这份草片模型大概率是沿本地 Z 方向生长。
        _GrassHeightAxisOS ("草高度轴 OS", Vector) = (0, 0, 1, 0)

        // 草根和草尖在高度轴上的范围，用于算 0 到 1 的高度遮罩。
        _GrassHeightMinOS ("草根高度 OS", Float) = -0.322
        _GrassHeightMaxOS ("草尖高度 OS", Float) = 0.322

        [Header(Billboard And Random)]
        // 是否让草片朝向摄像机。
        _EnableBillboard ("启用面向摄像机", Float) = 1

        // Billboard 后的宽高缩放。
        _BillboardWidthScale ("公告板宽度缩放", Float) = 1
        _BillboardHeightScale ("公告板高度缩放", Float) = 1

        // 每株草随机高度和宽度范围，用 pivot 世界坐标生成稳定随机值。
        _RandomHeightMin ("随机高度最小值", Float) = 0.75
        _RandomHeightMax ("随机高度最大值", Float) = 1.25
        _RandomWidthMin ("随机宽度最小值", Float) = 0.85
        _RandomWidthMax ("随机宽度最大值", Float) = 1.15

        [Header(Wind)]
        // 风的强度、速度、频率和方向。
        _WindStrength ("风强度", Range(0, 1)) = 0.08
        _WindSpeed ("风速度", Range(0, 10)) = 2.0
        _WindFrequency ("风频率", Range(0, 5)) = 1.0
        _WindDirectionWS ("风方向 WS", Vector) = (1, 0, 0, 0)

        [Header(Debug)]
        // 调试开关：打开后直接输出压弯 mask，方便看 RT 和脚部影响范围。
        _DebugInteractionMask ("显示交互遮罩", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassInteractionTex);
            SAMPLER(sampler_GrassInteractionTex);

            CBUFFER_START(UnityPerMaterial)

                float4 _BaseColor;
                float4 _TipColor;
                float4 _GroundColor;
                float _RandomNormal;
                float _CameraNormalStrength;
                float _IndirectStrength;
                float _DirectStrength;
                float _SpecularStrength;

                float4 _GrassInteractionRect;
                float4 _GrassBendDirWS;
                float _EnableGrassInteraction;
                float _GradientSampleDistance;
                float4 _GrassInteractionTex_TexelSize;

                float _EnableRadialPress;
                float4 _PressCenter0WS;
                float4 _PressCenter1WS;
                float _EnablePressCenter0;
                float _EnablePressCenter1;
                float _PressRadius0;
                float _PressRadius1;
                float _UseRadialBendDir;
                float _SpreadStrength;
                float _ArcLift;
                float _RadialMaskPower;
                float _SpreadHeightPower;

                float _BendStrength;
                float _FlattenStrength;
                float _HeightMaskPower;
                float4 _GrassHeightAxisOS;
                float _GrassHeightMinOS;
                float _GrassHeightMaxOS;

                float _EnableBillboard;
                float _BillboardWidthScale;
                float _BillboardHeightScale;
                float _RandomHeightMin;
                float _RandomHeightMax;
                float _RandomWidthMin;
                float _RandomWidthMax;

                float _WindStrength;
                float _WindSpeed;
                float _WindFrequency;
                float4 _WindDirectionWS;

                float _DebugInteractionMask;

            CBUFFER_END

            // 顶点输入。
            // positionOS 是草片网格的模型空间顶点位置。
            // uv 当前主要用于片元阶段保留原始 UV，后续如果加贴图会用到。
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 顶点到片元的数据。
            // bendMask 用于调试和压弯后变暗。
            // heightMask 用于控制风、压弯等效果主要影响草尖。
            // colorHeight 用于根部到草尖的颜色渐变。
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float bendMask : TEXCOORD1;
                float heightMask : TEXCOORD2;
                float colorHeight : TEXCOORD3;

                float3 positionWS : TEXCOORD4;
                half3 normalWS : TEXCOORD5;
            };



            // 安全归一化二维向量。
            // 当向量过短时使用 fallback，避免 normalize(0) 导致异常方向。
            float2 SafeNormalize2(float2 v, float2 fallback)
            {
                float lenSq = dot(v, v);

                if (lenSq < 0.0001)
                    return normalize(fallback);

                return v * rsqrt(lenSq);
            }

            // 安全归一化三维向量。
            // 主要用于草高度轴等可能被材质参数设置为 0 的情况。
            float3 SafeNormalize3(float3 v, float3 fallback)
            {
                float lenSq = dot(v, v);

                if (lenSq < 0.0001)
                    return normalize(fallback);

                return v * rsqrt(lenSq);
            }

            // 根据二维坐标生成 0 到 1 的伪随机值。
            // 这里用草的 pivot 世界坐标来生成随机宽高，所以同一株草每帧随机值稳定。
            float Hash12(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // 构造草用的假法线。
            // 草片 / billboard 草不适合直接用模型 normal，
            // 否则会出现正面亮、背面黑、转角突变的问题。
            // 这里接近 NiloCat 的做法：
            // world up + 随机侧向扰动 + 朝向摄像机的偏移。
            // 这样每株草会有稳定但不同的受光，整片草不再像一块平板。
            half3 BuildGrassNiloNormalWS(float3 pivotWS, float windNormalOffset)
            {
                float3 upWS = float3(0.0, 1.0, 0.0);

                float3 cameraRightWS = normalize(UNITY_MATRIX_V[0].xyz);
                float3 cameraForwardWS = normalize(-UNITY_MATRIX_V[2].xyz);

                float randomNormal = Hash12(pivotWS.xz + 31.7) * 2.0 - 1.0;
                float sideAmount = randomNormal * _RandomNormal + windNormalOffset;

                half3 normalWS = normalize(
                    upWS +
                    cameraRightWS * sideAmount -
                    cameraForwardWS * _CameraNormalStrength
                );

                return normalWS;
            }

            // 草的简化光照。
            // 使用 Half Lambert，让背光面不会死黑，模拟草叶透光感。
            // 使用 SampleSH 取得环境光。
            // 使用一个很弱的高光，让草尖有一点亮边。
            half3 ApplyGrassLighting(
                half3 albedo,
                float3 positionWS,
                half3 normalWS,
                half height01)
            {
                half3 N = normalize(normalWS);
                half3 V = normalize(_WorldSpaceCameraPos.xyz - positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 L = normalize(mainLight.direction);

                // Half Lambert，比 max(0, dot(N, L)) 更适合草。
                half halfLambert = dot(N, L) * 0.5h + 0.5h;
                halfLambert = saturate(halfLambert);

                half3 indirect = SampleSH(N) * albedo * _IndirectStrength;

                half3 direct =
                    albedo *
                    halfLambert *
                    mainLight.color *
                    mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation *
                    _DirectStrength;

                half3 H = normalize(L + V);

                half spec = saturate(dot(N, H));

                // 用多次平方代替 pow，便宜一点。
                spec *= spec;
                spec *= spec;
                spec *= spec;

                // 高光很弱，并且主要在草尖。
                spec *= _SpecularStrength * saturate(height01);
                spec *= mainLight.shadowAttenuation;

                half3 specular = spec * mainLight.color;

                return indirect + direct + specular;
            }


            // 把世界空间位置映射到交互 RT 的 UV。
            // _GrassInteractionRect 定义了 RT 在世界 XZ 平面覆盖的矩形范围。
            float2 GetGrassInteractionUV(float3 positionWS)
            {
                float2 rectMin = _GrassInteractionRect.xy;
                float2 rectMax = _GrassInteractionRect.zw;
                float2 rectSize = rectMax - rectMin;

                float2 uv = float2(
                    (positionWS.x - rectMin.x) / max(abs(rectSize.x), 0.0001),
                    (positionWS.z - rectMin.y) / max(abs(rectSize.y), 0.0001)
                );

                return uv;
            }

            // 判断 UV 是否在 0 到 1 范围内。
            // 返回 1 表示在 RT 覆盖范围内，返回 0 表示越界。
            float IsInside01(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            // 在指定 UV 上采样交互 RT。
            // 越界时直接变成 0，避免草在 RT 范围外也受到影响。
            float SampleGrassInteractionUV(float2 uv)
            {
                float inside = IsInside01(uv);

                float mask = SAMPLE_TEXTURE2D_LOD(
                    _GrassInteractionTex,
                    sampler_GrassInteractionTex,
                    uv,
                    0
                ).r;

                return mask * inside * saturate(_EnableGrassInteraction);
            }

            // 根据世界坐标采样交互 RT。
            // 调用方通常传入草根 rootWS，而不是草尖位置，这样整株草用同一个压弯状态。
            float SampleGrassInteraction(float3 positionWS)
            {
                return SampleGrassInteractionUV(GetGrassInteractionUV(positionWS));
            }

            // 根据 RT 中的 mask 梯度估算“向外散开”的方向。
            // 做法是采样左右上下四个点，比较两侧强度差，得到一个从高压区域向外的方向。
            float2 GetAccumulatedBendDir(float3 positionWS, float2 fallbackDir)
            {
                float2 uv = GetGrassInteractionUV(positionWS);
                float2 texel = _GrassInteractionTex_TexelSize.xy *
                               max(_GradientSampleDistance, 1.0);

                float leftMask = SampleGrassInteractionUV(uv - float2(texel.x, 0));
                float rightMask = SampleGrassInteractionUV(uv + float2(texel.x, 0));
                float downMask = SampleGrassInteractionUV(uv - float2(0, texel.y));
                float upMask = SampleGrassInteractionUV(uv + float2(0, texel.y));

                float2 outwardDir = float2(
                    leftMask - rightMask,
                    downMask - upMask
                );

                return SafeNormalize2(outwardDir, fallbackDir);
            }

            // 计算某一只脚对当前草根的压草强度。
            // 距离脚中心越近 mask 越接近 1，超过半径后为 0。
            float GetFootPressMask(float3 rootWS, float3 centerWS, float radius, float enable)
            {
                float dist = distance(rootWS.xz, centerWS.xz);
                float safeRadius = max(radius, 0.0001);

                float mask = 1.0 - saturate(dist / safeRadius);

                mask = smoothstep(0.0, 1.0, mask);
                mask = pow(mask, _RadialMaskPower);

                return mask * saturate(enable) * saturate(_EnableRadialPress);
            }

            // 计算脚部径向压弯方向。
            // 方向为从脚中心指向草根，所以草会从脚附近向四周摊开。
            float2 GetFootBendDir(float3 rootWS, float3 centerWS, float2 fallbackDir)
            {
                float2 dir = rootWS.xz - centerWS.xz;
                return SafeNormalize2(dir, fallbackDir);
            }

            // 构造朝向摄像机的 billboard 顶点世界坐标。
            // 它会以草根为 pivot，用摄像机 right/up 重新展开草片，再叠加随机宽高。
            // _EnableBillboard 为 0 时返回原始网格世界坐标，为 1 时完全使用 billboard 坐标。
            float3 BuildBillboardPositionWS(float3 positionOS,float heightOS,float3 heightAxisOS,float heightRandom,float widthRandom)
            {
                float3 originalPositionWS = TransformObjectToWorld(positionOS);

                float3 rootOS = heightAxisOS * _GrassHeightMinOS;
                float3 rootWS = TransformObjectToWorld(rootOS);

                float3 cameraRightWS = normalize(UNITY_MATRIX_V[0].xyz);
                float3 cameraUpWS = normalize(UNITY_MATRIX_V[1].xyz);

                float heightOffsetOS = heightOS - _GrassHeightMinOS;
                float widthOS = positionOS.x;

                float3 billboardPositionWS = rootWS;

                billboardPositionWS += cameraRightWS * widthOS * _BillboardWidthScale * widthRandom;
                billboardPositionWS += cameraUpWS * heightOffsetOS * _BillboardHeightScale * heightRandom;

                return lerp(originalPositionWS, billboardPositionWS, saturate(_EnableBillboard));
            }

            // 计算类似 NiloCat 草 Shader 的多层正弦风。
            // gust 负责顺风方向的阵风推力，sway 负责 -1~1 的往返摆动。
            // heightMask 让根部基本不动，草尖随风摆动更明显。
            float2 CalculateNiloStyleWind(float3 pivotWS, float heightMask, float heightRandom)
            {
                float2 windDir = SafeNormalize2(_WindDirectionWS.xz, float2(1, 0));
                float2 sideDir = float2(-windDir.y, windDir.x);

                float time = _Time.y * _WindSpeed;

                // 高草稍微更容易被风带动，矮草更稳一点。
                float randomHeight01 = saturate(
                    (heightRandom - _RandomHeightMin) /
                    max(_RandomHeightMax - _RandomHeightMin, 0.0001)
                );
                float heightWindScale = heightMask * lerp(0.85, 1.15, randomHeight01);

                // 0~1 的阵风强弱，只负责“顺风方向的持续风压”。
                float gustA = sin(
                    time * 0.78 +
                    pivotWS.x * _WindFrequency * 0.10 +
                    pivotWS.z * _WindFrequency * 0.10
                ) * 0.5 + 0.5;

                float gustB = sin(
                    time * 1.47 +
                    pivotWS.x * _WindFrequency * 0.37 +
                    pivotWS.z * _WindFrequency * 3.00
                ) * 0.5 + 0.5;

                float gustC = sin(
                    time * 2.21 +
                    pivotWS.x * _WindFrequency * 0.77 +
                    pivotWS.z * _WindFrequency * 3.00
                ) * 0.5 + 0.5;

                float gust = saturate(gustA * 0.62 + gustB * 0.28 + gustC * 0.10);
                float windPush = (0.25 + gust * 0.75) * _WindStrength * heightWindScale;

                // -1~1 的顺风方向往返摆动。
                // 它叠在 windPush 上，让草不会只是单向变强变弱。
                float forwardSway =
                    sin(
                        time * 1.33 +
                        pivotWS.x * _WindFrequency * 0.23 +
                        pivotWS.z * _WindFrequency * 0.41
                    ) * 0.65 +
                    sin(
                        time * 2.53 +
                        pivotWS.x * _WindFrequency * 1.11 +
                        pivotWS.z * _WindFrequency * 0.67
                    ) * 0.35;

                forwardSway *= _WindStrength * 0.30 * heightWindScale;

                // -1~1 的侧向摆动，让草叶之间更有错落。
                float sideSway =
                    sin(
                        time * 2.17 +
                        pivotWS.x * _WindFrequency * 1.37 +
                        pivotWS.z * _WindFrequency * 1.61
                    ) * 0.75 +
                    sin(
                        time * 3.31 +
                        pivotWS.x * _WindFrequency * 2.07 +
                        pivotWS.z * _WindFrequency * 0.97
                    ) * 0.25;

                sideSway *= _WindStrength * 0.55 * heightWindScale;

                return windDir * (windPush + forwardSway) + sideDir * sideSway;
            }

            // 顶点阶段负责所有草的几何变形：
            // 1. 根据草高度算根部到草尖的遮罩。
            // 2. 根据 pivot 生成随机宽高。
            // 3. 可选 billboard，让草片朝向摄像机。
            // 4. 从 RT 和两只脚得到压草 mask 与方向。
            // 5. 叠加风、径向摊开、弧形上拱和压平。
            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;

                float3 positionOS = input.positionOS.xyz;

                // 每株草的 pivot。GPU Instancing 时，不同实例的 ObjectToWorld 不同，
                // 所以这里可以用 pivotWS 生成每株草稳定但不同的随机数。
                float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));

                float3 heightAxisOS = SafeNormalize3(_GrassHeightAxisOS.xyz, float3(0, 0, 1));

                float heightOS = dot(positionOS, heightAxisOS);
                float heightRange = max(_GrassHeightMaxOS - _GrassHeightMinOS, 0.0001);

                // rawHeightMask 是线性的高度 0 到 1。
                // heightMask 是加过 pow 的版本，主要用于风和弯曲，让草尖变化更明显。
                float rawHeightMask = saturate((heightOS - _GrassHeightMinOS) / heightRange);
                float heightMask = pow(rawHeightMask, _HeightMaskPower);

                float heightRandom01 = Hash12(pivotWS.xz);
                float widthRandom01 = Hash12(pivotWS.zx + 17.13);

                float heightRandom = lerp(_RandomHeightMin, _RandomHeightMax, heightRandom01);
                float widthRandom = lerp(_RandomWidthMin, _RandomWidthMax, widthRandom01);

                float3 positionWS = BuildBillboardPositionWS(positionOS,heightOS,heightAxisOS,heightRandom,widthRandom);

                // 用草根位置采样压草信息，保证同一株草的所有顶点使用一致的交互状态。
                float3 rootOS = heightAxisOS * _GrassHeightMinOS;
                float3 rootWS = TransformObjectToWorld(rootOS);

                // 历史压草 mask，来自交互 RT。
                float rtBendMask = saturate(SampleGrassInteraction(rootWS) * _BendStrength);

                // 固定方向和 RT 梯度方向。
                // fixedBendDir 是兜底方向，rtRadialDir 是根据历史 RT 估算出来的向外方向。
                float2 fixedBendDir = SafeNormalize2(_GrassBendDirWS.xz, float2(0, 1));
                float2 rtRadialDir = GetAccumulatedBendDir(rootWS, fixedBendDir);

                // 分别计算左右脚对当前草根的影响强度。
                float footMask0 = GetFootPressMask(rootWS,_PressCenter0WS.xyz, _PressRadius0, _EnablePressCenter0);
                float footMask1 = GetFootPressMask(rootWS,_PressCenter1WS.xyz,_PressRadius1,_EnablePressCenter1);

                float2 footDir0 = GetFootBendDir(rootWS, _PressCenter0WS.xyz, fixedBendDir);
                float2 footDir1 = GetFootBendDir(rootWS, _PressCenter1WS.xyz, fixedBendDir);

                // 两只脚同时影响一株草时，不把两个方向相加。
                // 直接选择影响更强的那只脚，避免左右脚方向互相抵消。
                float useFoot1 = step(footMask0, footMask1);

                float footPressMask = max(footMask0, footMask1);
                float2 nearestFootDir = lerp(footDir0, footDir1, useFoot1);

                float2 radialBendDir = SafeNormalize2(nearestFootDir, fixedBendDir);
                float hasFootPress = step(0.0001, footPressMask);

                // 如果启用径向压弯，并且当前确实有脚部影响，则使用脚中心向外的方向。
                // 否则保留固定方向。
                float2 footBendDir = SafeNormalize2(
                    lerp(fixedBendDir,radialBendDir,saturate(_UseRadialBendDir) * hasFootPress),fixedBendDir);

                // finalPressMask 同时考虑历史 RT 和实时脚部影响。
                // 方向选择上，如果 RT 影响强于脚部，就使用 RT 梯度方向，否则使用最近脚方向。
                float finalPressMask = saturate(max(rtBendMask, footPressMask));
                float useRTDirection = step(footPressMask + 0.0001, rtBendMask);
                float2 combinedPressDir = SafeNormalize2(
                    lerp(footBendDir, rtRadialDir, useRTDirection),fixedBendDir );

                float2 windOffset = CalculateNiloStyleWind(pivotWS, heightMask, heightRandom);

                // 草被踩住时减少风的影响。
                // 这样可以避免已经倒伏的草还在大幅摆动。
                windOffset *= lerp(1.0, 0.12, finalPressMask);

                // 摊开位移的高度分布。
                // 数值越靠近草尖越大，根部基本不动。
                float spreadProfile = pow(rawHeightMask, _SpreadHeightPower);

                // 弧形上拱分布。
                // 这个公式在根部和尖端为 0，中段最大，所以被压时中段会拱起来。
                float arcProfile = 4.0 * rawHeightMask * (1.0 - rawHeightMask);

                float2 radialSpreadOffset =combinedPressDir *finalPressMask *_SpreadStrength *spreadProfile;

                // 水平位移：风 + 压草向外摊开。
                // 实时脚部和历史 RT 共用 finalPressMask，避免同一处被重复叠加得过长。
                positionWS.xz += windOffset + radialSpreadOffset;

                // 垂直位移：先用 ArcLift 做中段上拱，再用 FlattenStrength 把草整体压低。
                positionWS.y += finalPressMask * _ArcLift * arcProfile;
                positionWS.y -= finalPressMask * _FlattenStrength * rawHeightMask;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.bendMask = finalPressMask;
                output.heightMask = heightMask;
                output.colorHeight = rawHeightMask;

                output.positionWS = positionWS;

                float3 cameraRightWS = normalize(UNITY_MATRIX_V[0].xyz);
                float3 windOffsetWS = float3(windOffset.x, 0.0, windOffset.y);
                float windNormalOffset = dot(windOffsetWS, cameraRightWS) * -0.25;
                output.normalWS = BuildGrassNiloNormalWS(pivotWS, windNormalOffset);

                return output;
            }

            // 片元阶段比较简单：
            // 根据草高度混合根部色和尖端色；
            // 草被压弯后略微变暗；
            // Debug 模式下直接输出压草 mask。
            half4 frag(Varyings input) : SV_Target
            {
                float height = saturate(input.colorHeight);

                half3 bladeColor = lerp(_BaseColor.rgb, _TipColor.rgb, height);
                half3 albedo = lerp(_GroundColor.rgb, bladeColor, saturate(height * 1.2));
                half alpha = lerp(_BaseColor.a, _TipColor.a, height);

                // 被压弯后略微变暗。
                albedo *= lerp(1.0, 0.65, saturate(input.bendMask));

                if (_DebugInteractionMask > 0.5)
                {
                    float m = saturate(input.bendMask);
                    return half4(m, m, m, 1);
                }

                half3 litColor = ApplyGrassLighting(
                    albedo,
                    input.positionWS,
                    input.normalWS,
                    height
                );

                return half4(litColor, alpha);
            }

            ENDHLSL
        }
    }
}
