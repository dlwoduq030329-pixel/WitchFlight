Shader "WitchFlight/VFX/Blade Slash Trail"
{
    Properties
    {
        _Color("Color", Color) = (.7,.25,1,1)
        _MainTex("Trail texture (RGB + alpha)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A v) { V o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float edge=pow(saturate(1-i.uv.y),8);
                float alpha=smoothstep(0,.18,i.uv.x)*pow(saturate(1-i.uv.y),1.5);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(lerp(_Color.rgb,float3(1,.9,1),edge*.8)*tex.rgb,alpha*_Color.a*tex.a);
            }
            ENDHLSL
        }
    }
}
