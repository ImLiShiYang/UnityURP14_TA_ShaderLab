Shader "Hidden/Grass/InteractionAccumulate"
{
    Properties
    {
        // 当前帧 Brush RT。
        // GrassInteractionCamera 会把本帧生成的脚步 Brush 画到这张图里。
        _MainTex ("Current Brush RT", 2D) = "black" {}

        // 上一帧累积后的历史压草 RT。
        // 用它来保留走过的草痕。
        _LastTex ("Last Accum RT", 2D) = "black" {}

        // 历史 RT 的 UV 偏移。
        // 因为顶部交互相机跟着角色移动，所以历史 RT 需要反向偏移，避免压痕跟着相机漂移。
        _Offset ("History UV Offset", Vector) = (0, 0, 0, 0)

        // 每帧恢复量。
        // 数值越大，历史压草痕迹消失越快。
        _DecayAmount ("当前帧恢复强度", Float) = 0

        // RT 边缘淡出强度。
        // 用来避免历史压痕在 RT 边缘出现硬切。
        _EdgeSoftness ("Edge Softness", Float) = 25

        // 当前 RT 对应的世界空间 XZ 范围。
        // xy = minX, minZ
        // zw = maxX, maxZ
        _InteractionRect ("Interaction Rect", Vector) = (0, 0, 1, 1)

        // 左右脚实时压草中心，由 C# 每帧传入。
        _PressCenter0WS ("Left Foot Center WS", Vector) = (0, 0, 0, 0)
        _PressCenter1WS ("Right Foot Center WS", Vector) = (0, 0, 0, 0)

        // 左右脚是否启用实时压草。
        _EnablePressCenter0 ("Enable Left Foot", Float) = 0
        _EnablePressCenter1 ("Enable Right Foot", Float) = 0

        // 左右脚影响半径。
        _PressRadius0 ("Left Foot Radius", Float) = 0.45
        _PressRadius1 ("Right Foot Radius", Float) = 0.45

        // 是否启用实时脚部径向压草。
        _EnableRadialPress ("Enable Radial Press", Float) = 1

        // 脚部压草衰减曲线。
        // 数值越大，压草影响越集中在脚中心附近。
        _RadialMaskPower ("Radial Mask Power", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // 这是一个全屏 Blit Pass。
            // 不需要深度测试、不写深度、不剔除背面。
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_LastTex);
            SAMPLER(sampler_LastTex);

            CBUFFER_START(UnityPerMaterial)
                float2 _Offset;
                float _DecayAmount;
                float _EdgeSoftness;
                float4 _InteractionRect;

                float4 _PressCenter0WS;
                float4 _PressCenter1WS;
                float _EnablePressCenter0;
                float _EnablePressCenter1;
                float _PressRadius0;
                float _PressRadius1;

                float _EnableRadialPress;
                float _RadialMaskPower;

                // Unity 自动提供的纹理尺寸信息。
                // xy = 1 / width, 1 / height
                // zw = width, height
                float4 _LastTex_TexelSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // 全屏 Blit 顶点阶段。
            // 只负责把全屏三角形 / 四边形顶点转换到裁剪空间，并把 UV 传给片元阶段。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // 把 UV 对齐到纹素中心。
            // 这样采样历史 RT 时更稳定，可以减少相机移动时的细微抖动。
            float2 SnapUVToTexel(float2 uv)
            {
                float2 pixel = floor(uv * _LastTex_TexelSize.zw) + 0.5;
                return pixel * _LastTex_TexelSize.xy;
            }

            // 根据某只脚的世界坐标，计算当前 worldXZ 位置的实时压草强度。
            // 离脚中心越近，mask 越接近 1；超过半径后接近 0。
            float FootMask(float2 worldXZ, float3 centerWS, float radius, float enabled)
            {
                float safeRadius = max(radius, 0.0001);

                float mask = 1.0 - saturate(distance(worldXZ, centerWS.xz) / safeRadius);

                // 让半径边缘过渡更柔和。
                mask = smoothstep(0.0, 1.0, mask);

                // 控制衰减曲线。
                mask = pow(mask, max(_RadialMaskPower, 0.0001));

                return mask * saturate(enabled) * saturate(_EnableRadialPress);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // 当前帧 Brush 强度。
                // 这张图只表示当前帧新生成的脚步 Brush。
                float currentMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

                // 计算采样历史 RT 的 UV。
                // 当前交互相机跟随角色移动后，历史内容需要根据 _Offset 反向采样，
                // 这样历史压痕才能留在正确的世界位置。
                float2 historyUV = uv - _Offset;

                // 判断偏移后的历史 UV 是否仍在 0~1 范围内。
                // 超出范围说明历史内容已经移出当前 RT 覆盖区域，需要丢弃。
                float inside =
                    step(0.0, historyUV.x) *
                    step(0.0, historyUV.y) *
                    step(historyUV.x, 1.0) *
                    step(historyUV.y, 1.0);

                // 对齐到纹素中心后采样上一帧历史压草 RT。
                historyUV = SnapUVToTexel(historyUV);
                float historyMask = SAMPLE_TEXTURE2D(_LastTex, sampler_LastTex, historyUV).r;

                // 历史压痕恢复。
                // 每帧减去 _DecayAmount，让走过的草逐渐恢复。
                historyMask = saturate(historyMask - max(_DecayAmount, 0.0)) * inside;

                // 把当前 UV 转回世界空间 XZ。
                // 这样才能根据左右脚世界坐标计算实时脚部压草。
                float2 worldXZ = lerp(_InteractionRect.xy, _InteractionRect.zw, uv);

                // 计算左右脚对当前像素的实时压草强度。
                float leftFootMask = FootMask(
                    worldXZ,
                    _PressCenter0WS.xyz,
                    _PressRadius0,
                    _EnablePressCenter0
                );

                float rightFootMask = FootMask(
                    worldXZ,
                    _PressCenter1WS.xyz,
                    _PressRadius1,
                    _EnablePressCenter1
                );

                // 两只脚取较大值，避免左右脚叠加后过强。
                float liveFootMask = max(leftFootMask, rightFootMask);

                // 最终压草强度：
                // 当前帧 Brush、历史压草、实时脚部压草三者取最大值。
                float resultMask = max(currentMask, max(historyMask, liveFootMask));

                // RT 边缘淡出。
                // 越靠近边缘，mask 越弱，防止交互区域移动时边缘出现硬切痕迹。
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float edge = saturate(min(edgeX, edgeY) * _EdgeSoftness);

                resultMask = saturate(resultMask * edge);

                // 输出灰度 mask。
                // 草 Shader 后面主要采样 r 通道作为压草强度。
                return half4(resultMask, resultMask, resultMask, 1.0);
            }

            ENDHLSL
        }
    }
}