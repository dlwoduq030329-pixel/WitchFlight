using UnityEngine;

// Shared by lobby UI and every spawned character. No backend schema change required.
[CreateAssetMenu(fileName = "EyeTexturePresetTable", menuName = "WitchFlight/Customization/Eye Texture Presets")]
public sealed class EyeTexturePresetTable : ScriptableObject
{
    [System.Serializable]
    public sealed class Preset
    {
        public string label;
        [Tooltip("Stable RGB key stored in eyeColor. Do not change keys after players have saved them.")]
        public Color savedColorKey = Color.white;
        public Texture2D texture;
    }

    [Tooltip("Button indices start at 0. Keep existing indices/keys stable; edit textures or append entries.")]
    [SerializeField] private Preset[] presets;
    [Tooltip("Read-only compatibility entries for old saved keys, not selectable button indices.")]
    [SerializeField] private Preset[] legacyPresets;
    private static EyeTexturePresetTable defaultTable;
    public static EyeTexturePresetTable Default
    {
        get
        {
            if (defaultTable == null) defaultTable = Resources.Load<EyeTexturePresetTable>("EyeTexturePresetTable");
            return defaultTable;
        }
    }

    public bool TryGetPreset(int index, out Preset preset)
    {
        preset = presets != null && index >= 0 && index < presets.Length ? presets[index] : null;
        return preset != null && preset.texture != null;
    }

    // Existing arbitrary eye colors resolve deterministically to the closest authored preset.
    // Exact saved RGB keys always win, including after the backend's HTML color round-trip.
    public bool TryResolve(Color savedColor, out Preset preset)
    {
        preset = null;
        savedColor = new PlayerConfig { eyeColor = savedColor }.Sanitized().eyeColor;
        Color32 savedBytes = savedColor;
        float nearest = float.PositiveInfinity;
        foreach (Preset candidate in SavedPresets())
        {
            if (candidate == null || candidate.texture == null) continue;
            Color32 key = candidate.savedColorKey;
            if (savedBytes.r == key.r && savedBytes.g == key.g && savedBytes.b == key.b)
            {
                preset = candidate;
                return true;
            }
            Color c = key;
            float r = savedColor.r - c.r, g = savedColor.g - c.g, b = savedColor.b - c.b;
            float distance = r * r + g * g + b * b;
            if (distance >= nearest) continue;
            nearest = distance;
            preset = candidate;
        }
        return preset != null;
    }

    private System.Collections.Generic.IEnumerable<Preset> SavedPresets()
    {
        if (presets != null)
            foreach (Preset preset in presets) yield return preset;
        if (legacyPresets != null)
            foreach (Preset preset in legacyPresets) yield return preset;
    }
}
