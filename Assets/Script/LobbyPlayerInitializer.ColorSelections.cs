using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class LobbyPlayerInitializer
{
    private Transform colorSelectionRoot;
    private GameObject[] hairSelections, clothSelections, eyeSelections;

    private void RefreshColorSelections()
    {
        if (customizationPanel == null) return;
        if (colorSelectionRoot != customizationPanel.transform)
        {
            colorSelectionRoot = customizationPanel.transform;
            hairSelections = FindColorSelections("HairColors");
            clothSelections = FindColorSelections("CostumColor");
            eyeSelections = FindColorSelections("EyeColor");
        }
        bool ready = CanCustomize;
        SetColorSelection(hairSelections, ready ? FindSavedColor(hairColorPresets, DataConfig.hairColor) : -1);
        SetColorSelection(clothSelections, ready ? FindSavedColor(clothColorPresets, DataConfig.clothColor) : -1);
        SetColorSelection(eyeSelections, ready ? FindSavedEye() : -1);
    }

    private GameObject[] FindColorSelections(string groupName)
    {
        foreach (Transform group in colorSelectionRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(group.name, groupName, StringComparison.OrdinalIgnoreCase)) continue;
            var markers = new List<GameObject>();
            // Authored palette buttons are in preset-index order (0-based), including inactive buttons.
            // Only their child Image is a selection marker; the button's swatch image stays visible.
            foreach (Transform child in group)
            {
                if (child.GetComponent<Button>() == null) continue;
                var marker = child.Find("Image")?.GetComponent<Image>();
                if (marker != null) marker.raycastTarget = false;
                markers.Add(marker != null ? marker.gameObject : null);
            }
            return markers.ToArray();
        }
        return Array.Empty<GameObject>();
    }

    internal static int FindSavedColor(Color[] presets, Color saved)
    {
        if (presets == null) return -1;
        // BACKND serializes colors as #RRGGBBAA: compare the same 8-bit RGB, not float equality.
        Color32 key = saved;
        for (int i = 0; i < presets.Length; i++)
        {
            Color32 candidate = presets[i];
            if (candidate.r == key.r && candidate.g == key.g && candidate.b == key.b) return i;
        }
        return -1; // Arbitrary/old colors must not falsely highlight an unrelated preset.
    }

    private int FindSavedEye()
    {
        var table = eyeTexturePresets != null ? eyeTexturePresets : EyeTexturePresetTable.Default;
        if (table == null || !table.TryResolve(DataConfig.eyeColor, out var saved)) return -1;
        // Match the texture actually applied by PlayerAppearance, including legacy saved color keys.
        for (int i = 0; i < table.Count; i++)
            if (table.TryGetPreset(i, out var preset) && preset.texture == saved.texture) return i;
        return -1;
    }

    private static void SetColorSelection(GameObject[] markers, int selected)
    {
        if (markers == null) return;
        for (int i = 0; i < markers.Length; i++)
            if (markers[i] != null && markers[i].activeSelf != (i == selected)) markers[i].SetActive(i == selected);
    }
}
