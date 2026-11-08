using UnityEngine;

// Authored on the existing Hair panel. No polling or hierarchy searches.
[DisallowMultipleComponent]
public sealed class CustomizationBroomVisibility : MonoBehaviour
{
    [SerializeField] private LobbyPlayerInitializer preview;

    private void OnEnable()
    {
        if (preview != null) preview.SetBroomPreviewHidden(true);
    }

    private void OnDisable()
    {
        if (preview != null) preview.SetBroomPreviewHidden(false);
    }
}
