using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

// Keep this controller on the always-active NetworkGameManager, outside Matching Panel.
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class RandomMatchUI : MonoBehaviour
{
    [Header("Custom UI (empty fields get a generated fallback)")]
    [Tooltip("랜덤 매칭 중에만 표시할 패널. NetworkGameManager 자신이나 그 부모는 넣지 마세요.")]
    [SerializeField] private GameObject matchingPanel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image localProfileImage;
    [SerializeField] private Image opponentProfileImage;
    [SerializeField] private TMP_Text localNameText;
    [SerializeField] private TMP_Text opponentNameText;
    [Header("2v2: additional portraits (empty = generated)")]
    [SerializeField] private Image teammateProfileImage;
    [SerializeField] private TMP_Text teammateNameText;
    [SerializeField] private Image secondOpponentProfileImage;
    [SerializeField] private TMP_Text secondOpponentNameText;
    [Tooltip("OnClick에 별도로 함수를 넣지 않아도 자동 연결합니다. 기존 이벤트는 유지합니다.")]
    [SerializeField] private Button cancelButton;
    [Tooltip("선택: 매칭 중 표시하고 매칭 완료 시 숨길 로딩 오브젝트")]
    [SerializeField] private GameObject loadingObject;
    [Tooltip("선택: 회전시킬 로딩 이미지. 직접 애니메이션을 사용한다면 비워두세요.")]
    [SerializeField] private RectTransform loadingSpinner;

    [Header("Shared profile data / generated UI font")]
    [SerializeField] private ProfileImageTable profileTable;
    [SerializeField] private TMP_FontAsset uiFont;

    private NetworkGameManager manager;
    private Canvas generatedCanvas;
    private GameObject generatedInput;
    private Button boundCancelButton;
    private Sprite missingProfileSprite;
    private bool showing;
    private float nextSnapshot;
    private PlayerData localData, opponentData, teammateData, secondOpponentData;
    private readonly List<PlayerData> participants = new();
    private NetworkRunner snapshotRunner, previewRunner;
    private PlayerData previewLocal;
    private string previewRosterKey;
    private float previewStartedAt = -1f;
    private bool previewConfirmed;
    private readonly GameObject[] participantCards = new GameObject[4];
    private readonly List<GameObject> activatedParents = new();
    private static readonly Color Teal = new Color(0.22f, 0.86f, 0.83f);

    private void Awake()
    {
        manager = GetComponent<NetworkGameManager>();
        ValidatePanel();
        Show(matchingPanel, false);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start() => PrepareIdlePanel();

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PrepareIdlePanel();

    private void PrepareIdlePanel()
    {
        if (manager != null && manager.IsRandomMatch && manager.IsMatching) return;
        // Bind before the lobby's Battle button opens Multi, even when the panel
        // was accidentally saved active. Rebind after returning from Battle too.
        ValidatePanel();
        BindAuthoredPanel();
        Show(matchingPanel, false);
    }

    private void Update()
    {
        if (manager == null) manager = GetComponent<NetworkGameManager>();
        bool wanted = manager != null && manager.IsRandomMatch && manager.IsMatching && !manager.IsBattleSceneLoaded;
        if (!wanted)
        {
            if (showing) Hide();
            return;
        }

        if (!showing || matchingPanel == null)
        {
            EnsureUI();
            showing = true;
            nextSnapshot = 0f;
            ShowAuthoredPanel();
        }
        Show(matchingPanel, true);
        // Mouse input remains available in the lobby, including during connection startup.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Time.unscaledTime >= nextSnapshot || snapshotRunner != manager.Runner)
        {
            nextSnapshot = Time.unscaledTime + 0.1f;
            RefreshSnapshot();
        }

        bool ready = IsCurrentParticipant(localData) && manager.ConnectedPlayerCount == manager.RequiredPlayerCount &&
            BattleMatchRules.IsFullBalanced(participants, manager.RequiredPlayerCount);
        foreach (PlayerData data in participants) ready &= IsCurrentParticipant(data);
        bool cancelling = manager.IsCancellingMatch;
        // Keep the last completed presentation while Fusion loads Battle.
        if (!manager.IsBattleTransitioning)
        {
            SetPortrait(localProfileImage, ResolveProfile(localData != null && IsCurrentParticipant(localData) &&
                localData.IsLoadoutInitialized ? localData.playerprofile : DataConfig.playerprofile));
            SetText(localNameText, IsCurrentParticipant(localData) && localData.IsLoadoutInitialized
                ? localData.playerName.ToString() : DataConfig.playerName);
            ShowParticipant(teammateData, teammateProfileImage, teammateNameText, "팀원을 기다리는 중");
            ShowParticipant(opponentData, opponentProfileImage, opponentNameText, "상대를 기다리는 중");
            ShowParticipant(secondOpponentData, secondOpponentProfileImage, secondOpponentNameText, "상대를 기다리는 중");
            Show(participantCards[0], true);
            Show(participantCards[1], IsCurrentParticipant(teammateData) && teammateData.IsLoadoutInitialized);
            Show(participantCards[2], IsCurrentParticipant(opponentData) && opponentData.IsLoadoutInitialized);
            Show(participantCards[3], IsCurrentParticipant(secondOpponentData) && secondOpponentData.IsLoadoutInitialized);
        }
        SetText(statusText, cancelling ? "매칭 취소 중..." : ready || manager.IsBattleTransitioning ? "매칭 완료!" : "매칭중");
        Show(loadingObject, !ready && !manager.IsBattleTransitioning);
        if (loadingSpinner != null && loadingSpinner.gameObject.activeInHierarchy)
            loadingSpinner.Rotate(0f, 0f, -180f * Time.unscaledDeltaTime);
        if (cancelButton != null) cancelButton.interactable = manager.CanCancelMatch;
        if (!manager.IsBattleTransitioning) UpdatePreview(ready && !cancelling);
    }

    private bool IsCurrentParticipant(PlayerData data)
    {
        if (data == null || manager.Runner == null || !manager.Runner.IsRunning ||
            data.Object == null || !data.Object.IsValid || data.Runner != manager.Runner) return false;
        foreach (PlayerRef player in manager.Runner.ActivePlayers)
            if (player == data.ParticipantRef) return true;
        return false;
    }

    private void RefreshSnapshot()
    {
        snapshotRunner = manager.Runner;
        localData = opponentData = teammateData = secondOpponentData = null;
        BattleMatchRules.CollectRoster(snapshotRunner, participants);
        foreach (PlayerData data in participants)
            if (data.Object.HasInputAuthority) localData = data;
        if (localData == null) return;
        foreach (PlayerData data in participants)
        {
            if (data == localData) continue;
            if (data.teamIndex == localData.teamIndex) teammateData = data;
            else if (opponentData == null) opponentData = data;
            else secondOpponentData = data;
        }
    }

    private void ShowParticipant(PlayerData data, Image image, TMP_Text label, string emptyText)
    {
        bool ready = IsCurrentParticipant(data) && data.IsLoadoutInitialized;
        SetPortrait(image, ready ? ResolveProfile(data.playerprofile) : null);
        SetText(label, ready ? data.playerName.ToString() : emptyText);
    }

    private string RosterKey()
    {
        string key = "";
        foreach (PlayerData data in participants)
            key += $"/{data.Object.Id}:{data.playerprofile}:{data.teamIndex}:{data.playerName}";
        return key;
    }

    private void UpdatePreview(bool ready)
    {
        if (!ready || !IsPortraitVisible(localProfileImage, ResolveProfile(localData.playerprofile)) ||
            !IsPortraitVisible(opponentProfileImage, ResolveProfile(opponentData.playerprofile)) ||
            !IsCurrentParticipant(teammateData) || !IsCurrentParticipant(secondOpponentData) ||
            !IsPortraitVisible(teammateProfileImage, ResolveProfile(teammateData.playerprofile)) ||
            !IsPortraitVisible(secondOpponentProfileImage, ResolveProfile(secondOpponentData.playerprofile)))
        {
            ResetPreview();
            return;
        }
        string rosterKey = RosterKey();
        if (previewRunner != manager.Runner || previewLocal != localData || previewRosterKey != rosterKey)
        {
            ResetPreview();
            previewRunner = manager.Runner;
            previewLocal = localData;
            previewRosterKey = rosterKey;
            previewStartedAt = Time.unscaledTime;
            BattleAudio.Play(BattleSound.MatchFound, local: true);
        }
        if (!previewConfirmed && Time.unscaledTime - previewStartedAt >= 1f)
        {
            previewConfirmed = true;
            foreach (PlayerData data in participants)
                if (data != localData) localData.ConfirmLobbyProfilePreview(data);
        }
    }

    private void ResetPreview()
    {
        if (previewConfirmed && previewLocal != null && previewLocal.Object != null && previewLocal.Object.IsValid &&
            previewLocal.Runner != null && previewLocal.Runner.IsRunning)
            previewLocal.ClearLobbyProfilePreview();
        previewConfirmed = false;
        previewStartedAt = -1f;
        previewRunner = null;
        previewLocal = null;
        previewRosterKey = null;
    }

    private static bool IsPortraitVisible(Image image, Sprite expected)
    {
        if (image == null || expected == null || image.sprite != expected ||
            !image.isActiveAndEnabled || image.color.a <= 0.01f) return false;
        Canvas canvas = image.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled) return false;
        foreach (CanvasGroup group in image.GetComponentsInParent<CanvasGroup>())
            if (group.isActiveAndEnabled && group.alpha <= 0.01f) return false;
        return true;
    }

    // Optional manual OnClick entry point. Assigned Cancel Button is also auto-bound.
    public void CancelMatch() => manager?.CancelMatch();

    private void Hide()
    {
        ResetPreview();
        Show(matchingPanel, false);
        foreach (GameObject parent in activatedParents) Show(parent, false);
        activatedParents.Clear();
        if (generatedInput != null) Show(generatedInput, false);
        showing = false;
        localData = opponentData = teammateData = secondOpponentData = null;
        participants.Clear();
        snapshotRunner = null;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (boundCancelButton != null) boundCancelButton.onClick.RemoveListener(CancelMatch);
        if (generatedCanvas != null) Destroy(generatedCanvas.gameObject);
        if (generatedInput != null) Destroy(generatedInput);
        if (missingProfileSprite != null) Destroy(missingProfileSprite);
    }

    private Sprite ResolveProfile(int index)
    {
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        Sprite sprite = profileTable != null ? profileTable.GetSprite(index) : null;
        if (sprite != null) return sprite;
        // A missing optional art table must not leave matchmaking permanently blocked.
        if (missingProfileSprite == null)
            missingProfileSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        return missingProfileSprite;
    }

    private void ValidatePanel()
    {
        if (matchingPanel == null || !transform.IsChildOf(matchingPanel.transform)) return;
        Debug.LogWarning("RandomMatchUI: Matching Panel에는 컨트롤러가 아닌 별도 UI 패널을 넣어주세요. 기본 UI로 대체합니다.", this);
        matchingPanel = null;
    }

    private void BindAuthoredPanel()
    {
        bool generated = matchingPanel != null && generatedCanvas != null &&
            matchingPanel.transform.IsChildOf(generatedCanvas.transform);
        if (matchingPanel == null || generated)
        {
            Transform best = null;
            int bestScore = -1;
            // Only on opening/rebinding, including inactive scene UI. Never search each frame.
            foreach (RectTransform candidate in FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.name != "MatchingWaitingUI" || !candidate.gameObject.scene.isLoaded) continue;
                Transform multi = candidate.parent;
                while (multi != null && multi.name != "MultiUI" && multi.name != "Multi") multi = multi.parent;
                if (multi == null) continue;
                int count = MatchingCards(candidate).Count;
                if (count < 4) continue; // Old two-card copies must not override the 2v2 panel.
                int score = (multi.name == "MultiUI" ? 10 : 0) + (multi.gameObject.activeInHierarchy ? 1 : 0);
                if (score > bestScore) { best = candidate; bestScore = score; }
            }
            if (best == null) return;
            if (generated) Show(matchingPanel, false);
            matchingPanel = best.gameObject;
            statusText = null;
            cancelButton = null;
            loadingObject = null;
            loadingSpinner = null;
        }
        Transform panel = matchingPanel.transform;
        var cards = MatchingCards(panel);
        if (cards.Count < 4) return; // Manually assigned layouts still use the existing Inspector fields.
        // Old waiting-room bridges on this panel would overwrite the matchmaking roster.
        foreach (linkuserinfo bridge in panel.GetComponentsInChildren<linkuserinfo>(true)) bridge.enabled = false;
        Transform dialog = panel.Find("DIalogText") ?? panel.Find("DialogText");
        if (dialog != null) statusText = dialog.GetComponent<TMP_Text>();
        for (int i = 0; i < 4; i++) participantCards[i] = cards[i].gameObject;
        localProfileImage = cards[0].Find("Profile").GetComponent<Image>();
        teammateProfileImage = cards[1].Find("Profile").GetComponent<Image>();
        opponentProfileImage = cards[2].Find("Profile").GetComponent<Image>();
        secondOpponentProfileImage = cards[3].Find("Profile").GetComponent<Image>();
        localNameText = cards[0].Find("Text").GetComponent<TMP_Text>();
        teammateNameText = cards[1].Find("Text").GetComponent<TMP_Text>();
        opponentNameText = cards[2].Find("Text").GetComponent<TMP_Text>();
        secondOpponentNameText = cards[3].Find("Text").GetComponent<TMP_Text>();
        foreach (Transform card in cards) card.Find("Text").GetComponent<TMP_Text>().richText = false;
        Transform loading = panel.Find("LoadingImage");
        if (loading != null) loadingObject = loading.gameObject;
        Transform cancel = panel.Find("CancelButton") ?? panel.Find("Cancel Match");
        if (cancel != null) cancelButton = cancel.GetComponent<Button>();
    }

    internal static List<Transform> MatchingCards(Transform panel)
    {
        var cards = new List<Transform>();
        foreach (Transform child in panel)
            if (child.name.StartsWith("Card", System.StringComparison.OrdinalIgnoreCase) &&
                child.Find("Profile")?.GetComponent<Image>() != null && child.Find("Text")?.GetComponent<TMP_Text>() != null)
                cards.Add(child);
        // Card numbers in the authored asset are not in visual order.
        cards.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));
        return cards;
    }

    private void ShowAuthoredPanel()
    {
        if (matchingPanel == null) return;
        activatedParents.Clear();
        for (Transform parent = matchingPanel.transform.parent; parent != null; parent = parent.parent)
            if (!parent.gameObject.activeSelf) activatedParents.Add(parent.gameObject);
        for (int i = activatedParents.Count - 1; i >= 0; i--)
        {
            Show(activatedParents[i], true);
            foreach (DOTweenAnimation animation in activatedParents[i].GetComponents<DOTweenAnimation>()) ReplayTween(animation);
        }
        Show(matchingPanel, true);
        foreach (DOTweenAnimation animation in matchingPanel.GetComponentsInChildren<DOTweenAnimation>(true)) ReplayTween(animation);
    }

    private static void ReplayTween(DOTweenAnimation animation)
    {
        if (animation == null || !animation.isActiveAndEnabled || !animation.isActive || !animation.isValid) return;
        animation.CreateTween(false, false);
        if (animation.tween != null && animation.tween.IsActive())
            animation.tween.SetAutoKill(false).SetUpdate(true).Restart();
    }

    private void EnsureUI()
    {
        ValidatePanel();
        BindAuthoredPanel();
        if (matchingPanel == null)
        {
            if (generatedCanvas == null)
            {
                var root = new GameObject("Generated Random Match UI", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                root.transform.SetParent(transform, false);
                generatedCanvas = root.GetComponent<Canvas>();
                generatedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                generatedCanvas.sortingOrder = 300;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
            }
            Image panel = NewImage("Matching Panel", generatedCanvas.transform, Vector2.zero, Vector2.zero,
                new Color(0.025f, 0.04f, 0.085f, 0.97f));
            panel.raycastTarget = true;
            panel.rectTransform.anchorMin = Vector2.zero;
            panel.rectTransform.anchorMax = Vector2.one;
            matchingPanel = panel.gameObject;
            NewText("Heading", panel.transform, new Vector2(0, 325), new Vector2(800, 70), "2 vs 2 RANDOM MATCH", 30, Teal);
            NewText("Versus", panel.transform, Vector2.zero, new Vector2(160, 90), "VS", 48, Teal);
        }
        Transform parent = matchingPanel.transform;
        if (statusText == null) statusText = NewText("Status", parent, new Vector2(0, 225), new Vector2(900, 80), "매칭중", 48, Color.white);
        if (localProfileImage == null) localProfileImage = NewImage("My Profile", parent, new Vector2(-520, 0), new Vector2(190, 190), Color.white);
        if (opponentProfileImage == null) opponentProfileImage = NewImage("Opponent Profile", parent, new Vector2(270, 0), new Vector2(190, 190), Color.white);
        if (localNameText == null) localNameText = NewText("My Name", parent, new Vector2(-520, -135), new Vector2(240, 65), "", 24, Teal);
        if (opponentNameText == null) opponentNameText = NewText("Opponent Name", parent, new Vector2(270, -135), new Vector2(240, 65), "", 24, Color.white);
        if (teammateProfileImage == null) teammateProfileImage = NewImage("Teammate Profile", parent, new Vector2(-270, 0), new Vector2(190, 190), Color.white);
        if (secondOpponentProfileImage == null) secondOpponentProfileImage = NewImage("Second Opponent Profile", parent, new Vector2(520, 0), new Vector2(190, 190), Color.white);
        if (teammateNameText == null) teammateNameText = NewText("Teammate Name", parent, new Vector2(-270, -135), new Vector2(240,65), "", 24, Teal);
        if (secondOpponentNameText == null) secondOpponentNameText = NewText("Second Opponent Name", parent, new Vector2(520, -135), new Vector2(240,65), "", 24, Color.white);
        if (cancelButton == null)
        {
            Image background = NewImage("Cancel Match", parent, new Vector2(0, -350), new Vector2(300, 75), Teal);
            background.raycastTarget = true;
            cancelButton = background.gameObject.AddComponent<Button>();
            cancelButton.targetGraphic = background;
            NewText("Label", background.transform, Vector2.zero, new Vector2(280, 70), "매칭 취소", 29, new Color(0.04f, 0.12f, 0.16f));
        }
        if (boundCancelButton != cancelButton)
        {
            if (boundCancelButton != null) boundCancelButton.onClick.RemoveListener(CancelMatch);
            boundCancelButton = cancelButton;
            boundCancelButton.onClick.AddListener(CancelMatch);
        }
        if (loadingObject == null)
        {
            if (loadingSpinner != null) loadingObject = loadingSpinner.gameObject;
            else
            {
                loadingSpinner = NewRect("Loading Spinner", parent, new Vector2(0, -235), new Vector2(64, 64));
                loadingObject = loadingSpinner.gameObject;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Image bar = NewImage("Glow " + i, loadingSpinner, new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 22f,
                        new Vector2(5, 13), new Color(Teal.r, Teal.g, Teal.b, 0.2f + i * 0.1f));
                    bar.rectTransform.localRotation = Quaternion.Euler(0, 0, -i * 45f);
                }
            }
        }
        if (EventSystem.current == null)
        {
            if (generatedInput == null)
            {
                generatedInput = new GameObject("Random Match EventSystem", typeof(EventSystem));
                generatedInput.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
                generatedInput.AddComponent<InputSystemUIInputModule>();
#else
                generatedInput.AddComponent<StandaloneInputModule>();
#endif
            }
            Show(generatedInput, true);
        }
    }

    private static RectTransform NewRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image NewImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        Image image = NewRect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private TMP_Text NewText(string name, Transform parent, Vector2 position, Vector2 size, string value, float fontSize, Color color)
    {
        var text = NewRect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) text.font = uiFont;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.richText = false;
        text.text = value;
        return text;
    }

    private static void SetPortrait(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
    private static void SetText(TMP_Text text, string value) { if (text != null && text.text != value) text.text = value; }
    private static void Show(GameObject obj, bool visible) { if (obj != null && obj.activeSelf != visible) obj.SetActive(visible); }
}
