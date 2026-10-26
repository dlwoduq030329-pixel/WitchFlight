using UnityEngine;

/// <summary>Displays the logged-in account's persisted cat; never assigns random colors itself.</summary>
[DisallowMultipleComponent]
public sealed class WitchCatAppearance : MonoBehaviour
{
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Renderer eyeRenderer;
    [Tooltip("IDs 1..7: black, cheese, grey, silver tabby, tabby, tuxedo, white. Do not reorder.")]
    [SerializeField] private Material[] bodyMaterials = new Material[7];
    [Tooltip("IDs 1..3: blue, green, yellow. Do not reorder.")]
    [SerializeField] private Material[] eyeMaterials = new Material[3];

    private Material[] originalBody, originalEyes;
    private int appliedBody, appliedEyes;
    private bool followLocalAccount = true;

    public void ApplyAccountIndices(int body, int eyes)
    {
        followLocalAccount = false;
        if (body != appliedBody && Apply(bodyRenderer, bodyMaterials, body, originalBody?.Length ?? 1)) appliedBody = body;
        if (eyes != appliedEyes && Apply(eyeRenderer, eyeMaterials, eyes, originalEyes?.Length ?? 2)) appliedEyes = eyes;
    }

    private void Awake()
    {
        if (bodyRenderer != null) originalBody = bodyRenderer.sharedMaterials;
        if (eyeRenderer != null) originalEyes = eyeRenderer.sharedMaterials;
    }

    private void LateUpdate()
    {
        if (!followLocalAccount) return;
        var database = DatabaseManager.Instance;
        if (database == null || !database.TryGetCatAppearance(out int body, out int eyes))
        {
            RestoreOriginal();
            return;
        }
        if (body != appliedBody && Apply(bodyRenderer, bodyMaterials, body, originalBody?.Length ?? 1)) appliedBody = body;
        if (eyes != appliedEyes && Apply(eyeRenderer, eyeMaterials, eyes, originalEyes?.Length ?? 2)) appliedEyes = eyes;
    }

    private static bool Apply(Renderer target, Material[] choices, int id, int baseSlots)
    {
        if (target == null || choices == null || id < 1 || id > choices.Length || choices[id - 1] == null)
            return false;
        var slots = target.sharedMaterials;
        // The eye mesh contains two material slots (one per eye).
        // Keep any appended outline materials intact when owner data arrives later.
        for (int i = 0; i < Mathf.Min(slots.Length, baseSlots); i++) slots[i] = choices[id - 1];
        target.sharedMaterials = slots;
        return true;
    }

    private void OnDisable() => RestoreOriginal();

    private void RestoreOriginal()
    {
        if (appliedBody != 0 && bodyRenderer != null) bodyRenderer.sharedMaterials = originalBody;
        if (appliedEyes != 0 && eyeRenderer != null) eyeRenderer.sharedMaterials = originalEyes;
        appliedBody = appliedEyes = 0;
    }
}
