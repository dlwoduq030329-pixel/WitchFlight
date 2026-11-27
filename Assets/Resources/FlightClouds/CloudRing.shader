Shader "WitchFlight/CloudRing" { Properties {_Color("Color",Color)=(1,1,1,1)} SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha Pass {HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct A{float3 p:POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float4 c:COLOR;float2 uv:TEXCOORD0;};V vert(A a){V o;o.p=TransformObjectToHClip(a.p);o.c=a.c;o.uv=a.uv;return o;}half4 frag(V i):SV_Target{return half4(i.c.rgb,i.c.a*pow(saturate(1-abs(i.uv.y*2-1)),2));}
ENDHLSL } } }