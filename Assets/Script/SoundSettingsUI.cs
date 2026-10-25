using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SoundSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider voiceSlider;
    private Slider Find(string child) => transform.Find(child)?.GetComponentInChildren<Slider>(true);
    private void Awake()
    {
        if (masterSlider == null) masterSlider = Find("MasterVolume");
        if (bgmSlider == null) bgmSlider = Find("BGM");
        if (sfxSlider == null) sfxSlider = Find("SFX");
        if (voiceSlider == null) voiceSlider = Find("Voice");
        Setup(voiceSlider);
        Setup(masterSlider); Setup(bgmSlider); Setup(sfxSlider);
        if (masterSlider != null) masterSlider.onValueChanged.AddListener(SetMasterVolume);
        if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(SetBGMVolume);
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(SetSFXVolume);
        if (voiceSlider != null) voiceSlider.onValueChanged.AddListener(SetVoiceVolume);
    }
    private static void Setup(Slider slider)
    {
        if (slider == null) return;
        slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false;
    }
    private void OnEnable() { GameAudioSettings.Changed += Refresh; Refresh(); }
    private void OnDisable() { GameAudioSettings.Changed -= Refresh; PlayerPrefs.Save(); }
    private void OnDestroy()
    {
        if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(SetMasterVolume);
        if (bgmSlider != null) bgmSlider.onValueChanged.RemoveListener(SetBGMVolume);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(SetSFXVolume);
        if (voiceSlider != null) voiceSlider.onValueChanged.RemoveListener(SetVoiceVolume);
    }
    private void Refresh()
    {
        if (masterSlider != null) masterSlider.SetValueWithoutNotify(GameAudioSettings.Master);
        if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(GameAudioSettings.BGM);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameAudioSettings.SFX);
        if (voiceSlider != null) voiceSlider.SetValueWithoutNotify(GameAudioSettings.Voice);
    }
    public void SetMasterVolume(float value) => GameAudioSettings.SetMaster(value);
    public void SetBGMVolume(float value) => GameAudioSettings.SetBGM(value);
    public void SetSFXVolume(float value) => GameAudioSettings.SetSFX(value);
    public void SetVoiceVolume(float value) => GameAudioSettings.SetVoice(value);
}
