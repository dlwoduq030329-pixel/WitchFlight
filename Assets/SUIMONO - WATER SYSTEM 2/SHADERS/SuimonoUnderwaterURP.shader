Shader "Hidden/Suimono/UnderwaterURP" {
SubShader { Tags { "RenderPipeline"="UniversalPipeline" }
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
sampler2D _DistortTex,_DropsTex;
float4 _WaterColor,_WaterFog,_WaterDistort,_FogOptions,_HeightFog,_Transition,_TransitionOptions;
float _WaterLevel;
half4 _WaterLight;
TEXTURE2D_X_FLOAT(_SuimonoWaterDepth);
half3 Scene(float2 uv) {return SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,saturate(uv)).rgb;}
float3 WorldAt(float2 uv) {
 float raw=SampleSceneDepth(uv);
 if(_WaterFog.w>.5) {
   float water=SAMPLE_TEXTURE2D_X(_SuimonoWaterDepth,sampler_PointClamp,uv).r;
#if UNITY_REVERSED_Z
   raw=max(raw,water);
#else
   raw=min(raw,water);
#endif
 }
#if !UNITY_REVERSED_Z
 raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
#endif
 return ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);
}
float FogAt(float2 uv,float start) {
 float3 world=WorldAt(saturate(uv));
 float3 ray=world-_WorldSpaceCameraPos;
 float eye=max(0,-TransformWorldToView(world).z);
 float dist=lerp(eye,length(ray),_FogOptions.z)-_ProjectionParams.y;
 float fogDistance=dist*_FogOptions.x-start;
 if(_FogOptions.y>.5) {
   float fc=_WorldSpaceCameraPos.y-_HeightFog.x,fp=world.y-_HeightFog.x,k=step(fc,0);
   float g=min((1-2*k)*fp,0);
   fogDistance+=-length(_HeightFog.y*.5*ray)*(k*(fp+fc)-g*g/max(abs(ray.y),.00001));
 }
 return saturate((max(0,fogDistance)-_WaterFog.x)/max(.001,_WaterFog.y-_WaterFog.x));
}
half4 Frag(Varyings i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 uv=i.texcoord;
 float3 distort=UnpackNormal(tex2D(_DistortTex,uv*_WaterDistort.y+float2(_Time.x*_WaterDistort.z,-_Time.x*.5*_WaterDistort.z)));
 float2 duv=uv;duv.x+=distort.x*_WaterDistort.x+lerp(_WaterDistort.x,-_WaterDistort.x*.5,uv.x);
 half3 result=Scene(uv);
 if(_WaterFog.w>.5) {
   float3 direction=normalize(WorldAt(duv)-_WorldSpaceCameraPos);
   float depthFac=saturate(lerp(.3,1.6,dot(direction,float3(0,-1,0))));
   Light sun=GetMainLight();
   half3 fogColor=_WaterColor.rgb*.65;
#ifdef UNITY_COLORSPACE_GAMMA
   fogColor*=2.2;
#endif
   // Keep downward/deep views readable instead of fading nearly to black.
   fogColor=lerp(fogColor,fogColor*.55,depthFac);
   float lightFac=saturate(lerp(0,.8,dot(direction,sun.direction))-depthFac);
   fogColor=lerp(fogColor,fogColor*2,lightFac);
   float lightFac2=saturate(lerp(-5,.5,dot(direction,sun.direction))-depthFac)*2;
   fogColor=lerp(fogColor,fogColor*1.4,lightFac2);
   fogColor*=_WaterLight.rgb*_WaterFog.z;
   float heightDark=saturate(lerp(-.2,1,saturate(max(0,_WaterLevel-_WorldSpaceCameraPos.y)/_HeightFog.z)));
   fogColor=lerp(fogColor,fogColor*.35,heightDark);
   // Original ten depth-aware blur offsets. Each sample receives its own fog amount.
   const float2 taps[10]={float2(1,1),float2(-1,-1),float2(1,-1),float2(-1,1),float2(-1,1),float2(1,-1),float2(.5,.5),float2(-.5,-.5),float2(.25,-.25),float2(.25,-.25)};
   float2 blur=_BlitTexture_TexelSize.xy*_WaterDistort.w*4*FogAt(duv,5);
   half3 sum=0;
   [unroll] for(int n=0;n<10;n++) {float2 sampleUV=duv+blur*taps[n];sum+=lerp(Scene(sampleUV),fogColor,FogAt(sampleUV,_FogOptions.w));}
   // Each tap already contains fog and lighting; applying both again crushes visibility.
   result=sum*.1;
 } else {
   float3 baseTex=UnpackNormal(tex2D(_DistortTex,uv*.4-float2(_Time.x*1.3,_Time.x*1.3)));
   float baseDis=uv.x+baseTex.x*.05;
   float2 maskUV=uv*.1+float2(uv.x*.1+baseDis,_Time.x);
   float3 screenTex=UnpackNormal(tex2D(_DistortTex,maskUV));
   float mask1=tex2D(_DistortTex,maskUV).r;
   float2 suv=uv;suv.x+=screenTex.x*.08+lerp(.08,-.04,suv.x);
   mask1=saturate(lerp(.1-_Transition.x,11-11*_Transition.x,mask1));
   half3 tint=lerp(half3(1,1,1),half3(.1,.6,.8),.2);
   float strength=_TransitionOptions.x;
   result=lerp(result,result*.9*tint,mask1*strength);
   result=lerp(result,Scene(suv),saturate(mask1*1.2*strength));
   float total=mask1;
   [unroll] for(int d=0;d<2;d++) {
     float2 scale=d==0?float2(.4,.23):float2(.8,.6);
     float speed=d==0?.11:.075;
     float mask=tex2D(_DropsTex,uv*scale+float2(_Transition.z,_Time.x*speed+_Transition.w)).r;
     float2 ruv=uv;ruv.x+=mask*.1+lerp(mask*.1,mask*-.05,ruv.x);
     mask=saturate(lerp(.2-_Transition.y,2-2*_Transition.y,mask));
     result=lerp(result,result*.9*tint,mask*strength);
     result=lerp(result,lerp(Scene(ruv),Scene(suv),mask*.1),mask*1.2*strength);total+=mask;
   }
   if(_TransitionOptions.y>.5) result=saturate(total).xxx;
 }
 return half4(result,1);
}
ENDHLSL
}}}
