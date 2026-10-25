#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#ifdef SUIMONO_OVERLAY
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif
sampler2D _MainTex, _MaskMap, _HeightMap;
float4 _MainTex_ST, _MaskMap_ST, _TintColor, _DepthColor;
float _InvFade, _Strength, _Overlay, _Brightness;
struct Attributes { float3 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float depth:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
Varyings ParticleVert(Attributes v) { Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); VertexPositionInputs p=GetVertexPositionInputs(v.vertex); o.positionCS=p.positionCS; o.depth=-p.positionVS.z; o.color=v.color; o.uv=v.uv; return o; }
half4 ParticleFrag(Varyings i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 screenUV=GetNormalizedScreenSpaceUV(i.positionCS);
 float depth=max(0,LinearEyeDepth(SampleSceneDepth(screenUV),_ZBufferParams)-i.depth);
 #ifdef SUIMONO_SPLASH
 half mask=tex2D(_MaskMap,i.uv*_MaskMap_ST.xy+_MaskMap_ST.zw).r*i.color.a;
 float2 offset=(tex2D(_HeightMap,i.uv).rg*2-1)*_Strength*.0002*mask;
 half3 col=SampleSceneColor(saturate(screenUV+offset))*_Brightness;
 col=lerp(col,_DepthColor.rgb,_Overlay*_DepthColor.a);
 return half4(col,mask*saturate(depth));
 #elif defined(SUIMONO_OVERLAY)
 // Preserve the original splash/ waterfall brightness model, while allowing
 // particle tint and lifetime alpha to fade the effect in URP.
 half4 tex=tex2D(_MainTex,i.uv*_MainTex_ST.xy+_MainTex_ST.zw);
 Light light=GetMainLight();
 half3 rgb=(tex.rgb*_TintColor.rgb+tex.a)*light.color*2*i.color.rgb;
 half alpha=saturate(tex.a*2)*i.color.a*_TintColor.a;
 return half4(rgb,alpha*saturate(depth*max(.01,_InvFade)));
 #else
 half4 col=tex2D(_MainTex,i.uv*_MainTex_ST.xy+_MainTex_ST.zw)*i.color*_TintColor*2;
 col.a*=saturate(depth*max(.01,_InvFade)); return col;
 #endif
}
