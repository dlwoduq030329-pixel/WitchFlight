using UnityEngine;

// Local received voice only; does not change microphone transmission gain.
[RequireComponent(typeof(AudioSource))]
public sealed class VoicePlaybackGain : MonoBehaviour
{
    private volatile float gain = 1f;
    public void SetGain(float value) => gain = Mathf.Clamp(value, 1f, 4f);

    private void OnAudioFilterRead(float[] samples, int channels)
    {
        float multiplier = gain;
        if (multiplier <= 1f) return;
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i] * multiplier;
            float magnitude = System.Math.Abs(value);
            // Soft-limit peaks rather than hard-clipping boosted speech.
            if (magnitude > 0.9f)
            {
                float excess = magnitude - 0.9f;
                value = (value < 0f ? -1f : 1f) * (0.9f + 0.1f * excess / (0.1f + excess));
            }
            samples[i] = value;
        }
    }
}
