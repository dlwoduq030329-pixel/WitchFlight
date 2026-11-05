using Fusion;
using UnityEngine;

// Cached per pooled visual. No scene searches or particle enumeration during playback.
public sealed class TimeVfxSpeed : MonoBehaviour
{
    private NetworkRunner runner;
    private int team;
    private bool projectile;
    private ParticleSystem[] particles;
    private Animator[] animators;
    private float[] particleSpeeds, animatorSpeeds, appliedAnimatorSpeeds;
    private float multiplier = 1f;
    public float Multiplier => multiplier;

    public static TimeVfxSpeed Bind(GameObject root, NetworkRunner runner, int team, bool projectile)
    {
        if (root == null) return null;
        var clock = root.GetComponent<TimeVfxSpeed>();
        if (clock == null) clock = root.AddComponent<TimeVfxSpeed>();
        clock.runner = runner; clock.team = team; clock.projectile = projectile;
        return clock;
    }
    private void Awake()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        animators = GetComponentsInChildren<Animator>(true);
        particleSpeeds = new float[particles.Length];
        animatorSpeeds = new float[animators.Length]; appliedAnimatorSpeeds = new float[animators.Length];
        for (int i = 0; i < particles.Length; i++) particleSpeeds[i] = particles[i].main.simulationSpeed;
        for (int i = 0; i < animators.Length; i++) animatorSpeeds[i] = appliedAnimatorSpeeds[i] = animators[i].speed;
    }
    private void LateUpdate()
    {
        // Most frames contain no time field: no spatial queries or animator traversal.
        if (!MagicProjectile.HasTimeFields && multiplier == 1f) return;
        float next = runner != null && runner.IsRunning ? projectile
            ? MagicProjectile.TimeProjectileMultiplier(runner, transform.position)
            : MagicProjectile.TimeMovementMultiplier(runner, transform.position, team) : 1f;
        if (!Mathf.Approximately(next, multiplier))
            for (int i = 0; i < particles.Length; i++)
                if (particles[i] != null) { var main = particles[i].main; main.simulationSpeed = particleSpeeds[i] * next; }
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] == null) continue;
            // Respect cinematics which set a separate baseline animation speed.
            if (!Mathf.Approximately(animators[i].speed, appliedAnimatorSpeeds[i])) animatorSpeeds[i] = animators[i].speed;
            appliedAnimatorSpeeds[i] = animatorSpeeds[i] * next;
            if (!Mathf.Approximately(animators[i].speed, appliedAnimatorSpeeds[i])) animators[i].speed = appliedAnimatorSpeeds[i];
        }
        multiplier = next;
    }
    private void OnDisable()
    {
        if (particles == null) return;
        for (int i = 0; i < particles.Length; i++)
            if (particles[i] != null) { var main = particles[i].main; main.simulationSpeed = particleSpeeds[i]; }
        for (int i = 0; i < animators.Length; i++)
            if (animators[i] != null)
            {
                if (!Mathf.Approximately(animators[i].speed, appliedAnimatorSpeeds[i]))
                    animatorSpeeds[i] = animators[i].speed;
                animators[i].speed = appliedAnimatorSpeeds[i] = animatorSpeeds[i];
            }
        multiplier = 1f;
    }
}
