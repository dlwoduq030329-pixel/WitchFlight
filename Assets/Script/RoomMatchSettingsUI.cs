using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-active controller; generated controls live inside the existing code-room panel.
[DisallowMultipleComponent]
public sealed partial class RoomMatchSettingsUI : MonoBehaviour
{
    [SerializeField] private GameObject settingsRoot;
    [SerializeField] private TMP_Dropdown modeDropdown;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_FontAsset uiFont;
    private NetworkGameManager manager;
    private TMP_Dropdown boundDropdown;
    private GameObject generatedCanvas;

    private void Update()
    {
        if (manager == null) manager = GetComponent<NetworkGameManager>();
        bool visible = manager != null && manager.IsWaitingInCodeRoom;
        if (visible) EnsureUI();
        if (settingsRoot != null && settingsRoot != gameObject && !transform.IsChildOf(settingsRoot.transform))
            settingsRoot.SetActive(visible);
        if (!visible || modeDropdown == null) return;
        modeDropdown.SetValueWithoutNotify(manager.CurrentMatchMode == BattleMatchMode.OneVsOne ? 0 : 1);
        modeDropdown.interactable = manager.IsRoomHost;
        if (statusText != null)
            statusText.text = $"참가자 {manager.ConnectedPlayerCount} / {manager.RequiredPlayerCount}\n" +
                (manager.IsRoomHost ? (manager.ConnectedPlayerCount > 2 ? "1대1 변경은 2명 이하일 때 가능" : "방장만 대전 방식을 변경할 수 있어요") : "방장이 선택한 설정입니다");
    }

    public void SelectMode(int value)
    {
        if (manager != null) manager.TrySetRoomMatchMode(value);
        if (modeDropdown != null && manager != null)
            modeDropdown.SetValueWithoutNotify(manager.CurrentMatchMode == BattleMatchMode.OneVsOne ? 0 : 1);
    }

    private void EnsureUI()
    {
        if (settingsRoot == null)
        {
            Transform parent = null;
            foreach (linkuserinfo info in FindObjectsByType<linkuserinfo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (info.CodeWaitingRoomPanel != null)
                { parent = info.CodeWaitingRoomPanel.transform; break; }
            if (parent == null)
            {
                generatedCanvas = new GameObject("Room Settings Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                generatedCanvas.transform.SetParent(transform, false);
                var canvas = generatedCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 250;
                var scaler = generatedCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                parent = generatedCanvas.transform;
            }
            var root = new GameObject("MapSettings - Match Mode", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false); settingsRoot = root;
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-32, -108); rect.sizeDelta = new Vector2(360, 180);
            root.GetComponent<Image>().color = new Color(.035f, .08f, .14f, .96f);
            Label(root.transform, "대전 방식", new Vector2(16,-20), new Vector2(328,32), 26);
        }
        if (modeDropdown == null)
        {
            GameObject dropdown = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            dropdown.name = "MatchModeDropdown"; dropdown.transform.SetParent(settingsRoot.transform, false);
            var rect = dropdown.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(16, -55); rect.sizeDelta = new Vector2(328, 48);
            modeDropdown = dropdown.GetComponent<TMP_Dropdown>();
            foreach (TMP_Text text in dropdown.GetComponentsInChildren<TMP_Text>(true))
            { if (uiFont != null) text.font = uiFont; text.fontSize = 24; text.color = new Color(.03f,.13f,.18f); }
            modeDropdown.template.sizeDelta = new Vector2(modeDropdown.template.sizeDelta.x, 96);
            modeDropdown.itemText.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
            // TMP default sprites are optional; text remains clear without built-in art.
            Transform arrow = dropdown.transform.Find("Arrow");
            if (arrow != null) { var image = arrow.GetComponent<Image>(); if (image != null) image.enabled = false; }
        }
        if (statusText == null)
            statusText = Label(settingsRoot.transform, "", new Vector2(16,-112), new Vector2(328,60), 18);
        if (boundDropdown != modeDropdown)
        {
            if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
            boundDropdown = modeDropdown;
            modeDropdown.ClearOptions(); modeDropdown.AddOptions(new List<string> { "1 vs 1", "2 vs 2" });
            boundDropdown.onValueChanged.AddListener(SelectMode);
        }
    }

    private TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false); label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0,1);
        label.rectTransform.pivot = new Vector2(0,1); label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = size;
        if (uiFont != null) label.font = uiFont;
        label.fontSize = fontSize; label.color = Color.white; label.text = value; label.raycastTarget = false;
        return label;
    }

    private void OnDestroy()
    {
        ReleaseTeamDropdown();
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
        if (generatedCanvas != null) Destroy(generatedCanvas);
    }
}
