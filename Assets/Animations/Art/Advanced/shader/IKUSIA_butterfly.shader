// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "butterfly"
{
	Properties
	{
		[SingleLineTexture]_Main_Tex("Main_Tex", 2D) = "white" {}
		[HDR]_MainColor("MainColor", Color) = (1.593369,1.158856,0.7591049,1)
		_offset("offset", Float) = -8.94
		_size("size", Float) = 0.007
		_speed("speed", Float) = 9
		_sca("sca", Float) = 249.2
		_of("of", Float) = -8.1
		_sca2("sca2", Float) = 0.89
		_of2("of2", Float) = 0
		_sca3("sca3", Float) = 0
		_of3("of3", Float) = -0.01
		_fly_Level("fly_Level", Range( 0 , 1)) = 1
		_ObjectPosition("ObjectPosition", Vector) = (0,0,0,0)
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IsEmissive" = "true"  }
		Cull Back
		Blend SrcAlpha OneMinusSrcAlpha , SrcAlpha OneMinusSrcAlpha
		
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform float _offset;
		uniform float _speed;
		uniform float _sca3;
		uniform float _of3;
		uniform float _size;
		uniform float _sca;
		uniform float _of;
		uniform float _fly_Level;
		uniform float _sca2;
		uniform float _of2;
		uniform float3 _ObjectPosition;
		uniform float4 _MainColor;
		uniform sampler2D _Main_Tex;
		uniform float4 _Main_Tex_ST;

		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float3 ase_vertex3Pos = v.vertex.xyz;
			float3 break100 = ase_vertex3Pos;
			float temp_output_124_0 = ( sin( ( _Time.y * _speed ) ) * 1.0 );
			float temp_output_96_0 = ( ase_vertex3Pos.x * _size );
			float4 appendResult120 = (float4(break100.x , break100.y , ( ( sin( ( ( _offset * (temp_output_124_0*_sca3 + _of3) ) + ( temp_output_96_0 * -temp_output_96_0 ) ) ) * ( (temp_output_124_0*_sca + _of) * _fly_Level ) ) + break100.z ) , 0.0));
			float temp_output_122_0 = (temp_output_124_0*_sca2 + _of2);
			float4 appendResult106 = (float4(( temp_output_122_0 * -temp_output_122_0 ) , 0.0 , 0.9 , 0.0));
			v.vertex.xyz += ( ( appendResult120 * ( appendResult106 * _fly_Level ) ) + float4( _ObjectPosition , 0.0 ) ).xyz;
			v.vertex.w = 1;
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float2 uv_Main_Tex = i.uv_texcoord * _Main_Tex_ST.xy + _Main_Tex_ST.zw;
			float4 tex2DNode129 = tex2D( _Main_Tex, uv_Main_Tex );
			o.Emission = ( _MainColor * tex2DNode129 ).rgb;
			o.Alpha = ( _MainColor.a * tex2DNode129.a );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Unlit keepalpha fullforwardshadows vertex:vertexDataFunc 

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
				vertexDataFunc( v, customInputData );
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
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;85;0,0;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;butterfly;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;0.5;True;True;0;True;Transparent;;Transparent;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;2;5;False;;10;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;2;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.SimpleAddOpNode;89;-621.8234,34.51407;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;91;-315.5714,-541.793;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SinOpNode;92;-1007.166,-77.61694;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PosVertexDataNode;93;-2220.264,-295.4206;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode;94;-1201.166,-198.617;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode;95;-2135.813,-147.0085;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;96;-1921.812,-19.00854;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.NegateNode;97;-1921.911,169.0847;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;98;-1713.782,181.463;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode;100;-848.892,260.9676;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;101;-844.6671,36.98325;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;103;-2171.813,123.9915;Inherit;False;Property;_size;size;4;0;Create;True;0;0;0;False;0;False;0.007;0.02;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;104;-1412.02,804.5406;Inherit;False;Property;_sca;sca;6;0;Create;True;0;0;0;False;0;False;249.2;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;105;-1432.552,1032.179;Inherit;False;Property;_of;of;7;0;Create;True;0;0;0;False;0;False;-8.1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;106;-435.8979,706.1154;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0.9;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.NegateNode;107;-594.8483,1090.779;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;108;-318.8482,1015.779;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;109;-1582.806,-820.6272;Inherit;False;Property;_sca3;sca3;10;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode;111;-1346.843,-614.4469;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;280;False;2;FLOAT;-80;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;112;-1691.815,-392.5971;Inherit;False;Property;_offset;offset;3;0;Create;True;0;0;0;False;0;False;-8.94;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;113;-1421.481,-389.2162;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;115;-323.5714,-284.7931;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;116;-1171.062,1199.089;Inherit;False;Property;_of2;of2;9;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;117;-1167.53,967.4507;Inherit;False;Property;_sca2;sca2;8;0;Create;True;0;0;0;False;0;False;0.89;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;119;-831.7936,550.6792;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;120;-514.5793,250.0536;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode;121;-1085.847,541.3129;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;280;False;2;FLOAT;-80;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode;122;-766.4443,843.8816;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.9;False;2;FLOAT;-6.8;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;123;-2134.747,460.1306;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;124;-1513.666,424.7835;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;125;-1955.103,460.6142;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;6.6;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;126;-2152.43,664.369;Inherit;False;Property;_speed;speed;5;0;Create;True;0;0;0;False;0;False;9;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SinOpNode;127;-1748.285,455.6103;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;129;-699.9874,-429.6481;Inherit;True;Property;_Main_Tex;Main_Tex;0;1;[SingleLineTexture];Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;110;-1592.9,-605.3694;Inherit;False;Property;_of3;of3;11;0;Create;True;0;0;0;False;0;False;-0.01;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;128;-900.5472,1175.184;Inherit;False;Property;_fly_Level;fly_Level;12;0;Create;True;0;0;0;False;0;False;1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;114;-616.3663,-650.8367;Inherit;False;Property;_MainColor;MainColor;1;1;[HDR];Create;True;0;0;0;False;0;False;1.593369,1.158856,0.7591049,1;1,0.8726415,0.8726415,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;118;-290.0617,705.6138;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;90;-362.8846,287.1391;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleAddOpNode;132;-118.8257,459.7944;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.Vector3Node;130;-387.9935,542.289;Inherit;False;Property;_ObjectPosition;ObjectPosition;13;0;Create;True;0;0;0;False;0;False;0,0,0;1,1,1;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
WireConnection;85;2;91;0
WireConnection;85;9;115;0
WireConnection;85;11;132;0
WireConnection;89;0;101;0
WireConnection;89;1;100;2
WireConnection;91;0;114;0
WireConnection;91;1;129;0
WireConnection;92;0;94;0
WireConnection;94;0;113;0
WireConnection;94;1;98;0
WireConnection;95;0;93;0
WireConnection;96;0;95;0
WireConnection;96;1;103;0
WireConnection;97;0;96;0
WireConnection;98;0;96;0
WireConnection;98;1;97;0
WireConnection;100;0;93;0
WireConnection;101;0;92;0
WireConnection;101;1;119;0
WireConnection;106;0;108;0
WireConnection;107;0;122;0
WireConnection;108;0;122;0
WireConnection;108;1;107;0
WireConnection;111;0;124;0
WireConnection;111;1;109;0
WireConnection;111;2;110;0
WireConnection;113;0;112;0
WireConnection;113;1;111;0
WireConnection;115;0;114;4
WireConnection;115;1;129;4
WireConnection;119;0;121;0
WireConnection;119;1;128;0
WireConnection;120;0;100;0
WireConnection;120;1;100;1
WireConnection;120;2;89;0
WireConnection;121;0;124;0
WireConnection;121;1;104;0
WireConnection;121;2;105;0
WireConnection;122;0;124;0
WireConnection;122;1;117;0
WireConnection;122;2;116;0
WireConnection;124;0;127;0
WireConnection;125;0;123;0
WireConnection;125;1;126;0
WireConnection;127;0;125;0
WireConnection;118;0;106;0
WireConnection;118;1;128;0
WireConnection;90;0;120;0
WireConnection;90;1;118;0
WireConnection;132;0;90;0
WireConnection;132;1;130;0
ASEEND*/
//CHKSM=11EC08C6BA2D125F220DB4C9C15F37936DB3B545