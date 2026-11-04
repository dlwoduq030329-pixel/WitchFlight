using UnityEngine;
using System.Collections.Generic;

public class PlayerAppearance : MonoBehaviour
{
    [Header("Front hair BlendShapes (auto-detected if unassigned)")]
    [SerializeField] private SkinnedMeshRenderer bangsRenderer;
    [SerializeField] private SkinnedMeshRenderer sideHairRenderer;
    [System.Serializable]
    private sealed class ColorTarget
    {
        public Renderer renderer;
        [Min(0)] public int materialIndex;
        [Tooltip("Hair/cloth color property (empty = main color). Eyes ignore this and use _Main2ndTex / _Color2nd.")]
        public string colorProperty;
    }

    [Header("Hair color (empty = detect hair renderers/materials under this model)")]
    [SerializeField] private ColorTarget[] hairColorTargets;
    [Header("Cloth color (empty = detect WiccaMaid clothing, excluding props/skin)")]
    [SerializeField] private ColorTarget[] clothColorTargets;
    [Header("Eye texture targets (empty = Body / body material / 2nd eye layer)")]
    [SerializeField] private ColorTarget[] eyeColorTargets;
    [Tooltip("Empty uses Resources/EyeTexturePresetTable, shared with the lobby buttons.")]
    [SerializeField] private EyeTexturePresetTable eyeTexturePresets;
    // Retained only for old Inspector calls to ApplyHairLength(int).
    [SerializeField, Min(0)] private int maxHairLength = 10;

    private Mesh cachedMesh;
    private int short01Index = -1, short02Index = -1, longIndex = -1;
    private bool followsDataConfig;
    private bool missingRendererWarningShown;
    private bool missingHairColorWarningShown;
    private bool missingClothColorWarningShown, missingEyeColorWarningShown;
    private bool missingDirectionWarningShown, missingSideHairWarningShown;
    private MaterialPropertyBlock hairColorBlock;
    private readonly List<ColorTarget> detectedHairColorTargets = new List<ColorTarget>();
    private readonly List<ColorTarget> detectedClothColorTargets = new List<ColorTarget>();
    private readonly List<ColorTarget> detectedEyeColorTargets = new List<ColorTarget>();

    private void OnEnable()
    {
        if (!followsDataConfig) return;
        DataConfig.Changed += ApplyFromDataConfig;
        ApplyFromDataConfig();
    }

    private void OnDisable() => DataConfig.Changed -= ApplyFromDataConfig;

    // Only the lobby preview should opt into this global/local source.
    public void BindToDataConfig()
    {
        followsDataConfig = true;
        DataConfig.Changed -= ApplyFromDataConfig;
        if (isActiveAndEnabled) DataConfig.Changed += ApplyFromDataConfig;
        ApplyFromDataConfig();
    }

    public void UnbindFromDataConfig()
    {
        followsDataConfig = false;
        DataConfig.Changed -= ApplyFromDataConfig;
    }

    private void ApplyFromDataConfig()
    {
        if (!followsDataConfig) return;
        ApplyConfigValues(DataConfig.GetPlayerConfig());
    }

    // Explicit per-character source. Never copy another player's values into DataConfig.
    public void ApplyFromPlayerData(PlayerData data)
    {
        UnbindFromDataConfig();
        if (data == null || data.Object == null || !data.Object.IsValid || !data.IsLoadoutInitialized)
            return;
        ApplyPlayerConfig(data.GetPlayerConfig());
    }

    // Player calls this with its replicated snapshot on BOTH host and clients.
    public void ApplyPlayerConfig(PlayerConfig config)
    {
        UnbindFromDataConfig();
        ApplyConfigValues(config.Sanitized());
    }

    private void ApplyConfigValues(PlayerConfig config)
    {
        ApplyBangsLength(config.bangsLength);
        ApplyBangsDirection(config.bangsDirection);
        ApplySideHairLength(config.sideHairLength);
        ApplyHairColor(config.hairColor);
        ApplyClothColor(config.clothColor);
        ApplyEyeColor(config.eyeColor);
    }

    // Per-renderer overrides keep other characters and shared material assets unchanged.
    public void ApplyHairColor(Color color)
    {
        ApplyColorTargets(ResolveHairColorTargets(), color, string.Empty, "Hair Color Targets", ref missingHairColorWarningShown);
    }

    public void ApplyClothColor(Color color)
    {
        IEnumerable<ColorTarget> targets = clothColorTargets != null && clothColorTargets.Length > 0
            ? clothColorTargets : DetectColorTargets(detectedClothColorTargets, IsClothingSlot, string.Empty);
        ApplyColorTargets(targets, color, string.Empty, "Cloth Color Targets", ref missingClothColorWarningShown);
    }

