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
        if (sensitivitySlider == null) sensitivitySlider = transform.Find("Gamdo/Slider")?.GetComponent<Slider>();
        if (sensitivityText == null) sensitivityText = transform.Find("Gamdo/SliderText")?.GetComponent<TMP_Text>();
        if (bindings == null || bindings.Length == 0)
        {
            bindings = new BindingRow[rowNames.Length];
            for (int i = 0; i < bindings.Length; i++)
                bindings[i] = new BindingRow { action = (ControlAction)i,
                    button = transform.Find(rowNames[i] + "/Button")?.GetComponent<Button>() };
        }
        foreach (BindingRow row in bindings)
        {
            if (row == null || row.button == null) continue;
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
        if (bindings != null) foreach (BindingRow row in bindings) if (row?.button != null) row.button.interactable = value;
        if (sensitivitySlider != null) sensitivitySlider.interactable = value;
    }

    public void CancelRebind()
    {
        waiting = null;
        SetInteractable(true);
        Refresh();
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
