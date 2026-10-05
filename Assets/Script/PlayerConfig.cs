using System;
using Fusion;
using UnityEngine;

// One snapshot of customization data. Not a manager or an independent saved state.
// Length/direction values are model parameters, not world-space distances or angles.
[Serializable]
public struct PlayerConfig : INetworkStruct
{
    public int hairStylePreset;
    public float bangsLength;
    public float bangsDirection;
    public float sideHairLength;
    public float ahogeLength;
    public Color hairColor;
    public Color clothColor;
    public Color eyeColor;
    public int hatIndex;
    public int broomIndex;
    public int wandIndex;

    public static PlayerConfig Default => new PlayerConfig
    {
        bangsDirection = 0.5f,
        hairColor = Color.white,
        clothColor = Color.white,
        eyeColor = Color.white,
        hatIndex = (int)HatType.Classic,
        broomIndex = (int)BroomType.Standard
    };

    public PlayerConfig Sanitized()
    {
        PlayerConfig value = this;
        value.hairStylePreset = Mathf.Max(0, hairStylePreset);
        value.bangsLength = Mathf.Clamp01(FiniteOr(bangsLength, 0f));
        value.bangsDirection = Mathf.Clamp01(FiniteOr(bangsDirection, 0.5f));
        value.sideHairLength = Mathf.Clamp01(FiniteOr(sideHairLength, 0f));
        value.ahogeLength = Mathf.Max(0f, FiniteOr(ahogeLength, 0f));
        value.hairColor = SanitizeColor(hairColor);
        value.clothColor = SanitizeColor(clothColor);
        value.eyeColor = SanitizeColor(eyeColor);
        value.hatIndex = hatIndex >= (int)HatType.Classic && hatIndex <= (int)HatType.Cosmic
            ? hatIndex : (int)HatType.Classic;
        value.broomIndex = broomIndex >= (int)BroomType.Slow && broomIndex <= (int)BroomType.Speed
            ? broomIndex : (int)BroomType.Standard;
        value.wandIndex = Mathf.Max(0, wandIndex);
        return value;
    }

    private static float FiniteOr(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private static Color SanitizeColor(Color color) => new Color(
        Mathf.Clamp01(FiniteOr(color.r, 1f)), Mathf.Clamp01(FiniteOr(color.g, 1f)),
        Mathf.Clamp01(FiniteOr(color.b, 1f)), Mathf.Clamp01(FiniteOr(color.a, 1f)));
}
