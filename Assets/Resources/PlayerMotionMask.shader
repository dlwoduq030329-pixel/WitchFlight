Shader "Hidden/WitchFlight/PlayerMotionMask"
{
    Properties
    {
        _MainTex ("Main alpha", 2D) = "white" {}
        _Color ("Source alpha", Color) = (1,1,1,1)
        _AlphaMask ("Alpha mask", 2D) = "white" {}
        _AlphaMaskMode ("Mask mode", Float) = 0
        _AlphaMaskScale ("Mask scale", Float) = 1
        _AlphaMaskValue ("Mask offset", Float) = 0
        _Cutoff ("Cutoff", Float) = 0.001
        _Cull ("Cull", Float) = 0
        _Invisible ("Invisible", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PlayerMotionMask"
            ZWrite Off ZTest Always Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);
            TEXTURE2D_X(_SpeedSceneDepth);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _Color;
            float _AlphaMaskMode, _AlphaMaskScale, _AlphaMaskValue, _Cutoff, _Invisible;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }
            half2 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(0.5 - _Invisible);
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a;
                half mask = saturate(SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv).r * _AlphaMaskScale + _AlphaMaskValue);
                if (_AlphaMaskMode == 1) alpha = mask;
                else if (_AlphaMaskMode == 2) alpha *= mask;
                else if (_AlphaMaskMode == 3) alpha = saturate(alpha + mask);
                else if (_AlphaMaskMode == 4) alpha = saturate(alpha - mask);
                clip(alpha - max(0.001, _Cutoff));
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneZ = SAMPLE_TEXTURE2D_X(_SpeedSceneDepth, sampler_PointClamp, screenUV).r;
                float sceneEye = unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(sceneZ) : LinearEyeDepth(sceneZ, _ZBufferParams);
                float playerEye = unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(input.positionCS.z) : LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                clip(sceneEye + 0.015 - playerEye);
                // Premultiplied depth survives linear filtering at transparent silhouette edges.
                return half2(saturate(alpha), playerEye * saturate(alpha));
            }
            ENDHLSL
        }
    }
}
