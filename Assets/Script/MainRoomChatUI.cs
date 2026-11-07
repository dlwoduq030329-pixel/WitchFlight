using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Authored Main-scene UI. Only message rows are instantiated, then recycled at capacity.
[DefaultExecutionOrder(-200)]
public sealed class MainRoomChatUI : MonoBehaviour
{
    [SerializeField] private RectTransform chatRect;
    [SerializeField] private ScrollRect scrollView;
    [SerializeField] private RectTransform content;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private GameObject textPrefab;
    [SerializeField, Min(100f)] private float expandedHeight = 300f;
    [SerializeField, Range(10, 200)] private int retainedMessages = 100;
    private static MainRoomChatUI instance;
    public static bool BlocksLobbyInput => instance != null && instance.isActiveAndEnabled &&
        (instance.expanded || instance.closedFrame == Time.frameCount);
    private readonly Queue<(GameObject root, TMP_Text text)> rows = new();
    private Vector2 collapsedSize, scrollPosition, scrollSize;
    private RectTransform scrollRect;
    private bool expanded, layoutDirty, refocus, closeRequested;
    private int openedFrame, submittedFrame = -1, closedFrame = -1;
    private EventSystem chatEvents;
    private bool previousNavigation;
    private float nextSendTime;
    private CursorLockMode previousLock;
    private bool previousCursor;
    private NetworkRunner roomRunner;

    private bool CanChat => NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsWaitingInCodeRoom &&
        PlayerData.Local != null && PlayerData.Local.Object != null && PlayerData.Local.Object.IsValid &&
        PlayerData.Local.IsLoadoutInitialized;

    private void Awake()
    {
        instance = this;
        if (chatRect == null || scrollView == null || content == null || inputField == null || textPrefab == null)
        { Debug.LogError("MainRoomChatUI: assign ChatUI, Scroll View, Content, InputField and TextPrefab.", this); instance = null; enabled = false; return; }
        collapsedSize = chatRect.sizeDelta;
        scrollRect = (RectTransform)scrollView.transform;
        scrollPosition = scrollRect.anchoredPosition;
        scrollSize = scrollRect.sizeDelta;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = PlayerData.LobbyChatCharacterLimit;
        inputField.onSubmit.AddListener(Submit);
        inputField.onSelect.AddListener(OnInputSelected);
        inputField.gameObject.SetActive(false);
        scrollView.scrollSensitivity = 20f;
        scrollView.vertical = false;
    }

