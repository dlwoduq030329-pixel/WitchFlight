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
        int state = FramePerformanceMeter.State(performance.Fps);
        if (performanceSlider != null) performanceSlider.SetValueWithoutNotify(FramePerformanceMeter.Comfort(performance.Fps));
        if (performanceText != null) performanceText.text = (state == 0 ? "매우 쾌적" : state == 1 ? "쾌적" : "쾌적하지 않음") +
            " · " + Mathf.RoundToInt(performance.Fps) + " FPS";
        if (performanceFill != null) performanceFill.color = state == 0 ? new Color(.2f,.9f,.2f) :
            state == 1 ? new Color(1f,.85f,0f) : new Color(1f,.2f,.2f);
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
    public static int State(float fps) => fps > 500f ? 0 : fps > 200f ? 1 : 2;
    // Higher FPS fills more of the bar; reserve the final third for the green category.
    public static float Comfort(float fps) => fps <= 200f ? Mathf.Clamp01(fps / 200f) / 3f :
        fps <= 500f ? 1f / 3f + (fps - 200f) / 900f :
        2f / 3f + Mathf.Clamp01((fps - 500f) / 500f) / 3f;
}
