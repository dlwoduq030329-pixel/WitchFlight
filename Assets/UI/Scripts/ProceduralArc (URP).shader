Shader "URP/UI/ProceduralArc"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _Radius ("Radius", Range(0.0, 0.5)) = 0.4
        _Thickness ("Thickness", Range(0.0, 0.5)) = 0.1
        _ArcAngle ("Arc Angle", Range(0, 360)) = 80 // Total width of the arc
        _AlphaFalloff ("Alpha Falloff", Range(0.0, 8.0)) = 0.0
        
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                half4 color         : COLOR;
                float2 uv           : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Radius;
                float _Thickness;
                float _ArcAngle;
                float _AlphaFalloff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color; 
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv - 0.5;
                float dist = length(uv);

                // atan2(uv.x, uv.y) sets 0 degrees precisely at the top center.
                // It returns values from -PI to PI. We convert to degrees.
                float angle = atan2(uv.x, uv.y) * 57.29578; 

                // Smoothstep ring generation
                float edgeWidth = fwidth(dist);
                float outerEdge = smoothstep(_Radius, _Radius - edgeWidth, dist);
                float innerEdge = smoothstep(_Radius - _Thickness, _Radius - _Thickness + edgeWidth, dist);
                float ringAlpha = outerEdge * innerEdge;

                // Check if the absolute angle is within half of our total desired arc width.
                // This perfectly centers the arc at the top, eliminating wrap-around logic.
                float angleAlpha = step(abs(angle), _ArcAngle * 0.5);

                // Optional radial alpha falloff across the ring thickness.
                // 0 = disabled, higher values = stronger fade from center side to outer edge.
                float ringT = saturate((dist - (_Radius - _Thickness)) / max(_Thickness, 0.0001));
                float falloffAlpha = (_AlphaFalloff > 0.0) ? pow(ringT, _AlphaFalloff) : 1.0;

                half4 finalColor = input.color;
                finalColor.a *= ringAlpha * angleAlpha * falloffAlpha;

                return finalColor;
            }
            ENDHLSL
        }
    }
}