    public void ApplyEyeColor(Color color)
    {
        EyeTexturePresetTable table = eyeTexturePresets != null ? eyeTexturePresets : EyeTexturePresetTable.Default;
        if (table == null || !table.TryResolve(color, out EyeTexturePresetTable.Preset preset))
        {
            if (!missingEyeColorWarningShown)
                Debug.LogWarning("PlayerAppearance: EyeTexturePresetTable에 눈 텍스처를 등록해주세요.", this);
            missingEyeColorWarningShown = true;
            return;
        }
        IEnumerable<ColorTarget> targets = eyeColorTargets != null && eyeColorTargets.Length > 0
            ? eyeColorTargets : DetectColorTargets(detectedEyeColorTargets,
                (renderer, material) => renderer.name == "Body" && material.name == "body" &&
                    material.HasProperty("_Color2nd") && material.HasProperty("_UseMain2ndTex") &&
                    material.GetFloat("_UseMain2ndTex") > 0.5f && material.HasProperty("_Main2ndTex") &&
                    material.GetTexture("_Main2ndTex") != null, "_Color2nd");
        if (hairColorBlock == null) hairColorBlock = new MaterialPropertyBlock();
        CombatPresentation presentation = GetComponent<CombatPresentation>();
        bool applied = false;
        foreach (ColorTarget target in targets)
        {
            if (target == null || target.renderer == null) continue;
            Material[] materials = target.renderer.sharedMaterials;
            int index = target.materialIndex;
            if (index < 0 || index >= materials.Length || materials[index] == null) continue;
            Material material = materials[index];
            if (!material.HasProperty("_Main2ndTex") || !material.HasProperty("_Color2nd")) continue;
            target.renderer.GetPropertyBlock(hairColorBlock, index);
            // 여기는 홍채 색을 곱하지 않습니다. 완성된 눈 텍스처를 교체하고 틴트는 흰색 유지.
            hairColorBlock.SetTexture("_Main2ndTex", preset.texture);
            hairColorBlock.SetColor("_Color2nd", Color.white);
            target.renderer.SetPropertyBlock(hairColorBlock, index);
            if (presentation != null)
            {
                presentation.SetAppearanceLayerTexture(target.renderer, index, "_Main2ndTex", preset.texture);
                presentation.SetAppearanceLayerColor(target.renderer, index, "_Color2nd", Color.white);
            }
            applied = true;
        }
        if (!applied && !missingEyeColorWarningShown)
            Debug.LogWarning("PlayerAppearance: Eye Color Targets의 Body Renderer와 눈 머티리얼 슬롯을 확인해주세요.", this);
        missingEyeColorWarningShown = !applied;
    }

    private void ApplyColorTargets(IEnumerable<ColorTarget> targets, Color color, string defaultProperty,
        string label, ref bool warningShown)
    {
        color = new PlayerConfig { hairColor = color }.Sanitized().hairColor;
        if (hairColorBlock == null) hairColorBlock = new MaterialPropertyBlock();
        CombatPresentation presentation = GetComponent<CombatPresentation>();
        bool applied = false;
        foreach (ColorTarget target in targets)
        {
            if (target == null || target.renderer == null) continue;
            Material[] materials = target.renderer.sharedMaterials;
            int index = target.materialIndex;
            if (index < 0 || index >= materials.Length || materials[index] == null) continue;
            Material material = materials[index];
            string property = string.IsNullOrEmpty(target.colorProperty) ? defaultProperty : target.colorProperty;
            bool mainColor = string.IsNullOrEmpty(property) || property == "_Color" || property == "_BaseColor";
            bool hasColor = material.HasProperty("_Color");
            bool hasBaseColor = material.HasProperty("_BaseColor");
            if (mainColor ? (!hasColor && !hasBaseColor) : !material.HasProperty(property)) continue;

            hairColorBlock.Clear();
            target.renderer.GetPropertyBlock(hairColorBlock, index);
            if (mainColor)
            {
                if (hasColor) hairColorBlock.SetColor("_Color", color);
                if (hasBaseColor) hairColorBlock.SetColor("_BaseColor", color);
            }
            else hairColorBlock.SetColor(property, color);
            target.renderer.SetPropertyBlock(hairColorBlock, index);
            // Hit flashes/death fading must use the new appearance as their base color.
            if (presentation != null)
            {
                if (mainColor) presentation.SetAppearanceColor(target.renderer, index, color);
                else presentation.SetAppearanceLayerColor(target.renderer, index, property, color);
            }
            applied = true;
        }
        if (!applied && !warningShown)
        {
            Debug.LogWarning($"PlayerAppearance: {label}에 Renderer, 머티리얼 슬롯 번호, 색상 속성을 확인해주세요.", this);
            warningShown = true;
        }
        if (applied) warningShown = false;
    }

    private IEnumerable<ColorTarget> ResolveHairColorTargets()
    {
        if (hairColorTargets != null && hairColorTargets.Length > 0) return hairColorTargets;
        return DetectColorTargets(detectedHairColorTargets,
            (renderer, material) => renderer.name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                material.name.IndexOf("hair", System.StringComparison.OrdinalIgnoreCase) >= 0, string.Empty);
    }

