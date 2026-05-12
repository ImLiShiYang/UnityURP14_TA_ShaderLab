Shader "Unlit/WaterWave"
{
    Properties
    {
        _Color ("Main Color", Color) = (0, 0.15, 0.115, 1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _WaveMap ("Wave Map", 2D) = "bump" {}
        _Cubemap ("Environment Cubemap", Cube) = "_Skybox" {}
        _WaveXSpeed ("Wave Horizontal Speed", Range(-0.1, 0.1)) = 0.01
        _WaveYSpeed ("Wave Vertical Speed", Range(-0.1, 0.1)) = 0.01
        _Distortion ("Distortion", Range(0, 100)) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Name "WaterPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 tangentOS    : TANGENT;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float4 uv           : TEXCOORD0;          // xy: MainTex uv, zw: WaveMap uv
                float4 positionSS   : TEXCOORD1;          // 屏幕空间位置，用于采样透明纹理
                float3 viewDirWS    : TEXCOORD2;
                float3 normalWS     : TEXCOORD3;
                float3 tangentWS    : TEXCOORD4;
                float3 bitangentWS  : TEXCOORD5;
                float3 positionWS   : TEXCOORD6;
            };

            // 声明纹理和采样器
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_WaveMap);
            SAMPLER(sampler_WaveMap);
            TEXTURECUBE(_Cubemap);
            SAMPLER(sampler_Cubemap);
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _WaveMap_ST;
                float4 _Color;
                float _WaveXSpeed;
                float _WaveYSpeed;
                float _Distortion;
            CBUFFER_END

            // 法线解包函数
            float3 UnpackNormalCustom(float4 packedNormal)
            {
                #if defined(UNITY_NO_DXT5nm)
                    return packedNormal.xyz * 2.0 - 1.0;
                #else
                    float3 normal;
                    normal.xy = packedNormal.wy * 2.0 - 1.0;
                    normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
                    return normal;
                #endif
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 顶点位置转换：模型空间 → 裁剪空间
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;

                // 法线、切线、副法线转换：模型空间 → 世界空间
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS = normalInputs.normalWS;
                OUT.tangentWS = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;

                // 计算屏幕空间位置（用于采样透明纹理）
                OUT.positionSS = ComputeScreenPos(OUT.positionCS);

                // 计算世界空间视角方向
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(OUT.positionWS);

                // 计算 UV：主纹理和波纹贴图
                OUT.uv.xy = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.uv.zw = TRANSFORM_TEX(IN.uv, _WaveMap);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 时间控制的波浪滚动速度
                float2 speed = float2(_WaveXSpeed, _WaveYSpeed) * _Time.y;

                // 采样波纹法线贴图，并混合两层以产生动态流动效果
                float3 bump1 = UnpackNormalCustom(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, IN.uv.zw + speed));
                float3 bump2 = UnpackNormalCustom(SAMPLE_TEXTURE2D(_WaveMap, sampler_WaveMap, IN.uv.zw - speed));
                float3 bump = normalize(bump1 + bump2);

                // 计算折射偏移量（基于法线扰动）
                float2 screenUV = IN.positionSS.xy / IN.positionSS.w;
                float2 offset = bump.xy * _Distortion * 0.01;  // 缩放偏移量以适应纹理坐标范围
                float2 refractionUV = screenUV + offset;

                // 采样透明纹理，获取折射颜色
                half3 refractionColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refractionUV).rgb;

                // 获取主纹理颜色
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv.xy + speed).rgb;

                // 将切线空间的法线转换到世界空间
                float3x3 TBN = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 worldBump = normalize(mul(bump, TBN));

                // 计算反射向量和反射颜色
                float3 reflectDir = reflect(-IN.viewDirWS, worldBump);
                half3 reflectionColor = SAMPLE_TEXTURECUBE(_Cubemap, sampler_Cubemap, reflectDir).rgb * albedo * _Color.rgb;

                // 菲涅尔效应
                float fresnel = pow(1.0 - saturate(dot(IN.viewDirWS, worldBump)), 4.0);

                // 最终颜色 = 菲涅尔混合反射和折射
                half3 finalColor = reflectionColor * fresnel + (1.0 - fresnel) * refractionColor;

                return half4(finalColor, 1.0);
                // return half4(reflectionColor, 1.0);
            }
            ENDHLSL
        }
    }
    // 回退到内置管线的基础水面着色器（可选）
    // Fallback "Legacy Shaders/Transparent/VertexLit"
}