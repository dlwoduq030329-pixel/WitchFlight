Shader "WitchFlight/MovementDensity"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 8
        [Toggle] _ZWrite ("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _FaceShading ("Face shading", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Cull [_Cull]
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _FaceShading;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float3 world:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 n=normalize(cross(ddx(i.world),ddy(i.world)));
                float shade=lerp(1.0,.65+.35*abs(n.y),_FaceShading);
                return float4(i.color.rgb*shade,i.color.a);
            }
            ENDCG
        }
    }
}
