using UnityEngine;
using UnityEngine.Rendering;

// Cosmetic only: one bounded particle batch per root, with no network objects or colliders.
[DisallowMultipleComponent]
public sealed class IslandAtmosphere : MonoBehaviour
{
    public enum AtmosphereKind { Clouds, Birds }
    [SerializeField] private AtmosphereKind kind;
    [Header("Layout relative to this root")]
    [SerializeField] private Vector3 center;
    [SerializeField] private int seed = 173;
    [Header("Cloud banks around the island")]
    [Tooltip("Optional material for the generated cloud batch. Empty uses the original prefab material.")]
    [SerializeField] private Material cloudMaterial;
    [SerializeField, Range(12, 64)] private int cloudCount = 36;
    [SerializeField] private Vector2 cloudRadius = new Vector2(440f, 440f);
    [SerializeField] private Vector2 cloudHeights = new Vector2(-30f, 55f);
    [SerializeField] private Vector2 cloudSize = new Vector2(145f, 205f);
    [SerializeField, Range(0.5f, 3f)] private float cloudAspect = 1f;
    [SerializeField, Range(0f, 180f)] private float cloudRotationRange = 180f;
    [SerializeField, Range(0f, 1f)] private float cloudOpacity = 0.42f;
    [SerializeField, Min(0f)] private float cloudDrift = 8f;
    [Tooltip("Group clouds into uneven banks with open sky between them, instead of a continuous ring.")]
    [SerializeField] private bool clusteredClouds;
    [SerializeField, Range(3, 8)] private int cloudBankCount = 4;
    [SerializeField, Range(10f, 70f)] private float cloudBankSpread = 42f;
    [Header("Small flocks gliding around the island")]
    [SerializeField, Range(0, 48)] private int birdCount = 18;
    [SerializeField, Range(1, 6)] private int flockCount = 3;
    [SerializeField, Min(10f)] private float birdOrbitRadius = 235f;
    [SerializeField] private Vector2 birdHeights = new Vector2(65f, 100f);
    [SerializeField] private Vector2 orbitSeconds = new Vector2(65f, 95f);
    [SerializeField] private Vector2 birdSize = new Vector2(2.2f, 3.2f);
    [SerializeField, Min(0f)] private float heightVariation = 3f;

    private ParticleSystem particles;
    private ParticleSystem.Particle[] buffer;
    private ParticleSystem[] originalSystems;
    private bool[] originalActive;
    private bool[] originalRenderers;
    private double startedAt;
    private int configuredCount = -1;

    private void OnEnable()
    {
        originalSystems = GetComponentsInChildren<ParticleSystem>(true);
        if (originalSystems.Length == 0) return;
        // Keep the material assigned in the Battle scene, including prefab overrides.
        var source = originalSystems[0].GetComponent<ParticleSystemRenderer>();
        Material material = kind == AtmosphereKind.Clouds && cloudMaterial != null
            ? cloudMaterial : source != null ? source.sharedMaterial : null;
        if (material == null) return;
        originalActive = new bool[originalSystems.Length];
        originalRenderers = new bool[originalSystems.Length];
        for (int i = 0; i < originalSystems.Length; i++)
        {
            var system = originalSystems[i];
            originalActive[i] = system.gameObject.activeSelf;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            originalRenderers[i] = renderer != null && renderer.enabled;
            if (renderer != null) renderer.enabled = false;
            // Deactivate the old emitters too, so Play On Awake cannot restart them
            // later in the same scene-load activation sequence.
            system.gameObject.SetActive(false);
        }

        var visual = new GameObject(kind == AtmosphereKind.Clouds ? "Island cloud banks (generated)" : "Island bird flocks (generated)");
        visual.layer = originalSystems[0].gameObject.layer;
        visual.transform.SetParent(transform, false);
        particles = visual.AddComponent<ParticleSystem>();
        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startSpeed = 0f;
        main.startSize3D = kind == AtmosphereKind.Clouds;
        main.startLifetime = 1000f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission; emission.enabled = false;
        var shape = particles.shape; shape.enabled = false;
        var output = particles.GetComponent<ParticleSystemRenderer>();
        output.sharedMaterial = material;
        output.shadowCastingMode = ShadowCastingMode.Off;
        output.receiveShadows = false;
        output.lightProbeUsage = LightProbeUsage.Off;
        output.reflectionProbeUsage = ReflectionProbeUsage.Off;
        output.renderMode = kind == AtmosphereKind.Clouds ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
        output.sortMode = kind == AtmosphereKind.Clouds ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
        output.velocityScale = 0f;
        output.cameraVelocityScale = 0f;
        output.lengthScale = 1.3f;
        output.maxParticleSize = kind == AtmosphereKind.Clouds ? 1f : 0.03f;
        // Explicit bounds cover the complete orbit, even while the emitter is at the origin.
        float extent = kind == AtmosphereKind.Clouds
            ? Mathf.Max(cloudRadius.x, cloudRadius.y) + Mathf.Max(cloudSize.x, cloudSize.y) + cloudDrift
            : birdOrbitRadius * 1.5f + 20f;
        output.localBounds = new Bounds(center, Vector3.one * (extent * 2f + 240f));
        particles.Play(false);
        particles.Pause(false); // Motion is evaluated analytically; no emission or particle respawn pops.
        startedAt = Time.timeAsDouble;
        configuredCount = -1;
        UpdateParticles(0f);
    }

