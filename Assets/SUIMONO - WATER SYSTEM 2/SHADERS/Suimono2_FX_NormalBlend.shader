// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Suimono2/EffectNormalBlend" {


Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
	_Strength ("Strength",float) = 1.0
}

SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
 Pass { Tags { "LightMode"="SuimonoNormals" } Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 sampler2D _MainTex; float4 _MainTex_ST,_Color,_Mult; float _Strength;
 struct A {float3 p:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 V Vert(A a) {V o;o.p=TransformObjectToHClip(a.p);o.uv=a.uv*_MainTex_ST.xy+_MainTex_ST.zw;o.color=a.color;return o;}
 half4 Frag(V i):SV_Target { half4 t=tex2D(_MainTex,i.uv);half3 n=lerp(half3(.2,.2,1),t.rgb,saturate(i.color.a));n.xy*=_Strength;return half4(n,t.a*i.color.a); }
 ENDHLSL
 }}
SubShader {
	Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="SuimonoNormals"}
	
	Cull Back
	ZWrite Off
	Blend SrcAlpha OneMinusSrcAlpha 

	
	Pass {  
		CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			
			#include "UnityCG.cginc"

			struct appdata_t {
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f {
				float4 color : COLOR;
				float4 vertex : SV_POSITION;
				half2 texcoord : TEXCOORD0;
				UNITY_FOG_COORDS(1)
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _Color;
			float _Strength;

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.color = v.color;
				UNITY_TRANSFER_FOG(o,o.vertex);
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = tex2D(_MainTex, i.texcoord);
				col.rgb = lerp(half3(0.2,0.2,1.0),col.rgb,saturate(i.color.a));
				col.r = col.r * _Strength;
				col.g = col.g * _Strength;
				col.a = lerp((fixed)0.0,(fixed)col.a,saturate(i.color.a));

				return col;
			}
		ENDCG
	}
}

}

