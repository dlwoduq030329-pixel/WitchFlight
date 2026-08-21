// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "IKUSIA_particlefire"
{
	Properties
	{
		[SingleLineTexture]_MaskTex("MaskTex", 2D) = "white" {}
		_tilingsc("tilingsc", Vector) = (0,-0.74,0,0)
		[HDR]_MainColor("MainColor", Color) = (1,1,1,1)
		_noisescale("noisescale", Float) = 0.6
		_noisescale2("noisescale2", Float) = 3
		_noisescale3("noisescale3", Float) = 0.7
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Off
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float4 vertexColor : COLOR;
			float2 uv_texcoord;
		};

		uniform float4 _MainColor;
		uniform sampler2D _MaskTex;
		uniform float4 _MaskTex_ST;
		uniform float _noisescale;
		uniform float2 _tilingsc;
		uniform float _noisescale2;
		uniform float _noisescale3;


		float2 voronoihash8( float2 p )
		{
			p = p - 33 * floor( p / 33 );
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi8( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
		{
			float2 n = floor( v );
			float2 f = frac( v );
			float F1 = 8.0;
			float F2 = 8.0; float2 mg = 0;
			for ( int j = -1; j <= 1; j++ )
			{
				for ( int i = -1; i <= 1; i++ )
			 	{
			 		float2 g = float2( i, j );
			 		float2 o = voronoihash8( n + g );
					o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
					float d = 0.5 * dot( r, r );
			 		if( d<F1 ) {
			 			F2 = F1;
			 			F1 = d; mg = g; mr = r; id = o;
			 		} else if( d<F2 ) {
			 			F2 = d;
			
			 		}
			 	}
			}
			
F1 = 8.0;
for ( int j = -2; j <= 2; j++ )
{
for ( int i = -2; i <= 2; i++ )
{
float2 g = mg + float2( i, j );
float2 o = voronoihash8( n + g );
		o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
float d = dot( 0.5 * ( r + mr ), normalize( r - mr ) );
F1 = min( F1, d );
}
}
return F1;
		}


		float2 voronoihash38( float2 p )
		{
			p = p - 55 * floor( p / 55 );
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi38( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
		{
			float2 n = floor( v );
			float2 f = frac( v );
			float F1 = 8.0;
			float F2 = 8.0; float2 mg = 0;
			for ( int j = -1; j <= 1; j++ )
			{
				for ( int i = -1; i <= 1; i++ )
			 	{
			 		float2 g = float2( i, j );
			 		float2 o = voronoihash38( n + g );
					o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
					float d = 0.707 * sqrt(dot( r, r ));
			 		if( d<F1 ) {
			 			F2 = F1;
			 			F1 = d; mg = g; mr = r; id = o;
			 		} else if( d<F2 ) {
			 			F2 = d;
			
			 		}
			 	}
			}
			return F1;
		}


		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			o.Emission = ( _MainColor * i.vertexColor ).rgb;
			float2 uv_MaskTex = i.uv_texcoord * _MaskTex_ST.xy + _MaskTex_ST.zw;
			float2 temp_output_7_0 = ( _Time.y * _tilingsc );
			float time8 = temp_output_7_0.x;
			float2 voronoiSmoothId8 = 0;
			float2 uv_TexCoord9 = i.uv_texcoord + temp_output_7_0;
			float2 coords8 = uv_TexCoord9 * _noisescale;
			float2 id8 = 0;
			float2 uv8 = 0;
			float fade8 = 0.5;
			float voroi8 = 0;
			float rest8 = 0;
			for( int it8 = 0; it8 <8; it8++ ){
			voroi8 += fade8 * voronoi8( coords8, time8, id8, uv8, 0,voronoiSmoothId8 );
			rest8 += fade8;
			coords8 *= 2;
			fade8 *= 0.5;
			}//Voronoi8
			voroi8 /= rest8;
			float time38 = 1.55;
			float2 voronoiSmoothId38 = 0;
			float2 uv_TexCoord48 = i.uv_texcoord + ( ( _tilingsc * _Time.y ) * float2( 0.7,0.7 ) );
			float2 coords38 = uv_TexCoord48 * _noisescale2;
			float2 id38 = 0;
			float2 uv38 = 0;
			float voroi38 = voronoi38( coords38, time38, id38, uv38, 0, voronoiSmoothId38 );
			o.Alpha = ( tex2D( _MaskTex, uv_MaskTex ) * ( ( voroi8 * 5.88 ) * -( 1.0 - ( voroi38 + _noisescale3 ) ) ) * _MainColor.a * i.vertexColor.a ).r;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Unlit alpha:fade keepalpha fullforwardshadows 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
				half4 color : COLOR0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				o.color = v.color;
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.vertexColor = IN.color;
				SurfaceOutput o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutput, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				half alphaRef = tex3D( _DitherMaskLOD, float3( vpos.xy * 0.25, o.Alpha * 0.9375 ) ).a;
				clip( alphaRef - 0.01 );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;0,0;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;IKUSIA_particlefire;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Off;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;True;0;False;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;10;-385.053,-5.076721;Inherit;False;4;4;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;11;-787.053,-270.0767;Inherit;True;Property;_MaskTex;MaskTex;0;1;[SingleLineTexture];Create;True;0;0;0;False;0;False;-1;6fe2f9b81a4406a4c99fe327f77742cc;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ScaleNode;37;-601.8259,308.0859;Inherit;False;5.88;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VoronoiNode;8;-789.7727,73.1665;Inherit;False;0;0;5;4;8;True;33;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;0.96;False;2;FLOAT;0.6;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;43;-364.0334,342.3812;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;42;-666.2335,522.7813;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.NegateNode;45;-430.0983,604.3314;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;35;-978.0952,258.2796;Inherit;False;Property;_noisescale;noisescale;3;0;Create;True;0;0;0;False;0;False;0.6;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;7;-1447.773,-210.8335;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleTimeNode;5;-1643.833,-220.4252;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;9;-1217.773,-38.8335;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VoronoiNode;38;-1116.033,489.8812;Inherit;False;0;1;5;0;1;True;55;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;1.55;False;2;FLOAT;3.33;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.TextureCoordinatesNode;48;-1298.316,209.6603;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector2Node;6;-1826.653,3.17775;Inherit;False;Property;_tilingsc;tilingsc;1;0;Create;True;0;0;0;False;0;False;0,-0.74;0,-0.74;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;47;-1615.316,36.66034;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0.7;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;49;-1466.279,196.0379;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0.7,0.7;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;41;-913.5334,554.381;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.62;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;46;-1420.538,504.5749;Inherit;False;Property;_noisescale2;noisescale2;4;0;Create;True;0;0;0;False;0;False;3;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;50;-1038.088,768.2771;Inherit;False;Property;_noisescale3;noisescale3;5;0;Create;True;0;0;0;False;0;False;0.7;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;32;-288.4063,-557.669;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode;31;-560.4063,-702.669;Inherit;False;Property;_MainColor;MainColor;2;1;[HDR];Create;True;0;0;0;False;0;False;1,1,1,1;0,0,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VertexColorNode;30;-555.4063,-499.669;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
WireConnection;0;2;32;0
WireConnection;0;9;10;0
WireConnection;10;0;11;0
WireConnection;10;1;43;0
WireConnection;10;2;31;4
WireConnection;10;3;30;4
WireConnection;37;0;8;0
WireConnection;8;0;9;0
WireConnection;8;1;7;0
WireConnection;8;2;35;0
WireConnection;43;0;37;0
WireConnection;43;1;45;0
WireConnection;42;0;41;0
WireConnection;45;0;42;0
WireConnection;7;0;5;0
WireConnection;7;1;6;0
WireConnection;9;1;7;0
WireConnection;38;0;48;0
WireConnection;38;2;46;0
WireConnection;48;1;49;0
WireConnection;47;0;6;0
WireConnection;47;1;5;0
WireConnection;49;0;47;0
WireConnection;41;0;38;0
WireConnection;41;1;50;0
WireConnection;32;0;31;0
WireConnection;32;1;30;0
ASEEND*/
//CHKSM=1C9F4E187CEC5F9B8A669D913539EAAB3CF91B72