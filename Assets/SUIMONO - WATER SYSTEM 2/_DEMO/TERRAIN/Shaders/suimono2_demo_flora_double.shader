Shader "Suimono2/Demo/Flora_DoubleSided" {

	Properties {
		_WavingTint ("Fade Color", Color) = (.7,.6,.5, 0)
		_MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
		_SpecTex ("Specular", 2D) = "white" {}
		_SpecColor ("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
		_Shininess ("Shininess", Range (0.03, 1)) = 0.078125
		_BumpMap ("Normalmap", 2D) = "bump" {}
		_WaveAndDistance ("Wave, distance and speed", Vector) = (12, 3.6, 1, 1)
		_Cutoff ("Cutoff", float) = 0.5
		_Cutoff2 ("Cutoff2", float) = 0.5
	}
	 
	SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
Pass { Name "UniversalForwardOnly" Tags { "LightMode"="UniversalForwardOnly" } Cull Off ZWrite On
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_FLORA 1
#pragma multi_compile_fog
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
#pragma multi_compile _ _FORWARD_PLUS
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile_fragment _ _LIGHT_LAYERS
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
Pass { Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" } Cull Off ZWrite On
ColorMask 0
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_FLORA 1
#define SUIMONO_DEMO_SHADOW 1
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
Pass { Name "DepthOnly" Tags { "LightMode"="DepthOnly" } Cull Off ZWrite On
ColorMask 0
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_FLORA 1
#define SUIMONO_DEMO_DEPTH 1
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
}
SubShader {
		Tags {
			//"Queue" = "AlphaTest"
			"Queue" = "Geometry"
			//"IgnoreProjector"="True"
			"RenderType"="TransparentCutout"
		}
		Cull Off
		//LOD 200
		//ColorMask RGB
		 
		CGPROGRAM
		#pragma surface surf Standard addshadow alphatest:_Cutoff2
		//#pragma exclude_renderers flash
		//#include "TerrainEngine.cginc"
		 
		sampler2D _MainTex;
		sampler2D _SpecTex;
		sampler2D _BumpMap;
		half _Shininess;
		fixed _Cutoff;
		fixed trunkMask;
		 
		struct Input {
			float2 uv_MainTex;
			float2 uv_BumpMap;
			fixed4 color : COLOR;
		};

			 
		void surf (Input IN, inout SurfaceOutputStandard o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
			fixed4 s = tex2D(_SpecTex, IN.uv_MainTex);
			o.Albedo = c.rgb*2.0;// * IN.color;
			o.Alpha = c.a;
			clip (o.Alpha - _Cutoff);
			//o.Gloss = s.r;
			trunkMask = s.g;
			//o.Specular = _Shininess;
			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
			o.Metallic = 0.0;
			o.Smoothness = 0.2;
		}
		ENDCG
	}
 
 

	 
	Fallback Off
}