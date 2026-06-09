Shader "WaterRipple/WaterRippleWaveSurface"
{
    Properties
    {
        // ============================================================
        // Water Ripple RT
        // ============================================================

        // 水波 RT。
        // 由 RTManager 每帧传入。
        // 这里主要读取 A 通道。
        //
        // A 通道存储的是编码后的 signed height：
        // 0.5 = 无波动
        // < 0.5 = 下陷
        // > 0.5 = 隆起
        _WaterRippleTex ("Water Ripple RT A Height", 2D) = "gray" {}

        // 水波 RT 对应的世界坐标范围。
        //
        // x = 世界最小 X
        // y = 世界最小 Z
        // z = 世界最大 X
        // w = 世界最大 Z
        //
        // Shader 会用这个范围把世界坐标 XZ 映射到 RT UV。
        _WaterRippleRect ("Water Ripple Rect", Vector) = (0,0,1,1)

        // 水波总开关。
        // 0 = 关闭水波影响
        // 1 = 开启水波影响
        _EnableWaterRipple ("Enable Water Ripple", Float) = 0

        // 高度死区。
        // 用来过滤 0 附近的小误差，避免水面有细微脏闪。
        _WaterRippleSignedDeadZone ("Signed Height Dead Zone", Range(0, 0.2)) = 0.002


        // ============================================================
        // Water Color
        // ============================================================

        // 浅水颜色。
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.88, 1.00, 0.45)

        // 深水颜色。
        _DeepColor ("Deep Color", Color) = (0.02, 0.24, 0.42, 0.68)

        // 深浅水颜色混合比例。
        // 0 = 完全使用 DeepColor
        // 1 = 完全使用 ShallowColor
        _ColorBlend ("Color Blend", Range(0, 1)) = 0.72

        // 水面基础透明度。
        _Alpha ("Base Alpha", Range(0, 1)) = 0.52


        // ============================================================
        // Wave Response
        // ============================================================

        // 顶点位移高度。
        // 控制水面几何顶点上下起伏的幅度。
        // 注意：顶点位移依赖水面网格细分程度。
        _WaveHeight ("Vertex Wave Height", Range(0, 0.3)) = 0.035

        // 法线强度。
        // 值越大，水波导致的法线倾斜越明显，
        // 高光和折射也会更明显。
        _NormalStrength ("Normal Strength", Range(0.01, 12)) = 4.0

        // 水波区域颜色增强强度。
        // 水波越明显，越会混入一点 FresnelColor。
        _RippleColorStrength ("Ripple Color Strength", Range(0, 1)) = 0.08


        // ============================================================
        // Refraction
        // ============================================================

        // 基础折射强度。
        // 控制屏幕 UV 偏移幅度。
        _RefractionStrength ("Refraction Strength", Range(0, 0.08)) = 0.016

        // 水波坡度对折射的放大强度。
        // 值越大，水波附近画面扭曲越明显。
        _RefractionWaveStrength ("Refraction Wave Strength", Range(0, 4)) = 1.2

        // 水下场景色和水本身颜色的混合比例。
        // 0 = 更接近场景颜色
        // 1 = 更接近水体颜色
        _RefractionTintStrength ("Refraction Tint Strength", Range(0, 1)) = 0.28


        // ============================================================
        // Fresnel
        // ============================================================

        // Fresnel 边缘光颜色。
        _FresnelColor ("Fresnel Color", Color) = (0.65, 0.95, 1.0, 1)

        // Fresnel 幂次。
        // 值越大，边缘光范围越窄。
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.2

        // Fresnel 颜色强度。
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.6

        // Fresnel 对透明度的影响。
        // 边缘视角处 alpha 会增加。
        _FresnelAlpha ("Fresnel Alpha", Range(0, 1)) = 0.24


        // ============================================================
        // Specular
        // ============================================================

        // 高光颜色。
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)

        // 高光强度。
        _SpecularStrength ("Specular Strength", Range(0, 5)) = 1.2

        // 高光锐度。
        // 值越大，高光越小越尖。
        _SpecularPower ("Specular Power", Range(8, 256)) = 96


        // ============================================================
        // Foam
        // ============================================================

        // 泡沫颜色。
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)

        // 泡沫混合强度。
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.14

        // 泡沫出现阈值。
        // rippleAmount 高于这个值时开始出现泡沫。
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.32

        // 泡沫边缘柔和程度。
        _FoamSoftness ("Foam Softness", Range(0.001, 1)) = 0.22
    }

    SubShader
    {
        Tags
        {
            // 指定 URP 管线。
            "RenderPipeline"="UniversalPipeline"

            // 透明水面。
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            // Pass 名称。
            Name "WaterRippleSurfaceForward"

            // URP Forward 渲染 Pass。
            Tags { "LightMode"="UniversalForward" }

            // 普通透明混合。
            // final = src * srcAlpha + dst * (1 - srcAlpha)
            Blend SrcAlpha OneMinusSrcAlpha

            // 透明物体一般不写入深度，避免挡住后面的透明物体。
            ZWrite Off

            // 仍然进行深度测试，防止水面画到前景物体上。
            ZTest LEqual

            // 剔除背面。
            Cull Back

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            // 主光阴影变体。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            // 软阴影变体。
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // URP 基础函数，例如矩阵变换、屏幕坐标等。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // URP 光照函数，例如 GetMainLight、SampleSH。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // URP 阴影函数。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // 声明 Opaque Texture 采样函数 SampleSceneColor。
            // 折射效果需要用它采样屏幕背景颜色。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"


            // ========================================================
            // 顶点输入
            // ========================================================
            struct Attributes
            {
                // 模型空间顶点坐标。
                float4 positionOS : POSITION;

                // 模型空间法线。
                float3 normalOS   : NORMAL;

                // 模型 UV。
                // 当前这个 Shader 里没有直接使用 uv，
                // 因为水波采样是基于世界坐标 XZ。
                float2 uv         : TEXCOORD0;
            };


            // ========================================================
            // 顶点输出 / 片元输入
            // ========================================================
            struct Varyings
            {
                // 裁剪空间坐标，用于屏幕光栅化。
                float4 positionHCS : SV_POSITION;

                // 世界坐标。
                // 片元阶段要用它读取水波 RT。
                float3 positionWS  : TEXCOORD0;

                // 世界空间基础法线。
                float3 normalWS    : TEXCOORD1;

                // 屏幕坐标。
                // 片元阶段用它采样 Opaque Texture 做折射。
                float4 screenPos   : TEXCOORD2;
            };


            // ========================================================
            // 水波 RT 纹理声明
            // ========================================================
            TEXTURE2D(_WaterRippleTex);
            SAMPLER(sampler_WaterRippleTex);


            // ========================================================
            // 材质参数 CBUFFER
            // ========================================================
            CBUFFER_START(UnityPerMaterial)

                // 水波 RT 覆盖的世界 XZ 矩形。
                float4 _WaterRippleRect;

                // 水波启用开关。
                float _EnableWaterRipple;

                // 高度死区。
                float _WaterRippleSignedDeadZone;

                // 水颜色参数。
                float4 _ShallowColor;
                float4 _DeepColor;
                float _ColorBlend;
                float _Alpha;

                // 水波响应参数。
                float _WaveHeight;
                float _NormalStrength;
                float _RippleColorStrength;

                // 折射参数。
                float _RefractionStrength;
                float _RefractionWaveStrength;
                float _RefractionTintStrength;

                // Fresnel 参数。
                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelStrength;
                float _FresnelAlpha;

                // 高光参数。
                float4 _SpecularColor;
                float _SpecularStrength;
                float _SpecularPower;

                // 泡沫参数。
                float4 _FoamColor;
                float _FoamStrength;
                float _FoamThreshold;
                float _FoamSoftness;

                // Unity 自动提供的纹理尺寸信息。
                //
                // 对于一张 width x height 的纹理：
                // x = 1 / width
                // y = 1 / height
                // z = width
                // w = height
                //
                // 这里主要用 xy，表示一个 texel 在 UV 空间里的大小。
                float4 _WaterRippleTex_TexelSize;

            CBUFFER_END


            // ========================================================
            // 安全倒数
            // ========================================================
            float SafeRcp(float value)
            {
                // rcp(x) = 1 / x。
                //
                // 这里用 max(abs(value), 0.0001) 防止除以 0。
                // 如果 rectSize.x 或 rectSize.y 意外为 0，也不会导致 NaN。
                return rcp(max(abs(value), 0.0001));
            }


            // ========================================================
            // 世界坐标 XZ 转水波 RT UV
            // ========================================================
            float2 WorldXZToWaterRippleUV(float3 positionWS)
            {
                // 水波捕捉相机是俯视 XZ 平面，
                // 所以这里只关心世界坐标的 X 和 Z。
                //
                // _WaterRippleRect.xy = min world XZ
                // _WaterRippleRect.zw = max world XZ
                float2 rectSize = _WaterRippleRect.zw - _WaterRippleRect.xy;

                // 把世界 XZ 坐标归一化到 0~1。
                //
                // positionWS.x == rect min x 时，uv.x = 0
                // positionWS.x == rect max x 时，uv.x = 1
                //
                // positionWS.z == rect min z 时，uv.y = 0
                // positionWS.z == rect max z 时，uv.y = 1
                return float2(
                    (positionWS.x - _WaterRippleRect.x) * SafeRcp(rectSize.x),
                    (positionWS.z - _WaterRippleRect.y) * SafeRcp(rectSize.y)
                );
            }


            // ========================================================
            // 判断 UV 是否在水波 RT 范围内
            // ========================================================
            float WaterRippleUVInside(float2 uv)
            {
                // step(edge, x)：
                // x >= edge 返回 1
                // x < edge 返回 0
                //
                // step(0.0, uv.x) 表示 uv.x >= 0
                // step(0.0, uv.y) 表示 uv.y >= 0
                // step(uv.x, 1.0) 表示 1 >= uv.x，也就是 uv.x <= 1
                // step(uv.y, 1.0) 表示 1 >= uv.y，也就是 uv.y <= 1
                //
                // 四个条件相乘：
                // 只要有一个不满足，结果就是 0。
                return step(0.0, uv.x) *
                       step(0.0, uv.y) *
                       step(uv.x, 1.0) *
                       step(uv.y, 1.0);
            }


            // ========================================================
            // 应用 signed height 死区
            // ========================================================
            float ApplySignedDeadZone(float signedValue)
            {
                // signedValue 是已经解码后的高度：
                // -1 ~ 1
                //
                // 0 附近的小误差不要变成可见水波，
                // 否则水面会有轻微脏闪。
                float absValue = abs(signedValue);

                // 如果高度绝对值在死区范围内，直接认为没有波。
                if (absValue <= _WaterRippleSignedDeadZone)
                    return 0.0;

                // 超过死区后，重新映射有效范围。
                //
                // 例如死区是 0.2：
                // 原始有效范围是 0.2 ~ 1.0
                // 这里会把它重新映射成 0 ~ 1
                //
                // absValue = 0.2 -> remapped = 0
                // absValue = 0.6 -> remapped = 0.5
                // absValue = 1.0 -> remapped = 1
                //
                // max 防止分母为 0。
                float remapped =
                    (absValue - _WaterRippleSignedDeadZone) /
                    max(0.0001, 1.0 - _WaterRippleSignedDeadZone);

                // 前面 absValue 去掉了正负号，
                // 这里用 sign(signedValue) 把原来的正负号乘回来。
                //
                // 正数仍然表示隆起。
                // 负数仍然表示下陷。
                //
                // saturate 把 remapped 限制在 0~1。
                return sign(signedValue) * saturate(remapped);
            }


            // ========================================================
            // 根据 UV 读取水波高度
            // ========================================================
            float ReadRippleHeightUV(float2 uv)
            {
                // 采样水波 RT 的 A 通道。
                //
                // A 通道约定：
                // 0.5 = 无波动
                // < 0.5 = 下陷
                // > 0.5 = 隆起
                float encodedHeight =SAMPLE_TEXTURE2D_LOD(_WaterRippleTex, sampler_WaterRippleTex, uv, 0).a;
                    
                // 把 0~1 编码值解码成 -1~1 signed height。
                //
                // encoded 0.0 -> signed -1.0
                // encoded 0.5 -> signed  0.0
                // encoded 1.0 -> signed  1.0
                //
                // 然后应用死区过滤。
                return ApplySignedDeadZone(encodedHeight * 2.0 - 1.0);
            }


            // ========================================================
            // 根据世界坐标读取水波高度
            // ========================================================
            float ReadRippleHeightWS(float3 positionWS)
            {
                // 水波开关，限制到 0~1。
                float enable = saturate(_EnableWaterRipple);

                // 世界坐标 XZ 转 RT UV。
                float2 uv = WorldXZToWaterRippleUV(positionWS);

                // 判断当前位置是否在水波 RT 覆盖范围内。
                float inside = WaterRippleUVInside(uv);

                // 读取高度，并乘上：
                // enable：水波开关
                // inside：范围遮罩
                //
                // 如果关闭水波，返回 0。
                // 如果超出 RT 范围，返回 0。
                return ReadRippleHeightUV(uv) * enable * inside;
            }


            // ========================================================
            // 根据世界坐标读取水波坡度
            // ========================================================
            float2 ReadRippleSlopeWS(float3 positionWS)
            {
                // 水波开关。
                // 0 时最终坡度为 0。
                float enable = saturate(_EnableWaterRipple);

                // 把当前世界坐标转换成水波 RT UV。
                float2 uv = WorldXZToWaterRippleUV(positionWS);

                // 判断当前 UV 是否在 0~1 范围内。
                // 不在范围内就不产生坡度。
                float inside = WaterRippleUVInside(uv);

                // 获取一个 RT 像素在 UV 空间中的大小。
                //
                // _WaterRippleTex_TexelSize.xy 通常等于：
                // float2(1 / width, 1 / height)
                //
                // max 是为了防止异常情况下 texel 太小或为 0。
                float2 texel = max(_WaterRippleTex_TexelSize.xy,float2(0.0001, 0.0001) );

                // 用高度差近似坡度。
                //
                // 这里分别采样当前点：
                // 左边一个 texel
                // 右边一个 texel
                // 后边一个 texel
                // 前边一个 texel
                //
                // 通过两边高度差来估算当前水面的倾斜程度。
                // 这个坡度后面会同时用于：
                // 1. 构建水波法线
                // 2. 做屏幕折射偏移

                // 当前点左边的高度。
                float left = ReadRippleHeightUV(uv + float2(-texel.x, 0.0));

                // 当前点右边的高度。
                float right = ReadRippleHeightUV(uv + float2(texel.x, 0.0));

                // 当前点后边，也就是 UV y 负方向的高度。
                float back = ReadRippleHeightUV(uv + float2(0.0, -texel.y));

                // 当前点前边，也就是 UV y 正方向的高度。
                float front = ReadRippleHeightUV(uv + float2(0.0, texel.y));

                // 返回 X/Z 两个方向的坡度。
                //
                // x 分量：
                // left - right
                // 如果左边高、右边低，结果为正。
                // 如果右边高、左边低，结果为负。
                //
                // y 分量：
                // back - front
                // 如果后边高、前边低，结果为正。
                // 如果前边高、后边低，结果为负。
                //
                // 这里使用 left - right，而不是 right - left，
                // 是为了后面构建法线时方向一致。
                return float2(left - right, back - front) * enable * inside;
            }


            // ========================================================
            // 安全归一化
            // ========================================================
            float3 NormalizeSafe(float3 value)
            {
                // normalize(value) 本质是 value / length(value)。
                //
                // dot(value, value) 是长度平方。
                // rsqrt(x) 是 1 / sqrt(x)。
                //
                // max(..., 1e-6) 防止长度为 0 时产生 NaN。
                return value * rsqrt(max(dot(value, value), 1e-6));
            }


            // ========================================================
            // 根据水波坡度构建世界空间法线
            // ========================================================
            float3 BuildRippleNormalWS(float3 baseNormalWS, float2 slope)
            {
                // 基础水面法线。
                float3 n = NormalizeSafe(baseNormalWS);

                // 世界 X/Z 方向。
                // 因为水波 RT 是俯视 XZ 平面生成的，
                // 所以坡度也是基于世界 X/Z 方向。
                float3 worldX = float3(1.0, 0.0, 0.0);
                float3 worldZ = float3(0.0, 0.0, 1.0); 

                // 把世界 X 方向投影到当前水面平面上，得到水面切线方向。
                //
                // worldX - n * dot(worldX, n)
                // 表示去掉 worldX 在法线方向上的分量，
                // 保留贴着表面的分量。
                float3 tangentX = NormalizeSafe(worldX - n * dot(worldX, n));

                // 同理，把世界 Z 方向投影到水面平面上。
                float3 tangentZ = NormalizeSafe(worldZ - n * dot(worldZ, n));

                // 基础法线加上水波坡度扰动。
                //
                // slope.x 控制沿 tangentX 方向的法线偏移。
                // slope.y 控制沿 tangentZ 方向的法线偏移。
                //
                // _NormalStrength 越大，法线扰动越明显。
                float3 rippleNormal =
                    n +
                    tangentX * slope.x * _NormalStrength +
                    tangentZ * slope.y * _NormalStrength;

                // 返回单位长度法线。
                return NormalizeSafe(rippleNormal);
            }


            // ========================================================
            // 顶点函数
            // ========================================================
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // 模型空间坐标转世界空间。
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // 模型空间法线转世界空间。
                float3 normalWS = NormalizeSafe(TransformObjectToWorldNormal(IN.normalOS));

                // 根据当前世界坐标读取水波高度。
                float height = ReadRippleHeightWS(positionWS);

                // 顶点位移。
                //
                // 高度为正：沿法线方向抬起。
                // 高度为负：沿反法线方向下陷。
                //
                // 这里只做轻微起伏，
                // 主要细节仍然交给片元阶段的法线和折射。
                positionWS += normalWS * height * _WaveHeight;

                // 传递世界坐标给片元阶段。
                OUT.positionWS = positionWS;

                // 世界坐标转裁剪空间，供 GPU 光栅化。
                OUT.positionHCS = TransformWorldToHClip(positionWS);

                // 传递基础世界法线。
                OUT.normalWS = normalWS;

                // 计算屏幕坐标。
                // 后面片元阶段会用它采样 Opaque Texture 做折射。
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }


            // ========================================================
            // 片元函数
            // ========================================================
            half4 Frag(Varyings IN) : SV_Target
            {
                // 读取当前像素位置的水波高度。
                float height = ReadRippleHeightWS(IN.positionWS);

                // 读取当前像素附近的水波坡度。
                float2 slope = ReadRippleSlopeWS(IN.positionWS);

                // 用坡度构建受水波影响后的世界空间法线。
                float3 normalWS = BuildRippleNormalWS(IN.normalWS, slope);

                // 视线方向。
                // 从当前像素指向相机。
                float3 viewDirWS = NormalizeSafe(GetWorldSpaceViewDir(IN.positionWS));

                // 计算当前像素的主光阴影坐标。
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                // 获取 URP 主光源，包含方向、颜色、阴影衰减等。
                Light mainLight = GetMainLight(shadowCoord);

                // 主光方向。
                float3 lightDirWS = NormalizeSafe(mainLight.direction);

                // 法线和光照方向点乘。
                // 用于简单漫反射强度。
                float ndotl = saturate(dot(normalWS, lightDirWS));

                // Fresnel 计算。
                //
                // dot(normalWS, viewDirWS) 越小，
                // 说明视线越贴近水面，Fresnel 越强。
                //
                // 1 - dot 后，边缘视角会更亮。
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)),_FresnelPower);
                
                // 当前像素的屏幕 UV。
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // 折射偏移。
                //
                // slope 越大，说明水面坡度越明显，
                // 屏幕采样偏移越大，折射越明显。
                //
                // Fresnel 越强，折射偏移稍微加强。
                float2 refractionOffset =slope *_RefractionStrength *_RefractionWaveStrength *lerp(0.75, 1.35, fresnel);

                // 采样 URP Opaque Texture 得到水下/背景场景颜色。
                //
                // clamp 防止屏幕 UV 超出 0~1。
                half3 sceneColor = SampleSceneColor(clamp(screenUV + refractionOffset, 0.001, 0.999));
                
                // 计算水体基础颜色。
                // 在深水色和浅水色之间插值。
                half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, _ColorBlend);

                // 当前高度存在感。
                // 高度越大，说明水波越明显。
                float waveAmount = saturate(abs(height));

                // 当前坡度存在感。
                // 坡度越大，说明法线和折射变化越明显。
                //
                // * 4.0 是人为放大，让坡度更容易参与后续效果。
                float slopeAmount = saturate(length(slope) * 4.0);

                // 水波统一权重。
                //
                // height 和 slope 只要有一个明显，
                // 就认为当前位置有水波。
                float rippleAmount = saturate(max(waveAmount, slopeAmount));

                // 水波区域轻微增加 Fresnel 色。
                // 让波纹位置更容易被看见。
                waterTint += _FresnelColor.rgb * rippleAmount * _RippleColorStrength;

                // 混合折射到的场景色和水体颜色。
                //
                // _RefractionTintStrength 越小，越接近 sceneColor；
                // 越大，越接近 waterTint。
                half3 color = lerp(sceneColor, waterTint, _RefractionTintStrength);

                // 添加 Fresnel 边缘光。
                color += _FresnelColor.rgb * fresnel * _FresnelStrength;

                // ====================================================
                // 简化 Blinn-Phong 高光
                // ====================================================

                // 半角向量。
                // Blinn-Phong 使用 lightDir 和 viewDir 的中间方向。
                float3 halfDir = NormalizeSafe(lightDirWS + viewDirWS);

                // 高光强度。
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecularPower);

                // 应用高光强度参数。
                spec *= _SpecularStrength;

                // 应用主光距离衰减和阴影衰减。
                spec *= mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                // 把高光加到颜色上。
                color += _SpecularColor.rgb * spec * mainLight.color;

                // ====================================================
                // 泡沫
                // ====================================================

                // 根据 rippleAmount 生成泡沫 mask。
                //
                // rippleAmount 低于 _FoamThreshold 时基本没有泡沫。
                // 超过阈值后，经过 _FoamSoftness 平滑过渡。
                float foamMask = smoothstep(_FoamThreshold,_FoamThreshold + _FoamSoftness,rippleAmount);

                // 泡沫不单独模拟，
                // 只是在水波强的位置用柔和 mask 混入一点白色。
                color = lerp(color, _FoamColor.rgb, foamMask * _FoamStrength);

                // ====================================================
                // 环境光和直接光
                // ====================================================

                // 球谐环境光。
                half3 ambient = SampleSH(normalWS);

                // 主光直接光。
                //
                // ndotl * 0.35 + 0.65：
                // 让背光面也保持一定亮度，
                // 避免透明水面过黑。
                // half3 direct =mainLight.color *(ndotl * 0.35 + 0.65) *mainLight.distanceAttenuation *mainLight.shadowAttenuation;
                half3 direct =mainLight.color *(ndotl * 0.35 + 0.65) *mainLight.distanceAttenuation;

                // 把环境光和直接光作用到最终颜色上。
                color *= ambient + direct;

                // ====================================================
                // Alpha
                // ====================================================

                // 基础透明度。
                float alpha = _Alpha;

                // Fresnel 位置透明度略微增加。
                // 斜视角/边缘更明显。
                alpha += fresnel * _FresnelAlpha;

                // 泡沫区域略微更不透明。
                alpha += foamMask * 0.06;

                // 限制 alpha 到 0~1。
                alpha = saturate(alpha);

                // 输出最终透明水面颜色。
                return half4(color, alpha);
            }

            ENDHLSL
        }
    }
}