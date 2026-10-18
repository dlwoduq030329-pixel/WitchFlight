using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class EnterRoom : MonoBehaviour
{
    [Tooltip("왼쪽부터 1, 2, 3, 4번째 비밀번호 입력칸을 연결합니다.")]
    [SerializeField] private TMP_InputField[] texts;
    [SerializeField] private GameObject robbyUI;
    [SerializeField] private TextMeshProUGUI waringText;
    [SerializeField] private GameObject idInputText;

    private TMP_InputField[] boundFields;
    private UnityAction<string>[] valueListeners;
    private Coroutine focusRoutine;
    private bool requestPending;

    private void OnEnable()
    {
        UnbindInputs();
        if (texts == null || texts.Length != RoomCodeRules.Length)
        {
            ShowError("비밀번호 입력칸 4개를 왼쪽부터 연결해주세요.");
            return;
        }
        boundFields = (TMP_InputField[])texts.Clone();
        valueListeners = new UnityAction<string>[boundFields.Length];
        for (int i = 0; i < boundFields.Length; i++)
        {
            TMP_InputField field = boundFields[i];
            if (field == null) continue;
            field.characterLimit = 1;
            field.richText = false;
            field.onFocusSelectAll = true;
            int index = i;
            valueListeners[i] = value => OnCodeCharacterChanged(index, value);
            field.onValueChanged.AddListener(valueListeners[i]);
        }
        if (waringText != null) waringText.text = string.Empty;
        FocusInput(0);
    }

    private void OnDisable()
    {
        if (focusRoutine != null) StopCoroutine(focusRoutine);
        focusRoutine = null;
        UnbindInputs();
    }

    private void UnbindInputs()
    {
        if (boundFields != null && valueListeners != null)
        {
            for (int i = 0; i < boundFields.Length; i++)
                if (boundFields[i] != null && valueListeners[i] != null)
                    boundFields[i].onValueChanged.RemoveListener(valueListeners[i]);
        }
        boundFields = null;
        valueListeners = null;
    }

    private void OnCodeCharacterChanged(int index, string value)
    {
        if (string.IsNullOrEmpty(value) || !isActiveAndEnabled) return;
        if (value.Length != 1 || char.IsWhiteSpace(value[0]) ||
            char.IsControl(value[0]) || char.IsSurrogate(value[0]))
        {
            ShowError(RoomCodeRules.Hint);
            return;
        }
        if (waringText != null) waringText.text = string.Empty;
        // Changing selection during TMP's text event can re-enter its input processing.
        // Move on the next frame; the last field stays selected (no automatic join).
        if (index + 1 < texts.Length) FocusInput(index + 1);
    }

    private void FocusInput(int index)
    {
        if (focusRoutine != null) StopCoroutine(focusRoutine);
        focusRoutine = StartCoroutine(FocusInputNextFrame(index));
    }

    private IEnumerator FocusInputNextFrame(int index)
    {
        yield return null;
        focusRoutine = null;
        if (!isActiveAndEnabled || texts == null || index < 0 || index >= texts.Length) yield break;
        TMP_InputField field = texts[index];
        if (field == null || !field.isActiveAndEnabled || !field.IsInteractable()) yield break;
        field.Select();
        field.ActivateInputField();
    }

    public void TryEnterRoom()
    {
        if (requestPending) return;
        if (texts == null || texts.Length != RoomCodeRules.Length)
        {
            ShowError("비밀번호 입력칸 4개를 왼쪽부터 연결해주세요.");
            return;
        }
        string passwordPack = string.Empty;
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null || texts[i].text.Length != 1)
            {
                ShowError(RoomCodeRules.Hint);
                FocusInput(i);
                return;
            }
            passwordPack += texts[i].text;
        }
        if (!RoomCodeRules.TryValidate(passwordPack, out string code, out string error))
        {
            ShowError(error);
            return;
        }
        if (NetworkGameManager.Instance == null)
        {
            ShowError("네트워크 매니저가 준비되지 않았습니다.");
            return;
        }
        requestPending = true;
        NetworkGameManager.Instance.JoinRoom(code, OnJoinSuccess, OnJoinFailed);
    }

    private void OnJoinSuccess()
    {
        requestPending = false;
        if (robbyUI != null) robbyUI.SetActive(true);
        if (texts != null)
            foreach (TMP_InputField field in texts)
                if (field != null) field.SetTextWithoutNotify(string.Empty);
        if (idInputText != null) idInputText.SetActive(false);
    }

    private void OnJoinFailed(string message)
    {
        requestPending = false;
        ShowError(message);
    }

    private void ShowError(string message)
    {
        if (waringText != null) waringText.text = message;
        else Debug.LogWarning(message, this);
    }
}
