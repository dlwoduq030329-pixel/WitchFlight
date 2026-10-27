using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ControlSettingsUI : MonoBehaviour
{
    [Serializable]
    public class BindingRow
    {
        public ControlAction action;
        public Button button;
        public TMP_Text keyText;
        [NonSerialized] public UnityAction listener;
    }

    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityText;
    [SerializeField] private BindingRow[] bindings;
    [SerializeField] private Button resetButton;
    private static readonly string[] rowNames = {
        "Magic1Change", "Magic2Change", "Parring", "Boost", "Speedup",
        "LeftRotation", "RightRotation", "RockOn", "Magic"
    };
    private static readonly KeyCode[] allKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private BindingRow waiting;
    private int startedFrame;
    private bool captureReady;

    private void Awake()
    {
        EnsureResetButton();
        resetButton.onClick.AddListener(ResetControls);
        if (sensitivitySlider == null) sensitivitySlider = transform.Find("Gamdo/Slider")?.GetComponent<Slider>();
        if (sensitivityText == null) sensitivityText = transform.Find("Gamdo/SliderText")?.GetComponent<TMP_Text>();
        if (bindings == null || bindings.Length == 0)
        {
            bindings = new BindingRow[rowNames.Length];
            for (int i = 0; i < bindings.Length; i++)
                bindings[i] = new BindingRow { action = i == 7 ? ControlAction.QuickBrake : i == 8 ? ControlAction.Emote : (ControlAction)i,
                    button = transform.Find(rowNames[i] + "/Button")?.GetComponent<Button>() };
        }
        foreach (BindingRow row in bindings)
        {
            // Preserve existing scene/prefab button references while migrating the final two rows.
            if (row != null && row.action == ControlAction.LockOn) row.action = ControlAction.QuickBrake;
            if (row != null && row.action == ControlAction.MagicUse) row.action = ControlAction.Emote;
            if (row == null || row.button == null) continue;
            if (row.action == ControlAction.QuickBrake || row.action == ControlAction.Emote)
                foreach (var label in row.button.transform.parent.GetComponentsInChildren<TMP_Text>(true))
                    if (!label.transform.IsChildOf(row.button.transform))
                        label.text = row.action == ControlAction.QuickBrake ? "급제동" : "감정표현";
            if (row.keyText == null) row.keyText = row.button.GetComponentInChildren<TMP_Text>(true);
            row.listener = () => BeginRebind(row);
            row.button.onClick.AddListener(row.listener);
            // Prevent a binding key from also navigating/submitting a selected button.
            row.button.navigation = new Navigation { mode = Navigation.Mode.None };
        }
        if (sensitivitySlider != null)
        {
            // Keep the authored normalized slider and its existing percent text listener compatible.
            sensitivitySlider.minValue = .01f;
            sensitivitySlider.maxValue = 1f;
            sensitivitySlider.wholeNumbers = false;
            sensitivitySlider.navigation = new Navigation { mode = Navigation.Mode.None };
            sensitivitySlider.onValueChanged.AddListener(ChangeSensitivity);
        }
    }

    private void OnEnable() { ControlSettings.Changed += Refresh; Refresh(); }
    private void OnDisable() { ControlSettings.Changed -= Refresh; CancelRebind(); }
    private void OnDestroy()
    {
        if (resetButton != null) resetButton.onClick.RemoveListener(ResetControls);
        if (sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(ChangeSensitivity);
        if (bindings == null) return;
        foreach (BindingRow row in bindings)
            if (row?.button != null && row.listener != null) row.button.onClick.RemoveListener(row.listener);
    }
    private void ChangeSensitivity(float value) => ControlSettings.SetSensitivity(value * 100f);

    private void Refresh()
    {
        if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(ControlSettings.Sensitivity / 100f);
        if (sensitivityText != null) sensitivityText.text = Mathf.RoundToInt(ControlSettings.Sensitivity).ToString();
        if (bindings == null) return;
        foreach (BindingRow row in bindings)
            if (row?.keyText != null && row != waiting) row.keyText.text = ControlSettings.KeyLabel(ControlSettings.GetKey(row.action));
    }

    private void BeginRebind(BindingRow row)
    {
        CancelRebind();
        waiting = row;
        startedFrame = Time.frameCount;
        captureReady = !Input.anyKey;
        if (row.keyText != null) row.keyText.text = "키 입력 · Esc 취소";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        // Consume the next click as a binding, not another settings operation.
        SetInteractable(false);
    }

    private void SetInteractable(bool value)
    {
        if (resetButton != null) resetButton.interactable = value;
        if (bindings != null) foreach (BindingRow row in bindings) if (row?.button != null) row.button.interactable = value;
        if (sensitivitySlider != null) sensitivitySlider.interactable = value;
    }

    public void CancelRebind()
    {
        waiting = null;
        SetInteractable(true);
        Refresh();
    }

    public void ResetControls()
    {
        CancelRebind();
        ControlSettings.ResetToDefaults();
        Refresh();
    }

    private void EnsureResetButton()
    {
        if (resetButton != null) return;
        foreach (Button candidate in GetComponentsInChildren<Button>(true))
            if (candidate.name == "ResetControlsButton") { resetButton = candidate; return; }
        var root = new GameObject("ResetControlsButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-24, -24);
        rect.sizeDelta = new Vector2(240, 52);
        root.GetComponent<Image>().color = new Color(.08f, .65f, .63f);
        resetButton = root.GetComponent<Button>();
        resetButton.targetGraphic = root.GetComponent<Image>();
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        var fontSource = GetComponentInChildren<TMP_Text>(true);
        label.transform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        if (fontSource != null) label.font = fontSource.font;
        label.fontSize = 26; label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.text = "조작 설정 초기화";
    }

    private void OnApplicationFocus(bool focused) { if (!focused) CancelRebind(); }
    private void Update()
    {
        if (waiting == null || Time.frameCount <= startedFrame) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelRebind(); return; }
        // Ignore the click that opened capture and any keys already held at that moment.
        if (!captureReady) { captureReady = !Input.anyKey; if (!captureReady) return; }
        foreach (KeyCode key in allKeys)
        {
            if (key == KeyCode.None || key >= KeyCode.JoystickButton0 || !Input.GetKeyDown(key)) continue;
            if (ControlSettings.TryRebind(waiting.action, key, out string error)) CancelRebind();
            else if (waiting.keyText != null) waiting.keyText.text = error;
            return;
        }
    }
}
