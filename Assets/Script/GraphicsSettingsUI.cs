using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GraphicsSettingsUI : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private TMP_Dropdown postProcessingDropdown;
    [SerializeField] private Slider performanceSlider;
    [SerializeField] private TMP_Text performanceText;
    [Tooltip("기기 부하 표시에 사용하는 목표 FPS. 실제 CPU/GPU 사용률이 아닌 현재 화면 프레임 기준입니다.")]
    [SerializeField, Min(1f)] private float targetFps = 60f;
    private readonly FramePerformanceMeter performance = new();
    private Image performanceFill;
    private float warmupUntil;

    private void Awake()
    {
        if (displayModeDropdown == null) displayModeDropdown = transform.Find("DisplayMode/Dropdown")?.GetComponent<TMP_Dropdown>();
        if (qualityDropdown == null) qualityDropdown = transform.Find("GraphicQuality/Dropdown")?.GetComponent<TMP_Dropdown>();
        if (postProcessingDropdown == null) postProcessingDropdown = transform.Find("PostProcessing/Dropdown")?.GetComponent<TMP_Dropdown>();
        if (performanceSlider == null) performanceSlider = transform.Find("GraphicStatus/Slider")?.GetComponent<Slider>();
        if (performanceText == null) performanceText = transform.Find("GraphicStatus/ThemeText")?.GetComponent<TMP_Text>();
        if (displayModeDropdown != null) displayModeDropdown.onValueChanged.AddListener(ChangeDisplay);
        if (qualityDropdown != null) qualityDropdown.onValueChanged.AddListener(ChangeQuality);
        if (postProcessingDropdown != null) postProcessingDropdown.onValueChanged.AddListener(ChangePostProcessing);
        if (performanceSlider != null)
        {
            performanceSlider.interactable = false;
            performanceSlider.transition = Selectable.Transition.None;
            performanceSlider.minValue = 0f;
            performanceSlider.maxValue = 1f;
            performanceSlider.wholeNumbers = false;
            performanceFill = performanceSlider.fillRect != null ? performanceSlider.fillRect.GetComponent<Image>() : null;
        }
    }

    private void OnEnable() { GameGraphicsSettings.Changed += Refresh; Refresh(); ResetMeasurement(); }
    private void OnDisable() { GameGraphicsSettings.Changed -= Refresh; }
    private void OnDestroy()
    {
        if (displayModeDropdown != null) displayModeDropdown.onValueChanged.RemoveListener(ChangeDisplay);
        if (qualityDropdown != null) qualityDropdown.onValueChanged.RemoveListener(ChangeQuality);
        if (postProcessingDropdown != null) postProcessingDropdown.onValueChanged.RemoveListener(ChangePostProcessing);
    }
    private void ChangeDisplay(int value) { GameGraphicsSettings.SetDisplayMode(value); ResetMeasurement(); }
    private void ChangeQuality(int value) { GameGraphicsSettings.SetQuality(value); ResetMeasurement(); }
    private void ChangePostProcessing(int value) { GameGraphicsSettings.SetPostProcessing(value); ResetMeasurement(); }
    private void Refresh()
    {
        if (displayModeDropdown != null) displayModeDropdown.SetValueWithoutNotify(GameGraphicsSettings.DisplayMode);
        if (qualityDropdown != null) qualityDropdown.SetValueWithoutNotify(GameGraphicsSettings.Quality);
        if (postProcessingDropdown != null) postProcessingDropdown.SetValueWithoutNotify(GameGraphicsSettings.PostProcessing ? 0 : 1);
    }
    private void ResetMeasurement()
    {
        performance.Reset();
        warmupUntil = Time.unscaledTime + 1f;
        if (performanceText != null) performanceText.text = "측정 중…";
        if (performanceSlider != null) performanceSlider.SetValueWithoutNotify(0f);
        if (performanceFill != null) performanceFill.color = Color.gray;
    }
    private void OnApplicationFocus(bool focus) => ResetMeasurement();
    private void Update()
    {
        if (!Application.isFocused || Time.unscaledTime < warmupUntil) return;
        if (!performance.Sample(Time.unscaledDeltaTime)) return;
        float target = Mathf.Max(1f, targetFps);
        if (Application.targetFrameRate > 0) target = Mathf.Min(target, Application.targetFrameRate);
        if (QualitySettings.vSyncCount > 0)
        {
            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            if (refresh > 0f) target = Mathf.Min(target, refresh / QualitySettings.vSyncCount);
        }
        int state = FramePerformanceMeter.State(performance.Fps, target);
        if (performanceSlider != null) performanceSlider.SetValueWithoutNotify(FramePerformanceMeter.Load(performance.Fps, target));
        if (performanceText != null) performanceText.text = (state == 0 ? "쾌적" : state == 1 ? "보통" : "부하 높음") +
            " · " + Mathf.RoundToInt(performance.Fps) + " FPS";
        if (performanceFill != null) performanceFill.color = state == 0 ? new Color(.2f,.85f,.65f) :
            state == 1 ? new Color(1f,.75f,.2f) : new Color(1f,.3f,.3f);
    }
}

// Rolling sample by elapsed time, independent of timeScale and the settings UI's frame rate.
public sealed class FramePerformanceMeter
{
    private float elapsed;
    private int frames;
    public float Fps { get; private set; }
    public void Reset() { elapsed = 0f; frames = 0; Fps = 0f; }
    public bool Sample(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return false;
        elapsed += seconds;
        frames++;
        if (elapsed < 1f) return false;
        Fps = frames / elapsed;
        elapsed = 0f; frames = 0;
        return true;
    }
    public static int State(float fps, float target) => fps >= target * .9f ? 0 : fps >= target * .6f ? 1 : 2;
    public static float Load(float fps, float target) => Mathf.Clamp01((target / Mathf.Max(1f, fps) - .75f) / 1.25f);
}
