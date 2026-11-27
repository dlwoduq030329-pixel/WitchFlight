Shader "WitchFlight/FlightClouds" {
Properties { _Color("Cloud",Color)=(.94,.97,1,.7) _Shade("Underside",Color)=(.56,.66,.82,1) }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" } Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
Pass { HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float4 _Color,_Shade;
float4 _HolesA[16],_HolesB[16];
struct A {float3 positionOS:POSITION;float2 uv:TEXCOORD0;};
struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float3 center:TEXCOORD2;float radius:TEXCOORD3;};
V vert(A v){V o;float3 center=TransformObjectToWorld(float3(0,0,0));float radius=length(unity_ObjectToWorld._m00_m10_m20);float3 right=UNITY_MATRIX_I_V._m00_m10_m20;float3 up=UNITY_MATRIX_I_V._m01_m11_m21;float height=length(unity_ObjectToWorld._m01_m11_m21);o.world=center+right*v.positionOS.x*radius+up*v.positionOS.y*height;o.pos=TransformWorldToHClip(o.world);o.uv=v.uv;o.center=center;o.radius=radius*.5;return o;}
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
half4 frag(V i):SV_Target {
float2 uv=i.uv*2-1;float r=length(uv);float n=noise(uv*5+i.center.xz*.07+_Time.y*.025);
float alpha=(1-smoothstep(.65,1,r+(n-.5)*.12))*_Color.a;
float3 toward=normalize(_WorldSpaceCameraPos-i.center);float3 world=i.world+toward*sqrt(saturate(1-r*r))*i.radius*.45;
float rim=0;
[unroll] for(int j=0;j<16;j++) {if(_HolesA[j].w<=0)continue;float3 a=_HolesA[j].xyz,b=_HolesB[j].xyz;float3 ab=b-a;float t=saturate(dot(world-a,ab)/max(dot(ab,ab),.001));float d=distance(world,a+t*ab);float radius=_HolesA[j].w;float keep=smoothstep(radius*.72,radius,d);alpha*=keep;alpha*=1-(1-smoothstep(radius,radius+4,d))*keep*.45;rim=max(rim,(1-smoothstep(radius,radius+1.4,d))*keep*_HolesB[j].w);}
float light=saturate(.55+uv.y*.3+sqrt(saturate(1-r*r))*.25+(n-.5)*.1);
float3 color=lerp(_Shade.rgb,_Color.rgb,light)+rim*.15;
alpha*=smoothstep(0,4,distance(_WorldSpaceCameraPos,world));
return half4(color,alpha);}
ENDHLSL } } }
