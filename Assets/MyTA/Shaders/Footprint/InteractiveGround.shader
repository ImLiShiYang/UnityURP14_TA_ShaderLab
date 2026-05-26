Shader "Footprints/InteractiveGround"
{
    Properties
    {
        // ============================================================
        // Base
        // 地面基础颜色部分。
        // _BaseMap   : 地面本身的贴图。
        // _BaseColor : 额外乘上的颜色，用来整体调色。
        // _Brightness: 整体亮度倍率。
        // ============================================================
        [Header(Base)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0, 3)) = 1

        // ============================================================
        // Shadow
        // 控制主光源阴影对地面的影响。
        // _ShadowStrength:
        //      0 = 不使用阴影衰减，阴影区也接近正常亮度。
        //      1 = 完整使用 Unity 主光源阴影。
        //
        // _MinShadow:
        //      阴影最暗下限，避免阴影区域完全黑死。
        //      例如 0.15 表示阴影最暗也保留 15% 亮度。
        // ============================================================
        [Header(Shadow)]
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.9
        _MinShadow ("Min Shadow Light", Range(0,1)) = 0.15

        // ============================================================
        // Footprint RT Signed Height
        //
        // _FootstepTex:
        //      脚印系统最终累积出来的 RT。
        //      当前协议：
        //          RGB = 编码后的法线，通常默认背景是 (0.5, 0.5, 1)
        //          A   = Signed Height，高度信息
        //
        //      Alpha 的语义非常重要：
        //          A = 0.5 : 原始地面，没有下陷也没有隆起
        //          A < 0.5 : 下陷区域，越小越深
        //          A > 0.5 : 隆起区域，也就是泥边，越大越凸
        //
        // _FootstepRect:
        //      当前 Footstep RT 在世界空间 XZ 平面的覆盖范围。
        //      格式：
        //          x = minWorldX
        //          y = minWorldZ
        //          z = maxWorldX
        //          w = maxWorldZ
        //
        // _EnableFootstep:
        //      是否启用脚印效果。
        //      0 = 关闭脚印影响
        //      1 = 开启脚印影响
        // ============================================================
        [Header(Footprint RT Signed Height)]
        _FootstepTex ("Footstep RT RGB Normal A Signed Height", 2D) = "gray" {}
        _FootstepRect ("Footstep Rect", Vector) = (0,0,1,1)
        _EnableFootstep ("Enable Footstep", Float) = 0

        // ============================================================
        // Footprint Mask
        //
        // _FootprintStrength:
        //      脚印整体强度。
        //      会同时影响：
        //          1. 下陷 AO 强度
        //          2. 泥边 rim 强度
        //          3. 法线混合影响范围
        //
        // _FootprintSignedDeadZone:
        //      Signed Height 死区。
        //      因为 RT 经过采样、累积、过滤后，背景 0.5 附近可能有极小误差。
        //      例如：
        //          0.501、0.499 这种不应该被认为是有效脚印。
        //
        //      dead zone 的作用：
        //          abs(signedHeight) 小于这个值时，直接当作 0。
        //
        //      如果地面出现大面积轻微变色，可以适当调大。
        //      如果很淡的脚印细节丢失，可以适当调小。
        // ============================================================
        [Header(Footprint Mask)]
        _FootprintStrength ("Footprint Strength", Range(0,2)) = 1
        _FootprintSignedDeadZone ("Signed Height Dead Zone", Range(0,0.2)) = 0.005

        // ============================================================
        // Footprint Normal
        //
        // _FootprintNormalStrength:
        //      脚印 RT 中 RGB 法线对最终地面法线的影响强度。
        //      越大，脚印边缘和内部凹凸感越明显。
        //
        // _FlipFootprintNormalY:
        //      用来修正法线贴图 Y 方向。
        //      如果脚印的凹凸方向感觉反了，或者左右光照不对，可以切换这个。
        // ============================================================
        [Header(Footprint Normal)]
        _FootprintNormalStrength ("Footprint Normal Strength", Range(0,3)) = 1.5
        [Toggle] _FlipFootprintNormalY ("Flip Footprint Normal Y", Float) = 0

        // ============================================================
        // Footprint AO
        //
        // _FootprintAOStrength:
        //      下陷区域的压暗强度。
        //      只应该主要影响 A < 0.5 的脚印内部凹陷区域。
        //
        // _FootprintAOSmoothMin / _FootprintAOSmoothMax:
        //      用 smoothstep 把 raw mask 变成更柔和的 mask。
        //
        //      这两个值会影响：
        //          1. 下陷区域 depressionMask
        //          2. 泥边区域 rimMask
        //          3. 法线影响 influenceMask
        //
        //      简单理解：
        //          SmoothMin 越大，弱脚印越容易被过滤掉。
        //          SmoothMax 越小，mask 越容易达到 1，效果更硬更明显。
        //          SmoothMax 越大，过渡更柔和，但效果可能变淡。
        //
        // _FootprintSpecOcclusion:
        //      下陷区域高光遮蔽。
        //      因为凹进去的泥坑不应该和原地面一样亮、一样有高光。
        // ============================================================
        [Header(Footprint AO)]
        _FootprintAOStrength ("Footprint AO Strength", Range(0,1)) = 0.25
        _FootprintAOSmoothMin ("Footprint AO Smooth Min", Range(0,1)) = 0.02
        _FootprintAOSmoothMax ("Footprint AO Smooth Max", Range(0,1)) = 0.45
        _FootprintSpecOcclusion ("Footprint Spec Occlusion", Range(0,1)) = 0.35

        // ============================================================
        // Footprint Rim
        //
        // _FootprintRimLightStrength:
        //      泥边提亮强度。
        //      注意：这个只是颜色层面的轻微提亮。
        //      真正的立体感主要还是来自法线。
        //
        //      如果泥边看不清，优先检查：
        //          1. AccumA 的 Alpha 里 A > 0.5 的白色泥边是否存在
        //          2. rimMask 是否明显
        //          3. 这里的强度是否太低
        // ============================================================
        [Header(Footprint Rim)]
        _FootprintRimLightStrength ("Footprint Rim Light Strength", Range(0,0.5)) = 0.08

        // ============================================================
        // Blinn Phong
        //
        // 这里是一个简单的 Blinn-Phong 高光模型。
        // 用来让湿泥、泥边、凹陷边缘有一点反光变化。
        //
        // _SpecColor:
        //      高光颜色。
        //
        // _SpecStrength:
        //      高光强度。
        //
        // _SpecPower:
        //      高光锐度。
        //      数值越大，高光越集中、越小。
        // ============================================================
        [Header(Blinn Phong)]
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecStrength ("Spec Strength", Range(0,2)) = 0.35
        _SpecPower ("Spec Power", Range(1,256)) = 48
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardBlinnPhong"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            // URP 主光源阴影关键字。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // 地面基础贴图。
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 脚印累积 RT。
            // RGB = 法线
            // A   = signed height
            TEXTURE2D(_FootstepTex);
            SAMPLER(sampler_FootstepTex);

            // UnityPerMaterial CBUFFER。
            // 这里的字段需要和 Properties 对应。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Brightness;

                half _ShadowStrength;
                half _MinShadow;

                float4 _FootstepRect;
                half _EnableFootstep;

                half _FootprintStrength;
                half _FootprintSignedDeadZone;

                half _FootprintNormalStrength;
                half _FlipFootprintNormalY;

                half _FootprintAOStrength;
                half _FootprintAOSmoothMin;
                half _FootprintAOSmoothMax;
                half _FootprintSpecOcclusion;

                half _FootprintRimLightStrength;

                half4 _SpecColor;
                half _SpecStrength;
                half _SpecPower;
            CBUFFER_END

            // 顶点输入。
            // positionOS : 物体空间顶点位置。
            // normalOS   : 物体空间法线。
            // uv         : 模型 UV。
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            // 顶点到片元传递的数据。
            // positionHCS : 裁剪空间位置，用于最终光栅化。
            // positionWS  : 世界空间位置，用于计算脚印 RT UV、阴影等。
            // normalWS    : 世界空间法线。
            // uv          : 地面基础贴图 UV。
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            // ============================================================
            // DecodeNormalRGB
            //
            // 把 RT 里 0~1 编码的 RGB 法线还原到 -1~1。
            //
            // 常见法线贴图编码：
            //      0.5, 0.5, 1.0 表示默认朝上法线
            //
            // 解码后：
            //      0.5 * 2 - 1 = 0
            //      1.0 * 2 - 1 = 1
            //      得到 (0, 0, 1)
            // ============================================================
            half3 DecodeNormalRGB(half3 normalRGB)
            {
                return normalize(normalRGB * 2.0h - 1.0h);
            }

            // ============================================================
            // SafeSmoothStep
            //
            // smoothstep(min, max, x) 的安全版。
            //
            // 普通 smoothstep 要求 max > min。
            // 如果材质参数里不小心把 max 调得小于或等于 min，
            // 可能导致结果异常。
            //
            // 这里强制保证：
            //      maxVal >= minVal + 0.0001
            //
            // 用途：
            //      把 raw mask 变成柔和 mask。
            //      raw mask 通常是 0~1 的硬数据，
            //      smoothstep 后边缘会更自然。
            // ============================================================
            half SafeSmoothStep(half minVal, half maxVal, half x)
            {
                maxVal = max(maxVal, minVal + 0.0001h);
                return smoothstep(minVal, maxVal, x);
            }

            // ============================================================
            // ApplySignedDeadZone
            //
            // 对 signed height 做死区处理。
            //
            // 输入 signedValue 的范围大致是：
            //      -1 : 最深下陷
            //       0 : 原始地面
            //      +1 : 最高泥边
            //
            // 但是 RT 采样后，原本应该是 0 的地方可能会变成：
            //      +0.003
            //      -0.002
            //
            // 这些轻微误差不应该产生脚印效果，所以需要 deadZone。
            //
            // 处理逻辑：
            //      1. 取绝对值 absValue。
            //      2. 如果 absValue <= deadZone，直接返回 0。
            //      3. 如果超过 deadZone，把剩余部分重新映射到 0~1。
            //      4. 恢复原来的正负号。
            //
            // 这样可以避免脚印区域外出现轻微变色、轻微法线扰动。
            // ============================================================
            half ApplySignedDeadZone(half signedValue, half deadZone)
            {
                half absValue = abs(signedValue);

                if (absValue <= deadZone)
                    return 0.0h;

                half remapped = (absValue - deadZone) / max(0.0001h, 1.0h - deadZone);
                return sign(signedValue) * saturate(remapped);
            }

            // ============================================================
            // WorldXZToFootUV
            //
            // 把地面上某个世界坐标点，转换成 Footstep RT 的 UV。
            //
            // _FootstepRect 表示 RT 覆盖的世界区域：
            //      x = minWorldX
            //      y = minWorldZ
            //      z = maxWorldX
            //      w = maxWorldZ
            //
            // 例如：
            //      positionWS.x 等于 minWorldX 时，footUV.x = 0
            //      positionWS.x 等于 maxWorldX 时，footUV.x = 1
            //
            // 同理：
            //      positionWS.z 映射到 footUV.y
            //
            // 注意：
            //      这个脚印系统是俯视 XZ 平面投影，
            //      所以不用世界 Y。
            // ============================================================
            float2 WorldXZToFootUV(float3 positionWS)
            {
                float2 footUV;
                footUV.x = (positionWS.x - _FootstepRect.x) / (_FootstepRect.z - _FootstepRect.x);
                footUV.y = (positionWS.z - _FootstepRect.y) / (_FootstepRect.w - _FootstepRect.y);
                return footUV;
            }

            // ============================================================
            // FootUVInside
            //
            // 判断当前像素是否在 Footstep RT 的有效 UV 范围内。
            //
            // 返回值：
            //      1 = uv 在 0~1 内
            //      0 = uv 超出 RT 范围
            //
            // 为什么需要这个？
            //      因为地面可能比 Footstep RT 覆盖范围更大。
            //      超出范围的地方不应该采样脚印 RT 产生错误影响。
            //
            // step(a, b) 的含义：
            //      b >= a 返回 1
            //      b <  a 返回 0
            // ============================================================
            half FootUVInside(float2 uv)
            {
                return
                    step(0.0, uv.x) *
                    step(0.0, uv.y) *
                    step(uv.x, 1.0) *
                    step(uv.y, 1.0);
            }

            // ============================================================
            // NormalizeSafeCustom
            //
            // 安全归一化。
            //
            // 普通 normalize 如果输入接近 0 向量，可能会出数值问题。
            // 这里使用：
            //      rsqrt(max(dot(v, v), 1e-6))
            //
            // 等价于：
            //      v / max(length(v), 很小的数)
            //
            // 用在构造切线空间时更稳。
            // ============================================================
            float3 NormalizeSafeCustom(float3 v)
            {
                return v * rsqrt(max(dot(v, v), 1e-6));
            }

            // ============================================================
            // FootprintNormalToWorld
            //
            // 把脚印 RT 里的局部法线转换到世界空间。
            //
            // footprintNormal:
            //      从 _FootstepTex.rgb 解码出来的法线。
            //      它的含义接近：
            //          x = 沿世界 X 方向的扰动
            //          y = 沿世界 Z 方向的扰动
            //          z = 沿地面原始法线方向的强度
            //
            // baseNormalWS:
            //      地面原始世界法线。
            //
            // 为什么不用模型 Tangent？
            //      这个脚印是世界 XZ 投影出来的，
            //      不是普通模型 UV 切线空间法线。
            //      所以这里直接用世界 X / 世界 Z 在地面法线平面上投影，
            //      构造一个适合脚印 RT 的局部坐标系。
            //
            // 处理过程：
            //      1. N = 地面原始法线。
            //      2. worldX 投影到 N 的切平面，得到 tangentX。
            //      3. worldZ 投影到 N 的切平面，得到 tangentZ。
            //      4. footprintNormal.x 控制 tangentX 方向扰动。
            //      5. footprintNormal.y 控制 tangentZ 方向扰动。
            //      6. footprintNormal.z 控制沿 N 的强度。
            // ============================================================
            half3 FootprintNormalToWorld(half3 footprintNormal, half3 baseNormalWS)
            {
                float3 N = NormalizeSafeCustom(baseNormalWS);

                // 世界 X / Z 方向。
                // 因为脚印 RT 是俯视 XZ 平面，所以使用这两个方向作为基础。
                float3 worldX = float3(1.0, 0.0, 0.0);
                float3 worldZ = float3(0.0, 0.0, 1.0);

                // 把 worldX / worldZ 投影到地面切平面上。
                // 这样即使地面有倾斜，脚印法线也能跟随地面方向。
                float3 tangentX = worldX - N * dot(worldX, N);
                float3 tangentZ = worldZ - N * dot(worldZ, N);

                tangentX = NormalizeSafeCustom(tangentX);
                tangentZ = NormalizeSafeCustom(tangentZ);

                // 用脚印法线的 x/y/z 分量重新组合世界空间法线。
                float3 nWS =
                    tangentX * footprintNormal.x +
                    tangentZ * footprintNormal.y +
                    N        * footprintNormal.z;

                return normalize((half3)nWS);
            }

            // ============================================================
            // Vertex Shader
            //
            // 主要工作：
            //      1. 计算裁剪空间位置 positionHCS。
            //      2. 传递世界空间位置 positionWS。
            //      3. 传递世界空间法线 normalWS。
            //      4. 计算地面 BaseMap UV。
            // ============================================================
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                return OUT;
            }

            // ============================================================
            // Fragment Shader
            //
            // 总流程：
            //      1. 采样地面基础颜色。
            //      2. 根据世界坐标采样 Footstep RT。
            //      3. 从 Footstep RT Alpha 解出 signed height。
            //      4. 拆分出：
            //          depressionMask = 下陷区域
            //          rimMask        = 泥边隆起区域
            //          influenceMask  = 总影响区域
            //      5. 根据 Footstep RT RGB 混合法线。
            //      6. 下陷区域压暗 AO。
            //      7. 泥边区域轻微提亮。
            //      8. 计算主光源、阴影、环境光。
            //      9. 叠加 Blinn-Phong 高光。
            // ============================================================
            half4 Frag(Varyings IN) : SV_Target
            {
                // =====================================================
                // 1. Base
                // =====================================================

                // 采样地面基础贴图。
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                // 得到最终地面基础颜色。
                // 注意这里还没有脚印影响，只是普通地面颜色。
                half3 albedo = baseSample.rgb * _BaseColor.rgb * _Brightness;

                // 地面原始世界法线。
                half3 baseNormalWS = normalize(IN.normalWS);

                // 最终参与光照的法线。
                // 后面如果脚印生效，会把它向脚印法线方向混合。
                half3 finalNormalWS = baseNormalWS;

                // =====================================================
                // 2. Footprint RT Signed Height
                // =====================================================

                // 把当前像素的世界坐标转换成脚印 RT 的 UV。
                float2 footUV = WorldXZToFootUV(IN.positionWS);

                // 判断当前像素是否在脚印 RT 覆盖范围内。
                // 超出范围时 inside = 0，后面脚印效果会被关掉。
                half inside = FootUVInside(footUV);

                // 采样脚印 RT。
                // foot.rgb = 脚印法线
                // foot.a   = signed height 编码值
                half4 foot = SAMPLE_TEXTURE2D(_FootstepTex, sampler_FootstepTex, footUV);

                // -----------------------------------------------------
                // Alpha Signed Height 解码
                //
                // RT alpha 原始协议：
                //      A = 0.5 表示原始地面
                //      A < 0.5 表示下陷
                //      A > 0.5 表示泥边隆起
                //
                // 这里把它转换成 signedFoot：
                //      A = 0.0 -> signedFoot = -1
                //      A = 0.5 -> signedFoot =  0
                //      A = 1.0 -> signedFoot = +1
                //
                // signedFoot 的含义：
                //      signedFoot < 0 : 脚印内部凹陷
                //      signedFoot = 0 : 无脚印
                //      signedFoot > 0 : 泥边隆起
                // -----------------------------------------------------
                half signedFoot = (foot.a - 0.5h) * 2.0h;

                // 去掉 0 附近的小误差，避免没有脚印的地方也产生细微影响。
                signedFoot = ApplySignedDeadZone(signedFoot, _FootprintSignedDeadZone);

                // 如果当前像素不在 Footstep RT 范围内，或者脚印被关闭，
                // signedFoot 会被乘成 0。
                signedFoot *= inside * _EnableFootstep;

                // -----------------------------------------------------
                // 拆分 signedFoot
                // -----------------------------------------------------

                // 下陷原始强度。
                // signedFoot 是负数时表示下陷，所以取 -signedFoot。
                //
                // 例：
                //      signedFoot = -0.8
                //      depressionRaw = 0.8
                //
                // 如果 signedFoot 是正数，也就是泥边，depressionRaw 会变成 0。
                half depressionRaw = saturate(-signedFoot);

                // 泥边原始强度。
                // signedFoot 是正数时表示隆起泥边。
                //
                // 例：
                //      signedFoot = 0.6
                //      rimRaw = 0.6
                //
                // 如果 signedFoot 是负数，也就是下陷，rimRaw 会变成 0。
                half rimRaw = saturate(signedFoot);

                // 总影响区域。
                // 不关心是下陷还是隆起，只要 signedFoot 不是 0，就认为有脚印影响。
                //
                // 用途：
                //      主要给法线混合使用。
                //      因为下陷区域和泥边区域都应该影响法线。
                half influenceRaw = saturate(abs(signedFoot));

                // -----------------------------------------------------
                // influenceMask
                //
                // 从 influenceRaw 得到柔和后的总影响 mask。
                // 用来控制脚印法线混合强度。
                // -----------------------------------------------------
                half influenceMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    influenceRaw
                );

                influenceMask = saturate(influenceMask * _FootprintStrength);

                // -----------------------------------------------------
                // depressionMask
                //
                // 从 depressionRaw 得到柔和后的下陷 mask。
                //
                // 用途：
                //      1. AO 压暗
                //      2. 高光遮蔽
                //
                // 注意：
                //      它只应该影响脚印凹进去的部分，
                //      不应该影响泥边。
                // -----------------------------------------------------
                half depressionMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    depressionRaw
                );

                depressionMask = saturate(depressionMask * _FootprintStrength);

                // -----------------------------------------------------
                // rimMask
                //
                // 从 rimRaw 得到柔和后的泥边 mask。
                //
                // 用途：
                //      1. 泥边轻微提亮
                //      2. 之后如果要加湿泥边颜色，也可以用它
                //
                // 如果你觉得泥边不明显，可以优先 debug 这个值：
                //      return half4(rimMask, depressionMask, 0, 1);
                //
                // 显示结果：
                //      红色 = 泥边
                //      绿色 = 下陷
                //      黑色 = 没有脚印
                // -----------------------------------------------------
                half rimMask = SafeSmoothStep(
                    _FootprintAOSmoothMin,
                    _FootprintAOSmoothMax,
                    rimRaw
                );

                rimMask = saturate(rimMask * _FootprintStrength);

                // return half4(rimMask, depressionMask, influenceMask, 1);
                
                
                // =====================================================
                // 3. Footprint Normal Blend
                // =====================================================

                // 解码脚印 RT 的 RGB 法线。
                // 背景应该接近 (0.5, 0.5, 1)，解码后是 (0, 0, 1)。
                half3 footprintNormal = DecodeNormalRGB(foot.rgb);

                // 某些法线贴图或渲染流程里 Y 方向可能相反。
                // 如果脚印边缘光照方向看起来反了，就打开这个开关。
                if (_FlipFootprintNormalY > 0.5h)
                {
                    footprintNormal.y = -footprintNormal.y;
                }
                
                footprintNormal.xy *= _FootprintNormalStrength;
                footprintNormal = normalize(footprintNormal);

                // 把脚印局部法线转换到世界空间。
                // 这样后面可以直接和地面世界法线混合，并参与主光照计算。
                half3 footprintNormalWS = FootprintNormalToWorld(
                    footprintNormal,
                    baseNormalWS
                );

                // 只要在 Footstep RT 范围内，并且脚印开启，就直接使用脚印 RT 的法线。
                // 背景法线是 (0,0,1)，转换后基本等于 baseNormalWS，所以不会明显改变地面。
                half useFootprintNormal = inside * _EnableFootstep;

                // 把原始地面法线向脚印法线方向混合。
                //
                // normalBlend = 0:
                //      完全使用原地面法线。
                //
                // normalBlend = 1:
                //      完全使用脚印法线。
                finalNormalWS = normalize(lerp(finalNormalWS,footprintNormalWS,useFootprintNormal ));
                    
                    
                    
               

                // =====================================================
                // 4. AO / Rim Color Blend
                // =====================================================

                // -----------------------------------------------------
                // 下陷 AO
                //
                // 只压暗 depressionMask，也就是 A < 0.5 的凹陷区域。
                //
                // 不直接使用 influenceMask 的原因：
                //      influenceMask 同时包含下陷和泥边。
                //      如果用 influenceMask，泥边也会被压暗，
                //      泥边会更不明显。
                // -----------------------------------------------------
                half footprintAO = 1.0h - depressionMask * _FootprintAOStrength;
                albedo *= footprintAO;

                // -----------------------------------------------------
                // 泥边提亮
                //
                // rimMask 来自 A > 0.5 的区域，也就是隆起泥边。
                //
                // 这里的做法是：
                //      把 albedo 向 albedo * 1.25 插值。
                //
                // 也就是说泥边最多比原颜色亮 25%。
                //
                // 最终提亮强度还会乘：
                //      rimMask * _FootprintRimLightStrength
                //
                // 如果当前 _FootprintRimLightStrength = 0.08，
                // 实际提亮仍然会比较弱。
                //
                // 如果你只是为了确认泥边数据是否存在，可以临时改强：
                //      albedo = lerp(albedo, albedo * 1.6h, rimMask);
                //
                // 但正式效果不建议太白，否则会像描边贴图。
                // -----------------------------------------------------
                albedo = lerp(
                    albedo,
                    albedo * 1.25h,
                    rimMask * _FootprintRimLightStrength
                );

                // =====================================================
                // 5. Lighting
                // =====================================================

                // 计算当前像素的阴影坐标。
                // URP 主光源阴影采样需要这个。
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);

                // 获取主光源，包括：
                //      direction
                //      color
                //      distanceAttenuation
                //      shadowAttenuation
                Light mainLight = GetMainLight(shadowCoord);

                // 主光方向。
                half3 lightDirWS = normalize(mainLight.direction);

                // 视线方向，用于后面的 Blinn-Phong 高光。
                half3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);

                // Lambert 漫反射项。
                // 使用已经混合过脚印法线的 finalNormalWS。
                //
                // 所以脚印的凹凸会影响受光方向，
                // 这也是泥边和凹陷能产生立体感的关键。
                half ndotl = saturate(dot(finalNormalWS, lightDirWS));

                // Unity 主光源阴影衰减。
                half shadowAtten = mainLight.shadowAttenuation;

                // 根据 _ShadowStrength 控制阴影强度。
                //
                // _ShadowStrength = 0:
                //      shadowAtten 最终接近 1，阴影基本不影响。
                //
                // _ShadowStrength = 1:
                //      使用真实 shadowAtten，但不会低于 _MinShadow。
                shadowAtten = lerp(1.0h, max(_MinShadow, shadowAtten), _ShadowStrength);

                // 总光照衰减。
                half lightAtten = mainLight.distanceAttenuation * shadowAtten;

                // 环境光。
                // SampleSH 会根据法线从球谐环境光中取值。
                // 因为这里传的是 finalNormalWS，所以脚印法线也会影响环境光。
                half3 ambient = SampleSH(finalNormalWS);

                // 主光漫反射。
                half3 direct = mainLight.color * ndotl * lightAtten;

                // 基础光照颜色。
                half3 color = albedo * (ambient + direct);

                // =====================================================
                // 6. Blinn Phong Specular
                // =====================================================

                // Blinn-Phong 的 half vector。
                half3 halfDir = normalize(lightDirWS + viewDirWS);

                // 法线和 half vector 的夹角。
                half ndoth = saturate(dot(finalNormalWS, halfDir));

                // 高光项。
                //
                // _SpecPower 越大，高光越尖锐。
                // _SpecStrength 控制整体强度。
                half specTerm = pow(ndoth, _SpecPower);
                specTerm *= _SpecStrength;

                // 背光面不应该有高光。
                // ndotl 太低时直接把高光关掉。
                specTerm *= step(0.001h, ndotl);

                // 高光也受主光阴影影响。
                specTerm *= lightAtten;

                // 下陷处高光稍微弱一点。
                //
                // 只用 depressionMask，不用 rimMask。
                // 原因：
                //      凹进去的脚印内部应该更暗、更少高光。
                //      泥边是隆起区域，不应该被同样压掉高光。
                specTerm *= 1.0h - depressionMask * _FootprintSpecOcclusion;

                // 把高光叠加到最终颜色。
                color += specTerm * _SpecColor.rgb * mainLight.color;

                return half4(color, 1.0h);
            }

            ENDHLSL
        }
    }
}