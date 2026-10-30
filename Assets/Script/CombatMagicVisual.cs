using System.Collections.Generic;
using UnityEngine;

// Pure local VFX. Persistent beams are reused throughout a channel, not spawned every tick.
public sealed class CombatMagicVisual : MonoBehaviour
{
    private LineRenderer line;
    private Transform stretched;
    private Vector3 authoredScale;
    private Material ownedMaterial;
    private Player follow;
    private float expires;
    private float duration;
    private Vector3 shieldFacing;
    private float shieldOffset;
    private bool heldShield, shieldEnding;
    private bool spawnShield;
    private float spawnOrbitPhase;
    private float spawnOpacity = 1f;
    private LineRenderer shieldRim;
    private LineRenderer shieldImpactRing;
    private Color shieldRimColor;
    private float shieldRimWidth, shieldRadius, shieldPulseUntil;
    private int shieldStartSequence;
    private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    private readonly List<ShieldDissolveTarget> shieldDissolveTargets = new List<ShieldDissolveTarget>();

    private struct ShieldDissolveTarget
    {
        public Renderer renderer;
        public int materialIndex;
        public MaterialPropertyBlock properties;
        public Color tint;
        public bool hasTint;
    }

    public static CombatMagicVisual Beam(MagicType magic, GameObject prefab, Material material = null)
    {
        var root = new GameObject(magic + " beam VFX");
        var effect = root.AddComponent<CombatMagicVisual>();
        GameObject view = CombatPresentation.InstantiateVfx(prefab, Vector3.zero, Quaternion.identity, root.transform);
        if (view != null)
        {
            effect.line = view.GetComponentInChildren<LineRenderer>(true);
            effect.stretched = view.transform;
            effect.authoredScale = view.transform.localScale;
        }
        else
        {
            effect.line = root.AddComponent<LineRenderer>();
            if (material == null)
            {
                effect.ownedMaterial = CombatPresentation.CreateEffectMaterial(CombatPresentation.MagicColor(magic));
                effect.line.sharedMaterial = effect.ownedMaterial;
            }
            effect.line.startWidth = 0.13f;
            effect.line.endWidth = 0.08f;
            effect.line.numCapVertices = 4;
        }
        if (effect.line != null)
        {
            // Authored assets are only referenced here, never recolored or destroyed.
            if (material != null) effect.line.sharedMaterial = material;
            effect.line.useWorldSpace = true;
            effect.line.positionCount = 2;
        }
        return effect;
    }

    public void SetEndpoints(Vector3 origin, Vector3 end)
    {
        if (line != null) { line.SetPosition(0, origin); line.SetPosition(1, end); }
        else if (stretched != null)
        {
            Vector3 delta = end - origin;
            stretched.SetPositionAndRotation(origin, delta.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(delta) : Quaternion.identity);
            stretched.localScale = new Vector3(authoredScale.x, authoredScale.y, authoredScale.z * delta.magnitude);
        }
    }

