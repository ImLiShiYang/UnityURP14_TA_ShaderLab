Shader "Custom/URP/wave_surface"
{
    Properties
    {
        [Header(Water Color)]
        _ShallowColor ("Shallow Color", Color) = (0.55, 0.92, 1.00, 0.45)
        _DeepColor ("Deep Color", Color) = (0.02, 0.32, 0.50, 0.65)
        _ColorBlend ("Color Blend", Range(0, 1)) = 0.72
        _Alpha ("Base Alpha", Range(0, 1)) = 0.55

        [Header(Wave Texture)]
        _WaveTex ("Wave Texture", 2D) = "gray" {}
        _WaveHeight ("Wave Height", Range(0, 0.2)) = 0.008
        _NormalStrength ("Normal Strength", Range(0.01, 8)) = 3.0
        _WaveColorStrength ("Wave Color Strength", Range(0, 1)) = 0.03

        [Header(Refraction)]
        _RefractionStrength ("Refraction Strength", Range(0, 0.08)) = 0.018
        _RefractionWaveStrength ("Refraction Wave Strength", Range(0, 4)) = 1.2
        _RefractionTintStrength ("Refraction Tint Strength", Range(0, 1)) = 0.28

        [Header(Fresnel)]
        _FresnelColor ("Fresnel Color", Color) = (0.70, 0.95, 1.0, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.5
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.55
        _FresnelAlpha ("Fresnel Alpha", Range(0, 1)) = 0.22

        [Header(Specular)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularStrength ("Specular Strength", Range(0, 5)) = 1.0
        _SpecularPower ("Specular Power", Range(8, 256)) = 96

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.05
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.45
        _FoamSoftness ("Foam Softness", Range(0.001, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 用来采样 URP 的 _CameraOpaqueTexture。
            // 需要在 URP Asset / Renderer / Camera 中开启 Opaque Texture。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  waveHeight  : TEXCOORD3;
                float2 waveSlope   : TEXCOORD4;
                float4 screenPos   : TEXCOORD5;
            };

            TEXTURE2D(_WaveTex);
            SAMPLER(sampler_WaveTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _ColorBlend;
                float _Alpha;

                float _WaveHeight;
                float _NormalStrength;
                float _WaveColorStrength;

                float _RefractionStrength;
                float _RefractionWaveStrength;
                float _RefractionTintStrength;

                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelStrength;
                float _FresnelAlpha;

                float4 _SpecularColor;
                float _SpecularStrength;
                float _SpecularPower;

                float4 _FoamColor;
                float _FoamStrength;
                float _FoamThreshold;
                float _FoamSoftness;

                // SurfaceWave.cs 设置：
                // _Stride = float2(1 / textureWidth, 1 / textureHeight)
                float2 _Stride;
            CBUFFER_END

            float ReadWave(float2 uv)
            {
                // _WaveTex:
                // 0.5 = 静止水面
                // 1.0 = 波峰
                // 0.0 = 波谷
                return SAMPLE_TEXTURE2D_LOD(_WaveTex, sampler_WaveTex, uv, 0).r * 2.0 - 1.0;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float2 uv = IN.uv;

                float height = ReadWave(uv);

                // 顶点位移，建议保持很小。
                // 大水面上最好主要靠法线和折射表现，不要让顶点大幅上下动。
                float3 positionOS = IN.positionOS.xyz + IN.normalOS * height * _WaveHeight;

                float up    = ReadWave(uv + float2(0,  _Stride.y));
                float down  = ReadWave(uv + float2(0, -_Stride.y));
                float left  = ReadWave(uv + float2(-_Stride.x, 0));
                float right = ReadWave(uv + float2( _Stride.x, 0));

                float nx = left - right;
                float ny = down - up;

                // 把高度差保存下来，Fragment 里用它偏移屏幕 UV，形成折射。
                OUT.waveSlope = float2(nx, ny);

                // 这里沿用你原来的 XY 网格法线逻辑。
                // 如果你的水面法线反了，把 -_NormalStrength 改成 +_NormalStrength。
                float3 waveNormalOS = normalize(float3(nx, ny, -_NormalStrength));

                OUT.positionWS = TransformObjectToWorld(positionOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(waveNormalOS));
                OUT.uv = uv;
                OUT.waveHeight = height;

                // 屏幕坐标，用于采样 _CameraOpaqueTexture。
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float NdotL = saturate(dot(normalWS, lightDirWS));

                half3 ambient = SampleSH(normalWS);

                // ------------------------------------------------------------
                // 1. 屏幕折射
                // ------------------------------------------------------------
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // 使用水波斜率做屏幕空间偏移。
                // _RefractionStrength 是总折射强度。
                // _RefractionWaveStrength 控制波纹对折射的影响。
                float2 refractionOffset =
                    IN.waveSlope *
                    _RefractionStrength *
                    _RefractionWaveStrength;

                // 视角越贴近水面，折射可以稍微明显一点。
                float viewFresnel = pow(
                    1.0 - saturate(dot(normalWS, viewDirWS)),
                    _FresnelPower
                );

                refractionOffset *= lerp(0.75, 1.35, viewFresnel);

                float2 refractUV = screenUV + refractionOffset;
                refractUV = clamp(refractUV, 0.001, 0.999);

                // 采样不透明物体颜色。
                // 注意：它只包含透明物体之前已经渲染的不透明画面。
                half3 sceneColor = SampleSceneColor(refractUV);

                // ------------------------------------------------------------
                // 2. 基础水色
                // ------------------------------------------------------------
                half3 waterTint = lerp(_DeepColor.rgb, _ShallowColor.rgb, _ColorBlend);

                float waveAmount = saturate(abs(IN.waveHeight));

                // 水波只轻微提亮，不要强烈变黑，否则脚步会像黑色脚印。
                waterTint += _FresnelColor.rgb * waveAmount * _WaveColorStrength;

                // 折射画面与水色混合。
                // _RefractionTintStrength 越小越清澈，越大越偏蓝。
                half3 waterColor = lerp(sceneColor, waterTint, _RefractionTintStrength);

                // ------------------------------------------------------------
                // 3. Fresnel 边缘光
                // ------------------------------------------------------------
                float fresnel = viewFresnel;

                waterColor += _FresnelColor.rgb * fresnel * _FresnelStrength;

                // ------------------------------------------------------------
                // 4. 高光
                // ------------------------------------------------------------
                float3 halfDir = normalize(lightDirWS + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecularPower);

                waterColor +=
                    _SpecularColor.rgb *
                    spec *
                    _SpecularStrength *
                    mainLight.color;

                // ------------------------------------------------------------
                // 5. 轻微泡沫 / 波纹亮边
                // ------------------------------------------------------------
                float foamMask = smoothstep(
                    _FoamThreshold,
                    _FoamThreshold + _FoamSoftness,
                    waveAmount
                );

                waterColor = lerp(
                    waterColor,
                    _FoamColor.rgb,
                    foamMask * _FoamStrength
                );

                // ------------------------------------------------------------
                // 6. 光照
                // ------------------------------------------------------------
                half diffuse = NdotL * 0.35 + 0.65;
                half3 lighting = ambient + mainLight.color * diffuse;

                waterColor *= lighting;

                // ------------------------------------------------------------
                // 7. 透明度
                // ------------------------------------------------------------
                // 因为我们已经采样了 sceneColor 做折射，所以 alpha 不要太低。
                // 太低会把未折射的背景又混回来，折射会变弱。
                float alpha = _Alpha;
                alpha += fresnel * _FresnelAlpha;
                alpha += foamMask * 0.06;
                alpha = saturate(alpha);

                return half4(waterColor, alpha);
            }

            ENDHLSL
        }
    }
}