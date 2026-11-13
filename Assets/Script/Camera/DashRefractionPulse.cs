using System;
using UnityEngine;

// Local screen-space shockwave; observes actual boost release, never changes movement.
[Serializable]
public sealed class DashRefractionPulse
{
    public bool enabled = true;
    [Tooltip("대쉬 가속 시작 후 굴절 파동이 퍼지는 시간(초).")]
    [Min(.05f)] public float seconds = .55f;
    [Tooltip("화면 높이에 대한 최대 굴절량. 0.014는 1080p에서 약 15픽셀입니다.")]
    [Range(0f, .05f)] public float strength = .014f;
    [Tooltip("화면 높이를 기준으로 한 파동 띠의 반폭.")]
    [Range(.02f, .4f)] public float bandWidth = .15f;
    [Tooltip("파동 띠 내부 sin 진동 횟수. 높이면 잔물결이 촘촘해집니다.")]
    [Range(.5f, 6f)] public float cycles = 2f;
    [Tooltip("화면 높이를 기준으로 한 최종 확산 반지름.")]
    [Range(.3f, 2f)] public float maximumRadius = 1.3f;

    [NonSerialized] private Player owner;
    [NonSerialized] private int portal;
    [NonSerialized] private bool wasBoosting;
    [NonSerialized] private float startedAt = -1f;
    [NonSerialized] private Vector2 center;
    public Vector4 Ring { get; private set; } // center xy, radius, displacement amplitude
    public Vector4 Shape { get; private set; } // half width, cycles, aspect
    public bool Active => Ring.w > .000001f;

    public void Reset()
    {
        owner = null; wasBoosting = false; startedAt = -1f; Ring = Vector4.zero;
    }

    public void Update(Player player, Camera camera, bool allowed)
    {
        if (!enabled || strength <= 0f || !allowed || player == null || camera == null ||
            Time.timeScale <= 0f || Time.unscaledDeltaTime > .25f)
        { Reset(); return; }

        bool boosting = player.IsBoosting;
        if (owner != player || portal != player.PortalExitSequence)
        {
            owner = player; portal = player.PortalExitSequence; wasBoosting = boosting;
            startedAt = -1f; Ring = Vector4.zero;
            return; // Spawning, portals and returning to gameplay must not fake a dash.
        }
        if (boosting && !wasBoosting)
        {
            Vector3 viewport = camera.WorldToViewportPoint(player.LockAimPoint);
            bool onScreen = viewport.z > camera.nearClipPlane && viewport.x >= 0f && viewport.x <= 1f &&
                viewport.y >= 0f && viewport.y <= 1f;
            startedAt = onScreen ? Time.unscaledTime : -1f;
            center = new Vector2(viewport.x, viewport.y);
        }
        wasBoosting = boosting;
        if (!boosting) startedAt = -1f;
        float progress = startedAt < 0f ? 1f : (Time.unscaledTime - startedAt) / Mathf.Max(.05f, seconds);
        if (progress >= 1f) { startedAt = -1f; Ring = Vector4.zero; return; }
        float amplitude = Mathf.Clamp(strength, 0f, .05f) * Envelope(progress);
        Ring = new Vector4(center.x, center.y, Mathf.Clamp(maximumRadius, .3f, 2f) * progress, amplitude);
        Shape = new Vector4(Mathf.Clamp(bandWidth, .02f, .4f), Mathf.Clamp(cycles, .5f, 6f),
            Mathf.Max(.1f, camera.aspect), 0f);
    }

    internal static float Envelope(float progress)
    {
        if (progress <= 0f || progress >= 1f) return 0f;
        float fade = 1f - progress;
        return Mathf.SmoothStep(0f, 1f, progress / .08f) * fade * fade;
    }
}
