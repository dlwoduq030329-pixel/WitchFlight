using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Reuses the old bright streak + orange afterglow style. Only the emitters follow
// the caster; particles remain in world space. Retained under the pooled field.
public sealed class FlareFieldVisual : MonoBehaviour
{
    private readonly List<ParticleSystem> particles = new();
    private Material coreMaterial, glowMaterial;
    private BattleAudio.Loop audioLoop;
    private bool emitting;

    public static FlareFieldVisual Create(Transform owner, MagicStatEntry stats)
    {
        var root = new GameObject("Three-way rear flare");
        root.SetActive(false);
        root.transform.SetParent(owner, false);
        var view = root.AddComponent<FlareFieldVisual>();
        float scale = stats.flareVfxScale > 0f ? stats.flareVfxScale : 1.8f;
        float extra = Mathf.Clamp(stats.flareParticleExtraLifetime, 0f, 5f);
        float spread = Mathf.Clamp(stats.flareSpreadAngle, 0f, 80f);
        float speed = Mathf.Max(0f, stats.flareVfxSpeed);
        for (int i = 0; i < 3; i++)
        {
            var branch = new GameObject("Rear flare " + (i - 1) * spread);
            branch.transform.SetParent(root.transform, false);
            branch.transform.localRotation = Quaternion.Euler(0f, (i - 1) * spread, 0f);
            var custom = CombatPresentation.InstantiateVfx(stats.utilityVfxPrefab,
                branch.transform.position, branch.transform.rotation, branch.transform);
            if (custom != null)
            {
                custom.transform.localScale *= scale;
                foreach (var ps in custom.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main;
                    main.startLifetime = AddLifetime(main.startLifetime, extra);
                    view.Configure(ps);
                }
                continue;
            }
            // Same shaders, colors, sizes and stretched cores as the former flare.
            var core = view.CreateParticles(branch.transform, "Bright flare streaks", true, stats);
            var coreMain = core.main;
            coreMain.startLifetime = new ParticleSystem.MinMaxCurve(.55f + extra, 1.1f + extra);
            coreMain.startSpeed = new ParticleSystem.MinMaxCurve(speed * .65f, speed);
            coreMain.startSize = new ParticleSystem.MinMaxCurve(.22f * scale, .42f * scale);
            coreMain.startColor = new Color(1f, .48f, .09f, 1f); coreMain.maxParticles = 128;
            var renderer = core.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f; renderer.velocityScale = .12f;
            view.Shape(core, .4f * scale, 22f, 12);
            var glow = view.CreateParticles(branch.transform, "Flare orange afterglow", false, stats);
            var glowMain = glow.main;
            glowMain.startLifetime = new ParticleSystem.MinMaxCurve(.35f + extra, .7f + extra);
            glowMain.startSpeed = new ParticleSystem.MinMaxCurve(speed * .3f, speed * .6f);
            glowMain.startSize = new ParticleSystem.MinMaxCurve(.65f * scale, 1.1f * scale);
            glowMain.startColor = new Color(1f, .2f, .025f, .7f); glowMain.maxParticles = 48;
            view.Shape(glow, .35f * scale, 8f, 5);
        }
        return view;
    }

    // Clone curves so authoring assets remain untouched; supports custom prefab curves too.
    public static ParticleSystem.MinMaxCurve AddLifetime(ParticleSystem.MinMaxCurve value, float extra)
    {
        AnimationCurve Shift(AnimationCurve source)
        {
            if (source == null) return AnimationCurve.Constant(0f, 1f, extra);
            var keys = source.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value = keys[i].value * value.curveMultiplier + extra;
                keys[i].inTangent *= value.curveMultiplier; keys[i].outTangent *= value.curveMultiplier;
            }
            return new AnimationCurve(keys) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }
        if (value.mode == ParticleSystemCurveMode.Constant) return new ParticleSystem.MinMaxCurve(value.constant + extra);
        if (value.mode == ParticleSystemCurveMode.TwoConstants) return new ParticleSystem.MinMaxCurve(value.constantMin + extra, value.constantMax + extra);
        if (value.mode == ParticleSystemCurveMode.Curve) return new ParticleSystem.MinMaxCurve(1f, Shift(value.curve));
        return new ParticleSystem.MinMaxCurve(1f, Shift(value.curveMin), Shift(value.curveMax));
    }

    private void Configure(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.None;
        var collision = ps.collision; collision.enabled = false;
        var trigger = ps.trigger; trigger.enabled = false;
        particles.Add(ps);
    }
    private ParticleSystem CreateParticles(Transform parent, string label, bool bright, MagicStatEntry stats)
    {
        var obj = new GameObject(label); obj.transform.SetParent(parent, false);
        var ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Configure(ps);
        Material material = bright ? coreMaterial : glowMaterial;
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>(bright ? "FlareParticle" : "UtilityMagicParticle");
            material = shader != null ? new Material(shader) : CombatPresentation.CreateEffectMaterial(Color.white);
            if (bright)
            {
                coreMaterial = material;
                if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", stats.flareVfxBrightness > 0f ? stats.flareVfxBrightness : 2f);
            }
            else glowMaterial = material;
        }
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var colors = ps.colorOverLifetime; colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white,0f), new GradientColorKey(Color.white,1f) },
            new[] { new GradientAlphaKey(.1f,0f), new GradientAlphaKey(1f,.15f), new GradientAlphaKey(0f,1f) });
        colors.color = gradient;
        return ps;
    }
    private void Shape(ParticleSystem ps, float radius, float rate, short burst)
    {
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone;
        // Narrow cones keep the three +/-30 degree paths distinguishable.
        shape.angle = 7f; shape.radius = radius;
        var emission = ps.emission; emission.rateOverTime = rate;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });
    }
    public void SetEmitting(bool value)
    {
        if (emitting == value) return;
        emitting = value;
        if (value)
        {
            foreach (var ps in particles) if (ps != null) ps.Play(false);
            audioLoop = BattleAudio.StartMagicLoop(MagicType.Flare, MagicSound.EffectLoop, transform);
        }
        else
        {
            audioLoop.Stop();
            foreach (var ps in particles) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }
    private void OnDisable()
    {
        emitting = false; audioLoop.Stop();
        foreach (var ps in particles) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    private void OnDestroy()
    {
        audioLoop.Stop();
        if (coreMaterial != null) Destroy(coreMaterial);
        if (glowMaterial != null) Destroy(glowMaterial);
    }
}
