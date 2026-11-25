Shader "WitchFlight/VisionPageEnergy"
{
    Properties { _Spark("Spark", Float) = 0 _Steady("Steady bolt", Float) = 0 _Aura("Rising energy", Float) = 0 [HDR] _AuraTint("Aura tint",Color) = (.58,.18,1,1) [HDR] _AuraBlue("Aura blue",Color) = (.12,.38,1,1) _AuraClock("Aura time",Float) = 0 _AuraOpacity("Aura opacity",Float) = .45 _AuraBend("Aura bend",Vector) = (0,0,0,0) _Mote("Light speck",Float) = 0 }
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
            float _Steady;
            float _Aura;
            float4 _AuraTint;
            float4 _AuraBlue;
            float _AuraClock;
            float _AuraOpacity;
            float4 _AuraBend;
            float _Mote;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                if (_Aura > .5)
                {
                    float angle = v.uv.y * 6.2831853;
                    float bend = sin(angle*5-v.uv.x*5+_AuraClock*4)*.035*v.uv.x;
                    v.positionOS.xz += float2(cos(angle),sin(angle))*bend;
                }
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                if (_Aura > .5)
                {
                    float h = v.uv.x;
                    float sway = sin(_AuraClock*3.1-h*4)*.035;
                    // Movement bend is applied to mesh vertices on the CPU.
                }
                o.positionCS = TransformWorldToHClip(world);
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
                if (_Mote > .5)
                {
                    float r = length(i.uv*2-1);
                    float glow = exp(-r*r*6)*saturate(1-r);
                    return half4(i.color.rgb, i.color.a*glow);
                }
                float edge = saturate(1 - abs(i.uv.y * 2 - 1));
                // UV.x runs from the page (0) to the rising tip (1).
                float pulse = pow(.5 + .5 * sin(i.uv.x * 18 - _Time.y * 5), 3);
                float ribbon = pow(edge, 2.5) * lerp(.3 + .7 * pulse, 1.0, _Steady);
                float2 flow = i.uv * 5 + float2(_Time.y * .04, -_Time.y * .12);
                float cloud = noise(flow) * .65 + noise(flow * 2.1) * .35;
                float spark = pow(saturate(1 - length(i.uv * 2 - 1)), 1.5) * smoothstep(.12,.8,cloud);
                if (_Aura > .5)
                {
                    float h = i.uv.x;
                    float angle = i.uv.y*6.2831853;
                    // Periodic angular coordinates avoid a seam; all structure advects upward.
                    float travel = h-_AuraClock;
                    float2 ring = float2(cos(angle),sin(angle));
                    float warp = noise(ring*2.3+float2(travel*1.6,travel*2.1));
                    float folds = .5+.5*sin(angle*7+(warp-.5)*3.0-travel*2.0);
                    float detail = noise(ring*4.0+float2(travel*3.4,travel*2.8));
                    // Connected lower sheet with irregular long tips, not a uniform fog cylinder.
                    float crest = .4 + .48*pow(folds,1.3) + .10*(detail-.5);
                    float top = 1-smoothstep(crest-.22,crest,h);
                    float base = smoothstep(0,.10,h);
                    float bands = .25+.75*smoothstep(.25,.8,detail);
                    float upperFade = 1-smoothstep(.18,.82,h);
                    float density = base*top*pow(folds,2)*bands*upperFade;
                    half3 arcane = lerp(_AuraTint.rgb,_AuraBlue.rgb,smoothstep(.2,.85,detail));
                    half3 tint = lerp(arcane,half3(1.5,1.3,1.7),saturate(density)*.7);
                    return half4(tint, density*_AuraOpacity);
                }
                return half4(i.color.rgb, i.color.a * lerp(ribbon, spark, _Spark));
            }
            ENDHLSL
        }
    }
}
