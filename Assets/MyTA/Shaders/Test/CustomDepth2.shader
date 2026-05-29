Shader "Unlit/CustomDepth2"
{

	Properties
	{ }

	SubShader
	{
		Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

			struct Attributes
			{
				float4 positionOS   : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS  : SV_POSITION;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				return OUT;
			}

			half4 frag() : SV_Target
			{
				return half4(0, 0, 1, 1);
			}
			ENDHLSL
		}

		Pass
		{
			Name "CustomDepthPass"
			Tags
			{
				"LightMode" = "CustomDepth"
			}

			// -------------------------------------
			// Render State Commands
			ZWrite On
			ColorMask R

			HLSLPROGRAM
			#pragma target 2.0

			// -------------------------------------
			// Shader Stages
			#pragma vertex DepthOnlyVertex
			#pragma fragment DepthOnlyFragment

			// -------------------------------------
			// Material Keywords
			#pragma shader_feature_local_fragment _ALPHATEST_ON
			#pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA

			// -------------------------------------
			// Unity defined keywords
			#pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

			// -------------------------------------
			// Includes
			#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
			ENDHLSL
		}
	}

}