    private void LateUpdate()
    {
        if (particles != null) UpdateParticles((float)(Time.timeAsDouble - startedAt));
    }

    private void UpdateParticles(float time)
    {
        int count = kind == AtmosphereKind.Clouds ? Mathf.Clamp(cloudCount, 12, 64) : Mathf.Clamp(birdCount, 0, 48);
        if (count != configuredCount)
        {
            buffer = new ParticleSystem.Particle[count];
            var main = particles.main; main.maxParticles = Mathf.Max(1, count);
            configuredCount = count;
        }
        for (int i = 0; i < count; i++)
        {
            float variation = Variation(i, seed);
            Vector3 position;
            Vector3 velocity = Vector3.zero;
            if (kind == AtmosphereKind.Clouds)
            {
                position = GetCloudPosition(i, count, time);
                float size = Mathf.Lerp(cloudSize.x, cloudSize.y, variation);
                buffer[i].startSize3D = new Vector3(size * cloudAspect, size, size);
                buffer[i].startColor = new Color(1f, 1f, 1f, cloudOpacity * Mathf.Lerp(0.8f, 1f, variation));
                buffer[i].rotation = (variation * 2f - 1f) * cloudRotationRange + Mathf.Sin(time * 0.015f + i) * 3f;
            }
            else
            {
                position = BirdPosition(i, count, flockCount, time, birdOrbitRadius, birdHeights, orbitSeconds, heightVariation, seed);
                // A forward sample gives the renderer the tangent, including the gentle vertical glide.
                velocity = (BirdPosition(i, count, flockCount, time + 0.05f, birdOrbitRadius, birdHeights, orbitSeconds, heightVariation, seed) - position) * 20f;
                buffer[i].startSize = Mathf.Lerp(birdSize.x, birdSize.y, variation);
                buffer[i].startColor = Color.white;
            }
            buffer[i].position = center + position;
            buffer[i].velocity = velocity;
            buffer[i].startLifetime = 1000f;
            buffer[i].remainingLifetime = 1000f;
            buffer[i].randomSeed = (uint)(i + 1);
        }
        particles.SetParticles(buffer, count);
    }

    public static Vector3 CloudPosition(int index, int count, float time, Vector2 radius, Vector2 heights, float drift, int seed)
    {
        int bank = index % 2;
        int perBank = (count + 1 - bank) / 2;
        float angle = (index / 2 + bank * 0.5f) * Mathf.PI * 2f / perBank;
        float v = Variation(index, seed);
        float offset = Mathf.Sin(time * 0.018f + v * Mathf.PI * 2f) * drift;
        return new Vector3(Mathf.Cos(angle) * (radius.x + bank * 35f + offset),
            (bank == 0 ? heights.x : heights.y) + Mathf.Sin(time * 0.012f + index) * 5f,
            Mathf.Sin(angle) * (radius.y + bank * 35f + offset));
    }

