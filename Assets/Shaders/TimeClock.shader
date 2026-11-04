Shader "WitchFlight/Time Clock"
{
    Properties { [HDR] _Color("Emission", Color) = (0.4,2,4,0.85) }
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
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            Varyings Vert(Attributes input) { Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; return o; }
            half4 Frag(Varyings input):SV_Target
            {
                half alpha=_Color.a;
                if(input.uv.x>.5) alpha*=.35*(1-input.uv.y)*(.25+.75*pow(saturate(sin(_Time.y*3-input.uv.y*9)),2));
                return half4(_Color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
