using System;
using UnityEngine;
using UnityEngine.Audio;

public enum GameAudioCategory { SFX, BGM, Voice }

// Mixer gain leaves individual source volumes and speed-dependent envelopes intact.
public static class GameAudioSettings
{
    private static AudioMixer mixer;
    private static AudioMixerGroup sfx, bgm, voice;
    public static event Action Changed;
    public static float Master => Read("Master");
    public static float BGM => Read("BGM");
    public static float SFX => Read("SFX");
    public static float Voice => Read("Voice");
    private static float Read(string key) => Mathf.Clamp01(PlayerPrefs.GetFloat("Audio." + key, 1f));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { mixer = null; sfx = bgm = voice = null; Changed = null; }
    public static AudioMixerGroup Group(GameAudioCategory category)
    {
        if (mixer == null)
        {
            mixer = Resources.Load<AudioMixer>("GameAudioMixer");
            if (mixer == null) return null;
            var effects = mixer.FindMatchingGroups("SFX");
            var music = mixer.FindMatchingGroups("BGM");
            var voices = mixer.FindMatchingGroups("Voice");
            sfx = effects.Length > 0 ? effects[0] : null;
            bgm = music.Length > 0 ? music[0] : null;
            voice = voices.Length > 0 ? voices[0] : null;
        }
        return category == GameAudioCategory.Voice ? voice : category == GameAudioCategory.BGM ? bgm : sfx;
    }
    public static float Decibels(float volume) => volume <= 0f ? -80f : Mathf.Log10(Mathf.Clamp01(volume)) * 20f;
    public static void Apply()
    {
        Group(GameAudioCategory.SFX);
        if (mixer == null) return;
        mixer.SetFloat("MasterVolume", Decibels(Master));
        mixer.SetFloat("BGMVolume", Decibels(BGM));
        mixer.SetFloat("SFXVolume", Decibels(SFX));
        mixer.SetFloat("VoiceVolume", Decibels(Voice));
    }
    private static void Set(string key, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        PlayerPrefs.SetFloat("Audio." + key, Mathf.Clamp01(value));
        Apply(); Changed?.Invoke();
    }
    public static void SetMaster(float value) => Set("Master", value);
    public static void SetBGM(float value) => Set("BGM", value);
    public static void SetSFX(float value) => Set("SFX", value);
    public static void SetVoice(float value) => Set("Voice", value);
}

// Covers authored and third-party sources; explicit category components take precedence.
public sealed class GameAudioRouting : MonoBehaviour
{
    private float nextScan;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var root = new GameObject("Game Audio Settings");
        DontDestroyOnLoad(root);
        root.AddComponent<GameAudioRouting>();
    }
    private void Start() { GameAudioSettings.Apply(); Scan(); }
    private void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + .5f;
        Scan();
    }
    private static void Scan()
    {
        foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (source.outputAudioMixerGroup != null) continue;
            var tag = source.GetComponentInParent<AudioCategory>();
            source.outputAudioMixerGroup = GameAudioSettings.Group(tag != null ? tag.Category : GameAudioCategory.SFX);
        }
    }
    private void OnApplicationPause(bool paused) { if (paused) PlayerPrefs.Save(); }
    private void OnApplicationQuit() => PlayerPrefs.Save();
}
