using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Place on the always-active battlemanager, not inside either managed panel.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
public sealed class BattleRoundUI : MonoBehaviour
{
    [Header("Style / data")]
    [SerializeField] private TMP_FontAsset uiFont;
    [SerializeField] private MagicStatTable magicTable;
    [Header("Magic change panel (empty = generated UI)")]
    [SerializeField] private GameObject magicSwapPanel;
    [SerializeField] private TMP_Text swapCountdownText;
    [SerializeField] private TMP_Text roundScoreText;
    [SerializeField] private TMP_Text swapStatusText;
    [SerializeField] private TMP_Text selectedMagicNameText;
    [SerializeField] private Image slot1Icon;
    [SerializeField] private Image slot2Icon;
    [SerializeField] private TMP_Text slot1Text;
    [SerializeField] private TMP_Text slot2Text;
    [SerializeField] private Button equipButton;
    [Header("Final result panel (empty = generated UI)")]
    [SerializeField] private GameObject finalResultPanel;
    [SerializeField] private TMP_Text resultTitleText;
    [SerializeField] private TMP_Text resultScoreText;
    [SerializeField] private Button returnToMainButton;
    [SerializeField] private UnityEvent onVictory = new UnityEvent();
    [SerializeField] private UnityEvent onDefeat = new UnityEvent();

    private PlayerData localData;
    private Canvas generatedCanvas;
    private CanvasGroup generatedResultGroup;
    private int swapRound, selectedSlot = 1, nextRequestId, pendingRequestId, localTeam;
    private MagicType selectedMagic, requestedFirst, requestedSecond;
    private bool requestPending, requestAccepted, sawMatch, resultLatched, returning;
    private float requestStarted, resultShownAt;
    private string cachedScore, cachedResult;

    private void Awake()
    {
        if (uiFont == null) uiFont = GetComponent<BattleHud>()?.FeedbackFont;
        if (magicTable == null) magicTable = MagicStatTable.Default;
        ValidatePanel(ref magicSwapPanel);
        ValidatePanel(ref finalResultPanel);
        Show(magicSwapPanel, false);
        Show(finalResultPanel, false);
        if (equipButton != null) equipButton.onClick.AddListener(EquipSelectedMagic);
        if (returnToMainButton != null) returnToMainButton.onClick.AddListener(ReturnToMain);
    }

    private void ValidatePanel(ref GameObject panel)
    {
        if (panel == null || !transform.IsChildOf(panel.transform)) return;
        Debug.LogWarning("BattleRoundUI: panel must not contain its controller. Using generated UI.", this);
        panel = null;
    }

    private bool HasLocalData => localData != null && localData.Object != null && localData.Object.IsValid &&
        localData.Object.HasInputAuthority && localData.IsLoadoutInitialized;
    private bool CanEdit => HasLocalData && !returning && BattleManager.Instance != null &&
        BattleManager.Instance.Phase == BattleStartPhase.Intermission &&
        BattleManager.Instance.PhaseRemainingSeconds > 0f &&
        localData.MagicChangeRound == BattleManager.Instance.RoundNumber;

    private void BindLocalData()
    {
        PlayerData next = PlayerData.Local;
        if (localData == next) return;
        if (localData != null) localData.RoundMagicChangeResult -= OnMagicChangeResult;
        localData = next;
        if (localData != null) localData.RoundMagicChangeResult += OnMagicChangeResult;
        requestPending = false;
    }

