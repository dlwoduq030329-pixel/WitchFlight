#define _SPECULAR_COLOR 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
sampler2D _MainTex,_BumpMap;samplerCUBE _Cube,_ReflectionTex;
float4 _MainTex_ST,_BumpMap_ST,_Color,_SpecColor,_ReflectColor;
float _Shininess,_Cutoff,_Cutoff2;
float3 _LightDirection,_LightPosition;
struct DemoAttributes {float3 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct DemoVaryings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;half fog:TEXCOORD4;UNITY_VERTEX_OUTPUT_STEREO};
DemoVaryings DemoVertex(DemoAttributes v) {
 DemoVaryings o=(DemoVaryings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 o.positionWS=TransformObjectToWorld(v.positionOS);o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.tangentWS=half4(TransformObjectToWorldDir(v.tangentOS.xyz),v.tangentOS.w*GetOddNegativeScale());
 o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=v.uv;o.fog=ComputeFogFactor(o.positionCS.z);
#ifdef SUIMONO_DEMO_SHADOW
 float3 lightDirection=_LightDirection;
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
 lightDirection=normalize(_LightPosition-o.positionWS);
#endif
 o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection));
#if UNITY_REVERSED_Z
 o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
#else
 o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
#endif
#endif
 return o;
}
half4 DemoFragment(DemoVaryings i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 half4 tex=tex2D(_MainTex,i.uv*_MainTex_ST.xy+_MainTex_ST.zw);
#ifdef SUIMONO_DEMO_FLORA
 clip(tex.a-max(_Cutoff,_Cutoff2));
#elif defined(SUIMONO_DEMO_HAIR)
 clip(tex.a*_Color.a-_Cutoff);
#endif
#if defined(SUIMONO_DEMO_SHADOW) || defined(SUIMONO_DEMO_DEPTH)
 return 0;
#else
 half3 normalTS=UnpackNormal(tex2D(_BumpMap,i.uv*_BumpMap_ST.xy+_BumpMap_ST.zw));
 half3 n=normalize(i.normalWS);half3 t=normalize(i.tangentWS.xyz);half3 b=cross(n,t)*i.tangentWS.w;n=normalize(t*normalTS.x+b*normalTS.y+n*normalTS.z);
 half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);half3 albedo=tex.rgb*_Color.rgb;half gloss=tex.a;half power=_Shininess;half3 emission=0;
#ifdef SUIMONO_DEMO_HAIR
 albedo=_Color.rgb*tex.r;gloss=saturate(dot(view,n))*tex.b;
#elif defined(SUIMONO_DEMO_SKIN)
 half3 reflection=texCUBE(_ReflectionTex,reflect(-view,n)).rgb*_SpecColor.rgb*2;
 gloss=tex.a*_Shininess;power=max(.01,tex.a*reflection.r*2);albedo+=reflection*tex.a*_Shininess*.25;
#elif defined(SUIMONO_DEMO_FLORA)
 albedo=tex.rgb*2;
#else
 emission=texCUBE(_Cube,reflect(-view,n)).rgb*tex.a*_ReflectColor.rgb;
#endif
 InputData inputData=(InputData)0;inputData.positionWS=i.positionWS;inputData.normalWS=n;inputData.viewDirectionWS=view;
 inputData.shadowCoord=TransformWorldToShadowCoord(i.positionWS);inputData.bakedGI=SampleSH(n);inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);inputData.shadowMask=1;
#ifdef SUIMONO_DEMO_FLORA
 SurfaceData surface=(SurfaceData)0;surface.albedo=albedo;surface.smoothness=.2;surface.occlusion=1;surface.alpha=1;surface.normalTS=normalTS;
 half4 color=UniversalFragmentPBR(inputData,surface);
#else
 SurfaceData surface=(SurfaceData)0;surface.albedo=albedo;surface.specular=_SpecColor.rgb*gloss;
 surface.smoothness=(log2(max(.001,power)*128)-1)/10;surface.emission=emission;surface.occlusion=1;surface.alpha=1;surface.normalTS=normalTS;
 half4 color=UniversalFragmentBlinnPhong(inputData,surface);
#endif
 color.rgb=MixFog(color.rgb,i.fog);return color;
#endif
}
