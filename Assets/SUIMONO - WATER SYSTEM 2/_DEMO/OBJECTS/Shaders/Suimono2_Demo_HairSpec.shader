Shader "Suimono2/Demo/HairSpec" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_SpecColor ("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
	_Shininess ("Shininess", Range (0.03, 2.0)) = 0.078125
	_Cutoff ("Cutoff Range", Range (0.0, 1.0)) = 0.1
	_MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
	_ReflectionTex ("Reflection", CUBE) = "" {}
	_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0.0)
    _RimPower ("Rim Power", Range(0.01,1.0)) = 1.0
    _RimColor2 ("Rim Color2", Color) = (0.26,0.19,0.16,0.0)
    _RimPower2 ("Rim Power2", Range(0.1,8.0)) = 3.0
}

SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
Pass { Name "UniversalForwardOnly" Tags { "LightMode"="UniversalForwardOnly" } Cull Back ZWrite On
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_HAIR 1
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
Pass { Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" } Cull Back ZWrite On
ColorMask 0
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_HAIR 1
#define SUIMONO_DEMO_SHADOW 1
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
Pass { Name "DepthOnly" Tags { "LightMode"="DepthOnly" } Cull Back ZWrite On
ColorMask 0
HLSLPROGRAM
#pragma target 3.5
#pragma vertex DemoVertex
#pragma fragment DemoFragment
#pragma multi_compile_instancing
#define SUIMONO_DEMO_HAIR 1
#define SUIMONO_DEMO_DEPTH 1
#include "Assets/SUIMONO - WATER SYSTEM 2/SHADERS/SuimonoDemoURP.hlsl"
ENDHLSL
}
}
SubShader {
	Tags {"IgnoreProjector"="True" "RenderType"="TransparentCutout"}
	LOD 400
	
CGPROGRAM
#pragma target 3.0
#pragma surface surf BlinnPhong alphatest:_Cutoff

sampler2D _MainTex;
sampler2D _BumpMap;
float4 _Color;
float _Shininess;

struct Input {
	float2 uv_MainTex;
	float2 uv_BumpMap;
	float3 viewDir;
};

void surf (Input IN, inout SurfaceOutput o) {
	half4 tex = tex2D(_MainTex, IN.uv_MainTex);
	o.Albedo = _Color.rgb * tex.r;
	//o.Gloss = tex.b;
	o.Alpha = tex.a * _Color.a;
	o.Specular = _Shininess;
	o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
	
	half rim = saturate(dot(normalize(IN.viewDir), o.Normal));
	o.Gloss = rim * tex.b;
	
}
ENDCG
}

FallBack "Transparent/Cutout/VertexLit"
}
