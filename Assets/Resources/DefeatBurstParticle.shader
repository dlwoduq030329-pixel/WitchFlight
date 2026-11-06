Shader "WitchFlight/DefeatBurstParticle"
{
    Properties
    {
        _Flash ("Flash mode", Float) = 0
        _Brightness ("Brightness (HDR)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Flash, _Brightness;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2 - 1; o.color = v.color; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 a = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(a), hash(a+float2(1,0)), f.x),
                    lerp(hash(a+float2(0,1)), hash(a+1), f.x), f.y);
            }
            float4 frag(v2f i):SV_Target
            {
                float radius = length(i.uv);
                float alpha, shade;
                if (_Flash > 2.5)
                {
                    float n = noise(i.uv*4+13)*.65 + noise(i.uv*9+5)*.35;
                    float edge = radius + (n-.5)*.4;
                    alpha = (1-smoothstep(.2,.92,edge)) * lerp(.6,1,n);
                    shade = lerp(.6,1.6,saturate(1-radius));
                }
                else if (_Flash > 1.5)
                {
                    alpha = (1-smoothstep(.025,.09,abs(radius-.78))) * .8;
                    shade = 1;
                }
                else if (_Flash > .5)
                {
                    float core = exp(-radius*radius*8);
                    float halo = exp(-radius*radius*4) * .35;
                    float rays = (pow(saturate(1-abs(i.uv.x)*12),3) +
                        pow(saturate(1-abs(i.uv.y)*12),3)) * .35;
                    alpha = saturate(core+halo+rays) * (1-smoothstep(.65,1,radius));
                    shade = 1;
                }
                else
                {
                    float n = noise(i.uv*3.2+7)*.65 + noise(i.uv*8.5+19)*.35;
                    alpha = (1-smoothstep(.25,.94,radius+(n-.5)*.28)) * lerp(.55,1,n);
                    shade = lerp(.65,1.15,n) * lerp(.8,1.1,saturate(i.uv.y*.5+.5));
                }
                return float4(i.color.rgb * shade * _Brightness, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
