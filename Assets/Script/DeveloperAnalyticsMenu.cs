using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Scene navigation only. Log readers retain their existing local CSV/JSON workflow.
public sealed class DeveloperAnalyticsMenu : MonoBehaviour
{
    public const string MainScene = "Assets/Scenes/Main.unity";
    public const string MovementScene = "Assets/BattleLog.unity";
    public const string PickScene = "Assets/Scenes/MagicPick.unity";
    private static bool loading;
    private LoginFlowController lobby;
    private GameObject controls, choices;
    private Button toggle;
    private TMP_FontAsset font;
    private float nextRefresh;

    public static bool HasAccess => DatabaseManager.Instance != null &&
        DatabaseManager.Instance.IsDeveloper &&
        (LoginManager.Instance == null || !LoginManager.Instance.IsLoggingOut);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loading = false;
        SceneManager.sceneLoaded -= SceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Install(SceneManager.GetSceneAt(i));
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        loading = false;
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!scene.isLoaded || scene.path != MainScene) return;
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
            if (root.GetComponent<DeveloperAnalyticsMenu>() != null) return;
        var owner = new GameObject("DeveloperAnalyticsMenu");
        SceneManager.MoveGameObjectToScene(owner, scene);
        owner.AddComponent<DeveloperAnalyticsMenu>().Build(roots);
    }

    private void Build(GameObject[] roots)
    {
        // Resolve scene UI once, including inactive login/lobby objects.
        foreach (var root in roots)
        {
            if (lobby == null) lobby = root.GetComponentInChildren<LoginFlowController>(true);
            if (font == null)
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    if (label.font != null) { font = label.font; break; }
        }
        if (font == null) font = TMP_Settings.defaultFontAsset;
        var canvasObject = new GameObject("DeveloperCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var anchor = Rect("DeveloperOptions", canvas.transform, Vector2.zero, new Vector2(260, 56));
        anchor.anchorMin = anchor.anchorMax = new Vector2(1, 0);
        anchor.pivot = new Vector2(1, 0);
        anchor.anchoredPosition = new Vector2(-24, 24);
        controls = anchor.gameObject;
        toggle = MakeButton("DeveloperButton", "개발자 옵션", anchor, Vector2.zero, () =>
        {
            if (CanOpen()) choices.SetActive(!choices.activeSelf);
        });
        var panel = Rect("AnalyticsChoices", anchor, new Vector2(0, 132), new Vector2(260, 172));
        panel.gameObject.AddComponent<Image>().color = new Color(.06f, .09f, .14f, .96f);
        choices = panel.gameObject;
        MakeButton("MovementLogButton", "맵 로그 보기", panel, new Vector2(0, 36), OpenMovementLog);
        MakeButton("MagicPickButton", "마법 픽률 보기", panel, new Vector2(0, -36), OpenMagicPicks);
        choices.SetActive(false);
        controls.SetActive(false);
        Refresh();
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private Button MakeButton(string name, string text, Transform parent, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name, parent, position, new Vector2(244, 56));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(0x44, 0xDC, 0xCC, 255);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var label = Rect("Text", rect, Vector2.zero, new Vector2(236, 52)).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = 24;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(0x24, 0x24, 0x24, 255);
        label.raycastTarget = false;
        return button;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .25f;
        Refresh();
    }

    private void Refresh()
    {
        if (controls == null) return;
        bool visible = HasAccess && lobby != null && lobby.IsLobbyReady;
        if (controls.activeSelf != visible) controls.SetActive(visible);
        toggle.interactable = visible && CanOpen();
        if (!toggle.interactable) choices.SetActive(false);
    }

    private bool CanOpen() => HasAccess && !loading && lobby != null && lobby.IsLobbyReady &&
        SceneManager.GetActiveScene().path == MainScene &&
        (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsMatching);

    public void OpenMovementLog() { if (CanOpen()) Load(MovementScene); }
    public void OpenMagicPicks() { if (CanOpen()) Load(PickScene); }
    public static void ReturnToMain() => Load(MainScene);

    public static bool EnsureViewerAccess(MonoBehaviour viewer)
    {
        if (HasAccess) return true;
        viewer.enabled = false;
        ReturnToMain();
        return false;
    }

    private static void Load(string path)
    {
        if (loading) return;
        if (!Application.CanStreamedLevelBeLoaded(path))
        {
            Debug.LogError("분석 메뉴 씬을 Build Settings에 추가해주세요: " + path);
            return;
        }
        loading = true;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        try
        {
            if (SceneManager.LoadSceneAsync(path, LoadSceneMode.Single) == null) loading = false;
        }
        catch (Exception exception) { loading = false; Debug.LogException(exception); }
    }
}
