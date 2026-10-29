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
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-50" }
        ZWrite On Blend SrcAlpha OneMinusSrcAlpha
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
            float Noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                float n = dot(cell,float3(1,57,113));
                float4 a = frac(sin(n+float4(0,1,57,58))*43758.5453);
                float4 b = frac(sin(n+float4(113,114,170,171))*43758.5453);
                return lerp(lerp(lerp(a.x,a.y,f.x),lerp(a.z,a.w,f.x),f.y),
                    lerp(lerp(b.x,b.y,f.x),lerp(b.z,b.w,f.x),f.y),f.z);
            }
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
                clip(fade - .001);
                Light sun = GetMainLight();
                float light = saturate(dot(normalize(i.normalWS), sun.direction));
                float grain = Noise(i.positionWS * float3(.22,.05,.22));
                float strata = .96 + .04*sin(i.positionWS.y*.31 + grain*8);
                half3 color = i.color.rgb * (half3(.28,.37,.45) + sun.color * light * .75);
                color *= lerp(.72,1.15,grain) * strata;
                float haze = smoothstep(_HazeStart, max(_HazeStart+1,_HazeEnd), distanceToView);
                color = lerp(color, _HazeColor.rgb, haze * .55);
                return half4(color,fade);
            }
            ENDHLSL
        }
    }
}
