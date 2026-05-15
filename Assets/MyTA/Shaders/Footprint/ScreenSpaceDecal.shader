Shader "Custom/ScreenSpaceDecal_VolumeBox_POM"
{
    Properties
    {
        // ============================================================
        // Base / Decal Texture
        // ============================================================
        // 这张图一般应该是你的 GeneratedDecal。
        // RGB：脚印内部颜色，可以带一点泥土噪声。
        // Alpha：脚印形状透明度。
        // 注意：如果 Base RGB 很平，POM 偏移也不容易被看出来。
        _DecalTexture("Base / Decal Texture", 2D) = "white" {}

        // 最终颜色会乘上这个颜色。
        // 如果 _DecalTexture.rgb 很暗，再乘这个颜色会更暗。
        _DecalColor("Decal Color", Color) = (1,1,1,1)

        // ============================================================
        // Normal Texture
        // ============================================================
        // 这张图一般是 GeneratedNormal。
        // 它不会真正写入地面的 normal buffer，
        // 只是用于当前 decal 颜色的假光照计算。
        _DecalNormalTexture("Normal Texture", 2D) = "bump" {}

        // 法线强度。
        // 越大，脚印内部/边缘的明暗变化越明显。
        _NormalStrength("Normal Strength", Range(0, 2)) = 1.0

        // ============================================================
        // Height Texture
        // ============================================================
        // 这张图一般是 GeneratedHeight。
        // 约定：
        // _HeightGround = 0.5 时：
        // h = 0.5 表示原地面
        // h < 0.5 表示凹陷
        // h > 0.5 表示凸起
        _DecalHeightTexture("Height Texture", 2D) = "gray" {}

        // 地面基准高度。
        // 如果你的高度图外部是 0.5 灰色，这里就用 0.5。
        _HeightGround("Height Ground Level", Range(0, 1)) = 0.5

        // 高度对 POM depth 的放大倍率。
        // 当前代码里这个值会直接乘到 depth 上。
        // 如果 Height 图内部太黑，HeightContrast 又太大，depth 可能会被 saturate 成一整块 1。
        _HeightContrast("Height Contrast", Range(0, 8)) = 1.0

        // 是否反转高度。
        // 0 = 不反转
        // 1 = 反转
        // 不建议用中间值长期调试，最好只测试 0 或 1。
        _InvertHeight("Invert Height", Range(0, 1)) = 0.0

        // ============================================================
        // POM / Parallax
        // ============================================================
        // 视差强度。
        // 越大，UV 偏移越明显，但边缘更容易拉扯/锯齿。
        _ParallaxStrength("Parallax Strength", Range(-0.2, 0.2)) = 0.05

        // POM 最少步数。
        // 正视角时一般用较少步数。
        _POMMinSteps("POM Min Steps", Range(1, 32)) = 8

        // POM 最多步数。
        // 低角度斜看时会用较多步数。
        _POMMaxSteps("POM Max Steps", Range(1, 64)) = 32
    }

    SubShader
    {
        Tags
        {
            // 只在 URP 下使用。
            "RenderPipeline" = "UniversalPipeline"

            // 作为透明 decal 画在透明队列。
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ScreenSpaceDecalVolumeBox_POM"

            // Decal 不写深度。
            // 因为它只是覆盖到已有场景表面上，不是真的生成新几何。
            ZWrite Off

            // 一直通过深度测试。
            // 实际投射位置依赖 _CameraDepthTexture 重建出来的 worldPos。
            ZTest Always

            // Decal Volume Box 通常渲染背面，所以剔除正面。
            Cull Front

            // 普通 alpha 混合。
            // 最终颜色 = src * alpha + dst * (1 - alpha)
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            // URP 基础函数：矩阵、屏幕参数、世界坐标重建等。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 主光源 GetMainLight()。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // UnpackNormalScale() 用于解包 normal map。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            // ============================================================
            // Texture declarations
            // ============================================================

            // Base / Decal 贴图。
            // RGB 用于颜色，Alpha 用于脚印透明度。
            TEXTURE2D(_DecalTexture);
            SAMPLER(sampler_DecalTexture);

            // Normal 贴图。
            TEXTURE2D(_DecalNormalTexture);
            SAMPLER(sampler_DecalNormalTexture);

            // Height 贴图。
            TEXTURE2D(_DecalHeightTexture);
            SAMPLER(sampler_DecalHeightTexture);

            // 摄像机深度图。
            // Screen-space decal 需要用它从屏幕像素反推场景表面 worldPos。
            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            // ============================================================
            // Parameters from C# / material
            // ============================================================

            // Decal 世界到局部空间矩阵。
            // 用于把当前像素的 worldPos 转到 decal box local space。
            float4x4 _DecalWorldToLocal;

            // Decal 颜色。
            float4 _DecalColor;

            // _DecalParams:
            // x = opacity，整体透明度
            // y = edgeFade，decal box 边缘淡出宽度
            // z = cos(angleEnd)，角度淡出的结束阈值
            // w = cos(angleStart)，角度淡出的开始阈值
            float4 _DecalParams;

            // _DecalTilingOffset:
            // xy = tiling
            // zw = offset
            float4 _DecalTilingOffset;

            // Decal 投射背向方向。
            // 用于 angle fade，避免 decal 投到不该投的表面。
            float4 _DecalBackwardWS;

            // 距离淡出。
            // 当前一般只使用 x。
            float4 _DecalDistanceFade;

            // Decal 在世界空间下的 TBN。
            // Tangent   = decal local X
            // Bitangent = decal local Y
            // Normal    = decal 投射平面的法线方向
            float4 _DecalTangentWS;
            float4 _DecalBitangentWS;
            float4 _DecalNormalWS;

            // Normal 强度。
            float _NormalStrength;

            // Height / POM 参数。
            float _HeightGround;
            float _HeightContrast;
            float _InvertHeight;

            float _ParallaxStrength;
            float _POMMinSteps;
            float _POMMaxSteps;

            // ============================================================
            // Vertex input / output
            // ============================================================

            struct Attributes
            {
                // Decal Volume Box 的顶点位置。
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                // 裁剪空间坐标。
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // 这里只是在画 decal volume box。
                // 真正的投射位置不是来自这个 mesh UV，
                // 而是在 fragment 里通过深度图重建 worldPos 后计算出来。
                output.positionCS = TransformObjectToHClip(input.positionOS);

                return output;
            }

            // ============================================================
            // Screen UV / Depth reconstruction
            // ============================================================

            float2 GetScreenUV(float4 positionCS)
            {
                // SV_POSITION.xy 是屏幕像素坐标。
                // 除以 _ScaledScreenParams.xy 得到 0~1 的 screenUV。
                return positionCS.xy / _ScaledScreenParams.xy;
            }

            float SampleRawDepth(float2 screenUV)
            {
                // 从摄像机深度图读取 raw depth。
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture,sampler_CameraDepthTexture,screenUV).r;
            }

            float3 ReconstructWorldPosition(float2 screenUV)
            {
                float rawDepth = SampleRawDepth(screenUV);

                // 没有有效深度时丢弃。
                // UNITY_REVERSED_Z 下，远平面和无效深度接近 0。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                        discard;
                #else
                    if (rawDepth >= 0.999999)
                        discard;
                #endif

                // 用 screenUV + rawDepth + 逆 VP 矩阵还原世界坐标。
                return ComputeWorldSpacePosition(screenUV,rawDepth,UNITY_MATRIX_I_VP);
                    
                    
                    
                
            }

            float3 ReconstructWorldNormalFromDepth(float3 worldPos)
            {
                // 用屏幕空间导数近似重建场景表面法线。
                // 这个不是物体真实 normal，只是根据深度重建出来的近似 normal。
                float3 dx = ddx(worldPos);
                float3 dy = ddy(worldPos);

                float3 normalWS = normalize(cross(dy, dx));

                // 保证 normal 朝向摄像机这一侧，避免 angle fade 方向反掉。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                if (dot(normalWS, viewDirWS) < 0.0)
                    normalWS = -normalWS;

                return normalWS;
            }

            // ============================================================
            // Safe texture sampling
            // ============================================================

            bool IsUVOutside01(float2 uv)
            {
                // 判断 UV 是否出 0~1 范围。
                // Decal 贴图不希望 wrap 到另一边，所以出界时直接返回透明/默认值。
                return uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
            }

            half4 SampleBaseSafe(float2 uv)
            {
                // Base 贴图出界时返回 alpha=0。
                // 这样可以避免采到贴图边缘重复，出现方块。
                if (IsUVOutside01(uv))
                    return half4(0.0h, 0.0h, 0.0h, 0.0h);

                return SAMPLE_TEXTURE2D(_DecalTexture,sampler_DecalTexture,uv);
            }

            half4 SampleNormalSafe(float2 uv)
            {
                // Normal 出界时返回平法线。
                // RGB = 0.5, 0.5, 1 表示 tangent space flat normal。
                if (IsUVOutside01(uv))
                    return half4(0.5h, 0.5h, 1.0h, 1.0h);

                return SAMPLE_TEXTURE2D(_DecalNormalTexture,sampler_DecalNormalTexture,uv);

            }

            half SampleHeightRawSafe(float2 uv)
            {
                // Height 出界时返回地面高度。
                // 这样 POM 在 decal 外部不会产生额外偏移。
                if (IsUVOutside01(uv))
                    return _HeightGround;

                half h = SAMPLE_TEXTURE2D(_DecalHeightTexture,sampler_DecalHeightTexture,uv).r;

                // 可选反转 height。
                // _InvertHeight = 0：h
                // _InvertHeight = 1：1 - h
                h = lerp(h, 1.0h - h, _InvertHeight);

                return h;
            }

            half SampleDepthForPOM(float2 uv)
            {
                half h = SampleHeightRawSafe(uv);

                // 当前约定：
                // _HeightGround = 0.5
                // h < _HeightGround 表示凹陷。
                //
                // POM 需要的是 depth：
                // depth = 0 表示没有下陷
                // depth 越大表示越深。
                //
                // 注意：
                // 这里直接乘 _HeightContrast 后 saturate。
                // 如果 height 图内部很黑，HeightContrast 又高，
                // 很容易整块变成 depth=1，导致坑底噪声被压没。
                
                // 先把低于地面的部分归一化到 0~1。
                // h = _HeightGround 时 depth = 0。
                // h = 0 时 depth 接近 1。
                half depth = saturate((_HeightGround - h) / max(_HeightGround, 0.0001h));
                
                // 不要直接乘 _HeightContrast，否则很容易全白饱和。
                // 0.35 是为了保留坑底噪声细节。
                depth = saturate(depth * _HeightContrast * 0.35h);

                return depth;
            }

            // ============================================================
            // Parallax Occlusion Mapping
            // ============================================================
            //
            // 输入：
            // uv        = 原始 decalUV
            // viewDirTS = tangent space 视线方向
            //
            // 输出：
            // POM 后的 UV。
            //
            // 它不会改变真实几何，也不会改变深度。
            // 它只是在贴图采样前偏移 UV。
            //
            float2 ParallaxOcclusionMapping(float2 uv, float3 viewDirTS)
            {
                // 强度接近 0 时直接返回原始 UV。
                if (abs(_ParallaxStrength) < 0.00001)
                    return uv;

                viewDirTS = normalize(viewDirTS);

                // 防止低角度时除以很小的 z 导致 UV 偏移爆炸。
                float viewZ = max(abs(viewDirTS.z), 0.08);

                // ndotv 越接近 1，说明越正视表面；
                // 越接近 0，说明越低角度斜看。
                float ndotv = saturate(abs(viewDirTS.z));

                // 正视角用较少步数，斜视角用较多步数。
                int stepCount = (int)round(lerp(_POMMaxSteps, _POMMinSteps, ndotv));
                stepCount = clamp(stepCount, 1, 64);

                // 每一层的深度厚度。
                float layerDepth = 1.0 / stepCount;

                // 当前扫描到的层深度。
                float currentLayerDepth = 0.0;

                // 视线方向在 tangent plane 上的投影。
                float2 parallaxDir = viewDirTS.xy / viewZ;

                // 每一步要移动多少 UV。
                float2 deltaUV = parallaxDir * _ParallaxStrength / stepCount;

                float2 currentUV = uv;
                float2 previousUV = uv;

                // 当前采样点的高度深度。
                half currentDepth = SampleDepthForPOM(currentUV);
                half previousDepth = currentDepth;

                float previousLayerDepth = 0.0;

                // 从表面开始沿视线方向逐步往里走。
                // 当 currentLayerDepth >= currentDepth 时，认为射线碰到了高度层。
                [loop]
                for (int i = 0; i < 64; i++)
                {
                    if (i >= stepCount)
                        break;

                    if (currentLayerDepth >= currentDepth)
                        break;

                    previousUV = currentUV;
                    previousDepth = currentDepth;
                    previousLayerDepth = currentLayerDepth;

                    // 注意这里是 currentUV -= deltaUV。
                    // 如果方向看起来反了，可以调负的 _ParallaxStrength。
                    currentUV -= deltaUV;

                    currentLayerDepth += layerDepth;
                    currentDepth = SampleDepthForPOM(currentUV);
                }

                // 线性插值 refinement。
                // 让最终 UV 不只是停在某一层，减少明显分层。
                float afterDepth = currentDepth - currentLayerDepth;
                float beforeDepth = previousDepth - previousLayerDepth;
                float denom = afterDepth - beforeDepth;

                float weight = 0.0;

                if (abs(denom) > 0.00001)
                    weight = saturate(afterDepth / denom);

                float2 finalUV = lerp(currentUV, previousUV, weight);

                return finalUV;
            }

            // ============================================================
            // Fragment
            // ============================================================

            half4 Frag(Varyings input) : SV_Target
            {
                // ========================================================
                // 1. 当前 fragment 对应的屏幕 UV
                // ========================================================

                float2 screenUV = GetScreenUV(input.positionCS);

                if (screenUV.x < 0.0 || screenUV.x > 1.0 ||
                    screenUV.y < 0.0 || screenUV.y > 1.0)
                {
                    discard;
                }

                // ========================================================
                // 2. 根据深度图还原当前屏幕像素的世界坐标
                // ========================================================

                float3 worldPos = ReconstructWorldPosition(screenUV);

                // ========================================================
                // 3. 把 worldPos 转到 decal local space
                // ========================================================

                float3 decalLocalPos = mul(_DecalWorldToLocal,float4(worldPos, 1.0)).xyz;
                

                float3 absLocal = abs(decalLocalPos);

                // 如果像素不在 decal volume box 内，丢弃。
                // 这样 decal 只会影响盒子内部的场景表面。
                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
                    discard;

                // ========================================================
                // 4. 根据 decal local XY 生成 decalUV
                // ========================================================

                // local xy 范围大致是 -0.5 ~ 0.5。
                // +0.5 后变成 0~1。
                float2 decalUV = decalLocalPos.xy + 0.5;

                // 应用 tiling / offset。
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                // ========================================================
                // 5. 准备 Decal TBN
                // ========================================================

                half3 tangentWS = normalize(_DecalTangentWS.xyz);
                half3 bitangentWS = normalize(_DecalBitangentWS.xyz);
                half3 decalNormalWS = normalize(_DecalNormalWS.xyz);

                // 世界空间视线方向。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // 把视线方向转换到 decal tangent space。
                // POM 必须在 tangent space 中计算。
                float3 viewDirTS = float3(
                    dot(viewDirWS, tangentWS),
                    dot(viewDirWS, bitangentWS),
                    dot(viewDirWS, decalNormalWS)
                );

                // ========================================================
                // 6. POM：Height 只在这里影响 UV
                // ========================================================

                float2 pomUV = ParallaxOcclusionMapping(decalUV, viewDirTS);

                // ========================================================
                // 7. 用 POM 后的 UV 采样 Base / Normal
                // ========================================================

                // Base:
                // RGB 参与最终颜色。
                // Alpha 参与最终透明度。
                half4 baseTex = SampleBaseSafe(pomUV);

                // Normal:
                // 用 POM 后的 UV 采样，这样 normal 细节也会跟着视差移动。
                half4 packedNormal = SampleNormalSafe(pomUV);

                // 解包 tangent space normal，并应用强度。
                half3 normalTS = UnpackNormalScale(packedNormal, _NormalStrength);

                // 转到 world space。
                half3 bumpNormalWS = normalize(normalTS.x * tangentWS +normalTS.y * bitangentWS +normalTS.z * decalNormalWS);
                
                // ========================================================
                // 8. 简单主光照
                // ========================================================

                Light mainLight = GetMainLight();

                // URP 主光方向。
                half3 lightDirWS = normalize(mainLight.direction);

                // Lambert 漫反射。
                half ndotl = saturate(dot(bumpNormalWS, lightDirWS));

                // 最简单的 normal 光照。
                // 0.45 是保底亮度，避免完全黑。
                // 0.55 是主光贡献。
                half lighting = 0.45h + ndotl * 0.55h;

                // ========================================================
                // 9. 最终颜色
                // ========================================================

                half4 color;

                // 使用 Base RGB。
                // 这一步很重要：
                // 如果不乘 baseTex.rgb，你给 GeneratedDecal 加的 RGB 噪声就完全看不见。
                half3 albedo = baseTex.rgb * _DecalColor.rgb;

                // 临时增强贴图细节对比。
                // 这不是物理正确做法，只是为了让噪声和 POM 更容易观察。
                // 如果最终觉得太脏，可以删掉或改回 albedo = saturate(albedo);
                albedo = pow(saturate(albedo), 0.8h);

                color.rgb = albedo * lighting;

                // Alpha 使用 Base alpha，再乘 DecalColor alpha。
                // 注意这里 alpha 也来自 pomUV，因为 baseTex 是 SampleBaseSafe(pomUV)。
                // 这样视差最明显，但边缘更容易锯齿/拉扯。
                color.a = baseTex.a * _DecalColor.a;

                // ========================================================
                // 10. Decal Box 边缘淡出
                // ========================================================

                float distToPlaneEdge = min(
                    0.5 - absLocal.x,
                    0.5 - absLocal.y
                );

                float edgeFade = max(_DecalParams.y, 0.0001);
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                // ========================================================
                // 11. Angle Fade
                // ========================================================

                // 根据深度重建场景法线。
                float3 sceneNormalWS = ReconstructWorldNormalFromDepth(worldPos);

                // Decal 背向方向。
                float3 decalBackwardWS = normalize(_DecalBackwardWS.xyz);

                // facing 越大，说明表面越适合被这个 decal 投射。
                float facing = saturate(dot(sceneNormalWS, decalBackwardWS));

                // angleFade 控制 decal 不要投到侧面/背面。
                float angleFade = smoothstep(
                    _DecalParams.z,
                    _DecalParams.w,
                    facing
                );

                // ========================================================
                // 12. Final Alpha
                // ========================================================

                // _DecalParams.x = opacity
                color.a *= _DecalParams.x;

                // 距离淡出。
                color.a *= _DecalDistanceFade.x;

                // 角度淡出。
                color.a *= angleFade;

                // decal box 边缘淡出。
                color.a *= boxFade;

                return color;
            }

            ENDHLSL
        }
    }
}