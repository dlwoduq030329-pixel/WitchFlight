Shader "Hidden/WitchFlight/SmokeInterior"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+105" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float3 _SmokeOrigin, _SmokeCamera;
            float _ClearDistance, _FadeDistance;
            half4 _SmokeColor;
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = float4(input.positionOS.xy * 2, 0, 1);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Water reflections and auxiliary cameras never inherit the local veil.
                clip(.02 - distance(_WorldSpaceCameraPos, _SmokeCamera));
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float fog = smoothstep(_ClearDistance, _ClearDistance + max(.1, _FadeDistance), distance(world, _SmokeOrigin));
                return half4(_SmokeColor.rgb, fog * _SmokeColor.a);
            }
            ENDHLSL
        }
    }
}
