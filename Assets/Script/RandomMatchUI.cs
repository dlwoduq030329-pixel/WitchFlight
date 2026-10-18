using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
    private PlayerData localData, opponentData;
    private NetworkRunner snapshotRunner, previewRunner;
    private PlayerData previewLocal, previewOpponent;
    private int previewOwnProfile, previewOpponentProfile;
    private float previewStartedAt = -1f;
    private bool previewConfirmed;
    private static readonly Color Teal = new Color(0.22f, 0.86f, 0.83f);

    private void Awake()
    {
        manager = GetComponent<NetworkGameManager>();
        ValidatePanel();
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

        bool ready = IsCurrentParticipant(localData) && IsCurrentParticipant(opponentData) &&
            localData.IsLoadoutInitialized && opponentData.IsLoadoutInitialized &&
            localData.teamIndex > 0 && opponentData.teamIndex > 0 && localData.teamIndex != opponentData.teamIndex;
        bool cancelling = manager.IsCancellingMatch;
        // Keep the last completed presentation while Fusion loads Battle.
        if (!manager.IsBattleTransitioning)
        {
            SetPortrait(localProfileImage, ResolveProfile(localData != null && IsCurrentParticipant(localData) &&
                localData.IsLoadoutInitialized ? localData.playerprofile : DataConfig.playerprofile));
            SetText(localNameText, IsCurrentParticipant(localData) && localData.IsLoadoutInitialized
                ? localData.playerName.ToString() : DataConfig.playerName);
            SetPortrait(opponentProfileImage, ready ? ResolveProfile(opponentData.playerprofile) : null);
            SetText(opponentNameText, ready ? opponentData.playerName.ToString() : "상대를 찾고 있어요");
        }
        SetText(statusText, cancelling ? "매칭 취소 중..." : ready || manager.IsBattleTransitioning ? "매칭 완료" : "매칭 중");
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
            if (player == data.Object.InputAuthority) return true;
        return false;
    }

    private void RefreshSnapshot()
    {
        snapshotRunner = manager.Runner;
        localData = null;
        opponentData = null;
        if (snapshotRunner == null || !snapshotRunner.IsRunning) return;
        foreach (PlayerData data in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
        {
            if (!IsCurrentParticipant(data)) continue;
            if (data.Object.HasInputAuthority) localData = data;
            else opponentData = data;
        }
    }

    private void UpdatePreview(bool ready)
    {
        if (!ready || !IsPortraitVisible(localProfileImage, ResolveProfile(localData.playerprofile)) ||
            !IsPortraitVisible(opponentProfileImage, ResolveProfile(opponentData.playerprofile)))
        {
            ResetPreview();
            return;
        }
        if (previewRunner != manager.Runner || previewLocal != localData || previewOpponent != opponentData ||
            previewOwnProfile != localData.playerprofile || previewOpponentProfile != opponentData.playerprofile)
        {
            ResetPreview();
            previewRunner = manager.Runner;
            previewLocal = localData;
            previewOpponent = opponentData;
            previewOwnProfile = localData.playerprofile;
            previewOpponentProfile = opponentData.playerprofile;
            previewStartedAt = Time.unscaledTime;
        }
        if (!previewConfirmed && Time.unscaledTime - previewStartedAt >= 1f)
        {
            previewConfirmed = true;
            localData.ConfirmLobbyProfilePreview(opponentData);
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
        previewOpponent = null;
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
        if (generatedInput != null) Show(generatedInput, false);
        showing = false;
        localData = opponentData = null;
        snapshotRunner = null;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
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

    private void EnsureUI()
    {
        ValidatePanel();
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
            NewText("Heading", panel.transform, new Vector2(0, 325), new Vector2(800, 70), "RANDOM MATCH", 30, Teal);
            NewText("Versus", panel.transform, Vector2.zero, new Vector2(160, 90), "VS", 48, Teal);
        }
        Transform parent = matchingPanel.transform;
        if (statusText == null) statusText = NewText("Status", parent, new Vector2(0, 225), new Vector2(900, 80), "매칭 중", 48, Color.white);
        if (localProfileImage == null) localProfileImage = NewImage("My Profile", parent, new Vector2(-320, 0), new Vector2(210, 210), Color.white);
        if (opponentProfileImage == null) opponentProfileImage = NewImage("Opponent Profile", parent, new Vector2(320, 0), new Vector2(210, 210), Color.white);
        if (localNameText == null) localNameText = NewText("My Name", parent, new Vector2(-320, -150), new Vector2(500, 65), "", 27, Teal);
        if (opponentNameText == null) opponentNameText = NewText("Opponent Name", parent, new Vector2(320, -150), new Vector2(500, 65), "", 27, Color.white);
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