    private IEnumerable<ColorTarget> DetectColorTargets(List<ColorTarget> cache,
        System.Func<Renderer, Material, bool> matches, string property)
    {
        if (cache.Count > 0) return cache;
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = candidate.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material != null && matches(candidate, material))
                    cache.Add(new ColorTarget { renderer = candidate, materialIndex = index, colorProperty = property });
            }
        }
        return cache;
    }

    private static bool IsClothingSlot(Renderer renderer, Material material)
    {
        if (!material.name.StartsWith("Mat01_", System.StringComparison.Ordinal)) return false;
        switch (renderer.name)
        {
            case "Brouse_rurune": case "Vest_rurune": case "Skirt_rurune":
            case "Shoes_rurune": case "Sox_rurune": case "Pettipants_rurune":
                return true;
            default: return false;
        }
    }

    private static float UnitValue(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    public void ApplyBangsDirection(float normalizedValue)
    {
        if (!ResolveBangsRenderer()) return;
        int side = cachedMesh.GetBlendShapeIndex("Front→→");
        int up = cachedMesh.GetBlendShapeIndex("Front→↑");
        int down = cachedMesh.GetBlendShapeIndex("Front→↓");
        if (side < 0 || up < 0 || down < 0)
        {
            if (!missingDirectionWarningShown)
                Debug.LogWarning("PlayerAppearance: 앞머리 방향 BlendShape (Front→→ / ↑ / ↓)를 확인해주세요.", this);
            missingDirectionWarningShown = true;
            return;
        }
        float value = UnitValue(normalizedValue);
        // 0 = up, 0.5 = neutral, 1 = down. Side sweep is always disabled.
        bangsRenderer.SetBlendShapeWeight(side, 0f);
        bangsRenderer.SetBlendShapeWeight(up, Mathf.Max(0f, 1f - value * 2f) * 100f);
        bangsRenderer.SetBlendShapeWeight(down, Mathf.Max(0f, value * 2f - 1f) * 100f);
    }

    public void ApplySideHairLength(float normalizedValue)
    {
        if (sideHairRenderer == null)
        {
            foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name != "Hair_Side_rurune") continue;
                sideHairRenderer = candidate;
                break;
            }
        }
        Mesh mesh = sideHairRenderer != null ? sideHairRenderer.sharedMesh : null;
        int shorter = mesh != null ? mesh.GetBlendShapeIndex("Short") : -1;
        int longer = mesh != null ? mesh.GetBlendShapeIndex("Long") : -1;
        if (shorter < 0 || longer < 0)
        {
            if (!missingSideHairWarningShown)
                Debug.LogWarning("PlayerAppearance: Short / Long이 있는 Hair_Side_rurune Renderer를 연결해주세요.", this);
            missingSideHairWarningShown = true;
            return;
        }
        float value = UnitValue(normalizedValue);
        sideHairRenderer.SetBlendShapeWeight(shorter, (1f - value) * 100f);
        sideHairRenderer.SetBlendShapeWeight(longer, value * 100f);
    }

    // Display only: UI must write DataConfig, not call this to save settings.
    public void ApplyBangsLength(float normalizedValue)
    {
        if (!ResolveBangsRenderer()) return;
        float value = float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue)
            ? 0f : Mathf.Clamp01(normalizedValue);
        float shortWeight = (1f - value) * 100f;
        bangsRenderer.SetBlendShapeWeight(short01Index, shortWeight);
        bangsRenderer.SetBlendShapeWeight(short02Index, shortWeight);
        bangsRenderer.SetBlendShapeWeight(longIndex, value * 100f);
    }

    // Compatibility with older 0..10 integer callers; new code uses float 0..1.
    public void ApplyHairLength(int hairLength)
    {
        ApplyBangsLength(maxHairLength <= 0 ? 0f : (float)hairLength / maxHairLength);
    }

    private bool ResolveBangsRenderer()
    {
        if (bangsRenderer == null)
        {
            foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = candidate.sharedMesh;
                if (mesh == null || mesh.GetBlendShapeIndex("Front_Short01") < 0 ||
                    mesh.GetBlendShapeIndex("Front_Short02") < 0 || mesh.GetBlendShapeIndex("Front_Long") < 0)
                    continue;
                bangsRenderer = candidate;
                break;
            }
        }

        Mesh currentMesh = bangsRenderer != null ? bangsRenderer.sharedMesh : null;
        if (currentMesh != cachedMesh)
        {
            cachedMesh = currentMesh;
            short01Index = currentMesh != null ? currentMesh.GetBlendShapeIndex("Front_Short01") : -1;
            short02Index = currentMesh != null ? currentMesh.GetBlendShapeIndex("Front_Short02") : -1;
            longIndex = currentMesh != null ? currentMesh.GetBlendShapeIndex("Front_Long") : -1;
            missingRendererWarningShown = false;
        }
        if (currentMesh != null && short01Index >= 0 && short02Index >= 0 && longIndex >= 0)
            return true;

        if (!missingRendererWarningShown)
        {
            Debug.LogWarning("PlayerAppearance: Front_Short01 / Front_Short02 / Front_Long이 있는 앞머리 Renderer를 연결해주세요.", this);
            missingRendererWarningShown = true;
        }
        return false;
    }
}
