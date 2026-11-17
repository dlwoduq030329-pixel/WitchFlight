Shader "WitchFlight/VFX/Runic Greatsword"
{
    Properties
    {
        _BodyColor("Blade body", Color) = (.045,.08,.18,1)
        [HDR] _RuneColor("Rune emission", Color) = (.2,3,4,1)
        [HDR] _EdgeColor("Silver edge", Color) = (.65,.85,1.2,1)
        _RuneCount("Rune count", Range(3,14)) = 7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BodyColor, _RuneColor, _EdgeColor;
            float _RuneCount;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalOS:TEXCOORD1; };
            V Vert(A v)
            {
                V o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv; o.normalOS=v.normalOS; return o;
            }
            float Segment(float2 p,float2 a,float2 b)
            {
                float2 ab=b-a;
                return length(p-a-ab*saturate(dot(p-a,ab)/max(dot(ab,ab),.00001)));
            }
            half4 Frag(V i):SV_Target
            {
                float x=abs(i.uv.x-.5);
                float bevel=1-smoothstep(.65,.95,abs(normalize(i.normalOS).y));
                float border=smoothstep(.27,.34,x);
                float row=i.uv.y*_RuneCount;
                float2 p=float2((i.uv.x-.5)*3,frac(row)-.5);
                float variant=fmod(floor(row),3);
                float d=Segment(p,float2(0,-.32),float2(0,.32));
                d=min(d,Segment(p,float2(-.25,.05),float2(0,.3)));
                d=min(d,Segment(p,float2(0,.3),float2(.25,.05)));
                if(variant<1.5)d=min(d,Segment(p,float2(-.23,-.05),float2(.23,-.25)));
                if(variant>.5)d=min(d,Segment(p,float2(-.23,-.25),float2(.23,-.05)));
                float aa=max(fwidth(d),.003);
                float rune=1-smoothstep(.022-aa,.022+aa,d);
                rune*=smoothstep(.08,.13,i.uv.y)*(1-smoothstep(.78,.87,i.uv.y));
                float glow=exp(-d*24)*.2;
                glow*=step(.1,i.uv.y)*step(i.uv.y,.85);
                float3 body=lerp(_BodyColor.rgb,_EdgeColor.rgb,max(bevel,border));
                float rails=(1-smoothstep(.004,.014,abs(x-.25)))*step(.1,i.uv.y)*step(i.uv.y,.8);
                return half4(body+_RuneColor.rgb*(rune+glow+rails*.35),1);
            }
            ENDHLSL
        }
    }
}
