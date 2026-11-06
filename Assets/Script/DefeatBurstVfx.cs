using UnityEngine;
using UnityEngine.Rendering;

// Viewer-local presentation, leased from the existing VFX pool. No physics contacts,
// network objects, camera searches or per-frame allocations. Children are built once
// per pooled instance and owned/reset here (the pool captures the root before Awake).
[DisallowMultipleComponent]
public sealed class DefeatBurstVfx : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Material smokeMaterial;
    [SerializeField] private Material flashMaterial;
    [SerializeField] private Color smokeColor = new Color(.77f, .81f, .87f, .72f);
    [SerializeField] private Color flashColor = new Color(1f, .86f, .66f, 1f);
    [Header("Flash / actual lighting")]
    [SerializeField, Min(.03f)] private float flashSeconds = .18f;
    [SerializeField, Min(.1f)] private float flashSize = 16f;
    [SerializeField, Min(0f)] private float lightIntensity = 20f;
    [SerializeField, Min(.1f)] private float lightRange = 24f;
    [Header("Explosion impact")]
    [SerializeField, Min(.1f)] private float shockwaveSize = 24f;
    [SerializeField, Min(.1f)] private float shockwaveSeconds = .36f;
    [SerializeField, Range(8, 32)] private int fireCount = 24;
    [SerializeField, Min(.1f)] private float fireSpeed = 22f;
    [SerializeField, Min(.1f)] private float fireSize = 4f;
    [SerializeField, Min(.1f)] private float fireSeconds = .42f;
    [SerializeField, Range(0f, .2f)] private float smokeDelay = .065f;
    [Header("Smoke burst")]
    [SerializeField, Range(8, 64)] private int smokeCount = 28;
    [SerializeField, Range(.4f, 4f)] private float smokeSeconds = 1.8f;
    [SerializeField, Min(.1f)] private float smokeSize = 4.5f;
    [SerializeField, Min(0f)] private float burstSpeed = 10f;
    [Header("Falling body displaces smoke")]
    [SerializeField, Min(.1f)] private float wakeRadius = 4f;
    [SerializeField, Min(0f)] private float sidewaysPush = 9f;
    [SerializeField, Range(0f, 1f)] private float downwardDrag = .45f;
    [SerializeField, Min(0f)] private float curlStrength = 2f;

    private const int Capacity = 64, MaxConcurrentBursts = 12, MaxConcurrentLights = 3;
    private static int activeBursts, activeLights;
    private static GameObject warmedPrefab, warmInstance;
    private readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[Capacity];
    private ParticleSystem smoke, flash, shockwave, fire;
    private Light burstLight;
    private FallingDefeatVisual body;
    private Vector3 previousBody;
    private float elapsed;
    private bool running, ownsLight, smokeEmitted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCounters()
    {
        activeBursts = activeLights = 0;
        warmedPrefab = warmInstance = null;
    }

    // Called once when a network player initializes; pooled scene objects disappear on unload.
    // Prepare two instances without emitting particles or enabling a light.
    public static void Prewarm(GameObject prefab)
    {
        if (prefab == null || (warmedPrefab == prefab && warmInstance != null) ||
            !prefab.TryGetComponent<DefeatBurstVfx>(out _)) return;
        GameObject first = MagicVfxPool.Rent(prefab, Vector3.zero, Quaternion.identity);
        GameObject second = MagicVfxPool.Rent(prefab, Vector3.zero, Quaternion.identity);
        warmedPrefab = prefab;
        warmInstance = first;
        MagicVfxPool.Release(first);
        MagicVfxPool.Release(second);
    }

    public static void Show(GameObject prefab, Vector3 position, FallingDefeatVisual fallingBody)
    {
        if (prefab == null || activeBursts >= MaxConcurrentBursts) return;
        GameObject view = MagicVfxPool.Rent(prefab, position, Quaternion.identity);
        if (view == null) return;
        if (view.TryGetComponent(out DefeatBurstVfx burst)) burst.Begin(fallingBody);
        else MagicVfxPool.Release(view, 3f); // Also permits an authored replacement prefab.
    }

    private void Awake()
    {
        smoke = CreateParticles("Smoke / displaced by falling body", smokeMaterial, Capacity);
        flash = CreateParticles("Flash", flashMaterial, 1);
        shockwave = CreateParticles("Expanding pressure ring", flashMaterial, 1);
        fire = CreateParticles("Explosive flame and sparks", flashMaterial, 32);
        SetShaderMode(shockwave, 2f, 4f);
        SetShaderMode(fire, 3f, 3f);
        var fireFade = fire.colorOverLifetime;
        fireFade.enabled = true;
        var flameGradient = new Gradient();
        flameGradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
            new GradientColorKey(new Color(1f, .55f, .12f), .25f),
            new GradientColorKey(new Color(.65f, .16f, .04f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.9f, .35f), new GradientAlphaKey(0f, 1f) });
        fireFade.color = flameGradient;
        var fireGrowth = fire.sizeOverLifetime;
        fireGrowth.enabled = true;
        fireGrowth.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, .25f), new Keyframe(.2f, 1f), new Keyframe(1f, .35f)));
        var lightObject = new GameObject("Brief shadowless flash light");
        lightObject.transform.SetParent(transform, false);
        burstLight = lightObject.AddComponent<Light>();
        burstLight.type = LightType.Point;
        burstLight.shadows = LightShadows.None;
        burstLight.renderMode = LightRenderMode.ForcePixel;
        burstLight.enabled = false;

        var fade = smoke.colorOverLifetime;
        fade.enabled = true;
        var smokeFade = new Gradient();
        smokeFade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(.6f, 0), new GradientAlphaKey(1f, .12f),
                new GradientAlphaKey(.7f, .4f), new GradientAlphaKey(0f, 1f) });
        fade.color = smokeFade;
        var growth = smoke.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, .3f), new Keyframe(.14f, 1f), new Keyframe(1f, 1.6f)));
    }

    private static void SetShaderMode(ParticleSystem system, float mode, float brightness)
    {
        var block = new MaterialPropertyBlock();
        block.SetFloat("_Flash", mode);
        block.SetFloat("_Brightness", brightness);
        system.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
    }

    private ParticleSystem CreateParticles(string label, Material material, int count)
    {
        var child = new GameObject(label);
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = false;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = .65f;
        return system;
    }

    private void Begin(FallingDefeatVisual fallingBody)
    {
        body = fallingBody;
        previousBody = body != null ? body.EffectCenter : transform.position;
        elapsed = 0f;
        running = true;
        activeBursts++;
        smoke.Clear();
        flash.Clear();
        shockwave.Clear();
        fire.Clear();
        smokeEmitted = false;
        if (flashMaterial != null)
        {
            flash.Play(false);
            flash.Emit(new ParticleSystem.EmitParams { position = transform.position, velocity = Vector3.zero,
                startSize = flashSize * .25f, startLifetime = Mathf.Max(.03f, flashSeconds), startColor = flashColor }, 1);
            shockwave.Play(false);
            shockwave.Emit(new ParticleSystem.EmitParams { position = transform.position, velocity = Vector3.zero,
                startSize = 1f, startLifetime = shockwaveSeconds, startColor = flashColor }, 1);
            fire.Play(false);
            int count = Mathf.Clamp(fireCount, 8, 32);
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                bool spark = i % 3 == 0;
                fire.Emit(new ParticleSystem.EmitParams {
                    position = transform.position + direction * .2f,
                    velocity = direction * fireSpeed * (spark ? 1.5f : Random.Range(.55f, 1f)),
                    startSize = fireSize * (spark ? .2f : Random.Range(.65f, 1.1f)),
                    startLifetime = fireSeconds * Random.Range(.75f, 1f),
                    startColor = Color.white, rotation = Random.Range(0f, 360f)
                }, 1);
            }
        }
        ownsLight = lightIntensity > 0f && activeLights < MaxConcurrentLights;
        if (ownsLight) activeLights++;
        burstLight.color = flashColor;
        burstLight.range = Mathf.Max(.1f, lightRange);
        burstLight.intensity = lightIntensity;
        burstLight.enabled = ownsLight;
    }

    private void EmitSmoke()
    {
        smokeEmitted = true;
        if (smokeMaterial != null)
        {
            smoke.Play(false);
            int count = Mathf.Clamp(smokeCount, 8, Capacity);
            for (int i = 0; i < count; i++)
            {
                // Even spherical distribution with small random variations: avoids a dense
                // opaque centre hiding the body; remains different for each explosion.
                float y = 1f - 2f * (i + .5f) / count;
                float angle = i * 2.399963f + Random.Range(-.15f, .15f);
                float ring = Mathf.Sqrt(1f - y * y);
                Vector3 direction = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                smoke.Emit(new ParticleSystem.EmitParams {
                    position = transform.position + direction * Random.Range(.4f, 1.2f),
                    velocity = direction * burstSpeed * Random.Range(.65f, 1.15f),
                    startSize = smokeSize * Random.Range(.75f, 1.2f),
                    startLifetime = smokeSeconds * Random.Range(.8f, 1f),
                    startColor = smokeColor, rotation = Random.Range(0f, 360f)
                }, 1);
            }
        }
    }

    private void LateUpdate()
    {
        if (!running) return;
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { MagicVfxPool.Release(gameObject); return; }
        elapsed += Time.deltaTime;
        if (elapsed >= Mathf.Max(smokeSeconds + smokeDelay, Mathf.Max(fireSeconds, Mathf.Max(shockwaveSeconds, flashSeconds))) + .05f)
        { MagicVfxPool.Release(gameObject); return; }
        if (!smokeEmitted && elapsed >= smokeDelay) EmitSmoke();

        // Starts at full brightness immediately, brief hold, then fast falloff.
        float flashAlpha = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(.025f, Mathf.Max(.03f, flashSeconds), elapsed));
        if (ownsLight)
        {
            burstLight.intensity = lightIntensity * flashAlpha;
            if (flashAlpha <= 0f) ReleaseLight();
        }
        if (flash.GetParticles(buffer) > 0)
        {
            Color color = flashColor; color.a *= flashAlpha;
            buffer[0].startColor = color;
            buffer[0].startSize = flashSize * Mathf.Lerp(.25f, 1.2f, Mathf.Clamp01(elapsed / .045f));
            flash.SetParticles(buffer, 1);
        }
        if (shockwave.GetParticles(buffer) > 0)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.1f, shockwaveSeconds));
            buffer[0].startSize = Mathf.Lerp(1f, shockwaveSize, 1f - (1f - t) * (1f - t));
            Color color = flashColor; color.a *= (1f - t) * (1f - t);
            buffer[0].startColor = color;
            shockwave.SetParticles(buffer, 1);
        }

        float dt = Mathf.Min(Time.deltaTime, .05f);
        if (dt <= 0f) return;
        Vector3 centre = body != null ? body.EffectCenter : previousBody;
        Vector3 travel = centre - previousBody;
        Vector3 bodyVelocity = Vector3.ClampMagnitude(travel / Mathf.Max(Time.deltaTime, .0001f), 30f);
        Vector3 axis = bodyVelocity.sqrMagnitude > .01f ? bodyVelocity.normalized : Vector3.down;
        int count = smoke.GetParticles(buffer);
        float drag = Mathf.Exp(-2.8f * dt);
        for (int i = 0; i < count; i++)
        {
            Vector3 velocity = buffer[i].velocity * drag;
            if (body != null && bodyVelocity.sqrMagnitude > .01f)
            {
                // Sweep the body's travel segment so a fast fall cannot skip the cloud.
                Vector3 near = ClosestOnSegment(buffer[i].position, previousBody, centre);
                Vector3 offset = buffer[i].position - near;
                float influence = Mathf.Clamp01(1f - offset.magnitude / Mathf.Max(.1f, wakeRadius));
                if (influence > 0f)
                {
                    Vector3 radial = Vector3.ProjectOnPlane(offset, axis);
                    if (radial.sqrMagnitude < .001f)
                        radial = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right);
                    radial.Normalize();
                    Vector3 wake = radial * sidewaysPush + bodyVelocity * downwardDrag +
                        Vector3.Cross(axis, radial) * curlStrength;
                    velocity = Vector3.Lerp(velocity, wake, 1f - Mathf.Exp(-8f * influence * dt));
                }
            }
            buffer[i].velocity = velocity + Vector3.up * (.5f * dt);
        }
        smoke.SetParticles(buffer, count);
        previousBody = centre;
    }

    internal static Vector3 ClosestOnSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float lengthSquared = delta.sqrMagnitude;
        return lengthSquared > .000001f
            ? from + delta * Mathf.Clamp01(Vector3.Dot(point - from, delta) / lengthSquared) : from;
    }

    private void ReleaseLight()
    {
        if (burstLight != null) burstLight.enabled = false;
        if (ownsLight) { activeLights = Mathf.Max(0, activeLights - 1); ownsLight = false; }
    }

    private void OnDisable()
    {
        ReleaseLight();
        if (running) activeBursts = Mathf.Max(0, activeBursts - 1);
        running = false;
        body = null;
        if (smoke != null) smoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (flash != null) flash.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (shockwave != null) shockwave.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (fire != null) fire.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
