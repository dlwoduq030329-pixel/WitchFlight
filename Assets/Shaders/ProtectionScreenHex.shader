Shader "WitchFlight/UI/Protection Screen Hex"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BorderWidth ("Border Width", Range(.03,.35)) = .18
        _Aspect ("Aspect", Float) = 1.77778
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            float _BorderWidth, _Aspect;
            float4 _ClipRect;
            v2f vert(appdata v) { v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float frost(float2 p) { return noise(p)*.55+noise(p*2.13)*.3+noise(p*4.37)*.15; }
            float4 frag(v2f i):SV_Target
            {
                float2 p=i.uv*float2(_Aspect,1);
                float edge=min(min(i.uv.x,1-i.uv.x)*_Aspect,min(i.uv.y,1-i.uv.y));
                float mask=1-smoothstep(_BorderWidth*.2,_BorderWidth,edge);
                clip(mask-.001);
                // Staggered hexagonal cells, with a narrow transparent seam.
                float2 q=p*9;
                float2 grid=float2(1.7320508,3);
                float2 a=q-grid*floor(q/grid)-grid*.5;
                float2 b=(q-float2(.8660254,1.5));
                b=b-grid*floor(b/grid)-grid*.5;
                float2 cell=dot(a,a)<dot(b,b)?a:b;
                float2 h=abs(cell);
                float hex=max(h.x*.8660254+h.y*.5,h.y);
                float aa=max(fwidth(hex),.006);
                float tile=1-smoothstep(.82-aa,.86+aa,hex);
                float glow=pow(saturate(1-hex),.65);
                float pulse=.92+.08*sin(_Time.y*2.5+p.x*2+p.y*3);
                float3 rgb=lerp(float3(.015,.19,.36),float3(.015,.65,.95),glow)*i.color.rgb;
                float alpha=mask*tile*(.48+glow*.48)*pulse*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return float4(rgb,alpha);
            }
            ENDCG
        }
    }
}
