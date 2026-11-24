Shader "WitchFlight/UI/Ice Screen Frost"
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
                float n=frost(p*32);
                float mask=1-smoothstep(_BorderWidth*.3,_BorderWidth*(.7+n*.7),edge);
                // Hard-clear central view; the reference checkerboard is not part of the effect.
                clip(mask-.001);
                float2 q=p*90+float2(noise(p*21),noise(p*21+17))*2;
                float ridges=pow(saturate(1-abs(noise(q)*2-1)),18);
                float grains=pow(noise(p*480),9);
                float branches=pow(saturate(1-abs(noise(p*170+float2(n*8,n*4))*2-1)),24);
                // Sparse six-fold crystals, matching the reference's icy perimeter.
                float2 cell=floor(p*23), f=frac(p*23)-.5;
                f-=float2(hash(cell+2),hash(cell+9))*.28-.14;
                float angle=atan2(f.y,f.x)+hash(cell)*6.283185;
                float r=length(f);
                float spoke=1-smoothstep(.015,.045,abs(sin(angle*3))*r);
                float flake=spoke*(1-smoothstep(.15,.32,r))*step(.8,hash(cell+5));
                float ice=saturate(ridges*.5+branches*.65+grains+flake*.8);
                float outer=1-smoothstep(0,_BorderWidth*.55,edge);
                float3 blue=lerp(float3(.08,.19,.34),float3(.45,.7,.95),outer*.65+n*.3);
                float3 rgb=lerp(blue,float3(.84,.95,1),ice)*i.color.rgb;
                float alpha=mask*saturate(.12+outer*.48+ice*.65)*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return float4(rgb,alpha);
            }
            ENDCG
        }
    }
}
