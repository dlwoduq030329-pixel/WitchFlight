Shader "Custom/MaskedObject"
{

Properties 
{
    _Mask("Mask", Int) = 1
    _Color ("Color", Color) = (1,1,1,1)
    _MainTex ("Albedo (RGB)", 2D) = "white" {}
    _Glossiness ("Smoothness", Range(0,1)) = 0.5
    _Metallic ("Metallic", Range(0,1)) = 0.0

	[NoScaleOffset] _BumpMap    ("Normal Map"  , 2D         ) = "bump" {}
	_BumpScale  ("Normal Scale", Range(0, 1)) = 1.0

    _Cutoff     ("Cutoff"      , Range(0, 1)) = 0.5
}

SubShader 
{

Tags 
{ 
    "Queue"      = "Transparent"//"AlphaTest"
	"RenderType" = "TransparentCutout"
}

Stencil 
{
    Ref [_Mask]
    Comp Equal
}
    
CGPROGRAM

//#pragma surface surf Standard fullforwardshadows
#pragma surface surf Standard fullforwardshadows alphatest:_Cutoff
#pragma target 3.0

sampler2D _MainTex;
sampler2D _EmissionTex;

struct Input 
{
    float2 uv_MainTex;
    
    float2 uv_BumpMap2;
};

half _Glossiness;
half _Metallic;
fixed4 _Color;

			sampler2D _BumpMap;
			sampler2D _BumpMap2;
			half _BumpScale;
            half _BumpScale2;

void surf(Input IN, inout SurfaceOutputStandard o) 
{
    fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
    o.Albedo = c.rgb;
    o.Metallic = _Metallic;
    o.Smoothness = _Glossiness;
    o.Alpha = c.a;


	fixed3 n  = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
    fixed3 n2 = UnpackScaleNormal(tex2D(_BumpMap2, IN.uv_BumpMap2), _BumpScale2);	
	o.Normal =  BlendNormals(n, n2);
}

ENDCG

}
    FallBack "Diffuse"
}
