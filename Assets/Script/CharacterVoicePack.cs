using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterVoicePack", menuName = "WitchFlight/Items/Character Voice Pack")]
public sealed class CharacterVoicePack : ScriptableObject
{
    [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
    [Tooltip("상점 미리듣기. 비워두면 이 목소리의 Greet 음성, 없으면 첫 음성을 재생합니다.")]
    [SerializeField] private AudioClip previewClip;
    private Dictionary<string, AudioClip> byName;
    private HashSet<AudioClip> originals;
    private void OnEnable() => Invalidate();
    private void OnValidate() => Invalidate();
    private void Invalidate() { byName = null; originals = null; }
    private void Build()
    {
        if (byName != null) return;
        byName = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        originals = new HashSet<AudioClip>();
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            originals.Add(clip);
            if (!byName.TryAdd(clip.name, clip))
                Debug.LogWarning($"{name}: 중복 음성 이름 {clip.name}", this);
        }
    }
    public bool Contains(AudioClip clip) { Build(); return clip != null && originals.Contains(clip); }
    public bool HasClips { get { Build(); return originals.Count > 0; } }
    public AudioClip GetPreviewClip()
    {
        if (previewClip != null) return previewClip;
        Build();
        if (byName.TryGetValue("SFX_Voice_Greet", out var greet)) return greet;
        if (byName.TryGetValue("SFX_Voice_Greet_01", out greet)) return greet;
        foreach (var clip in clips) if (clip != null) return clip;
        return null;
    }
    public AudioClip Match(AudioClip original)
    {
        if (original == null) return null;
        Build();
        if (byName.TryGetValue(original.name, out var exact)) return exact;
        // Legacy Basic_Princess also contains Attack-03 (new packs use Attack_03).
        if (byName.TryGetValue(original.name.Replace('-', '_'), out var separatorMatch)) return separatorMatch;
        // Some legacy Basic_Princess singleton files omit the variant suffix.
        string alternate = original.name.EndsWith("_01", StringComparison.Ordinal)
            ? original.name.Substring(0, original.name.Length - 3) : original.name + "_01";
        return byName.TryGetValue(alternate, out var compatible) ? compatible : original;
    }
}
