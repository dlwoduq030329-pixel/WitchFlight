#ifndef SUIMONO_URP_INCLUDED
#define SUIMONO_URP_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#define fixed half
#define fixed2 half2
#define fixed3 half3
#define fixed4 half4
#define UNITY_PI PI
#define UNITY_CAN_COMPILE_TESSELLATION 1
#define UNITY_PROJ_COORD(x) x
#define UnityObjectToClipPos(x) TransformObjectToHClip((x).xyz)
#define DecodeHDR DecodeHDREnvironment
struct SurfaceOutput { half3 Albedo; half3 Normal; half3 Emission; half Specular; half Gloss; half Alpha; };
static half4 _LightColor0;
float _SuimonoCaptureValid;
float SuimonoSceneEye(float2 uv) {
    float d=SampleSceneDepth(saturate(uv));
    return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(d) : LinearEyeDepth(d,_ZBufferParams);
}
half4 SuimonoScene(float4 uv, bool caustic);
float3 SuimonoWorldReflection(float3 p,float3 t,float3 b,float3 n,float3 tangentNormal) {
    return reflect(-GetWorldSpaceNormalizeViewDir(p),normalize(t*tangentNormal.x+b*tangentNormal.y+n*tangentNormal.z));
}
#include "SuimonoOriginalURP.hlsl"
half4 SuimonoScene(float4 uv, bool caustic) {
    float2 st=saturate(uv.xy/max(0.0001,uv.w));
    if (_SuimonoCaptureValid > 0.5 && _isPlaying > 0.5)
        return caustic ? tex2D(_suimono_CausticTex,st) : tex2D(_suimono_TransTex,st);
    return half4(SampleSceneColor(st),SuimonoSceneEye(st)*_ProjectionParams.w);
}
float4 _NormalTexS_ST, _FoamTex_ST, _WaveTex_ST;
struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; float2 uv1:TEXCOORD1; float2 uv2:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; float3 normalWS:TEXCOORD2; float4 tangentWS:TEXCOORD3; float4 screen:TEXCOORD4; float2 waveUV:TEXCOORD5; half fog:TEXCOORD6; UNITY_VERTEX_OUTPUT_STEREO };
Varyings SuimonoVertex(Attributes v) {
    Varyings o=(Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    appdata a=(appdata)0; a.vertex=float4(v.positionOS.xyz,1); a.normal=v.normalOS; a.tangent=v.tangentOS; a.texcoord=v.uv; a.texcoord1=v.uv1; a.texcoord2=v.uv2;
    #ifndef SUIMONO_FAR
    SuimonoVert(a);
    #endif
    o.positionWS=TransformObjectToWorld(a.vertex.xyz); o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.tangentWS=float4(TransformObjectToWorldDir(v.tangentOS.xyz),v.tangentOS.w*GetOddNegativeScale());
    o.uv=v.uv; o.waveUV=v.uv2; o.screen=ComputeScreenPos(o.positionCS); o.fog=ComputeFogFactor(o.positionCS.z); return o;
}
half4 SuimonoFragment(Varyings i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    Input input=(Input)0; input.uv_NormalTexS=i.uv*_NormalTexS_ST.xy+_NormalTexS_ST.zw;
    input.uv2_FoamTex=i.uv*_FoamTex_ST.xy+_FoamTex_ST.zw;
    input.uv3_WaveTex=i.waveUV*_WaveTex_ST.xy+_WaveTex_ST.zw; input.screenPos=i.screen;
    input.worldPosition=i.positionWS; input.basisN=normalize(i.normalWS); input.basisT=normalize(i.tangentWS.xyz);
    input.basisB=cross(input.basisN,input.basisT)*i.tangentWS.w;
    input.waterEyeDepth=-TransformWorldToView(i.positionWS).z;
    SurfaceOutput s=(SurfaceOutput)0; SuimonoSurf(input,s);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS)); _LightColor0=half4(light.color,1);
    half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
    // Surface-shader lighting functions operate in tangent space.
    half3 lightTS=half3(dot(light.direction,input.basisT),dot(light.direction,input.basisB),dot(light.direction,input.basisN));
    half3 viewTS=half3(dot(view,input.basisT),dot(view,input.basisB),dot(view,input.basisN));
    #ifdef SUIMONO_UNDER
    half4 col=LightingSuimonoLightUnder(s,lightTS,viewTS,light.shadowAttenuation);
    #else
    half4 col=LightingSuimonoLight(s,lightTS,viewTS,light.shadowAttenuation);
    #endif
    // The infinite-ocean mesh has very large triangles. Clamping fog at its
    // vertices makes the horizon a flat band; evaluate depth at the actual pixel.
    float fog = ComputeFogFactor(TransformWorldToHClip(i.positionWS).z);
    col.rgb=MixFog(col.rgb,fog); return col;
}
#ifdef SUIMONO_TESSELLATION
Attributes SuimonoControlPoint(Attributes v) { return v; }
struct TessFactors { float edge[3]:SV_TessFactor; float inside:SV_InsideTessFactor; };
TessFactors SuimonoPatch(InputPatch<Attributes,3> p) {
    UNITY_SETUP_INSTANCE_ID(p[0]); TessFactors f;
    float4 t=suimono_tess_on>0.5 ? UnityDistanceBasedTess(p[0].positionOS,p[1].positionOS,p[2].positionOS,_minDist,max(_minDist+.001,_maxDist),max(1,_Tess*_tessScale)) : 1;
    f.edge[0]=clamp(t.x,1,64); f.edge[1]=clamp(t.y,1,64); f.edge[2]=clamp(t.z,1,64); f.inside=clamp(t.w,1,64); return f;
}
[domain("tri")][partitioning("fractional_odd")][outputtopology("triangle_cw")][patchconstantfunc("SuimonoPatch")][outputcontrolpoints(3)]
Attributes SuimonoHull(InputPatch<Attributes,3> p,uint id:SV_OutputControlPointID) { return p[id]; }
[domain("tri")]
Varyings SuimonoDomain(TessFactors factors,const OutputPatch<Attributes,3> p,float3 b:SV_DomainLocation) {
    Attributes v=(Attributes)0; UNITY_TRANSFER_INSTANCE_ID(p[0],v);
    v.positionOS=p[0].positionOS*b.x+p[1].positionOS*b.y+p[2].positionOS*b.z;
    v.normalOS=normalize(p[0].normalOS*b.x+p[1].normalOS*b.y+p[2].normalOS*b.z);
    v.tangentOS=p[0].tangentOS*b.x+p[1].tangentOS*b.y+p[2].tangentOS*b.z;
    v.uv=p[0].uv*b.x+p[1].uv*b.y+p[2].uv*b.z; v.uv1=p[0].uv1*b.x+p[1].uv1*b.y+p[2].uv1*b.z; v.uv2=p[0].uv2*b.x+p[1].uv2*b.y+p[2].uv2*b.z;
    return SuimonoVertex(v);
}
#endif
#endif

