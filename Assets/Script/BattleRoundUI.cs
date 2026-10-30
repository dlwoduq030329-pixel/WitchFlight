using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

// Place on the always-active battlemanager, not inside either managed panel.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
public sealed partial class BattleRoundUI : MonoBehaviour
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
    [Tooltip("설명·Slider를 묶은 패널의 MagicDescriptionUI를 연결하세요. 마법 버튼 클릭 시 켜고 장착 완료 시 해당 오브젝트를 끕니다. 이 컨트롤러는 패널 바깥에 두세요.")]
    [SerializeField] private MagicDescriptionUI magicDescriptionUI;
    [SerializeField] private Image slot1Icon;
    [SerializeField] private Image slot2Icon;
    [SerializeField] private TMP_Text slot1Text;
    [SerializeField] private TMP_Text slot2Text;
    [Tooltip("켜면 선택 표시와 슬롯 번호를 함께 표시합니다. 끄면 MagicStatTable의 장착 마법 이름만 표시합니다.")]
    [SerializeField] private bool showSlotNumberInText = true;
    [SerializeField] private Button equipButton;
    [SerializeField] private Button resetMagicButton;
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
    private bool descriptionOpen, closeDescriptionOnAccept;
    private int descriptionRevision, requestedDescriptionRevision;
    private float requestStarted, resultShownAt;
    private string cachedScore, cachedResult;
    private BattleResultsPresentation resultsPresentation;

    private void Awake()
    {
        resultsPresentation = GetComponent<BattleResultsPresentation>();
        if (resultsPresentation == null) resultsPresentation = gameObject.AddComponent<BattleResultsPresentation>();
        if (uiFont == null) uiFont = GetComponent<BattleHud>()?.FeedbackFont;
        if (magicTable == null) magicTable = MagicStatTable.Default;
        ValidatePanel(ref magicSwapPanel);
        ValidatePanel(ref finalResultPanel);
        SetDescriptionVisible(false);
        Show(magicSwapPanel, false);
        Show(finalResultPanel, false);
        EnsureUIInput();
        if (equipButton != null) equipButton.onClick.AddListener(EquipSelectedMagic);
        BindResetButton();
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
        localData.MagicChangeRound == BattleManager.Instance.RoundNumber &&
        !localData.RoundSwapReady && !readyRequestPending;

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
                SetDescriptionVisible(false);
                descriptionRevision++;
                requestPending = false;
                readyRequestPending = false;
                Status("마법을 장착한 뒤 준비 완료를 누르세요. 전원이 준비하면 다음 라운드를 시작합니다.");
            }
            UpdatePendingRequest();
            RefreshSwap(battle);
            RefreshReadyUI(battle);
        }
        else { requestPending = readyRequestPending = false; SetSwapEditingLocked(false); }
        Show(magicSwapPanel, swapping);
        if (resultLatched && !resultsPresentation.OwnsFinalUI)
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
        if (!resultsPresentation.OwnsFinalUI) Show(finalResultPanel, resultLatched);
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
        resultsPresentation.ShowResults(battle);
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
        RefreshMagicDescription();
        if (HasLocalData)
        {
            Icon(slot1Icon, localData.magic1);
            Icon(slot2Icon, localData.magic2);
            Text(slot1Text, SlotText(1, localData.magic1));
            Text(slot2Text, SlotText(2, localData.magic2));
        }
        if (equipButton != null) equipButton.interactable = CanEdit && !requestPending && selectedMagic != MagicType.None;
    }

    // Unity Button.OnClick APIs. Slot selection does NOT erase the chosen magic.
    public void SelectSlot(int slot) { if (CanEdit && !requestPending && slot >= 1 && slot <= 2) selectedSlot = slot; }
    public void SelectMagic(int magicId)
    {
        if (!CanEdit || requestPending) return;
        if (magicId < (int)MagicType.Fire || magicId > (int)MagicType.Smoke) return;
        selectedMagic = (MagicType)magicId;
        descriptionOpen = true;
        descriptionRevision++;
        RefreshMagicDescription();
    }

    private void RefreshMagicDescription()
    {
        if (magicTable == null) magicTable = MagicStatTable.Default;
        SetDescriptionVisible(descriptionOpen);
        if (magicDescriptionUI != null)
            magicDescriptionUI.ShowMagic(selectedMagic, magicTable, BattleManager.Instance != null ? BattleManager.Instance.Perks : null);
    }

    private void SetDescriptionVisible(bool visible)
    {
        descriptionOpen = visible;
        // Prefab references can be lost when a replacement swap panel is assigned.
        // Resolve only inside that panel, including inactive children; never another screen.
        if (magicDescriptionUI == null && magicSwapPanel != null)
            magicDescriptionUI = magicSwapPanel.GetComponentInChildren<MagicDescriptionUI>(true);
        if (magicDescriptionUI == null) return;
        GameObject panel = magicDescriptionUI.gameObject;
        // A wrong reference must not hide the controller or the entire selection screen.
        if (panel == magicSwapPanel || transform.IsChildOf(panel.transform)) return;
        Show(panel, visible);
    }

    public void EquipSelectedMagic()
    {
        if (!CanEdit || requestPending || selectedMagic == MagicType.None) return;
        SendLoadout(selectedSlot == 1 ? selectedMagic : localData.magic1,
            selectedSlot == 2 ? selectedMagic : localData.magic2, true);
    }

    public void ResetRoundChanges()
    {
        // InitialMagic is the host-accepted DataConfig loadout captured on entering the room.
        if (!CanEdit || requestPending) return;
        selectedMagic = MagicType.None;
        descriptionRevision++;
        SetDescriptionVisible(false);
        SendLoadout(localData.InitialMagic1, localData.InitialMagic2, true, true);
    }

    private void SendLoadout(MagicType first, MagicType second, bool closeDescription = false, bool resetInitial = false)
    {
        BattleManager battle = BattleManager.Instance;
        if (!resetInitial && !BattleRoundRules.IsAllowedLoadout(localData.MagicChangeBase1, localData.MagicChangeBase2,
            first, second, battle.AllowedMagicChanges))
        {
            Status($"최대 {battle.AllowedMagicChanges}슬롯만 변경할 수 있습니다. 다른 슬롯을 바꾸려면 먼저 기존 마법으로 되돌려주세요.");
            return;
        }
        requestPending = true;
        requestAccepted = false;
        pendingRequestId = ++nextRequestId;
        requestedFirst = first;
        requestedSecond = second;
        closeDescriptionOnAccept = closeDescription;
        requestedDescriptionRevision = descriptionRevision;
        requestStarted = Time.unscaledTime;
        Status("서버 확인 중...");
        if (!localData.RequestRoundMagicChange(battle.RoundNumber, first, second, pendingRequestId, resetInitial))
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
            // A later selection must not be closed by an earlier asynchronous response.
            if (closeDescriptionOnAccept && requestedDescriptionRevision == descriptionRevision)
                SetDescriptionVisible(false);
            Status("장착 완료. 이 경기에서만 적용됩니다.");
            BattleAudio.Play(BattleSound.MagicSelected, local: true);
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

    private string SlotText(int slot, MagicType magic)
    {
        string name = Name(magic);
        return showSlotNumberInText ? $"{(selectedSlot == slot ? "▶ " : "")}{slot}번 · {name}" : name;
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

    private void EnsureUIInput()
    {
        // Custom panels need input too. Do not tie EventSystem creation to generated UI.
        // Reuse scene-owned input and its configured actions without adding a second module.
        if (EventSystem.current != null) return;
        var root = new GameObject("Battle UI EventSystem", typeof(EventSystem));
        // Keep it outside panels which are hidden between phases, and unload it with this scene.
        root.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
        // OnEnable assigns/enables the package's default UI actions on a new module.
        root.AddComponent<InputSystemUIInputModule>();
#else
        root.AddComponent<StandaloneInputModule>();
#endif
    }

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
        EnsureUIInput();
        if (magicSwapPanel != null) { BindResetButton(); return; }
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
        resetMagicButton = Button(root, "준비 완료", new Vector2(-200, -330), new Vector2(280, 64), ToggleRoundReady);
        resetButtonBound = true;
        swapStatusText = Label(root, "Status", "", new Vector2(0, -405), new Vector2(1500, 60), 23);
    }

    private void EnsureResultPanel()
    {
        EnsureUIInput();
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
        readyRequestPending = false;
        SetSwapEditingLocked(false);
        SetDescriptionVisible(false);
        Show(magicSwapPanel, false);
        Show(finalResultPanel, false);
    }

    private void OnDestroy()
    {
        if (resetMagicButton != null) resetMagicButton.onClick.RemoveListener(ToggleRoundReady);
        if (equipButton != null) equipButton.onClick.RemoveListener(EquipSelectedMagic);
        if (returnToMainButton != null) returnToMainButton.onClick.RemoveListener(ReturnToMain);
    }

    private bool resetButtonBound;
    private void BindResetButton()
    {
        if (resetButtonBound || magicSwapPanel == null) return;
        if (resetMagicButton == null)
            foreach (Button button in magicSwapPanel.GetComponentsInChildren<Button>(true))
            {
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (button.name.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    button.name.IndexOf("init", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (label != null && label.text.Contains("초기화"))) { resetMagicButton = button; break; }
            }
        if (resetMagicButton == null) return;
        var readyBackground = resetMagicButton.GetComponent<Image>();
        if (readyBackground != null) resetMagicButton.targetGraphic = readyBackground;
        bool alreadyBound = false;
        for (int i = 0; i < resetMagicButton.onClick.GetPersistentEventCount(); i++)
        {
            if (resetMagicButton.onClick.GetPersistentTarget(i) == this &&
                resetMagicButton.onClick.GetPersistentMethodName(i) == nameof(ResetRoundChanges))
                resetMagicButton.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            if (resetMagicButton.onClick.GetPersistentTarget(i) == this &&
                resetMagicButton.onClick.GetPersistentMethodName(i) == nameof(ToggleRoundReady) &&
                resetMagicButton.onClick.GetPersistentListenerState(i) != UnityEventCallState.Off) alreadyBound = true;
        }
        resetMagicButton.onClick.RemoveListener(ResetRoundChanges);
        if (!alreadyBound) resetMagicButton.onClick.AddListener(ToggleRoundReady);
        resetButtonBound = true;
    }
}
