Shader "WitchFlight/Island Cloud Billboard"
{
    Properties
    {
        _MainTex ("Cloud Texture", 2D) = "white" {}
        _Color ("Cloud Tint", Color) = (0.87,0.92,1,1)
        _PuffShape ("Rounded Cumulus Shape", Range(0,1)) = 0
        _ShadowColor ("Cloud Underside", Color) = (0.64,0.7,0.85,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _Color;
            float4 _ShadowColor;
            float _PuffShape;
            float CloudNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3.0-2.0*f);
                float4 h = frac(sin(float4(dot(cell,float2(127.1,311.7)),
                    dot(cell+float2(1,0),float2(127.1,311.7)),
                    dot(cell+float2(0,1),float2(127.1,311.7)),
                    dot(cell+1,float2(127.1,311.7))))*43758.5453);
                return lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y);
            }
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                fixed4 cloud = tex2D(_MainTex, i.uv);
                // Five overlapping lobes give a rounded bank rather than stretched
                // wisps. Texture adds only subtle relief, not holes in the silhouette.
                float2 uv = i.uv;
                float shape = length((uv - float2(0.49,0.35)) / float2(0.46,0.23));
                shape = min(shape, length((uv-float2(0.3,0.49))/float2(0.23,0.28)));
                shape = min(shape, length((uv-float2(0.51,0.59))/float2(0.23,0.33)));
                shape = min(shape, length((uv-float2(0.71,0.48))/float2(0.23,0.27)));
                shape = min(shape, length((uv-float2(0.84,0.36))/float2(0.13,0.17)));
                float detail = CloudNoise(uv * float2(26,19))*0.65 + CloudNoise(uv*float2(57,43))*0.35;
                shape += (detail-0.5)*0.24;
                float alpha = 1.0-smoothstep(0.79,1.04,shape);
                float light = saturate(smoothstep(0.18,0.72,uv.y)*0.65
                    + sqrt(saturate(1.0-shape*shape))*0.28 + (detail-0.5)*0.15 + cloud.r*0.08);
                float3 puff = lerp(_ShadowColor.rgb,float3(1.0,0.98,0.94),light);
                cloud = lerp(cloud,float4(puff,alpha),_PuffShape);
                return fixed4(cloud.rgb * i.color.rgb, cloud.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
