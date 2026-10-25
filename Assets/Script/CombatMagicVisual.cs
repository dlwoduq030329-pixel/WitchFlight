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
    private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    private readonly List<ShieldDissolveTarget> shieldDissolveTargets = new List<ShieldDissolveTarget>();

    private struct ShieldDissolveTarget
    {
        public Renderer renderer;
        public int materialIndex;
        public MaterialPropertyBlock properties;
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

    public static void Shield(Player player, GameObject prefab, float seconds, Vector3 facing, float forwardOffset = 0f)
    {
        if (prefab == null || player == null || player.Object == null || !player.Object.IsValid || !player.IsAlive ||
            player.IsHiddenBySmokeFor(Player.LocalPlayer)) return;
        var root = new GameObject("Directional parry VFX");
        var effect = root.AddComponent<CombatMagicVisual>();
        effect.follow = player;
        effect.duration = Mathf.Max(0.05f, seconds);
        effect.expires = Time.unscaledTime + effect.duration;
        effect.shieldFacing = facing.sqrMagnitude > .0001f ? facing.normalized : player.transform.forward;
        effect.shieldOffset = Mathf.Max(0f, forwardOffset);
        Vector3 up = Mathf.Abs(Vector3.Dot(effect.shieldFacing, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(effect.shieldFacing, up);
        Vector3 position = player.LockAimPoint + effect.shieldFacing * effect.shieldOffset;
        root.transform.SetPositionAndRotation(position, rotation);
        GameObject view = CombatPresentation.InstantiateVfx(prefab, position, rotation, root.transform);
        if (view == null)
        {
            Destroy(root);
        }
        else
        {
            effect.InitializeShieldDissolve(view);
        }
    }

    private void InitializeShieldDissolve(GameObject view)
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
                shieldDissolveTargets.Add(new ShieldDissolveTarget
                {
                    renderer = renderer,
                    materialIndex = i,
                    properties = new MaterialPropertyBlock()
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
            target.renderer.SetPropertyBlock(target.properties, target.materialIndex);
        }
    }

    private void LateUpdate()
    {
        if (expires <= 0f) return;
        if (follow == null || follow.Object == null || !follow.Object.IsValid || !follow.IsAlive ||
            follow.IsHiddenBySmokeFor(Player.LocalPlayer) || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive)
        { Destroy(gameObject); return; }
        UpdateShieldDissolve(Mathf.Clamp01(1f - (expires - Time.unscaledTime) / duration));
        if (Time.unscaledTime >= expires) { Destroy(gameObject); return; }
        // Follow translation, but retain the world direction of the blocked attack during this flash.
        transform.position = follow.LockAimPoint + shieldFacing * shieldOffset;
        if (ownedMaterial != null)
        {
            Color color = ownedMaterial.color;
            color.a = 0.25f * Mathf.Clamp01((expires - Time.unscaledTime) / duration);
            ownedMaterial.color = color;
        }
    }

    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
