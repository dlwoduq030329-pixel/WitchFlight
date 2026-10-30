using UnityEngine;
using UnityEngine.Rendering;

// Local-only generated fallbacks. Cloud lifetime is owned by BattleFlag's replicated timer.
// No colliders, NetworkObjects, target selection, or damage is created here.
public sealed class UtilityMagicVisual : MonoBehaviour
{
    public static readonly System.Collections.Generic.HashSet<UtilityMagicVisual> SmokeViews = new();
    private Material ownedMaterial;
    private Material ownedFlareMaterial;
    private Player flareOwner;
    private ParticleSystem[] flareParticles;
    private float flareBackOffset, flareEmissionUntil, flareCleanupAt;
    private bool isFlare, flareStopped;
    private BattleAudio.Loop effectAudio;

    public static void PlayFlare(Player caster, Vector3 origin, Vector3 rear, MagicStatEntry stats)
    {
        rear = rear.sqrMagnitude > 0.001f ? rear.normalized : Vector3.back;
        Vector3 position = origin + rear * Mathf.Max(0f, stats.flareVfxBackOffset);
        var root = new GameObject("Flare (local VFX)");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(rear));
        var visual = root.AddComponent<UtilityMagicVisual>();
        visual.isFlare = true;
        visual.effectAudio = BattleAudio.StartMagicLoop(MagicType.Flare, MagicSound.EffectLoop, root.transform);
        visual.flareOwner = caster;
        visual.flareBackOffset = Mathf.Max(0f, stats.flareVfxBackOffset);
        GameObject custom = CombatPresentation.InstantiateVfx(stats.utilityVfxPrefab, position,
            root.transform.rotation, root.transform);
        float seconds = Mathf.Max(0.1f, stats.vfxLifetime);
        float scale = stats.flareVfxScale > 0f ? stats.flareVfxScale : 1.8f;
        visual.flareEmissionUntil = Time.time + seconds;
        if (custom != null) custom.transform.localScale *= scale;
        if (custom == null)
        {
            ParticleSystem particles = visual.CreateParticles(new Color(1f,.48f,.09f,1f), null, true);
            float brightness = stats.flareVfxBrightness > 0f ? stats.flareVfxBrightness : 2f;
            if (visual.ownedFlareMaterial != null && visual.ownedFlareMaterial.HasProperty("_Intensity"))
                visual.ownedFlareMaterial.SetFloat("_Intensity", brightness);
            var main = particles.main;
            main.loop = true;
            main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(stats.flareVfxSpeed * 0.65f, stats.flareVfxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f*scale, 0.42f*scale);
            main.maxParticles = 160;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 42f;
            shape.radius = 0.4f*scale;
            var emission = particles.emission;
            emission.rateOverTime = 64f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f;
            renderer.velocityScale = 0.12f;
            // Broad saturated outer glow stays readable even against a bright sky.
            // It uses alpha blending; the narrow bright core above uses additive blending.
            var glowObject = new GameObject("Flare orange afterglow");
            glowObject.transform.SetParent(root.transform,false);
            ParticleSystem glow = visual.CreateParticles(new Color(1f,.2f,.025f,.7f),glowObject);
            var glowMain = glow.main;
            glowMain.loop = true; glowMain.duration = seconds;
            glowMain.startLifetime = new ParticleSystem.MinMaxCurve(.35f,.7f);
            glowMain.startSpeed = new ParticleSystem.MinMaxCurve(stats.flareVfxSpeed*.3f,stats.flareVfxSpeed*.6f);
            glowMain.startSize = new ParticleSystem.MinMaxCurve(.65f*scale,1.1f*scale);
            glowMain.maxParticles = 48;
            var glowShape = glow.shape;
            glowShape.shapeType = ParticleSystemShapeType.Cone; glowShape.angle = 38f; glowShape.radius = .35f*scale;
            var glowEmission = glow.emission;
            glowEmission.rateOverTime = 22f;
            glowEmission.SetBursts(new[] { new ParticleSystem.Burst(0f,14) });
            CombatTransientEffect.PlayPulse(position,new Color(1f,.6f,.12f,.55f),1.5f*scale,.2f, caster);
        }
        visual.flareParticles = root.GetComponentsInChildren<ParticleSystem>(true);
        RearThreatCamera.ExcludeLocalEffect(root, caster);
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
            MagicVfxPool.Release(gameObject);
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
        effectAudio.Stop();
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
        SmokeViews.Add(visual);
        SmokeInteriorView.Ensure();
        visual.effectAudio = BattleAudio.StartMagicLoop(MagicType.Smoke, MagicSound.EffectLoop, root.transform);
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

    private ParticleSystem CreateParticles(Color color, GameObject emitter = null, bool brightFlare = false)
    {
        var particles = (emitter != null ? emitter : gameObject).AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = color;
        var collision = particles.collision;
        collision.enabled = false;
        var trigger = particles.trigger;
        trigger.enabled = false;
        var colorOverLife = particles.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.1f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;
        Material material = brightFlare ? ownedFlareMaterial : ownedMaterial;
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>(brightFlare ? "FlareParticle" : "UtilityMagicParticle");
            material = shader != null ? new Material(shader) : CombatPresentation.CreateEffectMaterial(Color.white);
            if (brightFlare) ownedFlareMaterial = material; else ownedMaterial = material;
        }
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    private void OnDestroy()
    {
        SmokeViews.Remove(this);
        effectAudio.Stop();
        if (ownedMaterial != null) Destroy(ownedMaterial);
        if (ownedFlareMaterial != null) Destroy(ownedFlareMaterial);
    }
}
