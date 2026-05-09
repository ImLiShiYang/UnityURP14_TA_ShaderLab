// 自定义 URP 屏幕空间贴花 shader。
//
// 这一版配合体积盒 DrawMesh 使用，不是全屏三角形版本。
//
// 工作流程：
// 1. C# 里绘制一个 decal 体积盒 cube。
// 2. shader 当前像素通过屏幕 UV 采样 _CameraDepthTexture。
// 3. 用深度重建当前像素对应的 world position。
// 4. 把 world position 转到 decal local space。
// 5. 判断这个点是否在 decal 投射盒内。
// 6. 如果在盒子内，就根据 local XY 采样贴花纹理并混合到画面上。
Shader "Custom/ScreenSpaceDecal_VolumeBox"
{
    // Properties 会显示在 Unity Material Inspector 中。
    //
    // 这里声明的属性可以在材质面板中编辑。
    // 同名变量也可以在 HLSL 中使用。
    Properties
    {
        // 贴花纹理。
        // "white" {} 表示默认使用白色纹理。
        _DecalTexture("Decal Texture", 2D) = "white" {}

        // 贴花颜色。
        // 默认是白色，也就是不额外改变纹理颜色。
        _DecalColor("Decal Color", Color) = (1,1,1,1)
    }

    // SubShader 是 ShaderLab 的子着色器块。
    //
    // 一个 shader 可以有多个 SubShader。
    // Unity 会根据当前渲染管线、平台能力选择合适的 SubShader。
    SubShader
    {
        // Tags 是 ShaderLab 标签。
        //
        // RenderPipeline = UniversalPipeline：
        // 表示这个 shader 用于 URP。
        //
        // Queue = Transparent：
        // 表示它属于透明队列。
        // 当前 shader 使用 Blend 混合，因此放在透明队列更符合语义。
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        // Pass 是一次实际绘制。
        //
        // 一个 shader 可以有多个 Pass。
        // C# DrawMesh 的最后一个参数 shaderPass = 0，就是绘制这里的第 0 个 Pass。
        Pass
        {
            // Pass 名字，方便 Frame Debugger 中查看。
            Name "ScreenSpaceDecalVolumeBox"

            // 不写入深度。
            //
            // decal 是叠加效果，不应该修改场景深度。
            ZWrite Off

            // 总是通过深度测试。
            //
            // 因为我们不是用 cube 自己的几何深度决定显示，
            // 而是通过 _CameraDepthTexture 重建场景表面位置后再判断。
            ZTest Always

            // 剔除正面，只绘制体积盒背面。
            //
            // 体积盒 decal 常见做法是只画盒子的某一侧，避免前后面都执行导致 alpha 叠加两次。
            //
            // 如果完全看不到 decal，可以临时改成：
            // Cull Off
            //
            // 如果显示结果异常，也可以尝试：
            // Cull Back
            //
            // 这通常和 cube mesh 的三角形绕序有关。
            Cull Front

            // Alpha 混合。
            //
            // SrcAlpha OneMinusSrcAlpha 是常见透明混合方式：
            // finalColor = sourceColor * sourceAlpha + targetColor * (1 - sourceAlpha)
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            // 指定顶点 shader 函数名。
            #pragma vertex Vert

            // 指定片元 shader 函数名。
            #pragma fragment Frag

            // 引入 URP Core.hlsl。
            //
            // 里面包含 TransformObjectToHClip、ComputeWorldSpacePosition、
            // UNITY_MATRIX_I_VP、_ScaledScreenParams 等常用函数和变量。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 声明贴花纹理和采样器。
            //
            // TEXTURE2D / SAMPLER 是 Unity HLSL 宏。
            // _DecalTexture 对应 Properties 中的 _DecalTexture。
            TEXTURE2D(_DecalTexture);
            SAMPLER(sampler_DecalTexture);

            // 声明相机深度纹理。
            //
            // TEXTURE2D_X_FLOAT 是 URP 中用于兼容 XR / 平台差异的纹理声明宏。
            // _CameraDepthTexture 由 URP 根据 ConfigureInput(ScriptableRenderPassInput.Depth) 准备。
            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            // world -> decal normalized local 矩阵。
            //
            // C# 中由 projector.GetWorldToDecalMatrix() 传入。
            // 用来把重建出来的 world position 转成 decal 局部坐标。
            float4x4 _DecalWorldToLocal;

            // 贴花颜色。
            //
            // C# 中由 mat.SetColor("_DecalColor", projector.decalColor) 传入。
            float4 _DecalColor;

            // decal 参数。
            //
            // x = opacity，整体透明度。
            // y = edgeFade，边缘淡出距离。
            // z = cos(angleEnd)，完全淡出角度对应的 cos 值。
            // w = cos(angleStart)，开始淡出角度对应的 cos 值。
            float4 _DecalParams;

            // UV 参数。
            //
            // xy = tiling。
            // zw = offset。
            float4 _DecalTilingOffset;

            // decal backward world direction。
            //
            // 约定 projector 的 local +Z 是投射方向，
            // 所以 backward 通常是 -transform.forward。
            //
            // shader 用它和场景表面 normal 做 dot，计算 angle fade。
            float4 _DecalBackwardWS;

            // 距离淡出参数。
            //
            // 当前只使用 x 分量。
            // x = distanceFade。
            float4 _DecalDistanceFade;

            // 顶点输入结构。
            //
            // POSITION 是 mesh 顶点位置语义。
            // 这里接收的是 cube mesh 的 object space 顶点坐标。
            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            // 顶点 shader 输出结构，同时也是 fragment shader 输入结构。
            //
            // SV_POSITION 是裁剪空间 / 屏幕空间位置语义。
            // 顶点 shader 必须输出它，GPU 才知道这个顶点画到屏幕哪里。
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // 顶点 shader。
            //
            // 作用：
            // 把 cube mesh 的 object space 顶点坐标转换到 homogeneous clip space。
            //
            // TransformObjectToHClip 是 URP Core.hlsl 提供的函数。
            // 它内部会使用当前物体的 ObjectToWorld 矩阵和相机 VP 矩阵。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            // 根据片元的屏幕位置计算屏幕 UV。
            //
            // input.positionCS 在 fragment shader 中对应当前像素的屏幕位置。
            // _ScaledScreenParams.xy 是当前渲染目标的宽高。
            //
            // 相除后得到 0 到 1 的屏幕 UV。
            float2 GetScreenUV(float4 positionCS)
            {
                return positionCS.xy / _ScaledScreenParams.xy;
            }

            // 采样原始深度值。
            //
            // 注意：
            // rawDepth 不是线性深度。
            // 它是硬件深度，范围和方向会受平台、是否 reversed Z 影响。
            float SampleRawDepth(float2 screenUV)
            {
                return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV).r;
            }

            // 通过屏幕 UV 和深度重建世界坐标。
            //
            // 屏幕空间 decal 的关键就在这里：
            // 我们并不直接使用 cube 表面的 world position，
            // 而是使用当前屏幕像素背后场景表面的 world position。
            float3 ReconstructWorldPosition(float2 screenUV)
            {
                float rawDepth = SampleRawDepth(screenUV);

                // UNITY_REVERSED_Z 是 Unity 定义的平台宏。
                //
                // 在 reversed Z 平台中：
                // 远平面深度通常接近 0。
                //
                // 在非 reversed Z 平台中：
                // 远平面深度通常接近 1。
                //
                // 如果深度表示远平面，通常说明这个像素没有有效场景几何体，
                // 这种地方不应该显示 decal。
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                    {
                        discard;
                    }
                #else
                    if (rawDepth >= 0.999999)
                    {
                        discard;
                    }
                #endif

                // ComputeWorldSpacePosition 是 URP Core.hlsl 提供的函数。
                //
                // 参数：
                // screenUV：当前像素屏幕 UV。
                // rawDepth：当前像素深度。
                // UNITY_MATRIX_I_VP：ViewProjection 矩阵的逆矩阵。
                //
                // 返回值：
                // 当前屏幕像素对应的世界空间位置。
                return ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
            }

            // 从重建出来的 world position 推导世界空间法线。
            //
            // 这是基于深度的近似法线重建。
            // 它不是 mesh 原始 normal，而是通过相邻像素 world position 的变化估算出来。
            float3 ReconstructWorldNormalFromDepth(float3 worldPos)
            {
                // ddx / ddy 是 GPU 片元导数函数。
                //
                // ddx(worldPos)：当前像素和屏幕 x 方向相邻像素的 worldPos 差异。
                // ddy(worldPos)：当前像素和屏幕 y 方向相邻像素的 worldPos 差异。
                float3 dx = ddx(worldPos);
                float3 dy = ddy(worldPos);

                // cross 求叉乘。
                //
                // 两个切线方向叉乘可以得到法线方向。
                // normalize 把结果单位化。
                float3 normalWS = normalize(cross(dy, dx));

                // 从当前世界点指向相机的方向。
                //
                // _WorldSpaceCameraPos 是 Unity 内置变量，表示当前相机世界坐标。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - worldPos);

                // 保证 normal 大致朝向相机。
                //
                // 如果 normal 和 viewDirWS 夹角大于 90 度，dot 会小于 0，
                // 说明法线朝背面，需要翻转。
                if (dot(normalWS, viewDirWS) < 0.0)
                {
                    normalWS = -normalWS;
                }

                return normalWS;
            }

            // 片元 shader。
            //
            // 每个被 cube mesh 覆盖到的屏幕像素都会执行这里。
            //
            // 返回值 half4 表示最终输出颜色。
            // SV_Target 表示输出到当前 render target。
            half4 Frag(Varyings input) : SV_Target
            {
                // 当前像素的屏幕 UV。
                float2 screenUV = GetScreenUV(input.positionCS);

                // 防止屏幕 UV 越界。
                //
                // 正常情况下 cube rasterization 后应该在屏幕内，
                // 但保留这个判断更安全。
                if (screenUV.x < 0.0 || screenUV.x > 1.0 || screenUV.y < 0.0 || screenUV.y > 1.0)
                {
                    discard;
                }

                // 通过当前像素的深度重建场景表面的世界坐标。
                float3 worldPos = ReconstructWorldPosition(screenUV);

                // 把世界坐标转换到 decal normalized local space。
                //
                // 转换后：
                // x/y/z 在 -0.5 到 0.5 内，表示落在 decal 盒子内部。
                float3 decalLocalPos = mul(_DecalWorldToLocal, float4(worldPos, 1.0)).xyz;

                // abs 取绝对值，方便判断是否超出盒子范围。
                float3 absLocal = abs(decalLocalPos);

                // normalized decal box 判断。
                //
                // 只有 x/y/z 都在 -0.5 到 0.5 内，
                // 当前世界点才算落入 decal 投射盒。
                if (absLocal.x > 0.5 || absLocal.y > 0.5 || absLocal.z > 0.5)
                {
                    discard;
                }

                // Unity Decal 风格坐标约定：
                //
                // local XY 是 decal 贴图平面。
                // local Z 是 projection depth。
                //
                // decalLocalPos.xy 范围是 -0.5 到 0.5。
                // 加 0.5 后转换成 0 到 1 的 UV。
                float2 decalUV = decalLocalPos.xy + 0.5;

                // 应用 UV tiling 和 offset。
                decalUV = decalUV * _DecalTilingOffset.xy + _DecalTilingOffset.zw;

                // 采样贴花纹理。
                half4 decalTex = SAMPLE_TEXTURE2D(_DecalTexture, sampler_DecalTexture, decalUV);

                // 贴花纹理乘以贴花颜色。
                half4 color = decalTex * _DecalColor;

                // 计算当前点距离 decal XY 平面边缘的距离。
                //
                // 这里只使用 X/Y 边缘做贴图淡出。
                // Z 是投射深度，不应该影响贴图边缘淡出。
                float distToPlaneEdge = min(0.5 - absLocal.x, 0.5 - absLocal.y);

                // edgeFade 防止为 0，避免 smoothstep 边界重合。
                float edgeFade = max(_DecalParams.y, 0.0001);

                // smoothstep 做柔和淡出。
                //
                // distToPlaneEdge 越接近 0，说明越靠近边缘，boxFade 越接近 0。
                // distToPlaneEdge 大于 edgeFade 后，boxFade 接近 1。
                float boxFade = smoothstep(0.0, edgeFade, distToPlaneEdge);

                // 从深度重建表面法线。
                float3 normalWS = ReconstructWorldNormalFromDepth(worldPos);

                // decal backward 方向单位化。
                float3 decalBackwardWS = normalize(_DecalBackwardWS.xyz);

                // 计算表面法线和 decal backward direction 的朝向关系。
                //
                // dot 越接近 1，说明表面越正对 decal。
                // dot 越接近 0，说明表面越侧向。
                float facing = saturate(dot(normalWS, decalBackwardWS));

                // 根据角度做淡出。
                //
                // _DecalParams.z = cos(angleEnd)。
                // _DecalParams.w = cos(angleStart)。
                //
                // 因为 cos 角度越大，夹角越小，
                // 所以这里用 smoothstep(cosEnd, cosStart, facing)。
                float angleFade = smoothstep(_DecalParams.z, _DecalParams.w, facing);

                // 应用所有 alpha 控制。
                //
                // _DecalParams.x：
                // 整体透明度。
                //
                // _DecalDistanceFade.x：
                // 距离淡出。
                //
                // boxFade：
                // XY 边缘淡出。
                //
                // angleFade：
                // 角度淡出。
                // color.a *= _DecalParams.x;
                // color.a *= _DecalDistanceFade.x;
                // color.a *= boxFade;
                color.a *= angleFade;

                return color;
            }

            ENDHLSL
        }
    }
}