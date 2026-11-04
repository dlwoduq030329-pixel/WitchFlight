Shader "WitchFlight/Raid Effect"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1,0.2,0.1,0.2)
        _Mode("0 volume, 1 magic circle, 2 flame", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Mode;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _Color;
                if (_Mode > .5 && _Mode < 1.5)
                {
                    float2 circleUV = input.uv - .5;
                    float radius = length(circleUV);
                    float angle = atan2(circleUV.y, circleUV.x);
                    float rings = 1 - smoothstep(.008, .018, min(abs(radius - .43), abs(radius - .31)));
                    float glyphs = step(.65, cos(angle * 12)) * step(.33, radius) * step(radius, .4);
                    float star = (1 - smoothstep(.008, .016, abs(circleUV.x) * abs(circleUV.y))) * step(radius, .22);
                    color.a *= saturate(rings + glyphs + star);
                }
                else if (_Mode > 1.5)
                {
                    float noise = .65 + .35 * sin(input.uv.y * 45 - _Time.y * 20 + sin(input.uv.x * 30) * 3);
                    color.rgb *= noise + .7;
                    color.a *= noise;
                }
                else
                {
                    float edge = 1 - abs(dot(normalize(input.normalWS), normalize(GetWorldSpaceViewDir(input.positionWS))));
                    color.a *= .25 + .75 * edge * edge;
                }
                return color;
            }
            ENDHLSL
        }
    }
}
