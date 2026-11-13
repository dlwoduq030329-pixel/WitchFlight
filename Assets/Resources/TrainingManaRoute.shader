Shader "WitchFlight/Tutorial/Mana Route"
{
    Properties { [HDR] _Color("Guide color",Color)=(.1,1.8,2.6,.85) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes i)
            { Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float edge=abs(i.uv.y-.5)*2;
                float arrow=frac(i.uv.x*.12-_Time.y*.8+edge*.22);
                float stripe=1-smoothstep(.25,.45,arrow);
                return half4(_Color.rgb*(.45+stripe*.55),_Color.a*(.2+stripe*.8)*(1-smoothstep(.7,1,edge)));
            }
            ENDHLSL
        }
    }
}
