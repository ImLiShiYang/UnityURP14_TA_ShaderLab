Shader "Unlit/Dissolve"   // 可重命名，例如 "URP/Dissolve"
{
    Properties
    {
        _BurnAmount ("Burn Amount", Range(0.0, 1.0)) = 0.0
        _LineWidth ("Burn Line Width", Range(0.0, 0.2)) = 0.1
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BurnFirstColor ("Burn First Color", Color) = (1, 0, 0, 1)
        _BurnSecondColor ("Burn Second Color", Color) = (1, 0, 0, 1)
        _BurnMap ("Burn Map", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags
        {
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }
        
        Pass
        {
            Name "Burn"
            Tags {"LightMode"="UniversalForward"}
            Cull Off
            
            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float _BurnAmount;
                float _LineWidth;
                float4  _BurnFirstColor;
                float4  _BurnSecondColor;
                float4  _MainTex_ST;
                float4  _BumpMap_ST;
                float4  _BurnMap_ST;
            CBUFFER_END
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_BurnMap);
            SAMPLER(sampler_BurnMap);

            struct a2v
            {
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
                float4 tangent:TANGENT;
                float3 normal:NORMAL;
            };

            struct v2f
            {
                float4 pos:SV_POSITION;
                float2 uvMain:TEXCOORD0;
                float2 uvBump:TEXCOORD1;
                float2 uvBurn:TEXCOORD2;
                float3 worldPos:TEXCOORD3;
                float3 worldNormal:TEXCOORD4;
                float3x3 tbnMatrix:TEXCOORD5;
            };
            
            v2f vert(a2v v)
            {
                v2f o;
                o.pos=TransformObjectToHClip(v.vertex.xyz);
                o.uvMain=TRANSFORM_TEX(v.uv,_MainTex);
                o.uvBump=TRANSFORM_TEX(v.uv,_BumpMap);
                o.uvBurn=TRANSFORM_TEX(v.uv,_BurnMap);
                
                o.worldPos=TransformObjectToWorld(v.vertex.xyz);
               
                VertexNormalInputs normalInput=GetVertexNormalInputs(v.normal,v.tangent);
                o.worldNormal=normalInput.normalWS;
                o.tbnMatrix=float3x3(normalInput.tangentWS,normalInput.bitangentWS,normalInput.normalWS);
                
                return  o;
            }
            
            float4 frag(v2f i):SV_Target
            {
                float c=SAMPLE_TEXTURE2D(_BurnMap,sampler_BurnMap,i.uvBurn).r;
                clip(c-_BurnAmount);
                
                float3 mainColor=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uvMain).rgb;
                
                float3 bumpNormal=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uvBump));
                float3 worldNormal=normalize(TransformTangentToWorld(bumpNormal,i.tbnMatrix)); 
                
                float3 ambient=SampleSH(worldNormal)*mainColor;
                
                float4 shadowAtten=TransformWorldToShadowCoord(i.worldPos);
                Light mainlight=GetMainLight(shadowAtten);
                float3 lightDir=normalize(mainlight.direction);
                float3 lightColor=mainlight.color;
                
                float res=c-_BurnAmount;
                //如果res接近0，说明几乎要被裁剪，此时就在边缘
                float rl=1-smoothstep(0,_LineWidth,res);
                
                float diff=saturate(dot(worldNormal,lightDir));
                float3 diffuse=lightColor*diff*mainColor;
                
                float4 burnColor=lerp(_BurnFirstColor,_BurnSecondColor,rl);
                burnColor=pow(burnColor,5);
                
                float3 bColor=ambient+diffuse;
                float3 finalColor=lerp(bColor,burnColor.rgb,rl*step(0.0001,_BurnAmount));
                
                return float4(finalColor,1);
            }
            
            ENDHLSL
        }
        
        
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float _BurnAmount;
                float4 _BurnMap_ST;
            CBUFFER_END

            TEXTURE2D(_BurnMap);
            SAMPLER(sampler_BurnMap);

            struct Attributes
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
                float2 texcoord : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uvBurn : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // 标准顶点变换（无阴影偏移，可能会产生阴影痤疮，但先保证裁剪功能正常）
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uvBurn = TRANSFORM_TEX(v.texcoord, _BurnMap);
                return o;
            }

            half4 frag(Varyings i) : SV_TARGET
            {
                float burnValue = SAMPLE_TEXTURE2D(_BurnMap, sampler_BurnMap, i.uvBurn).r;
                clip(burnValue - _BurnAmount);
                return 0;
            }
            

            ENDHLSL
        }
        
//        Pass
//        {
//            Name "DepthNormals"
//            Tags { "LightMode" = "DepthNormals" }
//
//            ZWrite On
//            Cull Back
//
//            HLSLPROGRAM
//            #pragma vertex DepthNormalsVertex
//            #pragma fragment DepthNormalsFragment
//
//            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
//            ENDHLSL
//        }
    }
    
}