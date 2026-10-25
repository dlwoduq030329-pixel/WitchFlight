Shader "Hidden/Suimono/CaptureURP" {
SubShader { Tags { "RenderPipeline"="UniversalPipeline" }
HLSLINCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
float4 _ShoreDepth;
float _BlurRadius;
half4 Pack(Varyings i):SV_Target {
 half4 c=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);
 float d=SampleSceneDepth(i.texcoord);
 c.a=(unity_OrthoParams.w>.5 ? LinearDepthToEyeDepth(d) : LinearEyeDepth(d,_ZBufferParams))*_ProjectionParams.w; return c;
}
half4 Shore(Varyings i):SV_Target {
 float d=SampleSceneDepth(i.texcoord); 
#if UNITY_REVERSED_Z
 d=1-d;
#endif
 return half4(1-_ShoreDepth.x*.5*d,saturate((1-_ShoreDepth.y*.5*d)*3),1-saturate(lerp(-.25,17,d)*2),0);
}
half4 Gaussian(float2 uv,float2 axis) {
 const half weights[7]={.0205,.0855,.232,.324,.232,.0855,.0205};
 half4 c=0;
 [unroll] for(int n=0;n<7;n++) c.rgb+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+_BlitTexture_TexelSize.xy*axis*_BlurRadius*(n-3)).rgb*weights[n];
 c.a=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).a;return c;
}
half4 Blur(Varyings i):SV_Target {return Gaussian(i.texcoord,float2(0,1));}
half4 BlurHorizontal(Varyings i):SV_Target {return Gaussian(i.texcoord,float2(1,0));}
half4 Downsample(Varyings i):SV_Target {
 float2 d=_BlitTexture_TexelSize.xy;
 return .25*(SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord+d)+SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord+d*float2(-.5,-.5))+SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord+d*float2(.5,-.5))+SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord+d*float2(-.5,.5)));
}
ENDHLSL
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Pack
ENDHLSL
}
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Shore
ENDHLSL
}
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Blur
ENDHLSL
}
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Downsample
ENDHLSL
}
Pass { ZWrite Off ZTest Always Cull Off
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment BlurHorizontal
ENDHLSL
}
}}


