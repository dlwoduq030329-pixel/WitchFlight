Shader "WitchFlight/VisionFloatingRunes"
{
    Properties { [HDR] _Tint ("Tint", Color) = (1,1,1,1) }
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
            half4 _Tint;
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            V vert(A a) { V o; o.pos=TransformObjectToHClip(a.pos.xyz); o.uv=a.uv; o.color=a.color*_Tint; return o; }
            float stroke(float2 p,float2 a,float2 b)
            {
                float2 d=b-a;
                return length(p-a-d*saturate(dot(p-a,d)/max(dot(d,d),.0001)));
            }
            half4 frag(V i):SV_Target
            {
                float2 tiled=i.uv*float2(4,2);
                int glyph=(int)floor(tiled.x)+(int)floor(tiled.y)*4;
                float2 p=frac(tiled)*2-1;
                float d=10;
                if(glyph==0) { // branching staff
                    d=stroke(p,float2(0,-.7),float2(0,.7));
                    d=min(d,stroke(p,float2(0,.1),float2(.5,.55)));
                    d=min(d,stroke(p,float2(0,-.3),float2(-.45,.1)));
                }
                else if(glyph==1) { // angular lightning
                    d=stroke(p,float2(.35,.7),float2(-.4,.1));
                    d=min(d,stroke(p,float2(-.4,.1),float2(.35,-.1)));
                    d=min(d,stroke(p,float2(.35,-.1),float2(-.3,-.7)));
                }
                else if(glyph==2) { // diamond and stem
                    d=abs(abs(p.x)*1.2+abs(p.y-.1)-.5)*.65;
                    d=min(d,stroke(p,float2(0,-.3),float2(0,-.75)));
                }
                else if(glyph==3) { // crossed branches
                    d=stroke(p,float2(-.45,-.65),float2(.45,.65));
                    d=min(d,stroke(p,float2(.45,-.65),float2(-.45,.65)));
                    d=min(d,stroke(p,float2(0,-.1),float2(0,.7)));
                }
                else if(glyph==4) { // ring with lower fork
                    d=abs(length(p-float2(0,.25))-.3);
                    d=min(d,stroke(p,float2(0,-.05),float2(0,-.6)));
                    d=min(d,stroke(p,float2(-.3,-.4),float2(.3,-.4)));
                }
                else if(glyph==5) { // double-chevron
                    d=stroke(p,float2(-.4,.6),float2(.35,.2));
                    d=min(d,stroke(p,float2(.35,.2),float2(-.4,-.15)));
                    d=min(d,stroke(p,float2(-.4,-.15),float2(.35,-.5)));
                }
                else if(glyph==6) { // hooked sigil
                    d=stroke(p,float2(-.25,-.65),float2(-.25,.6));
                    d=min(d,stroke(p,float2(-.25,.6),float2(.35,.25)));
                    d=min(d,stroke(p,float2(.35,.25),float2(-.25,-.1)));
                    d=min(d,stroke(p,float2(.05,-.3),float2(.4,-.6)));
                }
                else { // forked crown
                    d=stroke(p,float2(0,-.7),float2(0,.6));
                    d=min(d,stroke(p,float2(-.4,.6),float2(0,.1)));
                    d=min(d,stroke(p,float2(.4,.6),float2(0,.1)));
                    d=min(d,stroke(p,float2(-.3,-.4),float2(.3,-.4)));
                }
                float aa=max(fwidth(d),.008);
                float core=1-smoothstep(.025-aa,.025+aa,d);
                float halo=exp(-d*18)*.35;
                float mask=saturate(core+halo);
                return half4(i.color.rgb*3.5,mask*i.color.a);
            }
            ENDHLSL
        }
    }
}
