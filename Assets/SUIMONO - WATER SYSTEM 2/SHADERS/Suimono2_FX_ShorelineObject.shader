Shader "Suimono2/Suimono2_FX_ShorelineObject" {

    Properties {
        _MainTex ("Base (RGB)", 2D) = "white" {}
    }

    SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
 Pass { Tags { "LightMode"="SuimonoShore" } Cull Off ZWrite Off Blend One Zero
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 sampler2D _MainTex; float4 _MainTex_ST,_Color,_Mult; float _Strength;
 struct A {float3 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 V Vert(A a) {V o;o.p=TransformObjectToHClip(a.p);o.uv=a.uv*_MainTex_ST.xy+_MainTex_ST.zw;o.color=a.color;return o;}
 half4 Frag(V i):SV_Target { return tex2D(_MainTex,i.uv*_Mult.xy+_Mult.zw); }
 ENDHLSL
 }}
SubShader {
        Pass {
            Tags{"Queue"="Overlay"}
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            uniform sampler2D _MainTex;
            float4 _Mult;

            fixed4 frag(v2f_img i) : SV_Target {
                return tex2D(_MainTex, i.uv*_Mult.xy+_Mult.zw);
            }
            ENDCG

        }
    }
}
