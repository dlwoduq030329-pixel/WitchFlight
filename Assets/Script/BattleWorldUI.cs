using UnityEngine;

// In-world health bars/halos must use the gameplay camera's projection and depth,
// not the separate screen-space HUD camera. Keep the layer name shared by both paths.
public static class BattleWorldUI
{
    public const string LayerName = "BattleWorldUI";

    public static int IncludeInMask(int mask)
    {
        int layer = LayerMask.NameToLayer(LayerName);
        return layer >= 0 ? mask | (1 << layer) : mask;
    }

    public static void ApplyLayer(GameObject root)
    {
        if (root == null) return;
        int layer = LayerMask.NameToLayer(LayerName);
        // A project missing the settings change can still use the normal world layer,
        // never UI (5), which is rendered from the independent HUD camera.
        ApplyLayerRecursive(root.transform, layer >= 0 ? layer : 0);
    }

    private static void ApplyLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        // Include inactive templates too, so future cloned HP cells inherit the layer.
        for (int i = 0; i < root.childCount; i++)
            ApplyLayerRecursive(root.GetChild(i), layer);
    }
}
