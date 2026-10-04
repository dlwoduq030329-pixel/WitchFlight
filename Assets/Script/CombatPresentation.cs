using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Local presentation only. Damage, stealth and projectile hits remain host-authoritative.
[DisallowMultipleComponent]
public sealed class CombatPresentation : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float deathFadeSeconds = 2f;
    [SerializeField, Min(0.01f)] private float hitFlashSeconds = 0.18f;

    private sealed class Visual
    {
        public Renderer renderer;
        public bool authoredEnabled;
        public Material[] originals;
        public Material[] fading;
        public Color[] colors;
        public MaterialPropertyBlock[] originalBlocks;
    }

    private readonly List<Visual> visuals = new();
    private MaterialPropertyBlock block;
    private Player owner;
    private bool initialized;
    private int lastHit;
    private int lastDeath;
    private int lastParry;
    private bool wasParrying;
    private float hitFlashUntil;
    private bool wasFlashing;
    private float deathStarted = -1f;

    public static bool MenuOpen => BattleHud.MenuOpen;

    private void Awake()
    {
        // Unity native objects cannot be created from MonoBehaviour field initializers.
        block = new MaterialPropertyBlock();
        owner = GetComponent<Player>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;
            Material[] materials = renderer.sharedMaterials;
            var visual = new Visual
            {
                renderer = renderer,
                authoredEnabled = renderer.enabled,
                originals = materials,
                colors = new Color[materials.Length],
                originalBlocks = new MaterialPropertyBlock[materials.Length]
            };
            for (int i = 0; i < materials.Length; i++)
            {
                visual.colors[i] = ReadColor(materials[i]);
                visual.originalBlocks[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(visual.originalBlocks[i], i);
                if (visual.originalBlocks[i].HasColor("_BaseColor"))
                    visual.colors[i] = visual.originalBlocks[i].GetColor("_BaseColor");
                else if (visual.originalBlocks[i].HasColor("_Color"))
                    visual.colors[i] = visual.originalBlocks[i].GetColor("_Color");
            }
            visuals.Add(visual);
        }
    }

    // Called after PlayerAppearance writes the per-material property block.
    public void SetAppearanceColor(Renderer renderer, int materialIndex, Color color)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.colors[materialIndex] = color;
            renderer.GetPropertyBlock(visual.originalBlocks[materialIndex], materialIndex);
            return;
        }
    }

    // Layer tint (e.g. iris _Color2nd) is not the base color used for hit flashes.
    // Modify only this baseline property, never capture an in-progress flash as the base.
    public void SetAppearanceLayerColor(Renderer renderer, int materialIndex, string property, Color color)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.originalBlocks[materialIndex].SetColor(property, color);
            return;
        }
    }

    // Preserve the selected eye texture when hit/death presentation restores the property block.
    public void SetAppearanceLayerTexture(Renderer renderer, int materialIndex, string property, Texture texture)
    {
        foreach (Visual visual in visuals)
        {
            if (visual.renderer != renderer || materialIndex < 0 || materialIndex >= visual.colors.Length)
                continue;
            visual.originalBlocks[materialIndex].SetTexture(property, texture);
            return;
        }
    }

    private void LateUpdate()
    {
        if (owner == null || owner.Object == null || !owner.Object.IsValid)
            return;

        if (!initialized)
        {
            lastHit = owner.HitSequence;
            lastDeath = owner.DeathSequence;
            lastParry = owner.ParrySequence;
            initialized = true;
            if (!owner.IsAlive && owner.DeathSequence > 0)
                StartDeathFade();
        }

        if (lastHit != owner.HitSequence)
        {
            lastHit = owner.HitSequence;
            hitFlashUntil = Time.unscaledTime + hitFlashSeconds;
            BattleHud.ShowDamage(owner.LockAimPoint, owner.LastReceivedDamage);
            ShowImpact(owner.LockAimPoint, MagicType.Fire);
        }
        if (lastDeath != owner.DeathSequence)
        {
            lastDeath = owner.DeathSequence;
            StartDeathFade();
        }
        if ((!wasParrying && owner.IsParrying) || lastParry != owner.ParrySequence)
        {
            lastParry = owner.ParrySequence;
            SpawnPulse(owner.LockAimPoint, new Color(0.25f, 0.9f, 1f, 0.65f), 2.5f, 0.3f);
        }
        wasParrying = owner.IsParrying;
        UpdateMaterials();
    }

    private void StartDeathFade()
    {
        deathStarted = Time.unscaledTime;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null || visual.fading != null)
                continue;
            visual.fading = new Material[visual.originals.Length];
            for (int i = 0; i < visual.originals.Length; i++)
                visual.fading[i] = CreateFadeMaterial(visual.originals[i]);
            visual.renderer.sharedMaterials = visual.fading;
        }
    }

    private void UpdateMaterials()
    {
        bool dying = deathStarted >= 0f;
        float opacity = dying ? 1f - Mathf.Clamp01((Time.unscaledTime - deathStarted) / deathFadeSeconds) : 1f;
        float flash = Mathf.Clamp01((hitFlashUntil - Time.unscaledTime) / hitFlashSeconds);
        if (!dying && flash <= 0f && !wasFlashing)
            return;
        wasFlashing = flash > 0f;
        foreach (Visual visual in visuals)
        {
            if (visual.renderer == null)
                continue;
            if (dying && opacity <= 0f)
                visual.renderer.forceRenderingOff = true;
            for (int i = 0; i < visual.colors.Length; i++)
            {
                if (!dying && flash <= 0f)
                {
                    visual.renderer.SetPropertyBlock(visual.originalBlocks[i], i);
                    continue;
                }
                Color color = Color.Lerp(visual.colors[i], Color.red, flash * 0.9f);
                color.a *= opacity;
                visual.renderer.GetPropertyBlock(block, i);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                visual.renderer.SetPropertyBlock(block, i);
            }
        }
    }

    public void ShowCast(MagicType magic, Vector3 origin, Vector3 end)
    {
        Color color = MagicColor(magic);
        SpawnPulse(origin, color, 0.7f, 0.18f);
        if (magic != MagicType.Vision)
            return;

        GameObject beam = new GameObject("Vision beam");
        var line = beam.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        line.startWidth = 0.12f;
        line.endWidth = 0.04f;
        Material material = CreateEffectMaterial(color);
        line.sharedMaterial = material;
        var lifetime = beam.AddComponent<CombatTransientEffect>();
        lifetime.Initialize(0.16f, Vector3.one, Vector3.one, new[] { material });
    }

    public static void ShowImpact(Vector3 position, MagicType magic)
    {
        SpawnPulse(position, MagicColor(magic), 1.25f, 0.25f);
    }

    public void ShowScan(float radius)
    {
        SpawnPulse(transform.position, new Color(0.15f, 0.9f, 1f, 0.18f), Mathf.Max(1f, radius) * 2f, 0.65f);
    }

    public void ShowDecoy(float duration)
    {
        GameObject decoy = new GameObject("Magic decoy visual");
        decoy.transform.SetPositionAndRotation(transform.position, transform.rotation);
        var materials = new List<Material>();
        var ownedMeshes = new List<Mesh>();
        foreach (Visual visual in visuals)
        {
            Renderer source = visual.renderer;
            // A remote peer may receive this RPC after stealth disabled its renderers.
            // Clone the authored active outfit, not the temporary visibility state.
            if (source == null || !visual.authoredEnabled || !source.gameObject.activeInHierarchy)
                continue;
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh { name = "Decoy posed mesh" };
                skinned.BakeMesh(mesh);
                ownedMeshes.Add(mesh);
            }
            else if (source.TryGetComponent(out MeshFilter sourceFilter))
            {
                mesh = sourceFilter.sharedMesh;
            }
            if (mesh == null)
                continue;

            GameObject part = new GameObject(source.name);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;
            part.transform.SetParent(decoy.transform, true);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            var clones = new Material[visual.originals.Length];
            for (int i = 0; i < clones.Length; i++)
            {
                clones[i] = CreateFadeMaterial(visual.originals[i]);
                materials.Add(clones[i]);
            }
            renderer.sharedMaterials = clones;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        decoy.AddComponent<CombatTransientEffect>().Initialize(
            Mathf.Max(0.1f, duration), Vector3.one, Vector3.one, materials.ToArray(), ownedMeshes.ToArray(), 0.2f);
    }

    private static void SpawnPulse(Vector3 position, Color color, float diameter, float duration)
    {
        GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pulse.name = "Combat pulse";
        pulse.layer = 2;
        Collider collider = pulse.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        pulse.transform.position = position;
        var renderer = pulse.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        Material material = CreateEffectMaterial(color);
        renderer.sharedMaterial = material;
        pulse.AddComponent<CombatTransientEffect>().Initialize(
            duration, Vector3.one * 0.08f, Vector3.one * diameter, new[] { material });
    }

    public static Material CreateEffectMaterial(Color color)
    {
        Shader shader = Resources.Load<Shader>("CombatFade");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        return new Material(shader) { color = color };
    }

    private static Material CreateFadeMaterial(Material source)
    {
        Material result = CreateEffectMaterial(ReadColor(source));
        if (source == null)
            return result;
        string textureName = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        if (source.HasProperty(textureName))
        {
            result.mainTexture = source.GetTexture(textureName);
            result.mainTextureScale = source.GetTextureScale(textureName);
            result.mainTextureOffset = source.GetTextureOffset(textureName);
        }
        return result;
    }

    private static Color ReadColor(Material material)
    {
        if (material == null)
            return Color.white;
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    public static Color MagicColor(MagicType magic)
    {
        return magic switch
        {
            MagicType.Fire => new Color(1f, 0.3f, 0.08f, 0.85f),
            MagicType.Ice => new Color(0.2f, 0.75f, 1f, 0.8f),
            MagicType.Vision => new Color(0.95f, 0.3f, 1f, 0.85f),
            MagicType.Thunder => new Color(1f, 0.9f, 0.25f, 0.85f),
            _ => new Color(0.4f, 1f, 0.8f, 0.6f)
        };
    }

    private void OnDestroy()
    {
        foreach (Visual visual in visuals)
        {
            if (visual.fading == null)
                continue;
            foreach (Material material in visual.fading)
                if (material != null)
                    Destroy(material);
        }
    }
}
