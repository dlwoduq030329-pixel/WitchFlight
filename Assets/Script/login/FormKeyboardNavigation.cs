using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

// Form-local keyboard navigation; executes before EventSystem so IME state and Tab
// focus changes are captured before TMP processes this frame's keyboard events.
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class FormKeyboardNavigation : MonoBehaviour
{
    [Tooltip("Tab 이동 순서. InputField와 버튼을 넣으세요. 비활성 항목은 건너뜁니다.")]
    [SerializeField] private Selectable[] tabOrder = new Selectable[0];
    [Tooltip("입력 중 Enter로 누를 기존 확인 버튼. 비활성 버튼은 실행하지 않습니다.")]
    [SerializeField] private Button confirmButton;
    [Tooltip("겹친 화면 중 높은 값만 키보드 입력을 처리합니다. 로그인 0 / 회원가입 10 / 닉네임 20")]
    [SerializeField] private int priority;
    [SerializeField] private bool focusFirstOnOpen = true;

    private static readonly HashSet<FormKeyboardNavigation> activeForms = new();
    private static int handledFrame = -1;
    private readonly List<TMP_InputField> boundInputs = new();
    private bool pendingFocus, enterWasOnButton;
    private int enabledFrame, lastCompositionFrame = -100;
#if ENABLE_INPUT_SYSTEM
    private Keyboard imeKeyboard;
    private bool composing;
#endif

    private void OnEnable()
    {
        activeForms.Add(this);
        enabledFrame = Time.frameCount;
        pendingFocus = focusFirstOnOpen;
        foreach (Selectable selectable in tabOrder ?? System.Array.Empty<Selectable>())
        {
            if (selectable is TMP_InputField field && !boundInputs.Contains(field))
            {
                boundInputs.Add(field);
                field.onSubmit.AddListener(OnInputSubmit);
            }
        }
    }

    private void OnDisable()
    {
        activeForms.Remove(this);
        foreach (TMP_InputField field in boundInputs)
            if (field != null) field.onSubmit.RemoveListener(OnInputSubmit);
        boundInputs.Clear();
#if ENABLE_INPUT_SYSTEM
        if (imeKeyboard != null) imeKeyboard.onIMECompositionChange -= OnCompositionChanged;
        imeKeyboard = null;
        composing = false;
#endif
        pendingFocus = false;
        EventSystem events = EventSystem.current;
        if (events != null && Owns(events.currentSelectedGameObject)) events.SetSelectedGameObject(null);
    }

    private bool IsCurrentForm()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return false;
        foreach (FormKeyboardNavigation other in activeForms)
            if (other != null && other != this && other.isActiveAndEnabled && other.gameObject.activeInHierarchy &&
                other.priority > priority) return false;
        return true;
    }

    private void Update()
    {
        CaptureComposition();
        enterWasOnButton = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<Button>() != null;
        if (!IsCurrentForm() || EventSystem.current == null || Time.frameCount <= enabledFrame) return;
        bool tabPressed = TabPressed();
        if (pendingFocus)
        {
            pendingFocus = false;
            if (!tabPressed && !Owns(EventSystem.current.currentSelectedGameObject)) FocusNext(false);
        }
        if (tabPressed && handledFrame != Time.frameCount)
        {
            handledFrame = Time.frameCount;
            FocusNext(ShiftHeld());
        }
    }

    private void LateUpdate()
    {
        if (!IsCurrentForm() || EventSystem.current == null || !EnterPressed() || enterWasOnButton) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        // Buttons (including ID duplicate-check) already receive EventSystem.Submit.
        // Never invoke their onClick twice. Input fields / deselected form use confirm.
        if (selected == null || (Owns(selected) && selected.GetComponent<TMP_InputField>() != null)) TryConfirm();
    }

    private bool Owns(GameObject selected)
    {
        return selected != null && selected.transform.IsChildOf(transform);
    }

    private static bool CanUse(Selectable selectable)
    {
        return selectable != null && selectable.isActiveAndEnabled && selectable.gameObject.activeInHierarchy &&
            selectable.IsInteractable() && (!(selectable is TMP_InputField field) || !field.readOnly);
    }

    private void FocusNext(bool backwards)
    {
        if (tabOrder == null || tabOrder.Length == 0 || EventSystem.current == null) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        int current = -1;
        for (int i = 0; i < tabOrder.Length; i++)
            if (tabOrder[i] != null && tabOrder[i].gameObject == selected) { current = i; break; }
        if (current < 0) current = backwards ? 0 : -1;
        int direction = backwards ? -1 : 1;
        for (int step = 1; step <= tabOrder.Length; step++)
        {
            int index = (current + direction * step + tabOrder.Length) % tabOrder.Length;
            Selectable next = tabOrder[index];
            if (!CanUse(next)) continue;
            EventSystem.current.SetSelectedGameObject(next.gameObject);
            if (next is TMP_InputField field) field.ActivateInputField();
            return;
        }
    }

    private void OnInputSubmit(string unused)
    {
        // Do not treat Escape/end-edit, held-key repeat or IME confirmation as submit.
        if (EnterPressed()) TryConfirm();
    }

    private void TryConfirm()
    {
        if (!IsCurrentForm() || Time.frameCount <= enabledFrame || handledFrame == Time.frameCount ||
            lastCompositionFrame >= Time.frameCount - 1 || !CanUse(confirmButton)) return;
        handledFrame = Time.frameCount;
        // Retain validation, existing designer events, loading state and click behavior.
        confirmButton.onClick.Invoke();
    }

    private void CaptureComposition()
    {
#if ENABLE_INPUT_SYSTEM
        if (imeKeyboard != Keyboard.current)
        {
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange -= OnCompositionChanged;
            imeKeyboard = Keyboard.current;
            composing = false;
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange += OnCompositionChanged;
        }
        if (composing) lastCompositionFrame = Time.frameCount;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (!string.IsNullOrEmpty(Input.compositionString)) lastCompositionFrame = Time.frameCount;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void OnCompositionChanged(IMECompositionString composition)
    {
        composing = composition.Count > 0;
        // An empty composition event is itself the commit/cancel frame.
        lastCompositionFrame = Time.frameCount;
    }
#endif

    private static bool TabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }
    private static bool ShiftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }
    private static bool EnterPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
    }
}
