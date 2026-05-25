Shader "Custom/TerrainFootprintRT_URP_Simple"
{
    Properties
    {
        // ============================================================
        // Unity Terrain splat/control textures
        // ============================================================
        // Unity Terrain 会自动把地形混合权重图传给 _Control。
        // R/G/B/A 分别控制前 4 个 Terrain Layer 的权重。
        [HideInInspector] _Control("Control", 2D) = "red" {}

        // 前 4 层 Terrain Layer 的颜色纹理。
        // Unity Terrain 通常会自动把 TerrainLayer 贴图绑定到这些属性。
        [HideInInspector] _Splat0("Layer 0", 2D) = "white" {}
        [HideInInspector] _Splat1("Layer 1", 2D) = "white" {}
        [HideInInspector] _Splat2("Layer 2", 2D) = "white" {}
        [HideInInspector] _Splat3("Layer 3", 2D) = "white" {}

        // Unity 会给每层纹理传 ST。
        // xy = tiling，zw = offset。
        [HideInInspector] _Splat0_ST("Layer 0 ST", Vector) = (1,1,0,0)
        [HideInInspector] _Splat1_ST("Layer 1 ST", Vector) = (1,1,0,0)
        [HideInInspector] _Splat2_ST("Layer 2 ST", Vector) = (1,1,0,0)
        [HideInInspector] _Splat3_ST("Layer 3 ST", Vector) = (1,1,0,0)

        // ============================================================
        // Footprint receive settings
        // ============================================================
        [Header(Footprint RT Receive)]
        _FootprintAOMin("Footprint AO Min", Range(0, 1)) = 0.35
        _FootprintAOStrength("Footprint AO Strength", Range(0, 4)) = 1.5

        _FootprintNormalStrength("Footprint Normal Strength", Range(0, 20)) = 8.0
        _FootprintNormalBlend("Footprint Normal Blend", Range(0, 1)) = 0.65

        // 不同地表层对脚印的接收强度。
        // 例如：土路层可以设 1，草地层可以设 0.3，石头层设 0。
        [Header(Layer Receive Mask)]
        _Layer0FootprintReceive("Layer0 Receive", Range(0, 1)) = 1
        _Layer1FootprintReceive("Layer1 Receive", Range(0, 1)) = 1
        _Layer2FootprintReceive("Layer2 Receive", Range(0, 1)) = 1
        _Layer3FootprintReceive("Layer3 Receive", Range(0, 1)) = 1

        [Header(Lighting)]
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.35
        _DiffuseStrength("Diffuse Strength", Range(0, 2)) = 1.0

        [Header(Debug)]
        _DebugFootprint("Debug Footprint 0 Final 1 Mask 2 Depth 3 Receive", Range(0, 3)) = 0
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
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ============================================================
            // Terrain textures
            // ============================================================
            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);  SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);  SAMPLER(sampler_Splat1);
            TEXTURE2D(_Splat2);  SAMPLER(sampler_Splat2);
            TEXTURE2D(_Splat3);  SAMPLER(sampler_Splat3);

            float4 _Splat0_ST;
            float4 _Splat1_ST;
            float4 _Splat2_ST;
            float4 _Splat3_ST;

            // ============================================================
            // Global footprint RT from FootprintLocalRTPainter
            // ============================================================
            // _FootprintRT 的通道约定：
            // R = 脚印 mask
            // G = 脚印 depth / AO
            // B/A 暂时保留。
            TEXTURE2D(_FootprintRT); SAMPLER(sampler_FootprintRT);

            // x = minX
            // y = minZ
            // z = maxX
            // w = maxZ
            float4 _FootprintRect;

            // xy = 1 / textureSize
            // zw = textureSize
            float4 _FootprintTexelSize;

            // 1 = 启用，0 = 禁用。
            float _FootprintEnabled;

            // ============================================================
            // Material parameters
            // ============================================================
            float _FootprintAOMin;
            float _FootprintAOStrength;
            float _FootprintNormalStrength;
            float _FootprintNormalBlend;

            float _Layer0FootprintReceive;
            float _Layer1FootprintReceive;
            float _Layer2FootprintReceive;
            float _Layer3FootprintReceive;

            float _AmbientStrength;
            float _DiffuseStrength;
            float _DebugFootprint;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;

                return output;
            }

            // ============================================================
            // Terrain layer sampling
            // ============================================================
            half3 SampleTerrainAlbedo(float2 terrainUV, out half4 control)
            {
                control = SAMPLE_TEXTURE2D(_Control, sampler_Control, terrainUV);

                // 防止权重总和不是 1。
                half weightSum = max(control.r + control.g + control.b + control.a, 0.0001h);
                control /= weightSum;

                half3 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, terrainUV * _Splat0_ST.xy + _Splat0_ST.zw).rgb;
                half3 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, terrainUV * _Splat1_ST.xy + _Splat1_ST.zw).rgb;
                half3 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, terrainUV * _Splat2_ST.xy + _Splat2_ST.zw).rgb;
                half3 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, terrainUV * _Splat3_ST.xy + _Splat3_ST.zw).rgb;

                return c0 * control.r + c1 * control.g + c2 * control.b + c3 * control.a;
            }

            half ComputeLayerFootprintReceive(half4 control)
            {
                half receive = 0.0h;
                receive += control.r * _Layer0FootprintReceive;
                receive += control.g * _Layer1FootprintReceive;
                receive += control.b * _Layer2FootprintReceive;
                receive += control.a * _Layer3FootprintReceive;
                return saturate(receive);
            }

            // ============================================================
            // Footprint RT sampling
            // ============================================================
            float2 GetFootprintUV(float3 worldPos)
            {
                float2 minXZ = _FootprintRect.xy;
                float2 maxXZ = _FootprintRect.zw;
                float2 sizeXZ = max(maxXZ - minXZ, 0.0001.xx);

                return (worldPos.xz - minXZ) / sizeXZ;
            }

            bool IsUVOutside01(float2 uv)
            {
                return uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;
            }

            half4 SampleFootprintData(float2 footprintUV)
            {
                if (_FootprintEnabled < 0.5)
                    return half4(0, 0, 0, 0);

                if (IsUVOutside01(footprintUV))
                    return half4(0, 0, 0, 0);

                return SAMPLE_TEXTURE2D(_FootprintRT, sampler_FootprintRT, footprintUV);
            }

            half SampleFootprintDepth(float2 footprintUV)
            {
                return SampleFootprintData(footprintUV).g;
            }

            // 通过 FootprintRT 的 depth 梯度生成一个近似世界空间法线。
            // 这是针对 XZ 地面的简化版本：Y 轴作为向上方向。
            float3 BuildFootprintNormalWS(float2 footprintUV)
            {
                float2 texel = _FootprintTexelSize.xy;

                half hL = SampleFootprintDepth(footprintUV - float2(texel.x, 0));
                half hR = SampleFootprintDepth(footprintUV + float2(texel.x, 0));
                half hD = SampleFootprintDepth(footprintUV - float2(0, texel.y));
                half hU = SampleFootprintDepth(footprintUV + float2(0, texel.y));

                float dhdx = (hR - hL) * _FootprintNormalStrength;
                float dhdz = (hU - hD) * _FootprintNormalStrength;

                // FootprintRT.g 越大表示越深。
                // 梯度方向形成凹陷边缘的假法线。
                return normalize(float3(-dhdx, 1.0, -dhdz));
            }

            // ============================================================
            // Fragment
            // ============================================================
            half4 Frag(Varyings input) : SV_Target
            {
                half4 control;
                half3 albedo = SampleTerrainAlbedo(input.uv, control);

                float2 footprintUV = GetFootprintUV(input.positionWS);
                half4 footprint = SampleFootprintData(footprintUV);

                half footprintMask = footprint.r;
                half footprintDepth = footprint.g;

                // 根据 Terrain Layer 权重决定当前地表接收脚印的程度。
                half layerReceive = ComputeLayerFootprintReceive(control);

                footprintMask *= layerReceive;
                footprintDepth *= layerReceive;

                // Debug 1：显示脚印 mask。
                if (_DebugFootprint > 0.5 && _DebugFootprint < 1.5)
                    return half4(footprintMask.xxx, 1);

                // Debug 2：显示脚印 depth。
                if (_DebugFootprint >= 1.5 && _DebugFootprint < 2.5)
                    return half4(footprintDepth.xxx, 1);

                // Debug 3：显示当前 Terrain Layer 对脚印的接收强度。
                if (_DebugFootprint >= 2.5)
                    return half4(layerReceive.xxx, 1);

                // AO / darken：
                // depth 越大，颜色越暗。
                half aoMask = saturate(footprintDepth * _FootprintAOStrength);
                half footprintAO = lerp(1.0h, (half)_FootprintAOMin, aoMask);
                albedo *= footprintAO;

                // Normal Blend：
                // 使用 FootprintRT 的 depth 梯度构造一个凹陷法线。
                float3 terrainNormalWS = normalize(input.normalWS);
                float3 footprintNormalWS = BuildFootprintNormalWS(footprintUV);

                float normalBlend = saturate(footprintMask * _FootprintNormalBlend);
                float3 finalNormalWS = normalize(lerp(terrainNormalWS, footprintNormalWS, normalBlend));

                // 简单主光照。
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(finalNormalWS, normalize(mainLight.direction)));

                half lighting = saturate(_AmbientStrength + ndotl * _DiffuseStrength);
                half3 finalColor = albedo * lighting;

                return half4(finalColor, 1.0h);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
