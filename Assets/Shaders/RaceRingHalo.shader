Shader "WitchFlight/Race Ring Halo"
{
    Properties
    {
        [HDR] _Color("Blue halo", Color) = (0.05,0.5,1.5,0.7)
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv - .5);
                float glow = exp(-pow((radius - .355) / .07, 2));
                glow *= 1 - smoothstep(.46, .5, radius);
                return half4(_Color.rgb, _Color.a * glow);
            }
            ENDHLSL
        }
    }
}
