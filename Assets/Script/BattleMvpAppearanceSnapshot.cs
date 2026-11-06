using UnityEngine;

// Visual-only snapshot retained with the cached model, including after despawn.
public sealed class BattleMvpAppearanceSnapshot : MonoBehaviour
{
    public PlayerConfig config;
    public bool hasConfig;
    [System.NonSerialized] public readonly System.Collections.Generic.List<Material> ownedMaterials = new();
    private void OnDestroy()
    {
        foreach (var material in ownedMaterials) if (material != null) Destroy(material);
        ownedMaterials.Clear();
    }
}
