// MirrorShader_URP.shader
// URP 版本 - 支持更多现代特性
 
Shader "Custom/MirrorShader"
{
    Properties
    {
        _MainTex("Reflection Texture", 2D) = "white"{}
        _MirrorColor("Mirror Tint", Color) = (0.9, 0.95, 1.0, 1.0)
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.9
        _FresnelPower("Fresnel Power", Range(0.1, 5)) = 2
        _Distortion("Distortion", Range(0, 0.5)) = 0
    }
 
    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
 
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
 
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MirrorColor;
                float _ReflectionStrength;
                float _FresnelPower;
                float _Distortion;
            CBUFFER_END
 
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };
 
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };
 
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs posInputs = 
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = 
                    GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = posInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                
                // UV 翻转实现镜像
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.uv.x = 1.0 - output.uv.x;
                
                return output;
            }
 
            float4 frag(Varyings input) : SV_Target
            {
                // 可选：添加基于法线的扭曲效果
                // float2 distortion = input.normalWS.xy * _Distortion;
                // float2 finalUV = input.uv + distortion;
                 float2 distortion;
                distortion.x = sin(input.uv.y * 5.0) * _Distortion;
                distortion.y = cos(input.uv.x * 5.0) * _Distortion;
                float2 finalUV = input.uv + distortion;
                
                // 采样反射纹理
                float4 reflection = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, finalUV);
                    
                
                // 计算菲涅尔效果（边缘淡化）
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float fresnel = pow( 1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                    
                
                // 组合最终颜色
                float4 color = reflection * _MirrorColor * _ReflectionStrength;
                color.rgb = lerp(color.rgb, _MirrorColor.rgb, fresnel * 0.3);
                
                return color;
            }
            ENDHLSL
        }
    }
}