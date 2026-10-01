Shader "Hidden/WitchFlight/PeripheralSpeedBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "PeripheralSpeedBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D_X(_SpeedMotionTexture);
            float4 _PeripheralBlur; // strength, clear central radius, max UV distance

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // A soft elliptical peripheral mask; center/aim stays untouched by this pass.
                float radius = length((uv - 0.5) * 2.0);
                float edge = smoothstep(_PeripheralBlur.y, 1.0, radius);
                float2 velocity = SAMPLE_TEXTURE2D_X(_SpeedMotionTexture, sampler_PointClamp, uv).xy;
                float speed = length(velocity);
                // ForceNoMotion on the local player's renderers means no added blur here.
                if (edge < 0.001 || speed < 0.00001) return original;
                float2 offset = -velocity / speed * min(speed * _PeripheralBlur.x * edge, _PeripheralBlur.z);
                half3 sum = original.rgb;
                float total = 1.0;
                [unroll]
                for (int i = 1; i <= 12; i++)
                {
                    float t = (float)i / 12.0;
                    float2 sampleUV = saturate(uv + offset * t);
                    float2 sampleVelocity = SAMPLE_TEXTURE2D_X(_SpeedMotionTexture, sampler_PointClamp, sampleUV).xy;
                    // Do not smear a sharp character silhouette out into moving scenery.
                    float valid = step(0.00001, length(sampleVelocity));
                    float weight = valid * (1.0 - 0.5 * t);
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb * weight;
                    total += weight;
                }
                return half4(sum / total, original.a);
            }
            ENDHLSL
        }
    }
}
