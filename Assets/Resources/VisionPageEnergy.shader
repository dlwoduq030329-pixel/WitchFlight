Shader "WitchFlight/VisionPageEnergy"
{
    Properties { _Spark("Spark", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float _Spark;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.color = v.color; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 a = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),
                    lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);
            }
            half4 frag(Varyings i):SV_Target
            {
                float edge = saturate(1 - abs(i.uv.y * 2 - 1));
                // UV.x runs from the page (0) to the rising tip (1).
                float pulse = pow(.5 + .5 * sin(i.uv.x * 18 - _Time.y * 5), 3);
                float ribbon = pow(edge, 2.5) * (.3 + .7 * pulse);
                float2 flow = i.uv * 5 + float2(_Time.y * .04, -_Time.y * .12);
                float cloud = noise(flow) * .65 + noise(flow * 2.1) * .35;
                float spark = pow(saturate(1 - length(i.uv * 2 - 1)), 1.5) * smoothstep(.12,.8,cloud);
                return half4(i.color.rgb, i.color.a * lerp(ribbon, spark, _Spark));
            }
            ENDHLSL
        }
    }
}
