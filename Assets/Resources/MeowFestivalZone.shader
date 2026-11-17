Shader "WitchFlight/VFX/Meow Festival Zone"
{
    Properties { _ZoneColor("Zone color", Color) = (.15,1,.38,.22) _Phase("Rise phase", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _ZoneColor;
            float _Phase;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 local:TEXCOORD1; float wall:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.local = v.positionOS.xyz; o.uv = v.uv; o.wall = v.color.r;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float radius = length(i.local.xz);
                float aa = max(fwidth(radius), .001);
                float rim = 1 - smoothstep(.007, .007 + aa, abs(radius - .986));
                float halo = exp(-abs(radius - .976) * 65) * .32;
                float innerRing = (1 - smoothstep(.002, .002 + aa, abs(radius - .935))) * .22;
                float pulseRadius = frac(_Phase * .65);
                float pulse = exp(-pow((radius - pulseRadius) * 38, 2));
                pulse *= smoothstep(.08, .3, pulseRadius) * (1 - smoothstep(.75, 1, pulseRadius)) * .2;
                float disk = .055 + halo + rim + innerRing + pulse;
                // Low, broken ribbons lift from the rim; no opaque cylindrical wall.
                float arcs = pow(saturate(.5 + .5 * sin(i.uv.x * 37.699 - _Phase * 2)), 6);
                float lift = frac(_Phase + i.uv.x * 3);
                float ribbon = exp(-pow((i.uv.y - lift) * 8, 2));
                float wall = pow(1 - i.uv.y, 3) * (.07 + arcs * ribbon * .55);
                float highlight = saturate(rim * (1 - i.wall) * .6);
                float3 tint = lerp(_ZoneColor.rgb, float3(.65, 1, .78), highlight);
                return half4(tint, saturate(_ZoneColor.a * lerp(disk, wall, i.wall)));
            }
            ENDHLSL
        }
    }
}
