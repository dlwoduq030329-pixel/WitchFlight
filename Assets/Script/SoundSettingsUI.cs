using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public sealed class SoundSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider voiceSlider;
    [SerializeField] private TMP_Text masterText, bgmText, sfxText, voiceText;
    private Slider microphoneSlider;
    private TMP_Text microphoneText;
    private TMP_Dropdown voiceMode;
    private Dropdown legacyVoiceMode;
    private void SetMicrophone(float value) => TeamVoiceChat.SetInputVolume(value);
    private void SetMode(int value) => TeamVoiceChat.SaveVoiceMode(value);
    private Slider Find(string child) => transform.Find(child)?.GetComponentInChildren<Slider>(true);
    private void Awake()
    {
        if (masterSlider == null) masterSlider = Find("MasterVolume");
        if (bgmSlider == null) bgmSlider = Find("BGM");
        if (sfxSlider == null) sfxSlider = Find("SFX");
        if (voiceSlider == null) voiceSlider = Find("Voice");
        BindText(ref masterText, masterSlider);
        BindText(ref bgmText, bgmSlider);
        BindText(ref sfxText, sfxSlider);
        BindText(ref voiceText, voiceSlider);
        microphoneSlider = Find("VoiceChat");
        BindText(ref microphoneText, microphoneSlider);
        var modeRoot = transform.Find("VoiceChatToggle");
        if (modeRoot != null)
        {
            voiceMode = modeRoot.GetComponentInChildren<TMP_Dropdown>(true);
            legacyVoiceMode = modeRoot.GetComponentInChildren<Dropdown>(true);
            if (voiceMode != null) voiceMode.onValueChanged.AddListener(SetMode);
            if (legacyVoiceMode != null) legacyVoiceMode.onValueChanged.AddListener(SetMode);
        }
        foreach (var slider in new[] { voiceSlider, microphoneSlider })
            if (slider != null) { slider.minValue = 0f; slider.maxValue = 100f; slider.wholeNumbers = true; }
        if (microphoneSlider != null) microphoneSlider.onValueChanged.AddListener(SetMicrophone);
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
    private static void BindText(ref TMP_Text text, Slider slider)
    {
        if (text != null || slider == null) return;
        text = slider.transform.parent.Find("SliderText")?.GetComponent<TMP_Text>();
        if (text == null)
            foreach (var candidate in slider.transform.parent.GetComponentsInChildren<TMP_Text>(true))
                if (candidate.name == "SliderText") { text = candidate; break; }
    }
    private static void SetPercent(TMP_Text text, float value)
    { if (text != null) text.text = Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%"; }
    private void OnEnable() { GameAudioSettings.Changed += Refresh; TeamVoiceChat.SettingsChanged += Refresh; Refresh(); }
    private void OnDisable() { GameAudioSettings.Changed -= Refresh; TeamVoiceChat.SettingsChanged -= Refresh; PlayerPrefs.Save(); }
    private void OnDestroy()
    {
        if (microphoneSlider != null) microphoneSlider.onValueChanged.RemoveListener(SetMicrophone);
        if (voiceMode != null) voiceMode.onValueChanged.RemoveListener(SetMode);
        if (legacyVoiceMode != null) legacyVoiceMode.onValueChanged.RemoveListener(SetMode);
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
        if (voiceSlider != null) voiceSlider.SetValueWithoutNotify(TeamVoiceChat.OutputVolume);
        SetPercent(masterText, GameAudioSettings.Master);
        SetPercent(bgmText, GameAudioSettings.BGM);
        SetPercent(sfxText, GameAudioSettings.SFX);
        if (voiceText != null) voiceText.text = Mathf.RoundToInt(TeamVoiceChat.OutputVolume).ToString();
        if (microphoneSlider != null) microphoneSlider.SetValueWithoutNotify(TeamVoiceChat.InputVolume);
        if (microphoneText != null) microphoneText.text = Mathf.RoundToInt(TeamVoiceChat.InputVolume).ToString();
        if (voiceMode != null) voiceMode.SetValueWithoutNotify(TeamVoiceChat.SavedMode);
        if (legacyVoiceMode != null) legacyVoiceMode.SetValueWithoutNotify(TeamVoiceChat.SavedMode);
    }
    public void SetMasterVolume(float value) => GameAudioSettings.SetMaster(value);
    public void SetBGMVolume(float value) => GameAudioSettings.SetBGM(value);
    public void SetSFXVolume(float value) => GameAudioSettings.SetSFX(value);
    public void SetVoiceVolume(float value) => TeamVoiceChat.SetOutputVolume(value);
}
