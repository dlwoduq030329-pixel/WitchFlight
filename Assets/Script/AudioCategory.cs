using UnityEngine;

// Add to a music object (or parent) to route all its sources to BGM.
[DisallowMultipleComponent]
public sealed class AudioCategory : MonoBehaviour
{
    [SerializeField] private GameAudioCategory category = GameAudioCategory.BGM;
    public GameAudioCategory Category => category;
    private void OnEnable() => Apply();
    private void OnTransformChildrenChanged() => Apply();
    private void OnValidate() { if (Application.isPlaying) Apply(); }
    public void Apply()
    {
        foreach (var source in GetComponentsInChildren<AudioSource>(true))
            if (source.GetComponentInParent<AudioCategory>() == this)
                source.outputAudioMixerGroup = GameAudioSettings.Group(category);
    }
}