    private void Update()
    {
        if (expanded && !CanChat) { Collapse(); return; }
        if (!Application.isFocused) return;
        if (Input.GetKeyDown(KeyCode.Escape) && expanded) { closeRequested = true; return; }
        if (!(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) || !CanChat) return;
        if (inputField.isFocused) return; // TMP's onSubmit owns sending, never send twice per Enter.
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected != inputField.gameObject &&
            (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null)) return;
        OpenAndFocus();
    }

    private void OpenAndFocus()
    {
        NetworkRunner runner = PlayerData.Local.Runner;
        if (roomRunner != null && roomRunner != runner) ClearHistory();
        roomRunner = runner;
        if (!expanded)
        {
            expanded = true;
            previousLock = Cursor.lockState;
            previousCursor = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            chatEvents = EventSystem.current;
            if (chatEvents != null)
            {
                previousNavigation = chatEvents.sendNavigationEvents;
                chatEvents.sendNavigationEvents = false;
                chatEvents.SetSelectedGameObject(null);
            }
            chatRect.sizeDelta = new Vector2(collapsedSize.x, Mathf.Max(collapsedSize.y, expandedHeight));
            float inputHeight = ((RectTransform)inputField.transform).rect.height + 8f;
            scrollRect.anchoredPosition = scrollPosition + Vector2.up * (inputHeight * .5f);
            scrollRect.sizeDelta = scrollSize - Vector2.up * inputHeight;
            inputField.gameObject.SetActive(true);
            scrollView.vertical = true;
            layoutDirty = true;
        }
        openedFrame = Time.frameCount;
        inputField.interactable = true;
        inputField.Select();
        inputField.ActivateInputField();
    }

    private void OnInputSelected(string _) { if (!expanded && CanChat) OpenAndFocus(); }

    private void Submit(string value)
    {
        if (!expanded || closeRequested || submittedFrame == Time.frameCount) return;
        if (openedFrame == Time.frameCount) { refocus = true; return; }
        submittedFrame = Time.frameCount;
        if (!string.IsNullOrEmpty(Input.compositionString)) { refocus = true; return; }
        if (!CanChat) { closeRequested = true; return; }
        string message = PlayerData.CleanLobbyChat(value);
        if (message.Length == 0) { closeRequested = true; return; }
        if (Time.unscaledTime < nextSendTime || !PlayerData.Local.SendLobbyChat(message))
        { refocus = true; return; }
        nextSendTime = Time.unscaledTime + .4f;
        closeRequested = true;
    }

    private void LateUpdate()
    {
        // TMP finishes its submit callback before we hide/deactivate its input.
        if (closeRequested) Collapse();
        if (refocus && expanded)
        {
            refocus = false;
            inputField.Select();
            inputField.ActivateInputField();
        }
        if (!layoutDirty || !isActiveAndEnabled) return;
        layoutDirty = false;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scrollView.StopMovement();
        scrollView.verticalNormalizedPosition = 0f;
    }

    public static void Receive(NetworkRunner runner, string sender, string message, long unixTime)
    {
        if (instance == null || NetworkGameManager.Instance == null || NetworkGameManager.Instance.Runner != runner) return;
        instance.Append(runner, sender, message, unixTime);
    }

    private void Append(NetworkRunner runner, string sender, string message, long unixTime)
    {
        if (roomRunner != null && roomRunner != runner) ClearHistory();
        roomRunner = runner;
        (GameObject root, TMP_Text text) row;
        if (rows.Count >= Mathf.Clamp(retainedMessages, 10, 200)) row = rows.Dequeue();
        else
        {
            GameObject root = Instantiate(textPrefab, content, false);
            TMP_Text text = root.GetComponentInChildren<TMP_Text>(true);
            if (text == null) { Destroy(root); Debug.LogError("TextPrefab needs a TMP text component.", this); return; }
            text.richText = false;
            text.raycastTarget = false;
            row = (root, text);
        }
        row.root.transform.SetAsLastSibling();
        row.root.SetActive(true);
        string stamp = DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime().ToString("HH:mm");
        row.text.text = $"[{stamp}] {sender} : {message}";
        rows.Enqueue(row);
        layoutDirty = true;
    }

    public static void LeftRoom(NetworkRunner runner)
    {
        if (instance == null || instance.roomRunner != runner) return;
        instance.Collapse();
        instance.ClearHistory();
        instance.roomRunner = null;
    }

    private void ClearHistory()
    {
        while (rows.Count > 0) Destroy(rows.Dequeue().root);
        inputField.SetTextWithoutNotify(string.Empty);
        nextSendTime = 0f;
        layoutDirty = true;
    }

    private void Collapse()
    {
        if (!expanded) return;
        expanded = refocus = closeRequested = false;
        closedFrame = Time.frameCount;
        inputField.DeactivateInputField();
        inputField.SetTextWithoutNotify(string.Empty);
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == inputField.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        if (chatEvents != null) chatEvents.sendNavigationEvents = previousNavigation;
        chatEvents = null;
        inputField.gameObject.SetActive(false);
        chatRect.sizeDelta = collapsedSize;
        scrollRect.anchoredPosition = scrollPosition;
        scrollRect.sizeDelta = scrollSize;
        scrollView.vertical = false;
        Cursor.lockState = previousLock;
        Cursor.visible = previousCursor;
        layoutDirty = true;
    }

    private void OnDisable() => Collapse();
    private void OnDestroy()
    {
        if (inputField != null)
        {
            inputField.onSubmit.RemoveListener(Submit);
            inputField.onSelect.RemoveListener(OnInputSelected);
        }
        if (instance == this) instance = null;
    }
}
