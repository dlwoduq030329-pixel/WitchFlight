Shader "WitchFlight/Blade Energy"
{
    Properties { [HDR] _Color("Edge Color", Color) = (1.8,0.25,4,1) }
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
                half core = 1 - smoothstep(0, .3, input.uv.y);
                half alpha = (1 - input.uv.y) * saturate(sin(input.uv.x * 3.14159) * 3);
                return half4(lerp(_Color.rgb, half3(3,2,4), core), _Color.a * alpha);
            }
            ENDHLSL
        }
    }
}
