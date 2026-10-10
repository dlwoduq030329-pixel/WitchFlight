using UnityEngine;
using UnityEngine.Rendering;

// Local-only generated fallbacks. Cloud lifetime is owned by BattleFlag's replicated timer.
// No colliders, NetworkObjects, target selection, or damage is created here.
public sealed class UtilityMagicVisual : MonoBehaviour
{
    private Material ownedMaterial;
    private Player flareOwner;
    private ParticleSystem[] flareParticles;
    private float flareBackOffset, flareEmissionUntil, flareCleanupAt;
    private bool isFlare, flareStopped;

    public static void PlayFlare(Player caster, Vector3 origin, Vector3 rear, MagicStatEntry stats)
    {
        rear = rear.sqrMagnitude > 0.001f ? rear.normalized : Vector3.back;
        Vector3 position = origin + rear * Mathf.Max(0f, stats.flareVfxBackOffset);
        var root = new GameObject("Flare (local VFX)");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(rear));
        var visual = root.AddComponent<UtilityMagicVisual>();
        visual.isFlare = true;
        visual.flareOwner = caster;
        visual.flareBackOffset = Mathf.Max(0f, stats.flareVfxBackOffset);
        GameObject custom = CombatPresentation.InstantiateVfx(stats.utilityVfxPrefab, position,
            root.transform.rotation, root.transform);
        float seconds = Mathf.Max(0.1f, stats.vfxLifetime);
        visual.flareEmissionUntil = Time.time + seconds;
        if (custom == null)
        {
            ParticleSystem particles = visual.CreateParticles(CombatPresentation.MagicColor(MagicType.Flare));
            var main = particles.main;
            main.loop = true;
            main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(stats.flareVfxSpeed * 0.65f, stats.flareVfxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.maxParticles = 64;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.3f;
            var emission = particles.emission;
            emission.rateOverTime = 40f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3f;
            renderer.velocityScale = 0.08f;
        }
        visual.flareParticles = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particles in visual.flareParticles)
        {
            if (!particles.gameObject.activeInHierarchy) continue;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
            // Only the emitter follows the caster; previously emitted flares stay behind.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            particles.Play(false);
        }
    }

    private void LateUpdate()
    {
        if (!isFlare) return; // Smoke is stationary and its lifetime belongs to BattleFlag.
        if (flareStopped)
        {
            if (Time.time < flareCleanupAt && HasLiveFlareParticles()) return;
            Destroy(gameObject);
            return;
        }
        if (Time.time >= flareEmissionUntil || flareOwner == null || !flareOwner.isActiveAndEnabled ||
            flareOwner.Object == null || !flareOwner.Object.IsValid || !flareOwner.IsAlive)
        {
            StopFlareEmission();
            return;
        }
        Vector3 rear = -flareOwner.transform.forward;
        transform.SetPositionAndRotation(flareOwner.LockAimPoint + rear * flareBackOffset,
            Quaternion.LookRotation(rear));
    }

    private void StopFlareEmission()
    {
        flareStopped = true;
        flareOwner = null;
        // Allow existing particles to fade, with a hard bound for supplied looping sub-emitters.
        flareCleanupAt = Time.time + 8f;
        if (flareParticles == null) return;
        foreach (ParticleSystem particles in flareParticles)
            if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    private bool HasLiveFlareParticles()
    {
        if (flareParticles != null)
            foreach (ParticleSystem particles in flareParticles)
                if (particles != null && particles.IsAlive(false)) return true;
        return false;
    }

    public static GameObject CreateSmoke(Vector3 center, float radius, GameObject prefab)
    {
        var root = new GameObject("Smoke cloud (local VFX)");
        root.transform.position = center;
        var visual = root.AddComponent<UtilityMagicVisual>();
        GameObject custom = CombatPresentation.InstantiateVfx(prefab, center, Quaternion.identity, root.transform);
        if (custom != null)
        {
            custom.transform.localScale *= radius; // Author supplied clouds with a one-unit radius.
            return root;
        }
        Color smokeColor = CombatPresentation.MagicColor(MagicType.Smoke);
        smokeColor.a = 0.65f;
        ParticleSystem particles = visual.CreateParticles(smokeColor);
        var main = particles.main;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSpeed = radius * 0.04f;
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 0.9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 64;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.65f;
        var emission = particles.emission;
        emission.rateOverTime = 24f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });
        // Hold opacity through most of a puff's life so the cloud does not look thin.
        // Keep the same 64-particle cap and leave flare/custom-prefab VFX untouched.
        var colorOverLife = particles.colorOverLifetime;
        var smokeGradient = new Gradient();
        smokeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = smokeGradient;
        particles.Play();
        return root;
    }

    private ParticleSystem CreateParticles(Color color)
    {
        var particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = color;
        var collision = particles.collision;
        collision.enabled = false;
        var colorOverLife = particles.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.1f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;
        Shader shader = Resources.Load<Shader>("UtilityMagicParticle");
        ownedMaterial = shader != null ? new Material(shader) : CombatPresentation.CreateEffectMaterial(Color.white);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = ownedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    private void OnDestroy()
    {
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }
}
