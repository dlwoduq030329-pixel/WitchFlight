Shader "WitchFlight/Skybox/6 Sided Sun Source"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
        [Gamma] _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Sky Rotation", Range(0,360)) = 0
        [NoScaleOffset] _FrontTex ("Front [+Z] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _BackTex ("Back [-Z] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _LeftTex ("Left [+X] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _RightTex ("Right [-X] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _UpTex ("Up [+Y] (HDR)", 2D) = "grey" {}
        [NoScaleOffset] _DownTex ("Down [-Y] (HDR)", 2D) = "grey" {}
        [Header(Sun Source from Lighting Environment)]
        [ToggleUI] _ShowSun ("Show Sun", Float) = 1
        [HDR] _SunColor ("Sun Tint", Color) = (1,1,1,1)
        _SunIntensity ("Sun Intensity", Range(0,30)) = 8
        _SunSize ("Sun Angular Radius (degrees)", Range(0.05,3)) = 0.55
        _SunSoftness ("Sun Edge Softness", Range(0.01,0.5)) = 0.15
        _SunHaloSize ("Sun Halo Radius (degrees)", Range(0.1,20)) = 4
        _SunHaloIntensity ("Sun Halo Intensity", Range(0,5)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"
        half4 _Tint;
        half _Exposure;
        float _Rotation;
        float _ShowSun, _SunSize, _SunSoftness, _SunHaloSize;
        half4 _SunColor;
        half _SunIntensity, _SunHaloIntensity;

        // URP selects RenderSettings.sun as its main directional light. No Update,
        // scene searches, or material mutation are needed to follow Sun Source.
        float4 _MainLightPosition;
        half4 _MainLightColor;

        struct Attributes
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 directionWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float sine, cosine;
            sincos(radians(_Rotation), sine, cosine);
            float3 rotated = input.vertex.xyz;
            rotated.xz = float2(cosine * input.vertex.x - sine * input.vertex.z,
                               sine * input.vertex.x + cosine * input.vertex.z);
            output.position = UnityObjectToClipPos(float4(rotated, 1));
            output.directionWS = mul((float3x3)unity_ObjectToWorld, rotated);
            output.uv = input.uv;
            return output;
        }

        half3 SunRadiance(float3 directionWS)
        {
            float lightLengthSq = dot(_MainLightPosition.xyz, _MainLightPosition.xyz);
            float3 sunDirection = _MainLightPosition.xyz * rsqrt(max(lightLengthSq, 1e-8));
            float3 viewDirection = normalize(directionWS);
            // Chord distance avoids acos and remains precise for a small solar disk.
            float distanceToSun = length(viewDirection - sunDirection);
            float radius = 2.0 * sin(radians(_SunSize) * 0.5);
            float edge = max(radius * _SunSoftness, fwidth(distanceToSun));
            float disk = 1.0 - smoothstep(max(0, radius - edge), radius + edge, distanceToSun);
            float haloRadius = max(2.0 * sin(radians(_SunHaloSize) * 0.5), 1e-4);
            float haloDistance = distanceToSun / haloRadius;
            float halo = exp2(-4.0 * haloDistance * haloDistance);
            // Main light color becomes black when no active main light is available.
            return _SunColor.rgb * _MainLightColor.rgb * saturate(_ShowSun)
                * (disk * _SunIntensity + halo * _SunHaloIntensity);
        }

        half4 ShadeFace(Varyings input, sampler2D skyTexture, half4 hdrDecode)
        {
            // Match Unity's original six-sided skybox texture/tint/exposure treatment.
            half3 sky = DecodeHDR(tex2D(skyTexture, input.uv), hdrDecode);
            sky *= _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;
            return half4(sky + SunRadiance(input.directionWS), 1);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _FrontTex;
            half4 _FrontTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _FrontTex, _FrontTex_HDR); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _BackTex;
            half4 _BackTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _BackTex, _BackTex_HDR); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _LeftTex;
            half4 _LeftTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _LeftTex, _LeftTex_HDR); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _RightTex;
            half4 _RightTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _RightTex, _RightTex_HDR); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _UpTex;
            half4 _UpTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _UpTex, _UpTex_HDR); }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _DownTex;
            half4 _DownTex_HDR;
            half4 Frag(Varyings input) : SV_Target { return ShadeFace(input, _DownTex, _DownTex_HDR); }
            ENDCG
        }
    }
    Fallback Off
}
