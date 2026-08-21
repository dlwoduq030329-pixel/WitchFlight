// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "water_shader"
{
	Properties
	{
		_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalScale("Normal Scale", Float) = 1
		_NormalScale2("Normal Scale2", Float) = 1
		_Normalnoisescale("Normal noise scale", Float) = 2
		_speed("speed", Float) = 1
		_Mask("Mask", 2D) = "white" {}
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Geometry+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Off
		Blend SrcAlpha OneMinusSrcAlpha , SrcAlpha OneMinusSrcAlpha
		
		GrabPass{ }
		CGPROGRAM
		#include "UnityStandardUtils.cginc"
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex);
		#else
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex)
		#endif
		#pragma surface surf Unlit keepalpha noshadow exclude_path:deferred 
		struct Input
		{
			float4 screenPos;
			float2 uv_texcoord;
			float4 vertexColor : COLOR;
		};

		ASE_DECLARE_SCREENSPACE_TEXTURE( _GrabTexture )
		uniform sampler2D _NormalMap;
		uniform float4 _NormalMap_ST;
		uniform float _NormalScale;
		uniform float _Normalnoisescale;
		uniform float _speed;
		uniform float _NormalScale2;
		uniform sampler2D _Mask;
		uniform float4 _Mask_ST;


		inline float4 ASE_ComputeGrabScreenPos( float4 pos )
		{
			#if UNITY_UV_STARTS_AT_TOP
			float scale = -1.0;
			#else
			float scale = 1.0;
			#endif
			float4 o = pos;
			o.y = pos.w * 0.5f;
			o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
			return o;
		}


		float2 voronoihash43( float2 p )
		{
			
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi43( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
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
			 		float2 o = voronoihash43( n + g );
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
			return (F2 + F1) * 0.5;
		}


		float3 ASEUnpackNormalRGB(float4 PackedNormal, float Scale = 1.0 )
		{
			float3 normal;
			normal.xyz = PackedNormal.rgb * 2.0 - 1.0;
			normal.xy *= Scale;
			return normal;
		}


		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float4 ase_screenPos = float4( i.screenPos.xyz , i.screenPos.w + 0.00000000001 );
			float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( ase_screenPos );
			float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
			float2 uv_NormalMap = i.uv_texcoord * _NormalMap_ST.xy + _NormalMap_ST.zw;
			float time43 = ( _Time.y * _speed );
			float2 voronoiSmoothId43 = 0;
			float2 coords43 = i.uv_texcoord * _Normalnoisescale;
			float2 id43 = 0;
			float2 uv43 = 0;
			float voroi43 = voronoi43( coords43, time43, id43, uv43, 0, voronoiSmoothId43 );
			float4 temp_cast_1 = (voroi43).xxxx;
			float2 uv_Mask = i.uv_texcoord * _Mask_ST.xy + _Mask_ST.zw;
			float4 screenColor12 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GrabTexture,( ase_grabScreenPosNorm + ( float4( UnpackScaleNormal( tex2D( _NormalMap, uv_NormalMap ), _NormalScale ) , 0.0 ) * float4( ASEUnpackNormalRGB(temp_cast_1,_NormalScale2) , 0.0 ) * i.vertexColor.a * tex2D( _Mask, uv_Mask ) ) ).xy/( ase_grabScreenPosNorm + ( float4( UnpackScaleNormal( tex2D( _NormalMap, uv_NormalMap ), _NormalScale ) , 0.0 ) * float4( ASEUnpackNormalRGB(temp_cast_1,_NormalScale2) , 0.0 ) * i.vertexColor.a * tex2D( _Mask, uv_Mask ) ) ).w);
			o.Emission = screenColor12.rgb;
			o.Alpha = 1;
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;29;1.820351,-7.281405;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;water_shader;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Off;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;1;True;False;0;False;Transparent;;Geometry;ForwardOnly;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;False;2;5;False;;10;False;;2;5;False;;10;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;True;_Clip;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.VoronoiNode;43;-1539.719,625.4571;Inherit;False;0;1;1;3;1;False;1;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;21.73;False;2;FLOAT;4.99;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.VertexColorNode;40;-2128.702,410.3408;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;1;-2075.588,97.56308;Float;False;Property;_NormalScale;Normal Scale;1;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;53;-1774.777,633.8539;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;75;-1417.345,961.5502;Float;False;Property;_NormalScale2;Normal Scale2;2;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;76;-1733.83,974.9731;Float;False;Property;_Normalnoisescale;Normal noise scale;3;0;Create;True;0;0;0;False;0;False;2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.UnpackScaleNormalNode;73;-1319.63,602.9524;Inherit;False;Object;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;-1.61;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;52;-1997.644,843.4407;Float;False;Property;_speed;speed;4;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TimeNode;95;-2234.434,609.4792;Inherit;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;2;-1538.544,104.5462;Inherit;True;Property;_NormalMap;Normal Map;0;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;1;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;97;-1356.932,1186.821;Inherit;True;Property;_Mask;Mask;5;0;Create;True;0;0;0;False;0;False;-1;77bd0c145ae0c664cabea25cb04651e5;77bd0c145ae0c664cabea25cb04651e5;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;98;-813.4182,378.5275;Inherit;False;Property;_Clip;Clip;6;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;100;-635.8945,235.9268;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenColorNode;12;-433.6366,13.07594;Float;False;Global;_GrabScreen0;Grab Screen 0;1;0;Create;True;0;0;0;False;0;False;Object;-1;False;True;False;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;10;-872.9059,-6.083124;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GrabScreenPosition;8;-1175.422,-126.1118;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;74;-1047.15,441.3965;Inherit;False;4;4;0;FLOAT3;0,0,0;False;1;FLOAT3;1,1,1;False;2;FLOAT;0;False;3;COLOR;0,0,0,0;False;1;COLOR;0
WireConnection;29;2;12;0
WireConnection;43;1;53;0
WireConnection;43;2;76;0
WireConnection;53;0;95;2
WireConnection;53;1;52;0
WireConnection;73;0;43;0
WireConnection;73;1;75;0
WireConnection;2;5;1;0
WireConnection;100;0;98;0
WireConnection;12;0;10;0
WireConnection;10;0;8;0
WireConnection;10;1;74;0
WireConnection;74;0;2;0
WireConnection;74;1;73;0
WireConnection;74;2;40;4
WireConnection;74;3;97;0
ASEEND*/
//CHKSM=AF54622FF0E53B8CD52D1119F0168815A0803361