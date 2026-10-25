using System;
using UnityEngine;

// Viewer-local sensations only. Never writes Player state, steering or colliders.
[Serializable]
public sealed class FlightSpeedPresentation
{
    public bool enabled = true;
    [Header("Acceleration and boost")]
    [Range(0f, 10f)] public float boostFieldOfView = 4f;
    [Range(0f, 5f)] public float accelerationFieldOfView = 1.5f;
    [Min(0f)] public float cruiseCameraDistance = .25f;
    [Min(0f)] public float boostCameraDistance = .35f;
    [Min(.1f)] public float response = 5f;
    [Tooltip("가속 시작 순간을 강조하는 짧은 연출의 길이(초)입니다.")]
    [Min(.1f)] public float accelerationSeconds = .65f;
    [Header("Keep targeting clear")]
    [Range(.3f, .9f)] public float minimumClearRadius = .5f;
    [Range(.3f, .9f)] public float boostClearRadius = .4f;
    [Header("Passing scenery (local, ten samples per second)")]
    public LayerMask sceneryLayers = ~0;
    [Min(1f)] public float sceneryDistance = 10f;
    [Min(.1f)] public float passCooldown = .8f;

    [NonSerialized] private Player observedPlayer;
    [NonSerialized] private int portal;
    [NonSerialized] private bool initialized, previousBoost, nearSurface;
    [NonSerialized] private float previousRatio, pulseAge = 100f, nextSample, nextPass;
    [NonSerialized] private float suppressAccelerationUntil;
    [NonSerialized] private float speed, boost, acceleration, proximity;
    [NonSerialized] private Vector3 previousPosition;
    [NonSerialized] private RaycastHit[] hits;
    [NonSerialized] private BattleAudioTable audioTable;
    [NonSerialized] private GameObject audioRoot;
    [NonSerialized] private AudioSource windAudio, passAudio;
    [NonSerialized] private float passAge = 100f, passStrength, passPan;

