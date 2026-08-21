// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "IKUSIA_drink"
{
	Properties
	{
		[SingleLineTexture]_Main("Main", 2D) = "white" {}
		[HDR]_MainColor("MainColor", Color) = (1,0,0,0)
		_noiseScale("noiseScale", Float) = 3.25
		_noiselevel("noiselevel", Range( 0 , 1)) = 1
		_height("height", Float) = 0.84
		_speed("speed", Float) = 0
		_transparency("transparency", Float) = 0.7
		_bokasi("bokasi", Float) = 1
		_shakespeed("shakespeed", Range( 0 , 10)) = 0
		_shakelevel("shake level", Range( 0 , 0.5)) = 0.1
		_men("men", Float) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" }
		Cull [_men]
		Blend SrcAlpha OneMinusSrcAlpha , SrcAlpha OneMinusSrcAlpha
		
		AlphaToMask On
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.5
		struct Input
		{
			float2 uv_texcoord;
			float3 worldPos;
		};

		uniform float _noiseScale;
		uniform float _speed;
		uniform float _noiselevel;
		uniform float4 _MainColor;
		uniform float _men;
		uniform sampler2D _Main;
		uniform float4 _Main_ST;
		uniform float _shakespeed;
		uniform float _shakelevel;
		uniform float _height;
		uniform float _bokasi;
		uniform float _transparency;


		float2 voronoihash90( float2 p )
		{
			
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi90( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
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
			 		float2 o = voronoihash90( n + g );
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
			return (F2 + F1) * 0.5;
		}


		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float time90 = ( _Time.y * _speed );
			float2 voronoiSmoothId90 = 0;
			float2 coords90 = i.uv_texcoord * _noiseScale;
			float2 id90 = 0;
			float2 uv90 = 0;
			float voroi90 = voronoi90( coords90, time90, id90, uv90, 0, voronoiSmoothId90 );
			float4 temp_output_96_0 = ( ( voroi90 * _noiselevel ) + _MainColor + ( _men * 0.0 ) );
			o.Albedo = temp_output_96_0.rgb;
			float2 uv_Main = i.uv_texcoord * _Main_ST.xy + _Main_ST.zw;
			float4 transform102 = mul(unity_ObjectToWorld,float4( 0,0,0,1 ));
			float3 ase_worldPos = i.worldPos;
			float4 temp_output_47_0 = ( transform102 - float4( ase_worldPos , 0.0 ) );
			float mulTime66 = _Time.y * _shakespeed;
			float4 temp_cast_3 = (( 1.0 - _height )).xxxx;
			float4 clampResult44 = clamp( ( ( ( ( temp_output_47_0 * sin( mulTime66 ) * _shakelevel ) + (temp_output_47_0).y ) - temp_cast_3 ) / _bokasi ) , float4( 0,0,0,0 ) , float4( 1,1,1,0 ) );
			o.Alpha = ( ( temp_output_96_0 * tex2D( _Main, uv_Main ) ).a * ( clampResult44 * _transparency ) ).x;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard keepalpha fullforwardshadows 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			AlphaToMask Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
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
				surfIN.worldPos = worldPos;
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
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
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;0,0;Float;False;True;-1;3;ASEMaterialInspector;0;0;Standard;IKUSIA_drink;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;1;True;True;0;True;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;2;5;False;;10;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;1;-1;-1;-1;0;True;0;0;True;_men;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;43;-712.9854,350.582;Inherit;True;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;48;-1482.278,283.628;Inherit;True;3;3;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleAddOpNode;50;-1184.279,352.6281;Inherit;True;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SinOpNode;51;-1629.179,192.4278;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;55;-932.1788,1124.728;Float;False;Property;_shakespeed;shakespeed;9;0;Create;True;0;0;0;False;0;False;0;0;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;66;-2506.057,171.6162;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;61;-1788.672,281.1611;Float;False;Property;_shakelevel;shake level;10;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;0.5;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;45;-950.1157,351.2488;Inherit;True;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.WorldPosInputsNode;46;-2445.283,493.1878;Float;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.ComponentMaskNode;49;-1479.185,496.1142;Inherit;False;False;True;False;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;67;-1718.832,723.6412;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;69;-1922.5,718.7;Float;False;Property;_height;height;5;0;Create;True;0;0;0;False;0;False;0.84;0.84;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;44;-488.4421,351.9469;Inherit;True;3;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;2;FLOAT4;1,1,1,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;47;-2199.73,396.4027;Inherit;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;42;-269.7964,350.136;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SamplerNode;54;-1401.617,674.3757;Inherit;True;Property;_Main;Main;0;1;[SingleLineTexture];Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;88;-142.1011,234.7881;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleAddOpNode;96;-650.3896,-48.40509;Inherit;False;3;3;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;92;-1504.971,-333.8841;Inherit;False;Property;_speed;speed;6;0;Create;True;0;0;0;False;0;False;0;4.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;93;-1452.606,-587.1422;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;94;-1272.792,-410.799;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VoronoiNode;90;-1077.751,-421.3532;Inherit;False;0;0;1;3;1;False;1;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT2;1;FLOAT2;2
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;97;-837.9316,-226.8155;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;91;-1382.468,-241.7546;Inherit;False;Property;_noiseScale;noiseScale;3;0;Create;True;0;0;0;False;0;False;3.25;4.5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;53;-445.1931,141.8502;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.BreakToComponentsNode;89;-294.801,127.188;Inherit;False;COLOR;1;0;COLOR;0,0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.RangedFloatNode;98;-1188.931,-193.8154;Inherit;False;Property;_noiselevel;noiselevel;4;0;Create;True;0;0;0;False;0;False;1;4.5;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;60;-960.4672,-40.59411;Float;False;Property;_MainColor;MainColor;2;1;[HDR];Create;True;0;0;0;False;0;False;1,0,0,0;1,1,1,0;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;99;-910.2642,166.8292;Inherit;False;Property;_men;men;11;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;100;-840.2642,256.8292;Inherit;False;Constant;_Float0;Float 0;12;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;101;-715.2642,136.8292;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;56;-1044.91,591.3545;Inherit;False;Property;_bokasi;bokasi;8;0;Create;True;0;0;0;False;0;False;1;1;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ObjectToWorldTransfNode;102;-2546.765,286.4786;Inherit;False;1;0;FLOAT4;0,0,0,1;False;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;57;-628.8164,1132.793;Float;True;Property;_transparency;transparency;7;0;Create;True;0;0;0;False;0;False;0.7;0.501;0;0;0;1;FLOAT;0
WireConnection;0;0;96;0
WireConnection;0;9;88;0
WireConnection;43;0;45;0
WireConnection;43;1;56;0
WireConnection;48;0;47;0
WireConnection;48;1;51;0
WireConnection;48;2;61;0
WireConnection;50;0;48;0
WireConnection;50;1;49;0
WireConnection;51;0;66;0
WireConnection;66;0;55;0
WireConnection;45;0;50;0
WireConnection;45;1;67;0
WireConnection;49;0;47;0
WireConnection;67;0;69;0
WireConnection;44;0;43;0
WireConnection;47;0;102;0
WireConnection;47;1;46;0
WireConnection;42;0;44;0
WireConnection;42;1;57;0
WireConnection;88;0;89;3
WireConnection;88;1;42;0
WireConnection;96;0;97;0
WireConnection;96;1;60;0
WireConnection;96;2;101;0
WireConnection;94;0;93;0
WireConnection;94;1;92;0
WireConnection;90;1;94;0
WireConnection;90;2;91;0
WireConnection;97;0;90;0
WireConnection;97;1;98;0
WireConnection;53;0;96;0
WireConnection;53;1;54;0
WireConnection;89;0;53;0
WireConnection;101;0;99;0
WireConnection;101;1;100;0
ASEEND*/
//CHKSM=C3AB4199AA9DB364AE96BB886D1786350820A398