    private void LateUpdate()
    {
        BindLocalData();
        BattleManager battle = BattleManager.Instance;
        if (HasLocalData) localTeam = localData.teamIndex;
        BattleFlag flag = BattleFlag.Instance;
        bool connected = flag != null && flag.Object != null && flag.Object.IsValid;
        if (connected) sawMatch = true;
        if (!resultLatched && battle != null && battle.IsBattleEnded) CaptureResult(battle);
        if (sawMatch && !connected && !returning && !resultLatched)
        {
            resultLatched = true;
            cachedResult = "연결이 종료되었습니다";
            cachedScore = "메인 화면에서 다시 매칭해주세요.";
            resultShownAt = Time.unscaledTime;
        }
        bool swapping = !resultLatched && battle != null && battle.Phase == BattleStartPhase.Intermission;
        if (swapping)
        {
            EnsureSwapPanel();
            if (swapRound != battle.RoundNumber)
            {
                swapRound = battle.RoundNumber;
                selectedSlot = 1;
                selectedMagic = MagicType.None;
                requestPending = false;
                Status("슬롯과 마법을 선택한 뒤 장착하세요. 시간 종료 시 마지막 승인된 장비로 시작합니다.");
            }
            UpdatePendingRequest();
            RefreshSwap(battle);
        }
        else requestPending = false;
        Show(magicSwapPanel, swapping);
        if (resultLatched)
        {
            EnsureResultPanel();
            Text(resultTitleText, cachedResult);
            Text(resultScoreText, cachedScore);
            if (returnToMainButton != null)
                returnToMainButton.interactable = !returning && Time.unscaledTime - resultShownAt >= 1f;
            if (generatedResultGroup != null)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - resultShownAt) / 0.6f);
                generatedResultGroup.alpha = t;
                if (resultTitleText != null)
                    resultTitleText.transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.2f);
            }
        }
        Show(finalResultPanel, resultLatched);
        if (swapping || resultLatched)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void CaptureResult(BattleManager battle)
    {
        resultLatched = true;
        bool won = localTeam > 0 && battle.WinningTeam == localTeam;
        cachedResult = won ? "승리!" : "패배";
        cachedScore = Score(battle) + "  ·  2선승 경기 종료";
        resultShownAt = Time.unscaledTime;
        EnsureResultPanel();
        Show(finalResultPanel, true);
        if (resultTitleText != null) resultTitleText.color = won ? new Color(0.2f, 0.9f, 0.85f) : new Color(1f, 0.4f, 0.45f);
        if (won) onVictory.Invoke(); else onDefeat.Invoke();
    }

    private string Score(BattleManager battle)
    {
        int mine = localTeam == 2 ? battle.Team2Wins : battle.Team1Wins;
        int theirs = localTeam == 2 ? battle.Team1Wins : battle.Team2Wins;
        return $"나 {mine} : {theirs} 상대";
    }

    private void RefreshSwap(BattleManager battle)
    {
        Text(swapCountdownText, $"마법 교체  {Mathf.CeilToInt(battle.PhaseRemainingSeconds)}초");
        Text(roundScoreText, $"ROUND {battle.RoundNumber} 종료  ·  {Score(battle)}  ·  최대 {battle.AllowedMagicChanges}슬롯 변경");
        Text(selectedMagicNameText, selectedMagic == MagicType.None ? "변경할 마법을 선택하세요" : Name(selectedMagic));
        if (HasLocalData)
        {
            Icon(slot1Icon, localData.magic1);
            Icon(slot2Icon, localData.magic2);
            Text(slot1Text, $"{(selectedSlot == 1 ? "▶ " : "")}1번 · {Name(localData.magic1)}");
            Text(slot2Text, $"{(selectedSlot == 2 ? "▶ " : "")}2번 · {Name(localData.magic2)}");
        }
        if (equipButton != null) equipButton.interactable = CanEdit && !requestPending && selectedMagic != MagicType.None;
    }

    // Unity Button.OnClick APIs. Slot selection does NOT erase the chosen magic.
    public void SelectSlot(int slot) { if (slot >= 1 && slot <= 2) selectedSlot = slot; }
    public void SelectMagic(int magicId)
    {
        if (magicId < (int)MagicType.Fire || magicId > (int)MagicType.Smoke) return;
        selectedMagic = (MagicType)magicId;
    }

    public void EquipSelectedMagic()
    {
        if (!CanEdit || requestPending || selectedMagic == MagicType.None) return;
        SendLoadout(selectedSlot == 1 ? selectedMagic : localData.magic1,
            selectedSlot == 2 ? selectedMagic : localData.magic2);
    }

    public void ResetRoundChanges()
    {
        if (CanEdit && !requestPending) SendLoadout(localData.MagicChangeBase1, localData.MagicChangeBase2);
    }

    private void SendLoadout(MagicType first, MagicType second)
    {
        BattleManager battle = BattleManager.Instance;
        if (!BattleRoundRules.IsAllowedLoadout(localData.MagicChangeBase1, localData.MagicChangeBase2,
            first, second, battle.AllowedMagicChanges))
        {
            Status($"최대 {battle.AllowedMagicChanges}슬롯만 변경할 수 있습니다. 다른 슬롯을 바꾸려면 변경 초기화를 누르세요.");
            return;
        }
        requestPending = true;
        requestAccepted = false;
        pendingRequestId = ++nextRequestId;
        requestedFirst = first;
        requestedSecond = second;
        requestStarted = Time.unscaledTime;
        Status("서버 확인 중...");
        if (!localData.RequestRoundMagicChange(battle.RoundNumber, first, second, pendingRequestId))
        {
            requestPending = false;
            Status("접속 정보를 확인해주세요.");
        }
    }

    private void OnMagicChangeResult(int id, int round, bool accepted, string error)
    {
        if (!requestPending || id != pendingRequestId || round != swapRound) return;
        requestAccepted = accepted;
        if (!accepted) { requestPending = false; Status(error); }
    }

    private void UpdatePendingRequest()
    {
        if (!requestPending) return;
        if (requestAccepted && HasLocalData && localData.magic1 == requestedFirst && localData.magic2 == requestedSecond)
        {
            requestPending = false;
            Status("장착 완료. 이 경기에서만 적용됩니다.");
        }
        else if (Time.unscaledTime - requestStarted > 5f)
        {
            requestPending = false;
            Status("응답이 지연됩니다. 현재 장착 아이콘을 확인하고 다시 시도해주세요.");
        }
    }

    public async void ReturnToMain()
    {
        if (returning || !resultLatched) return;
        returning = true;
        try
        {
            NetworkGameManager network = NetworkGameManager.Instance;
            if (network != null)
            {
                bool success = await network.ReturnToMainMenuAsync();
                if (this != null && !success) { returning = false; cachedScore = "메인 씬 설정을 확인해주세요."; }
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                await SceneManager.LoadSceneAsync(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (this != null) returning = false;
        }
    }

    private string Name(MagicType magic)
    {
        if (magic == MagicType.None) return "비어 있음";
        string displayName = magicTable != null ? magicTable.GetStats(magic).displayName : null;
        return string.IsNullOrWhiteSpace(displayName) ? magic.ToString() : displayName;
    }
    private void Icon(Image image, MagicType magic)
    {
        if (image == null) return;
        if (magicTable == null) magicTable = MagicStatTable.Default;
        Sprite sprite = magicTable != null ? magicTable.GetIcon(magic) : null;
        if (image.sprite != sprite) image.sprite = sprite;
        image.enabled = sprite != null;
    }
    private void Status(string value) => Text(swapStatusText, value);
    private static void Text(TMP_Text label, string value)
    {
        if (label == null) return;
        label.richText = false;
        if (label.text != value) label.text = value;
    }
    private static void Show(GameObject panel, bool show) { if (panel != null && panel.activeSelf != show) panel.SetActive(show); }

    private void EnsureCanvas()
    {
        if (generatedCanvas != null) return;
        var root = new GameObject("Generated round UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        generatedCanvas = root.GetComponent<Canvas>();
        generatedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        generatedCanvas.sortingOrder = 200;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        if (EventSystem.current == null)
            new GameObject("Round UI EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform);
    }

    private GameObject Panel(string name)
    {
        EnsureCanvas();
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(generatedCanvas.transform, false);
        var rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        obj.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.07f, 0.96f);
        return obj;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }

    private TMP_Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var label = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) label.font = uiFont;
        label.fontSize = fontSize; label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white; label.raycastTarget = false;
        Text(label, value);
        return label;
    }

    private Button Button(Transform parent, string caption, Vector2 position, Vector2 size, UnityAction action)
    {
        var rect = Rect(caption, parent, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.1f, 0.32f, 0.36f, 1f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        Label(rect, "Label", caption, Vector2.zero, size - new Vector2(12, 8), 23);
        return button;
    }

    private Image Image(Transform parent, Vector2 position, Vector2 size)
    {
        var image = Rect("Icon", parent, position, size).gameObject.AddComponent<Image>();
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }

    private void EnsureSwapPanel()
    {
        if (magicSwapPanel != null) return;
        magicSwapPanel = Panel("Between rounds - magic changes");
        Transform root = magicSwapPanel.transform;
        swapCountdownText = Label(root, "Time", "", new Vector2(0, 360), new Vector2(1100, 70), 46);
        roundScoreText = Label(root, "Round score", "", new Vector2(0, 295), new Vector2(1200, 50), 26);
        for (int i = 1; i <= (int)MagicType.Smoke; i++)
        {
            int id = i;
            Vector2 pos = new Vector2(-500 + ((i - 1) % 6) * 200, 160 - ((i - 1) / 6) * 160);
            Button button = Button(root, "", pos, new Vector2(184, 144), () => SelectMagic(id));
            Image icon = Image(button.transform, new Vector2(0, 20), new Vector2(70, 70));
            Icon(icon, (MagicType)id);
            Label(button.transform, "Magic name", Name((MagicType)id), new Vector2(0, -45), new Vector2(174, 42), 22);
        }
        Button first = Button(root, "", new Vector2(-250, -175), new Vector2(460, 92), () => SelectSlot(1));
        Button second = Button(root, "", new Vector2(250, -175), new Vector2(460, 92), () => SelectSlot(2));
        slot1Icon = Image(first.transform, new Vector2(-170, 0), new Vector2(64, 64));
        slot2Icon = Image(second.transform, new Vector2(-170, 0), new Vector2(64, 64));
        slot1Text = Label(first.transform, "Slot 1", "", new Vector2(40, 0), new Vector2(350, 75), 24);
        slot2Text = Label(second.transform, "Slot 2", "", new Vector2(40, 0), new Vector2(350, 75), 24);
        selectedMagicNameText = Label(root, "Selection", "", new Vector2(0, -260), new Vector2(1100, 45), 25);
        equipButton = Button(root, "선택한 슬롯에 장착", new Vector2(170, -330), new Vector2(360, 64), EquipSelectedMagic);
        Button(root, "변경 초기화", new Vector2(-200, -330), new Vector2(280, 64), ResetRoundChanges);
        swapStatusText = Label(root, "Status", "", new Vector2(0, -405), new Vector2(1500, 60), 23);
    }

    private void EnsureResultPanel()
    {
        if (finalResultPanel != null) return;
        finalResultPanel = Panel("Final result");
        generatedResultGroup = finalResultPanel.AddComponent<CanvasGroup>();
        resultTitleText = Label(finalResultPanel.transform, "Result", "", new Vector2(0, 100), new Vector2(1400, 160), 92);
        resultScoreText = Label(finalResultPanel.transform, "Score", "", new Vector2(0, -45), new Vector2(1300, 80), 34);
        returnToMainButton = Button(finalResultPanel.transform, "메인 화면으로", new Vector2(0, -200), new Vector2(380, 80), ReturnToMain);
    }

    private void OnDisable()
    {
        if (localData != null) localData.RoundMagicChangeResult -= OnMagicChangeResult;
        localData = null;
        Show(magicSwapPanel, false);
        Show(finalResultPanel, false);
    }

    private void OnDestroy()
    {
        if (equipButton != null) equipButton.onClick.RemoveListener(EquipSelectedMagic);
        if (returnToMainButton != null) returnToMainButton.onClick.RemoveListener(ReturnToMain);
    }
}