    private Vector3 GetCloudPosition(int index, int count, float time)
    {
        if (!clusteredClouds)
            return CloudPosition(index, count, time, cloudRadius, cloudHeights, cloudDrift, seed);
        int groups = Mathf.Clamp(cloudBankCount, 3, 8);
        int bank = index % groups;
        float bankVariation = Variation(bank + 211, seed);
        float v = Variation(index, seed);
        float angle = (bank * 360f / groups + 17f + (bankVariation - 0.5f) * 22f
            + (v - 0.5f) * cloudBankSpread) * Mathf.Deg2Rad;
        float radiusScale = Mathf.Lerp(1f, 1.18f, Variation(index + 61, seed));
        float drift = Mathf.Sin(time * 0.018f + v * Mathf.PI * 2f) * cloudDrift;
        float height = Mathf.Lerp(cloudHeights.x, cloudHeights.y,
            Mathf.Clamp01(bankVariation * 0.6f + Variation(index + 113, seed) * 0.4f));
        return new Vector3(Mathf.Cos(angle) * (cloudRadius.x * radiusScale + drift),
            height + Mathf.Sin(time * 0.012f + index) * 5f,
            Mathf.Sin(angle) * (cloudRadius.y * radiusScale + drift));
    }

    public static Vector3 BirdPosition(int index, int count, int groups, float time, float radius, Vector2 heights, Vector2 periods, float bob, int seed)
    {
        groups = Mathf.Clamp(groups, 1, Mathf.Max(1, count));
        int group = index % groups;
        int member = index / groups;
        float v = Variation(index, seed);
        float groupV = Variation(group + 97, seed);
        float period = Mathf.Max(10f, Mathf.Lerp(periods.x, periods.y, groupV));
        float direction = group % 2 == 0 ? 1f : -1f;
        float angle = group * Mathf.PI * 2f / groups + member * 0.085f + direction * time * Mathf.PI * 2f / period;
        float r = radius * Mathf.Lerp(0.8f, 1.1f, groupV) + (v - 0.5f) * 14f;
        return new Vector3(Mathf.Cos(angle) * r,
            Mathf.Lerp(heights.x, heights.y, groupV) + (v - 0.5f) * 8f + Mathf.Sin(time * 0.38f + v * 6.283185f) * bob,
            Mathf.Sin(angle) * r * Mathf.Lerp(0.82f, 1f, groupV));
    }

    private static float Variation(int index, int seed)
    {
        uint value = unchecked((uint)(index + seed) * 747796405u + 2891336453u);
        value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
        return ((value >> 22) ^ value) / (float)uint.MaxValue;
    }

    private void OnDisable()
    {
        if (particles != null)
        {
            particles.gameObject.SetActive(false);
            Destroy(particles.gameObject);
            particles = null;
        }
        if (originalSystems == null || originalActive == null) return;
        for (int i = 0; i < originalSystems.Length; i++)
        {
            var system = originalSystems[i];
            if (system == null) continue;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = originalRenderers[i];
            system.gameObject.SetActive(originalActive[i]);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = kind == AtmosphereKind.Clouds ? new Color(0.65f, 0.9f, 1f) : Color.yellow;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        if (kind == AtmosphereKind.Clouds)
        {
            for (int i = 0; i < cloudCount; i++)
                Gizmos.DrawWireSphere(center + GetCloudPosition(i, cloudCount, 0), Mathf.Min(cloudSize.x, cloudSize.y) * 0.5f);
        }
        else
        {
            for (int i = 0; i < birdCount; i++)
                Gizmos.DrawSphere(center + BirdPosition(i, birdCount, flockCount, 0, birdOrbitRadius, birdHeights, orbitSeconds, heightVariation, seed), 2f);
        }
        Gizmos.matrix = previous;
    }
}
