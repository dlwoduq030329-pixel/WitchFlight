Shader "Suimono2/Bumped Specular Double" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_SpecColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
	_Shininess ("Shininess", Range (0.01, 1)) = 0.078125
	_ReflectColor ("Reflection Color", Color) = (1,1,1,0.5)
	_MainTex ("Base (RGB) RefStrGloss (A)", 2D) = "white" {}
	_Cube ("Reflection Cubemap", Cube) = "" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
}

SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
Pass { Name "UniversalForwardOnly" Tags { "LightMode"="UniversalForwardOnly" } Cull Off ZWrite On
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_REFLECT 1
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
#define SUIMONO_DEMO_REFLECT 1
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
#define SUIMONO_DEMO_REFLECT 1
#define SUIMONO_DEMO_DEPTH 1
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
}
SubShader {
	Tags { "RenderType"="Opaque" }
	cull off
	
CGPROGRAM
#pragma surface surf BlinnPhong
#pragma target 3.0

sampler2D _MainTex;
sampler2D _BumpMap;
samplerCUBE _Cube;

float4 _Color;
float4 _ReflectColor;
float _Shininess;

struct Input {
	float2 uv_MainTex;
	float2 uv_BumpMap;
	float3 worldRefl;
	INTERNAL_DATA
};

void surf (Input IN, inout SurfaceOutput o) {
	half4 tex = tex2D(_MainTex, IN.uv_MainTex);
	half4 c = tex * _Color;
	o.Albedo = c.rgb;
	
	o.Gloss = tex.a;
	o.Specular = _Shininess;
	
	o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
	
	float3 worldRefl = WorldReflectionVector (IN, o.Normal);
	half4 reflcol = texCUBE (_Cube, worldRefl);
	reflcol *= tex.a;
	o.Emission = reflcol.rgb * _ReflectColor.rgb;
	o.Alpha = reflcol.a * _ReflectColor.a;
}
ENDCG
}

FallBack "Reflective/Bumped Diffuse"
}
