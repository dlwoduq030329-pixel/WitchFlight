Shader "WitchFlight/Floating Island Backdrop"
{
    Properties
    {
        _HazeColor ("Distance haze", Color) = (0.66,0.79,0.87,1)
        _HazeStart ("Haze start", Float) = 350
        _HazeEnd ("Haze end", Float) = 1250
        _FadeStart ("Far fade start", Float) = 900
        _FadeEnd ("Far fade end (before camera clip)", Float) = 980
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _HazeColor;
            float _HazeStart, _HazeEnd, _FadeStart, _FadeEnd;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float distanceToView = distance(_WorldSpaceCameraPos, i.positionWS);
                float fade = 1.0 - smoothstep(_FadeStart, max(_FadeStart+1,_FadeEnd), distanceToView);
                // Fade before the existing 1000m far clip, without adding a distant camera.
                float dither = frac(52.9829189 * frac(dot(floor(i.positionCS.xy), float2(.06711056,.00583715))));
                clip(fade - dither - .0001);
                Light sun = GetMainLight();
                float light = saturate(dot(normalize(i.normalWS), sun.direction));
                half3 color = i.color.rgb * (half3(.45,.52,.62) + sun.color * light * .65);
                float haze = smoothstep(_HazeStart, max(_HazeStart+1,_HazeEnd), distanceToView);
                color = lerp(color, _HazeColor.rgb, haze * .78);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
