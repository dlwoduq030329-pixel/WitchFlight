Shader "Hidden/WitchFlight/PeripheralSpeedBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "SeparatedSpeedMotionBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D_X(_SpeedMotionTexture);
            TEXTURE2D_X(_PlayerMotionMask);
            TEXTURE2D_X(_SpeedSceneDepth);
            float4 _PeripheralBlur; // strength, clear central radius, maximum UV distance
            float4 _BackgroundBlur; // intensity, max UV distance, quality
            float4 _PlayerTrail; // viewport translation xy, radial scale delta, strength
            float4 _PlayerAnchor; // viewport center xy, eye-depth offset, body preservation
            float4 _PlayerRect;

            half2 PlayerMask(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                return SAMPLE_TEXTURE2D_X(_PlayerMotionMask, sampler_LinearClamp, uv).rg;
            }

            half3 BackgroundBlur(float2 uv, half3 original, half playerMask)
            {
                // Both center pixels AND every background sample exclude the player's silhouette.
                if (playerMask > 0.001) return original;
                float2 velocity = SAMPLE_TEXTURE2D_X(_SpeedMotionTexture, sampler_PointClamp, uv).xy;
                float speed = length(velocity);
                if (speed < 0.00001) return original;
                float edge = smoothstep(_PeripheralBlur.y, 1.0, length((uv - 0.5) * 2.0));
                float mainDistance = min(speed * _BackgroundBlur.x, _BackgroundBlur.y);
                float extraDistance = min(speed * _PeripheralBlur.x * edge, _PeripheralBlur.z);
                if (mainDistance + extraDistance < 0.00001) return original;
                float2 direction = -velocity / speed;
                int samples = extraDistance > 0.00001 ? 12 : (_BackgroundBlur.z < 0.5 ? 4 : (_BackgroundBlur.z < 1.5 ? 6 : 8));
                half3 sum = original;
                float total = 1.0;
                [loop]
                for (int i = 0; i < samples; i++)
                {
                    float t = (i + 0.5) / samples;
                    float2 sampleUV = saturate(uv + direction * lerp(-mainDistance, mainDistance + extraDistance, t));
                    float valid = 1.0 - step(0.001, PlayerMask(sampleUV).r);
                    float weight = valid * (1.0 - 0.5 * abs(t * 2.0 - 1.0));
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb * weight;
                    total += weight;
                }
                return sum / total;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half mask = PlayerMask(uv).r;
                half3 background = BackgroundBlur(uv, original.rgb, mask);
                float2 anchor = _PlayerAnchor.xy;
                float2 shift = _PlayerTrail.xy;
                float4 rect = _PlayerRect;
                #if UNITY_UV_STARTS_AT_TOP
                    // WorldToViewportPoint is bottom-left; D3D render textures are top-left.
                    anchor.y = 1.0 - anchor.y;
                    shift.y = -shift.y;
                    rect.yw = 1.0 - rect.wy;
                #endif
                // Character blur costs taps only in its small, expanded screen rectangle.
                if (_PlayerTrail.w < 0.001 || any(uv < rect.xy) || any(uv > rect.zw))
                    return half4(background, original.a);

                float sceneZ = SAMPLE_TEXTURE2D_X(_SpeedSceneDepth, sampler_PointClamp, uv).r;
                float sceneEye = unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(sceneZ) : LinearEyeDepth(sceneZ, _ZBufferParams);
                half3 sum = 0;
                float coverage = 0;
                float total = 0;
                [unroll]
                for (int i = 1; i <= 16; i++)
                {
                    float t = (float)i / 16.0;
                    // Inverse of moving the current silhouette BACK along world movement.
                    // This also grows/shrinks correctly when flying toward/away from camera.
                    float scale = max(0.1, 1.0 + _PlayerTrail.z * t);
                    float2 sampleUV = anchor + (uv - anchor - shift * t) / scale;
                    half2 sourceMask = PlayerMask(sampleUV);
                    float sourceEye = sourceMask.g / max(sourceMask.r, 0.001);
                    float behindWall = step(sceneEye + 0.02, sourceEye + _PlayerAnchor.z * t);
                    float kernel = (1.0 - t) * (1.0 - t);
                    float alpha = sourceMask.r * (1.0 - behindWall) * kernel;
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(sampleUV)).rgb * alpha;
                    coverage += alpha;
                    total += kernel;
                }
                float opacity = saturate(coverage / max(total, 0.0001) * _PlayerTrail.w);
                opacity *= 1.0 - mask * _PlayerAnchor.w;
                half3 trailColor = sum / max(coverage, 0.0001);
                return half4(lerp(background, trailColor, opacity), original.a);
            }
            ENDHLSL
        }
    }
}
