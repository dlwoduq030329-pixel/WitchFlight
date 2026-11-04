using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Audio feedback only. Never changes the field's text, validation or selection.
[DisallowMultipleComponent]
public sealed class GameUITypingSound : MonoBehaviour
{
    private TMP_InputField tmpInput;
    private InputField legacyInput;
    private string previousComposition = "";
    private int lastPlayedFrame = -1;

    private void OnEnable()
    {
        tmpInput = GetComponent<TMP_InputField>();
        legacyInput = GetComponent<InputField>();
        if (tmpInput != null) tmpInput.onValueChanged.AddListener(OnTextChanged);
        else if (legacyInput != null) legacyInput.onValueChanged.AddListener(OnTextChanged);
        previousComposition = Input.compositionString;
    }

    private void OnDisable()
    {
        if (tmpInput != null) tmpInput.onValueChanged.RemoveListener(OnTextChanged);
        else if (legacyInput != null) legacyInput.onValueChanged.RemoveListener(OnTextChanged);
        previousComposition = "";
    }

    private bool CanPlay => Application.isFocused && isActiveAndEnabled &&
        (tmpInput != null ? tmpInput.isFocused && tmpInput.IsInteractable() && !tmpInput.readOnly :
         legacyInput != null && legacyInput.isFocused && legacyInput.IsInteractable() && !legacyInput.readOnly);

    private void OnTextChanged(string unused)
    {
        // Ignore initialization / programmatic updates without an actual edit input.
        if (!CanPlay) return;
        foreach (char key in Input.inputString)
            if (!char.IsControl(key) || key == '\b' || key == '\u007f') { Play(); return; }
        bool shortcut = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
            Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (Input.GetKey(KeyCode.Backspace) || Input.GetKey(KeyCode.Delete) ||
            shortcut && (Input.GetKeyDown(KeyCode.V) || Input.GetKeyDown(KeyCode.X)) ||
            !string.IsNullOrEmpty(Input.compositionString)) Play();
    }

    private void LateUpdate()
    {
        string composition = Input.compositionString;
        // Korean IME composition changes before onValueChanged commits a syllable.
        if (CanPlay && composition.Length > 0 && composition != previousComposition) Play();
        previousComposition = composition;
    }

    private void Play()
    {
        if (lastPlayedFrame == Time.frameCount) return;
        lastPlayedFrame = Time.frameCount;
        BattleAudio.Play(BattleSound.UITyping, local: true);
    }
}