    public float ExtraFieldOfView => enabled ? boost * boostFieldOfView + acceleration * accelerationFieldOfView : 0f;
    public float CameraDistance => enabled ? speed * speed * cruiseCameraDistance + boost * boostCameraDistance : 0f;
    public float BlurWeight => enabled ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1f, speed)) *
        Mathf.Lerp(.55f, 1f, Mathf.Max(boost, acceleration)) : 1f;
    public float WindWeight => enabled ? Mathf.Clamp01(speed * speed * .6f + boost * .3f + acceleration * .25f + proximity * .2f) : 1f;
    public float WindTravel => enabled ? Mathf.Lerp(.55f, 1.65f, speed) + boost * .45f : 1f;
    public float ClearRadius => enabled ? Mathf.Lerp(minimumClearRadius, boostClearRadius, boost) : 0f;

    public float FieldOfViewScale(int stage)
        => !enabled ? 1f : ActualStageWeight(stage, speed);

    internal static float ActualStageWeight(int stage, float ratio)
    {
        if (stage <= 0) return 0f;
        return Mathf.Clamp01(ratio / (Mathf.Clamp(stage, 1, 3) / 3f));
    }

    internal static float Pulse(float age, float duration)
    {
        if (age < 0f || duration <= 0f || age >= duration) return 0f;
        float t = age / duration;
        return t < .2f ? Mathf.SmoothStep(0f, 1f, t / .2f) :
            1f - Mathf.SmoothStep(0f, 1f, (t - .2f) / .8f);
    }

    internal static float PassWeight(float distance, float range, float facing, float ratio)
    {
        if (range <= 0f || distance < 0f) return 0f;
        // A frontal wall impact is not a fly-by; parallel motion is strongest.
        return (1f - Mathf.Clamp01(distance / range)) *
            (1f - Mathf.Clamp01(Mathf.Abs(facing))) * Mathf.InverseLerp(.35f, .9f, ratio);
    }

    public void Update(Player player, Transform camera, bool allowed)
    {
        if (!enabled || !allowed || Time.timeScale <= 0f || Time.deltaTime > .25f)
        {
            Reset();
            return;
        }
        float ratio = player.CurrentSpeedStage > 0 ? player.ForwardCruiseSpeedRatio : 0f;
        bool boosting = player.CurrentSpeedStage > 0 && player.IsBoosting;
        bool discontinuity = !initialized || observedPlayer != player || portal != player.PortalExitSequence ||
            (player.transform.position - previousPosition).sqrMagnitude >
            Mathf.Pow(Mathf.Max(12f, Mathf.Abs(player.CurrentSpeed) * Time.deltaTime * 3f), 2f);
        if (discontinuity)
        {
            Reset();
            initialized = true;
            observedPlayer = player;
            portal = player.PortalExitSequence;
            previousRatio = ratio;
            previousBoost = boosting;
        }
        float dt = Mathf.Min(Time.deltaTime, .1f);
        // A selected stage or blocked motor must not sound like forward travel.
        float travelledSpeed = discontinuity ? 0f : Mathf.Max(0f,
            Vector3.Dot(player.transform.position - previousPosition, player.transform.forward) / Mathf.Max(.001f, dt));
        ratio *= Mathf.Clamp01(travelledSpeed / Mathf.Max(.1f, Mathf.Abs(player.CurrentSpeed)));
        float blend = 1f - Mathf.Exp(-Mathf.Max(.1f, response) * dt);
        float accelerationRate = (ratio - previousRatio) / Mathf.Max(.001f, dt);
        // No pulse on spawn/portal/resume and no retrigger while holding boost.
        if (!discontinuity && Time.time >= suppressAccelerationUntil && ((boosting && !previousBoost) ||
            (accelerationRate > .65f && previousRatio < .12f && ratio >= .12f)))
            pulseAge = 0f;
        previousRatio = ratio;
        previousBoost = boosting;
        previousPosition = player.transform.position;
        pulseAge += dt;
        speed = Mathf.Lerp(speed, ratio, blend);
        boost = Mathf.Lerp(boost, boosting ? ratio : 0f, blend);
        acceleration = Pulse(pulseAge, accelerationSeconds) * Mathf.Clamp01(ratio * 3f);
        if (Time.time >= nextSample)
        {
            nextSample = Time.time + .1f;
            SampleScenery(player, camera);
        }
        UpdateAudio(camera, dt);
    }

    private void SampleScenery(Player player, Transform camera)
    {
        hits ??= new RaycastHit[24];
        float best = 0f, pan = 0f;
        Vector3 origin = player.LockAimPoint;
        Vector3 forward = player.transform.forward;
        for (int ray = 0; ray < 6; ray++)
        {
            Vector3 direction = ray switch
            {
                0 => camera.right, 1 => -camera.right, 2 => camera.up,
                3 => -camera.up, 4 => (forward + camera.right).normalized,
                _ => (forward - camera.right).normalized
            };
            int count = Physics.RaycastNonAlloc(origin, direction, hits, Mathf.Max(1f, sceneryDistance),
                sceneryLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.attachedRigidbody != null ||
                    collider.GetComponentInParent<Player>() != null ||
                    collider.GetComponentInParent<MagicProjectile>() != null) continue;
                float strength = PassWeight(hits[i].distance, sceneryDistance,
                    Vector3.Dot(forward, hits[i].normal), speed);
                if (strength <= best) continue;
                best = strength;
                pan = Vector3.Dot(direction, camera.right) * .65f;
            }
        }
        proximity = best;
        if (!nearSurface && best > .3f && Time.time >= nextPass)
        {
            nextPass = Time.time + Mathf.Max(.1f, passCooldown);
            passAge = 0f;
            passStrength = best;
            passPan = pan;
        }
        // Following one wall cannot continually retrigger the pass sound.
        if (best > .3f) nearSurface = true;
        else if (best < .12f) nearSurface = false;
    }

    private void EnsureAudio(Transform camera)
    {
        if (audioRoot != null) return;
        audioTable = Resources.Load<BattleAudioTable>("BattleAudioTable");
        if (audioTable == null) return;
        audioRoot = new GameObject("Flight wind (local)");
        audioRoot.transform.SetParent(camera, false);
        windAudio = audioRoot.AddComponent<AudioSource>();
        passAudio = audioRoot.AddComponent<AudioSource>();
        foreach (AudioSource source in new[] { windAudio, passAudio })
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.priority = 180; // Combat cues have priority over ambient wind.
        }
    }

    private void UpdateAudio(Transform camera, float dt)
    {
        EnsureAudio(camera);
        if (audioTable == null || windAudio == null) return;
        float master = Mathf.Clamp01(audioTable.masterVolume);
        float windVolume = Mathf.Clamp01(audioTable.flightWindVolume) * master *
            Mathf.Clamp01(speed * speed * .65f + boost * .25f + acceleration * .15f);
        ApplyAudio(windAudio, audioTable.flightWindClip, windVolume, Mathf.Lerp(.8f, 1.25f, speed) + boost * .1f, 0f);
        passAge += dt;
        float passVolume = Pulse(passAge, .55f) * passStrength * Mathf.Clamp01(audioTable.flightPassVolume) * master;
        ApplyAudio(passAudio, audioTable.flightPassClip != null ? audioTable.flightPassClip : audioTable.flightWindClip,
            passVolume, 1.3f, passPan);
    }

    private void ApplyAudio(AudioSource source, AudioClip clip, float volume, float pitch, float pan)
    {
        if (source.clip != clip) { source.Stop(); source.clip = clip; }
        source.outputAudioMixerGroup = audioTable.outputMixer;
        source.volume = volume;
        source.pitch = pitch;
        source.panStereo = pan;
        if (clip != null && volume > .001f) { if (!source.isPlaying) source.Play(); }
        else if (source.isPlaying) source.Stop();
    }

    public void Reset()
    {
        initialized = false;
        observedPlayer = null;
        speed = boost = acceleration = proximity = 0f;
        pulseAge = passAge = 100f;
        nearSurface = false;
        nextSample = 0f;
        nextPass = Time.time + .5f;
        suppressAccelerationUntil = Time.time + .35f;
        if (windAudio != null) windAudio.Stop();
        if (passAudio != null) passAudio.Stop();
    }

    public void Dispose()
    {
        Reset();
        if (audioRoot != null) UnityEngine.Object.Destroy(audioRoot);
        audioRoot = null;
        windAudio = passAudio = null;
    }
}
