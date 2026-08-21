// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "MenuButton"
{
	Properties
	{
		_MainTex("MainTex", 2D) = "white" {}
		_Color("Color", Color) = (1,1,1,1)
		_Alpha("Alpha", Float) = 1
		_meido("meido", Range( 0 , 2)) = 2
		_saido("saido", Range( -1 , 2)) = 0
		_flash("flash", Range( 0 , 10)) = 0
		_startAlpha("startAlpha", Range( 0 , 1)) = 0.1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IsEmissive" = "true"  }
		Cull Off
		Blend SrcAlpha OneMinusSrcAlpha
		
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#pragma surface surf Unlit keepalpha 
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform sampler2D _MainTex;
		uniform float4 _MainTex_ST;
		uniform float4 _Color;
		uniform float _meido;
		uniform float _saido;
		uniform float _Alpha;
		uniform float _startAlpha;
		uniform float _flash;


		float3 HSVToRGB( float3 c )
		{
			float4 K = float4( 1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0 );
			float3 p = abs( frac( c.xxx + K.xyz ) * 6.0 - K.www );
			return c.z * lerp( K.xxx, saturate( p - K.xxx ), c.y );
		}


		float3 RGBToHSV(float3 c)
		{
			float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
			float4 p = lerp( float4( c.bg, K.wz ), float4( c.gb, K.xy ), step( c.b, c.g ) );
			float4 q = lerp( float4( p.xyw, c.r ), float4( c.r, p.yzx ), step( p.x, c.r ) );
			float d = q.x - min( q.w, q.y );
			float e = 1.0e-10;
			return float3( abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode53 = tex2D( _MainTex, uv_MainTex );
			float3 hsvTorgb27 = RGBToHSV( ( tex2DNode53 * _Color ).rgb );
			float3 hsvTorgb29 = HSVToRGB( float3(hsvTorgb27.x,hsvTorgb27.y,( hsvTorgb27.z * _meido )) );
			float3 desaturateInitialColor42 = hsvTorgb29;
			float desaturateDot42 = dot( desaturateInitialColor42, float3( 0.299, 0.587, 0.114 ));
			float3 desaturateVar42 = lerp( desaturateInitialColor42, desaturateDot42.xxx, ( _saido * -1.0 ) );
			o.Emission = desaturateVar42;
			float mulTime22 = _Time.y * ( _flash * 0.6 );
			o.Alpha = ( _Color.a * _Alpha * ( _startAlpha + ( 1.0 - ( round( frac( mulTime22 ) ) * 1 ) ) ) * tex2DNode53.a );
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19105
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;65.06815,-38.40089;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;MenuButton;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Off;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;0.5;True;False;0;True;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;2;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;3;-84.29733,60.89898;Inherit;False;4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;5;-333.86,23.21448;Inherit;False;Property;_Alpha;Alpha;3;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;24;-918.6387,-213.722;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;25;-1217.423,-398.2122;Inherit;False;Property;_startAlpha;startAlpha;7;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RGBToHSVNode;27;-544.0118,-990.8351;Inherit;False;1;0;FLOAT3;0,0,0;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.HSVToRGBNode;29;-112.1322,-988.3102;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.DesaturateOpNode;42;137.3854,-1095.659;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;50;-144.7179,-676.2277;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;52;-359.0478,-1268.419;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;43;-412.1636,-795.9357;Inherit;False;Property;_saido;saido;5;0;Create;True;0;0;0;False;0;False;0;0;-1;2;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;51;-312.718,-625.2277;Inherit;False;Constant;_Float0;Float 0;7;0;Create;True;0;0;0;False;0;False;-1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;35;-715.3475,-1304.655;Inherit;False;Property;_meido;meido;4;0;Create;True;0;0;0;False;0;False;2;0.1;0;2;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;13;-842.4342,-469.9351;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode;1;-1076.674,-642.5861;Inherit;False;Property;_Color;Color;1;0;Create;True;0;0;0;False;0;False;1,1,1,1;0,0,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.FunctionNode;20;-1106.377,-153.6224;Inherit;False;Square Wave;-1;;6;6f8df4c09ccca5d42b0d3d422aad9cbd;0;1;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;22;-1252.25,-39.69279;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;53;-1138.239,-895.575;Inherit;True;Property;_MainTex;MainTex;0;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;23;-1774.669,-306.0126;Inherit;False;Property;_flash;flash;6;0;Create;True;0;0;0;False;0;False;0;0;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;54;-1437.941,-192.3643;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.6;False;1;FLOAT;0
WireConnection;0;2;42;0
WireConnection;0;9;3;0
WireConnection;3;0;1;4
WireConnection;3;1;5;0
WireConnection;3;2;24;0
WireConnection;3;3;53;4
WireConnection;24;0;25;0
WireConnection;24;1;20;0
WireConnection;27;0;13;0
WireConnection;29;0;27;1
WireConnection;29;1;27;2
WireConnection;29;2;52;0
WireConnection;42;0;29;0
WireConnection;42;1;50;0
WireConnection;50;0;43;0
WireConnection;50;1;51;0
WireConnection;52;0;27;3
WireConnection;52;1;35;0
WireConnection;13;0;53;0
WireConnection;13;1;1;0
WireConnection;20;1;22;0
WireConnection;22;0;54;0
WireConnection;54;0;23;0
ASEEND*/
//CHKSM=473856CA74A95EA901ABBD6B24D5CB4F38C156E4