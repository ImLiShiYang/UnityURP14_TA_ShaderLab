Shader "Custom/ScreenSpaceDecal_VolumeBox_POM_Merged"
{
    Properties
    {
        // ============================================================
        // Base / Color
        // ============================================================
        // 主贴花纹理。
        // RGB：脚印颜色 / 泥土噪声 / 细节颜色。
        // Alpha：脚印形状遮罩，最终会参与透明度。
        _DecalTexture("Base / Decal Texture RGB + Alpha", 2D) = "white" {}

        // 贴花整体颜色乘子。
        // 最终颜色会乘 _DecalColor.rgb，透明度会乘 _DecalColor.a。
        _DecalColor("Decal Color", Color) = (1,1,1,1)

        // 是否使用 _DecalTexture 的 RGB。
        // 0：忽略贴图 RGB，只使用白色 * _DecalColor，适合只看 Alpha / 法线 / 高度效果。
        // 1：使用贴图 RGB，适合显示泥土颜色和脚印细节。
        _UseBaseRGB("Use Base RGB 0 ColorOnly 1 BaseRGB", Range(0, 1)) = 1

        // ============================================================
        // Normal
        // ============================================================
        // 脚印法线贴图。
        // 注意：这不会真的修改地面法线 Buffer，只是在这个贴花 pass 内做假的局部光照。
        [Normal] _DecalNormalTexture("Normal Texture", 2D) = "bump" {}

        // 法线强度。
        // 越大，脚印边缘和内部凹凸光照越明显。
        // 太大容易像塑料凸起，脚印建议从 0.5 ~ 1.5 之间试。
        _NormalStrength("Normal Strength", Range(0, 3)) = 1.0

        // ============================================================
        // Height / POM
        // ============================================================
        // 高度图。
        // 约定：
        // _HeightGround = 0.5 时，0.5 表示原始地面高度；
        // 小于 0.5 表示凹陷；
        // 大于 0.5 表示凸起。
        _DecalHeightTexture("Height Texture", 2D) = "gray" {}

        // 高度图里的“地面基准高度”。
        // 如果高度图背景是 0.5 灰，这里就设 0.5。
        _HeightGround("Height Ground Level", Range(0, 1)) = 0.5

        // 高度凹陷深度的对比增强。
        // Shader 内部会把低于 _HeightGround 的部分转换成 depth，
        // 再乘 _HeightContrast * 0.35。
        // 值越大，POM 凹陷越深，但过大可能整块饱和，丢失细节。
        _HeightContrast("Height Contrast", Range(0, 8)) = 3.0

        // 是否反转高度图。
        // 0：黑色更低，白色更高。
        // 1：白色更低，黑色更高。
        // 如果脚印看起来是凸起来的，可以先切到 1 试。
        _InvertHeight("Invert Height", Range(0, 1)) = 0.0

        // POM 视差强度。
        // 控制 UV 沿视线方向偏移的距离。
        // 绝对值越大，凹陷视觉越明显，但边缘越容易拉伸。
        // 如果凹凸方向反了，可以尝试负值。
        _ParallaxStrength("Parallax Strength", Range(-0.2, 0.2)) = 0.06

        // POM 最小步数。
        // 正视角时使用较少步数，节省性能。
        _POMMinSteps("POM Min Steps", Range(1, 32)) = 12

        // POM 最大步数。
        // 低角度斜看时使用较多步数，减少分层感。
        // 注意：步数越高，性能开销越大。
        _POMMaxSteps("POM Max Steps", Range(1, 96)) = 64

        // ============================================================
        // Lighting
        // ============================================================
        // 环境光保底强度。
        // 越大，脚印整体越亮，不容易黑死。
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.25

        // 主光漫反射强度。
        // 越大，法线贴图带来的明暗变化越明显。
        _DiffuseStrength("Diffuse Strength", Range(0, 2)) = 1.0

        // ============================================================
        // Alpha / Debug
        // ============================================================
        // Alpha 是否跟随 POM 后的 UV。
        // 0：Alpha 使用原始 decalUV，脚印轮廓稳定，但边缘视差弱。
        // 1：Alpha 使用 pomUV，视差更明显，但边缘可能锯齿或拉扯。
        _AlphaFromPOM("Alpha From POM 0 Stable 1 POM", Range(0, 1)) = 1

        // 调试模式。
        // 0：正常最终效果。
        // 1：显示 POM depth，越白表示凹陷越深。
        // 2：显示 POM UV offset，方便判断视差偏移是否生效。
        _DebugView("Debug View 0 Final 1 HeightDepth 2 Offset", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags
        {
            // 限定在 URP 管线中使用。
            "RenderPipeline" = "UniversalPipeline"

            // 透明队列，因为 decal 通过 Alpha 混合叠加到相机颜色上。
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ScreenSpaceDecalVolumeBox_POM_Merged"

            // 不写入深度。
            // Decal 只是覆盖已有场景表面，不是真正生成新几何。
            ZWrite Off

            // 总是通过深度测试。
            // 真正的投射范围由 fragment 中的深度重建 + decal box 判断决定。
            ZTest Always

            // 体积盒 decal 通常渲染盒子的背面。
            // 摄像机在盒子外看时，Cull Front 可以让体积盒内部表面被绘制。
            Cull Front

            // 标准 Alpha 混合。
            // src * alpha + dst * (1 - alpha)
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // URP 基础函数：坐标变换、屏幕参数、ComputeWorldSpacePosition 等。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 主光源 GetMainLight()。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // UnpackNormalScale()，用于解包 Unity normal map。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            // ============================================================
            // Textures
            // ============================================================
            TEXTURE2D(_DecalTexture);        SAMPLER(sampler_DecalTexture);
            TEXTURE2D(_DecalNormalTexture);  SAMPLER(sampler_DecalNormalTexture);
            TEXTURE2D(_DecalHeightTexture);  SAMPLER(sampler_DecalHeightTexture);

            // 相机深度图。
            // Screen Space Decal 的核心：用它反推当前屏幕像素对应的世界坐标。
            TEXTURE2D_X_FLOAT(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);

            // ============================================================
            // Parameters from C# / MaterialPropertyBlock
            // ============================================================

            // world position -> decal normalized local position 的矩阵。
            // 转换结果预期落在 -0.5 ~ 0.5 的盒子范围内。
            float4x4 _DecalWorldToLocal;

            float4 _DecalColor;
            float _UseBaseRGB;

            // x = opacity，整体透明度。
            // y = edgeFade，贴花盒子 XY 边缘淡出宽度。
            // z = cos(angleEnd)，角度淡出结束阈值。
            // w = cos(angleStart)，角度淡出开始阈值。
            float4 _DecalParams;

            // xy = tiling，zw = offset。
            float4 _DecalTilingOffset;

            // Decal backward world direction。
            // 用于和场景表面法线做 dot，避免贴到侧面 / 背面。
            float4 _DecalBackwardWS;

            // 距离淡出。
            // 当前只使用 x。
            float4 _DecalDistanceFade;

            // Decal 自身的世界空间 TBN。
            // Tangent   = decal local X，对应贴图 U 方向。
            // Bitangent = decal local Y，对应贴图 V 方向。
            // Normal    = decal 投射平面的表面法线方向。
            float4 _DecalTangentWS;
            float4 _DecalBitangentWS;
            float4 _DecalNormalWS;

            float _NormalStrength;

            float _HeightGround;
            float _HeightContrast;
            float _InvertHeight;

            float _ParallaxStrength;
            float _POMMinSteps;
            float _POMMaxSteps;

            float _AmbientStrength;
            float _DiffuseStrength;

            float _AlphaFromPOM;
            float _DebugView;

            struct Attributes
            {
                // 体积盒 mesh 的局部坐标。
                // 这个 mesh 本身没有 UV，UV 是 fragment 中通过 worldPos -> decalLocalPos 计算出来的。
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                // 裁剪空间 / 屏幕空间位置。
                // fragment 里会用 SV_POSITION.xy 算 screenUV。
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // 正常把体积盒顶点变换到裁剪空间。
                // 注意：这里只是在画 decal box，真正的贴花落点来自深度重建。
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            // 把当前 fragment 的屏幕像素坐标转换成 0~1 的 screenUV。
            float2 GetScreenUV(float4 positionCS)
            {
                return positionCS.xy / _ScaledScreenParams.xy;
            }

            // 从相机深度图采样 raw depth。
            float SampleRawDepth(float2 screenUV)
            {
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV).r;
            }

            // 通过 screenUV + rawDepth + inverse VP 矩阵重建世界坐标。
            float3 ReconstructWorldPosition(float2 screenUV)
            {
                float rawDepth = SampleRawDepth(screenUV);

                // 无效深度直接 discard。
                // Reversed Z 下远平面接近 0；普通 Z 下远平面接近 1。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001) discard;
                #else
                    if (rawDepth >= 0.999999) discard;
                #endif

                return ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
            }

            // 用深度重建出来的 worldPos 的屏幕导数估算场景表面法线。
            // 这个 normal 不是模型真实法线，但足够用于 angle fade。
            float3 ReconstructWorldNormalFromDepth(float3 worldPos)
            {
                float3 dx = ddx(worldPos);
                float3 dy = ddy(worldPos);

                float3 normalWS = normalize(cross(dy, dx));
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // 确保法线朝向摄像机一侧，避免 angle fade 方向错乱。
                if (dot(normalWS, viewDirWS) < 0.0)
                    normalWS = -normalWS;

                return normalWS;
            }

            // 判断 UV 是否超出 0~1。
            // Decal 不希望 Wrap 到另一边，所以出界直接返回透明或默认值。
            bool IsUVOutside01(float2 uv)
            {
                return uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
            }

            half4 SampleBaseSafe(float2 uv)
            {
                // Base 出界时返回透明黑，避免出现方形边框或重复采样。
                if (IsUVOutside01(uv))
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);

                return SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, uv);
            }

            half4 SampleNormalSafe(float2 uv)
            {
                // Normal 出界时返回平法线。
                // 0.5,0.5,1 是 tangent space flat normal。
                if (IsUVOutside01(uv))
                    return half4(0.5h, 0.5h, 1.0h, 1.0h);

                return SAMPLE_TEXTURE2D(_DecalNormalTexture, sampler_DecalNormalTexture, uv);
            }

            half SampleHeightRawSafe(float2 uv)
            {
                // Height 出界时返回地面基准高度，表示没有凹陷也没有凸起。
                if (IsUVOutside01(uv))
                    return _HeightGround;

                half h = SAMPLE_TEXTURE2D(_DecalHeightTexture, sampler_DecalHeightTexture, uv).r;

                // 根据 _InvertHeight 选择是否反转高度。
                h = lerp(h, 1.0h - h, _InvertHeight);
                return h;
            }

            // 把 height 值转换成 POM 使用的 depth 值。
            // depth = 0：没有凹陷。
            // depth = 1：最深。
            half SampleDepthForPOM(float2 uv)
            {
                half h = SampleHeightRawSafe(uv);

                // 这里整合 BaseHeightNormal_POM 的视差深度算法：
                // 原测试 shader 中使用的是：
                // depth = saturate((_HeightCenter - h) * _HeightContrast)
                //
                // 当前 decal shader 没有 _HeightCenter，使用已有的 _HeightGround 作为中心高度。
                // h == _HeightGround：没有凹陷，depth = 0。
                // h <  _HeightGround：低于地面，depth 增大。
                // h >  _HeightGround：高于地面，不作为凹陷处理，depth = 0。
                //
                // 相比之前的：
                // ((_HeightGround - h) / _HeightGround) * _HeightContrast * 0.35
                // 这个版本不会被 0.35 压弱，凹陷会明显更深。
                return saturate((_HeightGround - h) * _HeightContrast);
            }

            // ============================================================
            // Parallax Occlusion Mapping
            // ============================================================
            // 这一段来自 BaseHeightNormal_POM 的 POM 核心逻辑，
            // 但已经适配当前 Screen Space Decal shader：
            // 1. 使用当前 shader 的 _HeightGround / _DecalHeightTexture。
            // 2. 保留 uvOffset 输出，供 DebugView=2 显示偏移量。
            // 3. 保留当前 shader 的 96 最大步数上限，匹配 _POMMaxSteps Range(1,96)。
            //
            // uv：原始 decalUV。
            // viewDirTS：视线方向，已经转换到 decal tangent space。
            // uvOffset：输出最终 UV 相对原始 UV 的偏移量，用于 DebugView=2。
            float2 ParallaxOcclusionMapping(float2 uv, float3 viewDirTS, out float2 uvOffset)
            {
                // 默认没有偏移。
                uvOffset = 0.0;

                // 强度非常小时跳过 POM，直接返回原始 UV。
                // 这样可以避免不必要的循环开销。
                if (abs(_ParallaxStrength) < 0.00001)
                    return uv;

                viewDirTS = normalize(viewDirTS);

                // 防止低角度时除以接近 0 的 z，导致 UV 偏移爆炸。
                // viewDirTS.z 越小，视线越贴近表面。
                float viewZ = max(abs(viewDirTS.z), 0.08);

                // ndotv 越大，说明越正视表面；越小，说明越斜看。
                float ndotv = saturate(abs(viewDirTS.z));

                // 正视角使用较少步数，斜视角使用较多步数。
                int stepCount = (int)round(lerp(_POMMaxSteps, _POMMinSteps, ndotv));
                stepCount = clamp(stepCount, 1, 96);

                // 每一层代表的深度厚度。
                float layerDepth = 1.0 / stepCount;
                float currentLayerDepth = 0.0;

                // 视线方向在 UV 平面上的投影。
                float2 parallaxDir = viewDirTS.xy / viewZ;

                // 每一步 UV 偏移量。
                // 如果视差方向反了，优先把 _ParallaxStrength 调成负值测试。
                float2 deltaUV = parallaxDir * _ParallaxStrength / stepCount;

                float2 currentUV = uv;
                float2 previousUV = uv;

                half currentDepth = SampleDepthForPOM(currentUV);
                half previousDepth = currentDepth;

                float previousLayerDepth = 0.0;

                // 从表面开始沿视线方向逐层推进。
                // 当扫描层深度 currentLayerDepth >= 高度图深度 currentDepth 时，
                // 认为视线已经碰到高度层。
                [loop]
                for (int i = 0; i < 96; i++)
                {
                    if (i >= stepCount)
                        break;

                    if (currentLayerDepth >= currentDepth)
                        break;

                    previousUV = currentUV;
                    previousDepth = currentDepth;
                    previousLayerDepth = currentLayerDepth;

                    currentUV -= deltaUV;
                    currentLayerDepth += layerDepth;
                    currentDepth = SampleDepthForPOM(currentUV);
                }

                // 在命中前后的两个 UV 之间做一次线性 refinement，减少明显的层级感。
                float afterDepth = currentDepth - currentLayerDepth;
                float beforeDepth = previousDepth - previousLayerDepth;
                float denom = afterDepth - beforeDepth;

                float weight = 0.0;
                if (abs(denom) > 0.00001)
                    weight = saturate(afterDepth / denom);

                float2 finalUV = lerp(currentUV, previousUV, weight);

                // 输出偏移给 DebugView=2。
                uvOffset = finalUV - uv;

                return finalUV;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // ============================================================
                // 1. Screen UV + depth reconstruct
                // ============================================================

                float2 screenUV = GetScreenUV(input.positionCS);

                // 超出屏幕范围直接丢弃。
                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0)
                    discard;

                // 当前屏幕像素对应的场景表面世界坐标。
                float3 worldPos = ReconstructWorldPosition(screenUV);

                // ============================================================
                // 2. Decal box clipping
                // ============================================================

                // 把当前像素的 worldPos 转到 decal box 的归一化局部空间。
                float3 decalLocalPos = mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;
                float3 absLocal = abs(decalLocalPos);

                // 只保留落在 decal 体积盒内部的像素。
                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
                    discard;

                // ============================================================
                // 3. Decal UV
                // ============================================================

                // local xy 从 -0.5~0.5 转换为 0~1。
                float2 decalUV = decalLocalPos.xy + 0.5;

                // 应用每个 projector 的 tiling / offset。
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                // ============================================================
                // 4. Decal TBN + view direction
                // ============================================================

                half3 tangentWS = normalize(_DecalTangentWS.xyz);
                half3 bitangentWS = normalize(_DecalBitangentWS.xyz);
                half3 decalNormalWS = normalize(_DecalNormalWS.xyz);

                // 世界空间视线方向。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // 把视线方向投影到 decal 的 tangent space。
                // POM 必须在 tangent space 中计算，因为 UV 偏移发生在贴图平面上。
                float3 viewDirTS = float3(
                    dot(viewDirWS, tangentWS),
                    dot(viewDirWS, bitangentWS),
                    dot(viewDirWS, decalNormalWS)
                );

                // ============================================================
                // 5. POM
                // ============================================================

                float2 uvOffset;
                float2 pomUV = ParallaxOcclusionMapping(decalUV, viewDirTS, uvOffset);

                // POM UV：用于颜色和 normal，使凹陷产生视差。
                half4 basePOM = SampleBaseSafe(pomUV);

                // 原始 UV：用于稳定 Alpha 轮廓。
                half4 baseStable = SampleBaseSafe(decalUV);

                // ============================================================
                // 6. Debug
                // ============================================================

                // DebugView = 1：显示当前高度图转换后的 depth。
                // 白色越多，代表 POM 认为越深。
                if (_DebugView > 0.5 && _DebugView < 1.5)
                {
                    half d = SampleDepthForPOM(decalUV);
                    return half4(d, d, d, 1.0h);
                }

                // DebugView = 2：显示 UV offset。
                // 越亮代表 POM 偏移越大。
                if (_DebugView >= 1.5)
                {
                    float2 o = abs(uvOffset) * 80.0;
                    return half4(o.x, o.y, 0.0h, 1.0h);
                }

                // ============================================================
                // 7. Normal lighting
                // ============================================================

                // 使用 POM 后的 UV 采样法线，让法线细节和视差位置一致。
                half4 packedNormal = SampleNormalSafe(pomUV);
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);

                // tangent space normal -> world space normal。
                half3 bumpNormalWS = normalize(
                    normalTS.x * tangentWS +
                    normalTS.y * bitangentWS +
                    normalTS.z * decalNormalWS
                );

                // 只使用 URP 主光做一个轻量的假光照。
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(bumpNormalWS, normalize(mainLight.direction)));

                // 最终 lighting = 环境保底 + 主光漫反射。
                half lighting = _AmbientStrength + ndotl * _DiffuseStrength;
                lighting = saturate(lighting);

                // ============================================================
                // 8. Base color + alpha
                // ============================================================

                // _UseBaseRGB = 0：baseRGB 为白色，只看 DecalColor 和光照。
                // _UseBaseRGB = 1：使用贴图 RGB。
                half3 baseRGB = lerp(half3(1.0h, 1.0h, 1.0h), basePOM.rgb, _UseBaseRGB);

                // 增强脚印纹理对比。
                // 这一步会让泥土噪声和脚印细节更明显。
                baseRGB = saturate((baseRGB - 0.03h) * 1.8h);

                half4 color;

                // 根据高度 depth 做额外变暗。
                // 越深的地方越暗，更像泥地被踩下去。
                half depthShade = SampleDepthForPOM(pomUV);
                half heightDarken = lerp(1.0h, 0.62h, depthShade);

                color.rgb = baseRGB * _DecalColor.rgb * lighting * heightDarken;

                // Alpha 可以在“稳定轮廓”和“POM 轮廓”之间插值。
                // alphaStable：原始 UV，边缘稳定。
                // alphaPOM：POM UV，视差明显但边缘可能抖动。
                half alphaStable = baseStable.a;
                half alphaPOM = basePOM.a;
                color.a = lerp(alphaStable, alphaPOM, _AlphaFromPOM) * _DecalColor.a;

                // ============================================================
                // 9. Box Fade / Angle Fade / Distance Fade
                // ============================================================

                // 贴花盒子 XY 边缘淡出。
                // 防止 decal volume box 的边缘出现硬切线。
                float distToPlaneEdge = min(0.5 - absLocal.x, 0.5 - absLocal.y);
                float edgeFade = max(_DecalParams.y, 0.0001);
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                // 根据深度重建场景法线。
                float3 sceneNormalWS = ReconstructWorldNormalFromDepth(worldPos);

                // 根据接收表面方向过滤 decal。
                // facing 越大，说明这个表面越朝向 decal projector。
                float3 decalBackwardWS = normalize(_DecalBackwardWS.xyz);
                float facing = saturate(dot(sceneNormalWS, decalBackwardWS));

                // 使用 C# 传入的 cos(angleEnd/start) 做角度淡出。
                float angleFade = smoothstep(_DecalParams.z, _DecalParams.w, facing);

                // 总透明度叠乘：
                // 1. projector opacity
                // 2. 距离淡出
                // 3. 角度淡出
                // 4. 盒子边缘淡出
                color.a *= _DecalParams.x;
                color.a *= _DecalDistanceFade.x;
                color.a *= angleFade;
                color.a *= boxFade;

                return color;
            }

            ENDHLSL
        }
    }
}
