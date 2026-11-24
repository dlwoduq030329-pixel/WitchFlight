Shader "Unlit/UnlitMaskedShader"
{
    Properties
    {
        _Mask("Mask", Int) = 1
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _EmissionColor ("Page Glow", Color) = (0,0,0,0)
         [KeywordEnum(None, Front, Back)] _Cull("Culling", Int) = 0
         _CutoffParam("Alpha cutoff", Range(0,1)) = 0.0 // ’Ç‰Á
    }
    SubShader
    {

        
        Tags { "RenderType"="Opaque" }
        LOD 100

        Cull [_Cull]
        Stencil 
        {
            Ref [_Mask]
            Comp Equal
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _CutoffParam;
            half4 _EmissionColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = tex2D(_MainTex, i.uv);

                clip(col.a - _CutoffParam);

                // Keep the printed page texture readable while emitting HDR light.
                col.rgb += (0.35h + col.rgb * 0.65h) * _EmissionColor.rgb;

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