    public static CombatMagicVisual Shield(Player player, GameObject prefab, float seconds, Vector3 facing, float forwardOffset = 0f, float opacity = .75f,
        bool holdForParry = false, float rimRadius = 2.2f, float rimWidth = .035f, Color? rimColor = null)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || !player.IsAlive ||
            player.IsHiddenBySmokeFor(Player.LocalPlayer)) return null;
        var root = new GameObject("Directional parry VFX");
        var effect = root.AddComponent<CombatMagicVisual>();
        effect.follow = player;
        effect.duration = Mathf.Max(0.05f, seconds);
        effect.expires = Time.unscaledTime + effect.duration;
        effect.heldShield = holdForParry;
        effect.shieldStartSequence = player.ParryStartSequence;
        effect.shieldFacing = facing.sqrMagnitude > .0001f ? facing.normalized : player.transform.forward;
        effect.shieldOffset = Mathf.Max(0f, forwardOffset);
        Vector3 up = Mathf.Abs(Vector3.Dot(effect.shieldFacing, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(effect.shieldFacing, up);
        Vector3 position = player.LockAimPoint + effect.shieldFacing * effect.shieldOffset;
        root.transform.SetPositionAndRotation(position, rotation);
        GameObject view = CombatPresentation.InstantiateVfx(prefab, position, rotation, root.transform);
        if (view != null)
        {
            // Hold the authored shield size for the accepted block window. Pool startup
            // samples the animation's zero-scale first frame before this call.
            if (holdForParry) HoldShieldPose(view);
            effect.InitializeShieldDissolve(view, opacity);
        }
        if (holdForParry || view == null)
            effect.CreateShieldRim(rimRadius, rimWidth, rimColor ?? new Color(.35f, .95f, 1f, .9f), view == null);
        RearThreatCamera.ExcludeLocalEffect(root, player);
        return effect;
    }

    internal static void HoldShieldPose(GameObject view)
    {
        var lease = view.GetComponent<MagicVfxLease>();
        foreach (Animator animator in view.GetComponentsInChildren<Animator>(true))
        {
            if (animator.gameObject.name != "Shield_03") continue;
            animator.enabled = false;
            if (lease != null) lease.RestoreAuthoredScale(animator.transform);
        }
    }

    public static CombatMagicVisual SpawnShield(Player player, GameObject prefab, float phase, float opacity, float rimRadius)
    {
        if (player == null || player.Runner == null) return null;
        var table = FlightCombatTable.Default;
        float orbitRadius = table != null ? table.spawnProtectionOrbitRadius : .8f;
        float angle = (float)player.Runner.SimulationTime * 180f + phase;
        Vector3 facing = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
        float opacityMultiplier = table != null ? table.spawnShieldOpacity : .5f;
        var effect = Shield(player, prefab, .15f, facing, orbitRadius, opacity * opacityMultiplier, true, rimRadius);
        if (effect == null) return null;
        effect.name = "Spawn protection shield";
        effect.spawnShield = true;
        effect.spawnOrbitPhase = phase;
        effect.spawnOpacity = opacityMultiplier;
        effect.transform.localScale = Vector3.one * (table != null ? table.spawnShieldScale : .5f);
        return effect;
    }

    private void CreateShieldRim(float radius, float width, Color color, bool hemisphere)
    {
        shieldRadius = Mathf.Max(.1f, radius);
        shieldRimWidth = Mathf.Max(.005f, width);
        shieldRimColor = color;
        ownedMaterial = CombatPresentation.CreateEffectMaterial(Color.white);
        shieldRim = CreateShieldLine("Active shield rim", 65);
        for (int i = 0; i <= 64; i++)
        {
            float angle = i * Mathf.PI * 2f / 64f;
            shieldRim.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * shieldRadius);
        }
        if (!hemisphere) return;
        // A light wire hemisphere when no authored prefab is assigned.
        for (int axis = 0; axis < 2; axis++)
        {
            LineRenderer arc = CreateShieldLine("Shield hemisphere arc", 33);
            for (int i = 0; i <= 32; i++)
            {
                float angle = i * Mathf.PI / 32f;
                float side = Mathf.Cos(angle) * shieldRadius;
                arc.SetPosition(i, new Vector3(axis == 0 ? side : 0f, axis == 1 ? side : 0f, Mathf.Sin(angle) * shieldRadius));
            }
        }
    }

    private LineRenderer CreateShieldLine(string title, int count)
    {
        var child = new GameObject(title);
        child.transform.SetParent(transform, false);
        var rim = child.AddComponent<LineRenderer>();
        rim.sharedMaterial = ownedMaterial;
        rim.useWorldSpace = false;
        rim.positionCount = count;
        rim.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        rim.widthMultiplier = shieldRimWidth;
        rim.startColor = rim.endColor = shieldRimColor;
        rim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rim.receiveShadows = false;
        return rim;
    }

    public void ParrySuccess(Vector3 facing)
    {
        if (facing.sqrMagnitude > .0001f) shieldFacing = facing.normalized;
        shieldPulseUntil = Time.unscaledTime + .12f;
        if (ownedMaterial != null && shieldImpactRing == null)
        {
            shieldImpactRing = CreateShieldLine("Shield impact ripple", 49);
            for (int i = 0; i <= 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                shieldImpactRing.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
            }
            // New renderer must also be hidden from its owner's rear cameras.
            RearThreatCamera.ExcludeLocalEffect(shieldImpactRing.gameObject, follow);
        }
        if (follow != null)
            CombatTransientEffect.PlayPulse(follow.LockAimPoint + shieldFacing * (shieldOffset + shieldRadius),
                new Color(1f, .9f, .4f, .7f), .65f, .12f, follow);
    }

    public void RefreshParry()
    {
        if (follow == null || shieldStartSequence == follow.ParryStartSequence) return;
        shieldStartSequence = follow.ParryStartSequence;
        shieldFacing = follow.transform.forward;
        shieldPulseUntil = 0f;
    }

    public void EndParry()
    {
        if (!heldShield || shieldEnding) return;
        shieldEnding = true;
        expires = Time.unscaledTime + duration;
        // No lingering bright outline after the real block window has ended.
        if (shieldRim != null) shieldRim.enabled = false;
        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
            particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void InitializeShieldDissolve(GameObject view, float opacity)
    {
        foreach (Renderer renderer in view.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null ||
                    (material.name != "Shield_03" && material.name != "Shield_03 (Instance)") ||
                    !material.HasProperty(DissolveAmountId)) continue;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, i);
                if (properties.isEmpty) renderer.GetPropertyBlock(properties);
                if (material.HasProperty("_TintColor"))
                {
                    Color tint = material.GetColor("_TintColor");
                    tint.a *= Mathf.Clamp01(opacity);
                    properties.SetColor("_TintColor", tint);
                }
                renderer.SetPropertyBlock(properties, i);
                shieldDissolveTargets.Add(new ShieldDissolveTarget
                {
                    renderer = renderer,
                    materialIndex = i,
                    properties = properties,
                    hasTint = material.HasProperty("_TintColor"),
                    tint = material.HasProperty("_TintColor") ? properties.GetColor("_TintColor") : Color.white
                });
            }
        }
        UpdateShieldDissolve(0f);
    }

    private void UpdateShieldDissolve(float amount)
    {
        // Per-renderer, per-slot overrides keep simultaneous parries and the shared asset independent.
        foreach (ShieldDissolveTarget target in shieldDissolveTargets)
        {
            if (target.renderer == null) continue;
            target.renderer.GetPropertyBlock(target.properties, target.materialIndex);
            if (target.properties.isEmpty) target.renderer.GetPropertyBlock(target.properties);
            target.properties.SetFloat(DissolveAmountId, amount);
            if (spawnShield && target.hasTint)
            {
                Color tint = target.tint;
                tint.a *= follow != null ? follow.SpawnShieldVisibility : 0f;
                target.properties.SetColor("_TintColor", tint);
            }
            target.renderer.SetPropertyBlock(target.properties, target.materialIndex);
        }
    }

    private void LateUpdate()
    {
        if (expires <= 0f) return;
        if (follow == null || follow.Object == null || !follow.Object.IsValid || !follow.IsAlive ||
            follow.IsHiddenBySmokeFor(Player.LocalPlayer) || BattleManager.Instance == null ||
            !(BattleManager.Instance.IsGameplayActive || (spawnShield && follow.IsCountdownProtected)))
        { MagicVfxPool.Release(gameObject); return; }
        if (heldShield)
        {
            if (spawnShield)
            {
                if (!follow.IsSpawnProtected) EndParry();
                float angle = (float)follow.Runner.SimulationTime * 180f + spawnOrbitPhase;
                shieldFacing = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            }
            else if (!follow.IsParrying || follow.IsReturningToMap) EndParry();
            if (!shieldEnding)
            {
                expires = Time.unscaledTime + duration;
                if (shieldRim != null)
                {
                    bool pulse = Time.unscaledTime < shieldPulseUntil;
                    shieldRim.startColor = shieldRim.endColor = pulse ? new Color(1f, .9f, .4f, 1f) : shieldRimColor;
                    shieldRim.widthMultiplier = shieldRimWidth * (pulse ? 2f : 1f);
                }
            }
        }
        UpdateShieldDissolve(heldShield && !shieldEnding ? 0f : Mathf.Clamp01(1f - (expires - Time.unscaledTime) / duration));
        if (Time.unscaledTime >= expires) { MagicVfxPool.Release(gameObject); return; }
        // Follow translation, but retain the world direction of the blocked attack during this flash.
        transform.position = follow.LockAimPoint + shieldFacing * shieldOffset;
        Vector3 up = Mathf.Abs(Vector3.Dot(shieldFacing, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
        transform.rotation = Quaternion.LookRotation(shieldFacing, up);
        if (shieldImpactRing != null)
        {
            float remaining = Mathf.Clamp01((shieldPulseUntil - Time.unscaledTime) / .12f);
            shieldImpactRing.enabled = remaining > 0f;
            shieldImpactRing.transform.localPosition = Vector3.forward * shieldRadius;
            shieldImpactRing.transform.localScale = Vector3.one * Mathf.Lerp(.65f, .08f, remaining);
            Color rippleColor = new Color(1f, .9f, .4f, remaining);
            shieldImpactRing.startColor = shieldImpactRing.endColor = rippleColor;
        }
        if (ownedMaterial != null)
        {
            Color color = Color.white;
            color.a = heldShield && !shieldEnding ? 1f : Mathf.Clamp01((expires - Time.unscaledTime) / duration);
            if (spawnShield) color.a *= spawnOpacity * follow.SpawnShieldVisibility;
            ownedMaterial.color = color;
        }
    }

    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
