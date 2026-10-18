Shader "WitchFlight/FlareParticle"
{
    Properties { _Intensity ("Glow intensity", Range(0.1, 8)) = 2.4 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Intensity;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                o.color = v.color;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv,i.uv);
                float core = 1.0 - smoothstep(0.0,0.25,r2);
                float alpha = exp(-r2*2.0)*(1.0-smoothstep(0.55,1.0,r2))*i.color.a;
                float3 warmWhite = float3(1.0,0.92,0.66);
                return float4(lerp(i.color.rgb,warmWhite,core*0.85)*_Intensity,alpha);
            }
            ENDCG
        }
    }
}
