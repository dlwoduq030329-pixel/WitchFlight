Shader "WitchFlight/DeathMagicBeam"
{
    Properties
    {
        _CoreColor ("Dark core", Color) = (.025,.005,.05,1)
        [HDR] _RimColor ("Violet rim", Color) = (2,.2,4,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _CoreColor, _RimColor;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            float4 frag(v2f i):SV_Target
            {
                float y=abs(i.uv.y*2-1);
                float rim=smoothstep(.30,.58,y);
                float highlight=(1-smoothstep(.02,.12,abs(y-.58)));
                float3 color=lerp(_CoreColor.rgb,_RimColor.rgb,rim)+float3(1.5,1,2)*highlight;
                return float4(color, (1-smoothstep(.72,1,y))*i.color.a);
            }
            ENDCG
        }
    }
}
