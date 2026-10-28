using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;

[Serializable]
public class CatTutorialButtonBinding { public CatTutorialTarget target; public Button button; }

[DefaultExecutionOrder(500)]
public sealed partial class CatTutorialDirector : MonoBehaviour
{
    [SerializeField] private CatCompanionTable table;
    [SerializeField] private GameObject tutorialUI;
    [SerializeField] private RawImage catImageRaw;
    [SerializeField] private TMP_Text explanationText;
    [SerializeField] private Transform lobbyCharacter;
    [SerializeField] private Camera lobbyCamera;
    [SerializeField] private CatTutorialButtonBinding[] buttons;
    private Canvas canvas;
    private CatAvatar avatar;
    private GameObject stage;
    private Camera portraitCamera;
    private RenderTexture portrait;
    private Button next;
    private TMP_Text nextHint;
    private Image[] blockers, border;
    private RectTransform highlight;
    private TMP_Text arrow, nameText;
    private readonly Dictionary<CatTutorialTarget, Button> targets = new();
    private Button subscribed, replayButton;
    private Transform lobbyUI;
    private Transform customizationUI;
    [SerializeField] private RawImage lobbySurface;
    private readonly Vector3[] surfaceCorners = new Vector3[4], previousCorners = new Vector3[4];
    private bool sampledSurface;
    private float surfaceStableAt, revealStarted = -1;
    private int step, body, eyes;
    private bool running, ready, advanceQueued, pendingFirstTutorial;
    private float showAt, clickAfter, nextHighlightSound, nextPetAt;
    private bool petting;
    private Vector2 petPointer;
    private float petOrbitPause, petStrokeTravel;
    private int petStrokeDirection, lastPetStroke;
    private readonly Queue<float> recentPetStrokes = new();
    private float nextYawnAt, petAllowedAt, scratchEndsAt;
    private string account;
    private AudioSource audioSource;
    private BattleAudio.Loop petPurr;
    private bool petPurrPlaying;
    private float petPurrUntil;
    private CanvasGroup greetingGroup;
    private bool greeting, greetingClosing;
    private float greetingAt, greetingCloseAt;
    private Vector2 greetingPosition;
    private CatGreetingKind? pendingGreeting;
    private static readonly HashSet<string> greetedAccounts = new();
    private static string resultAccount;
    private static CatGreetingKind? resultGreeting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGreetings() { greetedAccounts.Clear(); resultAccount = null; resultGreeting = null; }

    public static void QueueMatchGreeting(bool won)
    {
        var db = DatabaseManager.Instance;
        if (db == null || string.IsNullOrEmpty(db.LoadedAccountId)) return;
        resultAccount = db.LoadedAccountId;
        resultGreeting = won ? CatGreetingKind.Victory : CatGreetingKind.Defeat;
    }
    private readonly List<RaycastResult> uiHits = new();
    private bool trainingScene;
    private CatTutorialStep[] Steps => trainingScene ? table.trainingTutorial : table.tutorial;
    // Local, account-scoped completion: no new mandatory BACKND column for existing accounts.
    private static string TrainingKey => "WitchFlight.CatTraining.v1." + DatabaseManager.Instance.LoadedAccountId;
    public static bool TrainingGuideRequired => DatabaseManager.Instance != null &&
        !string.IsNullOrEmpty(DatabaseManager.Instance.LoadedAccountId) &&
        CatCompanionTable.Default != null && CatCompanionTable.Default.trainingTutorial != null &&
        CatCompanionTable.Default.trainingTutorial.Length > 0 && PlayerPrefs.GetInt(TrainingKey, 0) != 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || (scene.name != "Battle" && scene.name != "Main" && scene.buildIndex != 0) || CatCompanionTable.Default == null) return;
        foreach (var director in FindObjectsByType<CatTutorialDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (director.gameObject.scene == scene) return;
        var host = new GameObject("WitchCat Tutorial & Companion");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<CatTutorialDirector>();
    }
    private Transform Find(string rootName, string child = null)
    {
        foreach (var tr in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.gameObject.scene == gameObject.scene && tr.name == rootName)
            {
                if (string.IsNullOrEmpty(child)) return tr;
                var match = tr.Find(child); if (match != null) return match;
            }
        return null;
    }
    private void Bind(CatTutorialTarget target, string root, string path)
    {
        if (targets.ContainsKey(target)) return;
        var tr = Find(root, path);
        if (tr != null && tr.TryGetComponent(out Button button)) targets[target] = button;
    }
    private void Start()
    {
        trainingScene = gameObject.scene.name == "Battle";
        if (table == null) table = CatCompanionTable.Default;
        if (table == null) { enabled = false; return; }
        if (buttons != null) foreach (var binding in buttons)
            if (binding.button != null) targets[binding.target] = binding.button;
        Bind(CatTutorialTarget.Wardrobe, "RobbyUI", "UI/UnderButtons/CustumButton");
        Bind(CatTutorialTarget.WeaponTab, "CustumUI", "Tab/Weapon");
        Bind(CatTutorialTarget.CustomBack, "CustumUI", "Theme/BackButton");
        Bind(CatTutorialTarget.BattleMenu, "RobbyUI", "UI/UnderButtons/BattleButton");
        Bind(CatTutorialTarget.Training, "Multi", "RensuButton");
        Bind(CatTutorialTarget.CreateRoom, "Multi", "Button_MakeRoom");
        Bind(CatTutorialTarget.RandomMatch, "Multi", "Button_RandomMatch");
        Bind(CatTutorialTarget.BattleBack, "Multi", "Theme/BackButton");
        var bindings = new List<CatTutorialButtonBinding>();
        foreach (var item in targets) bindings.Add(new CatTutorialButtonBinding { target = item.Key, button = item.Value });
        buttons = bindings.ToArray();
        if (lobbyCamera == null) lobbyCamera = Find("RobbyCamera01")?.GetComponent<Camera>();
        if (lobbyCharacter == null) lobbyCharacter = Find("Robby", "ChPrefab");
        if (lobbyCharacter == null) lobbyCharacter = Find("ChPrefab");
        lobbyUI = Find("RobbyUI");
        customizationUI = Find("CustumUI");
        BuildUI();
        replayButton = Find("Multi", "TutorialButton")?.GetComponent<Button>();
        if (replayButton != null) replayButton.onClick.AddListener(ReplayTutorial);
        ready = enabled && canvas != null && tutorialUI != null && explanationText != null && catImageRaw != null;
        if (!ready) Debug.LogError("WitchCat: Tutorial의 CatImageRaw와 ExplanationText 연결을 확인해주세요.", this);
    }

    private void BuildUI()
    {
        var go = new GameObject("Cat tutorial overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32600;
        var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        blockers = new Image[4];
        for (int i = 0; i < blockers.Length; i++) blockers[i] = Image("Tutorial input shield " + i, go.transform, new Color(0, 0, 0, .12f), true);
        highlight = Image("Button highlight", go.transform, Color.clear, false).rectTransform;
        border = new Image[4];
        for (int i = 0; i < border.Length; i++) border[i] = Image("Glow edge " + i, highlight, table.highlightColor, false);
        Edge(border[0].rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 4));
        Edge(border[1].rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 4));
        Edge(border[2].rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(4, 0));
        Edge(border[3].rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(4, 0));
        arrow = Label("Guide arrow", highlight, "▼", 38);
        Edge(arrow.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(64, 64));
        arrow.rectTransform.anchoredPosition = new Vector2(0, 40);
        arrow.color = table.highlightColor;
        if (tutorialUI == null) tutorialUI = Find("Tutorial")?.gameObject;
        if (tutorialUI == null && table.tutorialPrefab != null) tutorialUI = Instantiate(table.tutorialPrefab);
        if (tutorialUI == null) { Debug.LogError("Cat tutorial needs Tutorial.prefab", this); enabled = false; return; }
        tutorialUI.transform.SetParent(go.transform, false);
        var rt = tutorialUI.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero; rt.anchoredPosition = new Vector2(20, 12);
        foreach (var image in tutorialUI.GetComponentsInChildren<RawImage>(true)) if (image.name == "CatImageRaw") catImageRaw = image;
        foreach (var label in tutorialUI.GetComponentsInChildren<TMP_Text>(true))
        { if (label.name == "ExplanationText") explanationText = label; if (label.name == "NameText") nameText = label; }
        if (explanationText != null)
        {
            // Authored fitter expands both axes; long personality lines need bounded wrapping.
            var fitter = explanationText.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            explanationText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 155);
            explanationText.enableAutoSizing = true; explanationText.fontSizeMin = 22; explanationText.fontSizeMax = 36;
        }
        // The authored static CatImage is replaced by the live portrait.
        foreach (var image in tutorialUI.GetComponentsInChildren<Image>(true)) if (image.name == "CatImage") image.enabled = false;
        foreach (var graphic in tutorialUI.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        if (catImageRaw != null) catImageRaw.gameObject.SetActive(true);
        // Make the authored speech bubble clickable; do not hide it on guided-button steps.
        var bubble = explanationText != null ? explanationText.transform.parent : tutorialUI.transform;
        var bubbleGraphic = bubble.GetComponent<Image>();
        if (bubbleGraphic == null) { bubbleGraphic = bubble.gameObject.AddComponent<Image>(); bubbleGraphic.color = Color.clear; }
        bubbleGraphic.raycastTarget = true;
        next = bubble.GetComponent<Button>();
        if (next == null) next = bubble.gameObject.AddComponent<Button>();
        next.targetGraphic = bubbleGraphic;
        next.transition = Selectable.Transition.None;
        next.navigation = new Navigation { mode = Navigation.Mode.None };
        nextHint = Label("Click to continue", bubble, "클릭하여 다음", 18);
        nextHint.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        nextHint.color = new Color(.18f, .22f, .24f, .8f);
        nextHint.alignment = TextAlignmentOptions.Right;
        Edge(nextHint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(180, 26));
        nextHint.rectTransform.pivot = new Vector2(1, 0);
        nextHint.rectTransform.anchoredPosition = new Vector2(-25, 12);
        if (explanationText != null) nextHint.font = explanationText.font;
        next.onClick.AddListener(Next);
        audioSource = go.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
        audioSource.outputAudioMixerGroup = GameAudioSettings.Group(GameAudioCategory.SFX);
        canvas.gameObject.SetActive(false);
    }

    private void EnsureAvatar()
    {
        if (avatar != null) { avatar.SetAppearance(body, eyes); return; }
        GameObject existing = null;
        if (!trainingScene) foreach (var cat in FindObjectsByType<WitchCatAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cat.gameObject.scene == gameObject.scene) { existing = cat.gameObject; break; }
        avatar = CatAvatar.Create(null, body, eyes, true, existing);
    }
    private void BuildPortrait()
    {
        if (stage != null) return;
        stage = new GameObject("WitchCat portrait stage (outside map)"); stage.transform.SetParent(transform, false);
        stage.transform.position = table.portraitStagePosition;
        var cameraObject = new GameObject("WitchCat portrait camera", typeof(Camera));
        cameraObject.transform.SetParent(stage.transform, false);
        cameraObject.transform.localPosition = table.portraitCameraOffset;
        cameraObject.transform.LookAt(stage.transform.position + Vector3.up * (table.catHeight * .5f));
        portraitCamera = cameraObject.GetComponent<Camera>(); portraitCamera.orthographic = true;
        portraitCamera.orthographicSize = table.portraitCameraSize; portraitCamera.nearClipPlane = .01f; portraitCamera.farClipPlane = 10;
        portraitCamera.cullingMask = 1 << 31; portraitCamera.clearFlags = CameraClearFlags.SolidColor;
        portraitCamera.backgroundColor = Color.clear; portraitCamera.allowHDR = false; portraitCamera.allowMSAA = true;
        portrait = new RenderTexture(table.portraitResolution, table.portraitResolution, 24, RenderTextureFormat.ARGB32) { name = "WitchCat Tutorial Portrait" };
        portrait.Create(); portraitCamera.targetTexture = portrait;
        var lightObject = new GameObject("Portrait light", typeof(Light)); lightObject.transform.SetParent(stage.transform, false);
        lightObject.transform.localRotation = Quaternion.Euler(35, -30, 0);
        var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.cullingMask = 1 << 31;
        if (catImageRaw != null) catImageRaw.texture = portrait;
    }
    public void ReplayTutorial()
    {
        if (!ready || DatabaseManager.Instance == null || !DatabaseManager.Instance.TryGetCatAppearance(out body, out eyes)) return;
        if (greeting) StopTutorial();
        pendingGreeting = null;
        nextHint.text = "클릭하여 다음";
        EnsureAvatar(); if (avatar == null || explanationText == null || catImageRaw == null) return;
        // Replay is offered in Multi; return to the lobby before its wardrobe step.
        if (targets.TryGetValue(CatTutorialTarget.BattleBack, out var back) && back != null && back.gameObject.activeInHierarchy)
            back.onClick.Invoke();
        pendingFirstTutorial = false;
        BuildPortrait(); stage.SetActive(true); avatar.transform.SetParent(stage.transform, false);
        avatar.transform.localPosition = Vector3.zero; avatar.transform.localRotation = Quaternion.identity;
        avatar.SetLayer(31); avatar.SetVisible(true);
        avatar.transform.localScale = Vector3.one;
        tutorialUI.SetActive(true); canvas.gameObject.SetActive(true); running = true; step = 0; ShowStep();
    }
    private void ShowStep()
    {
        EndPracticeStep();
        Unsubscribe(); advanceQueued = false;
        if (Steps == null || step >= Steps.Length) { Complete(); return; }
        var data = Steps[step];
        showAt = Time.unscaledTime + (body == 4 ? data.dreamyPauseSeconds : 0);
        clickAfter = showAt + .25f;
        explanationText.text = Time.unscaledTime < showAt ? "……냥?" : Dialogue(data);
        if (nameText != null) nameText.text = table.GetCatName(body);
        avatar.Play(Time.unscaledTime < showAt ? CatAnimation.Loaf : data.animation);
        next.interactable = !data.waitForTargetClick;
        next.targetGraphic.raycastTarget = !data.waitForTargetClick;
        nextHint.gameObject.SetActive(!data.waitForTargetClick);
        nextHint.text = trainingScene && data.practice != CatTrainingAction.None ? "클릭하여 실습 시작" : "클릭하여 다음";
        if (data.waitForTargetClick)
        {
            if (targets.TryGetValue(data.highlight, out subscribed) && subscribed != null) subscribed.onClick.AddListener(TargetClicked);
            else Debug.LogError($"WitchCat tutorial: {data.highlight} 버튼 연결이 없습니다. Buttons에서 할당해주세요.", this);
        }
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        nextHighlightSound = Time.unscaledTime;
    }
    public void Next()
    {
        if (greeting) { if (Time.unscaledTime - greetingAt >= .15f) CloseGreeting(); return; }
        if (!running || Time.unscaledTime < clickAfter || Steps[step].waitForTargetClick) return;
        if (trainingScene && Steps[step].practice != CatTrainingAction.None)
        {
            if (!practiceStarted) BeginPractice();
            return;
        }
        step++; ShowStep();
    }
    private void TargetClicked()
    {
        if (running && Time.unscaledTime >= clickAfter) advanceQueued = true;
    }
    private void Unsubscribe() { if (subscribed != null) subscribed.onClick.RemoveListener(TargetClicked); subscribed = null; }
    private void Complete()
    {
        StopTutorial();
        // Existing account autosave retries failed requests and guards account changes.
        if (trainingScene) { PlayerPrefs.SetInt(TrainingKey, 1); PlayerPrefs.Save(); }
        else DataConfig.catTutorialCompleted = 1;
    }
    private void StopTutorial()
    {
        RestoreTrainingPractice();
        if (greetingGroup != null) greetingGroup.alpha = 1f;
        if (greeting && tutorialUI != null) tutorialUI.GetComponent<RectTransform>().anchoredPosition = greetingPosition;
        greeting = greetingClosing = false;
        running = advanceQueued = false; Unsubscribe();
        ResetLobbyReveal();
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (avatar != null) { avatar.transform.SetParent(null, false); avatar.Play(CatAnimation.SittingIdle, false); }
        if (stage != null) stage.SetActive(false);
        if (trainingScene && avatar != null) avatar.SetVisible(false);
    }

    private void Update()
    {
        if (!ready) return;
        var db = DatabaseManager.Instance;
        if (db == null || !db.TryGetCatAppearance(out body, out eyes))
        {
            if (running || greeting) StopTutorial();
            EndPetting(); if (avatar != null) avatar.SetVisible(false);
            if (account != null)
            {
                greetedAccounts.Remove(account);
                if (resultAccount == account) { resultAccount = null; resultGreeting = null; }
            }
            account = null; pendingGreeting = null; return;
        }
        if (account != db.LoadedAccountId)
        {
            StopTutorial(); account = db.LoadedAccountId; EnsureAvatar();
            pendingFirstTutorial = !trainingScene && DataConfig.catTutorialCompleted != 1;
            if (!trainingScene)
            {
                if (resultAccount == account && resultGreeting.HasValue)
                { pendingGreeting = resultGreeting; resultGreeting = null; resultAccount = null; }
                else if (!pendingFirstTutorial && !greetedAccounts.Contains(account)) pendingGreeting = CatGreetingKind.Welcome;
                greetedAccounts.Add(account);
            }
        }
        EnsureAvatar();
        if (petPurrPlaying && Time.unscaledTime >= petPurrUntil) StopPetPurr();
        if (greeting) { UpdateGreeting(); return; }
        if (trainingScene)
        {
            bool show = BattleManager.Instance != null && BattleManager.Instance.IsTraining &&
                BattleManager.Instance.Phase == BattleStartPhase.TrainingTutorial;
            if (show && !running && TrainingGuideRequired) ReplayTutorial();
            if (!show && running) StopTutorial();
            if (!running) { avatar?.SetVisible(false); return; }
        }
        // Login can finish before the lobby transition. Never cover the login/intro screen.
        if (pendingFirstTutorial && lobbyUI != null && lobbyUI.gameObject.activeInHierarchy &&
            lobbyCharacter != null && lobbyCharacter.gameObject.activeInHierarchy && lobbyCamera != null && lobbyCamera.isActiveAndEnabled)
            ReplayTutorial();
        if (!trainingScene && !running && !pendingFirstTutorial && pendingGreeting.HasValue && LobbyReadyForGreeting())
        { ShowGreeting(pendingGreeting.Value); pendingGreeting = null; return; }
        if (running)
        {
            if (advanceQueued) { step++; ShowStep(); }
            if (!running) return;
            if (Time.unscaledTime >= showAt && explanationText.text != Dialogue(Steps[step]))
            { explanationText.text = Dialogue(Steps[step]); avatar.Play(Steps[step].animation); }
            if (trainingScene) UpdatePractice();
            UpdateHighlight();
        }
    }

    private void LateUpdate()
    {
        if (ready && !trainingScene && !running && !greeting && account != null) UpdateLobbyCat();
        else avatar?.StopCursorLook();
    }

    private void UpdateLobbyCat()
    {
        if (avatar == null || lobbyCharacter == null || lobbyCamera == null) return;
        bool visible = lobbyCharacter.gameObject.activeInHierarchy && lobbyCamera.isActiveAndEnabled &&
            (customizationUI == null || !customizationUI.gameObject.activeInHierarchy) &&
            lobbyUI != null && lobbyUI.gameObject.activeInHierarchy;
        if (!visible) { ResetLobbyReveal(); avatar.StopCursorLook(); avatar.SetVisible(false); return; }
        if (!LobbySurfaceSettled()) { revealStarted = -1; avatar.StopCursorLook(); avatar.SetVisible(false); return; }
        if (revealStarted < 0) revealStarted = Time.unscaledTime;
        float reveal = Mathf.SmoothStep(0, 1, (Time.unscaledTime - revealStarted) / Mathf.Max(.05f, table.lobbyRevealSeconds));
        avatar.SetVisible(true);
        avatar.transform.localScale = Vector3.one * Mathf.Max(.001f, reveal);
        int layer = lobbyCharacter.gameObject.layer;
        var renderer = lobbyCharacter.GetComponentInChildren<Renderer>(); if (renderer != null) layer = renderer.gameObject.layer;
        avatar.SetLayer(layer);
        if (petting && !Input.GetMouseButton(0)) EndPetting();
        if (petting || Time.unscaledTime < scratchEndsAt) petOrbitPause += Time.unscaledDeltaTime;
        float t = (Time.unscaledTime - petOrbitPause) * table.orbitSpeed;
        Vector3 offset = table.lobbyOffset + new Vector3(Mathf.Cos(t) * table.orbitRadius, Mathf.Sin(t * 2) * table.bobHeight, Mathf.Sin(t) * table.orbitRadius);
        offset.x += (1 - reveal) * .25f;
        avatar.transform.position = lobbyCharacter.position + lobbyCamera.transform.right * offset.x + Vector3.up * offset.y + lobbyCamera.transform.forward * offset.z;
        avatar.transform.rotation = Quaternion.Euler(0, lobbyCamera.transform.eulerAngles.y + 180 + Mathf.Sin(t) * 15, Mathf.Sin(t * 1.5f) * 3);
        bool hasPointer = TryGetLobbyPointerRay(false, out Ray lookRay);
        avatar.FollowLobbyCursor(lobbyCamera, lookRay, hasPointer && !petting && reveal >= .99f);
        UpdateLobbyIdle(reveal);
        if (petting) { UpdatePetting(); return; }
        if (reveal < .99f || Time.unscaledTime < petAllowedAt || !Input.GetMouseButtonDown(0)) return;
        if (!TryGetLobbyPointerRay(true, out Ray ray)) return;
        RawImage surface = lobbySurface;
        if (EventSystem.current != null)
        {
            uiHits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, uiHits);
            foreach (var hit in uiHits)
            {
                if (surface != null && hit.gameObject == surface.gameObject) break;
                // Decorative full-screen lobby Images must not swallow the portrait's click.
                // Real controls and overlays outside the lobby still take precedence.
                if (hit.gameObject.GetComponentInParent<Selectable>() != null ||
                    lobbyUI == null || !hit.gameObject.transform.IsChildOf(lobbyUI)) return;
            }
        }
        if (avatar.Intersects(ray))
        {
            petting = true; petPointer = Input.mousePosition;
            petStrokeTravel = 0; petStrokeDirection = lastPetStroke = 0; nextPetAt = 0;
        }
    }

    private bool TryGetLobbyPointerRay(bool requireInsidePortrait, out Ray ray)
    {
        ray = default;
        Vector3 pointer = Input.mousePosition;
        if (lobbyCamera == null || !Application.isFocused || pointer.x < 0 || pointer.y < 0 ||
            pointer.x > Screen.width || pointer.y > Screen.height) return false;
        if (lobbyCamera.targetTexture == null) { ray = lobbyCamera.ScreenPointToRay(pointer); return true; }
        var surface = lobbySurface;
        if (surface == null || surface.canvas == null) return false;
        var parent = surface.canvas;
        Camera uiCamera = parent.renderMode == RenderMode.ScreenSpaceOverlay ? null : parent.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface.rectTransform, pointer, uiCamera, out var local)) return false;
        Rect rect = surface.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0 || requireInsidePortrait && !rect.Contains(local)) return false;
        Rect uv = surface.uvRect;
        ray = lobbyCamera.ViewportPointToRay(new Vector3(uv.x + (local.x - rect.x) / rect.width * uv.width,
            uv.y + (local.y - rect.y) / rect.height * uv.height));
        return true;
    }

    private void UpdatePetting()
    {
        // Dragging outside the cat no longer counts as stroking it.
        if (!TryGetLobbyPointerRay(true,out Ray ray) || !avatar.Intersects(ray)) { EndPetting(); return; }
        Vector2 pointer = Input.mousePosition;
        Vector2 delta = (pointer - petPointer) * (1080f / Mathf.Max(1, Screen.height));
        petPointer = pointer;
        if (Mathf.Abs(delta.y) < .5f || Mathf.Abs(delta.y) < Mathf.Abs(delta.x)) return;
        int direction = delta.y > 0 ? 1 : -1;
        if (direction != petStrokeDirection) { petStrokeDirection = direction; petStrokeTravel = 0; }
        petStrokeTravel += Mathf.Abs(delta.y);
        if (petStrokeTravel < 14 || direction == lastPetStroke || Time.unscaledTime < nextPetAt) return;
        float now = Time.unscaledTime;
        while (recentPetStrokes.Count > 0 && now - recentPetStrokes.Peek() > Mathf.Max(1f,table.petScratchWindowSeconds))
            recentPetStrokes.Dequeue();
        recentPetStrokes.Enqueue(now);
        ScheduleYawn();
        if (recentPetStrokes.Count >= Mathf.Max(1,table.petScratchStrokeCount))
        {
            EndPetting();
            avatar.Play(CatAnimation.Scratch);
            BattleAudio.Play(BattleSound.CatAnnoyed, local: true);
            scratchEndsAt = now + Mathf.Max(.15f,table.GetAnimation(CatAnimation.Scratch)?.seconds ?? 1.8f);
            petAllowedAt = Mathf.Max(scratchEndsAt,now + Mathf.Max(0f,table.petScratchCooldownSeconds));
            recentPetStrokes.Clear();
            return;
        }
        avatar.Play(CatAnimation.Knead);
        if (!petPurrPlaying) { petPurr = BattleAudio.StartLoop(BattleSound.CatPetPurr, avatar.transform, true); petPurrPlaying = true; }
        petPurrUntil = Time.unscaledTime + 1f;
        lastPetStroke = direction; nextPetAt = Time.unscaledTime + .7f;
    }

    private void ScheduleYawn()
    {
        float min = Mathf.Max(3.2f,table.lobbyYawnInterval.x);
        nextYawnAt = Time.unscaledTime + UnityEngine.Random.Range(min,Mathf.Max(min,table.lobbyYawnInterval.y));
    }
    private void UpdateLobbyIdle(float reveal)
    {
        if (nextYawnAt <= 0f) ScheduleYawn();
        if (!Application.isFocused || reveal < .99f || petting || avatar.IsBusy || Time.unscaledTime < petAllowedAt) return;
        if (Time.unscaledTime < nextYawnAt) return;
        avatar.Play(CatAnimation.Yawn);
        StopPetPurr();
        BattleAudio.Play(BattleSound.CatYawn, local: true);
        ScheduleYawn();
    }

    private void EndPetting()
    {
        StopPetPurr();
        if (!petting) return;
        petting = false; petStrokeTravel = 0; petStrokeDirection = lastPetStroke = 0;
        if (avatar != null) avatar.Play(CatAnimation.SittingIdle, false);
    }

    private void StopPetPurr() { petPurr.Stop(); petPurrPlaying = false; }

    private bool LobbyReadyForGreeting() => lobbyUI != null && lobbyUI.gameObject.activeInHierarchy &&
        lobbyCharacter != null && lobbyCharacter.gameObject.activeInHierarchy && lobbyCamera != null && lobbyCamera.isActiveAndEnabled &&
        (customizationUI == null || !customizationUI.gameObject.activeInHierarchy) && LobbySurfaceSettled();

    private void ShowGreeting(CatGreetingKind kind)
    {
        EnsureAvatar(); if (avatar == null) return;
        StopPetPurr(); Unsubscribe();
        BuildPortrait(); stage.SetActive(true);
        avatar.transform.SetParent(stage.transform, false);
        avatar.transform.localPosition = Vector3.zero; avatar.transform.localRotation = Quaternion.identity;
        avatar.transform.localScale = Vector3.one; avatar.SetLayer(31); avatar.SetVisible(true);
        avatar.Play(kind == CatGreetingKind.Victory ? CatAnimation.Cheer : CatAnimation.SittingIdle);
        explanationText.text = table.Greeting(kind, body);
        if (nameText != null) nameText.text = table.GetCatName(body);
        foreach (var blocker in blockers) blocker.gameObject.SetActive(false);
        highlight.gameObject.SetActive(false);
        next.interactable = true; next.targetGraphic.raycastTarget = true;
        nextHint.text = "클릭하여 닫기"; nextHint.gameObject.SetActive(true);
        greetingGroup = tutorialUI.GetComponent<CanvasGroup>();
        if (greetingGroup == null) greetingGroup = tutorialUI.AddComponent<CanvasGroup>();
        greetingGroup.alpha = 1f;
        greetingPosition = tutorialUI.GetComponent<RectTransform>().anchoredPosition;
        tutorialUI.SetActive(true); canvas.gameObject.SetActive(true);
        greeting = true; greetingClosing = false; greetingAt = Time.unscaledTime;
        if (kind == CatGreetingKind.Victory) BattleAudio.Play(BattleSound.CatVictory, local: true);
        else if (kind == CatGreetingKind.Defeat) BattleAudio.Play(BattleSound.CatDefeat, local: true);
    }

    private void CloseGreeting()
    {
        if (greetingClosing) return;
        greetingClosing = true; greetingCloseAt = Time.unscaledTime;
        next.interactable = false;
    }

    private void UpdateGreeting()
    {
        if (lobbyUI == null || !lobbyUI.gameObject.activeInHierarchy ||
            customizationUI != null && customizationUI.gameObject.activeInHierarchy) { StopTutorial(); return; }
        if (Time.unscaledTime - greetingAt >= Mathf.Max(.1f, table.greetingSeconds)) CloseGreeting();
        if (!greetingClosing) return;
        float t = Mathf.Clamp01((Time.unscaledTime - greetingCloseAt) / Mathf.Max(.05f, table.greetingFadeSeconds));
        greetingGroup.alpha = 1f - t;
        tutorialUI.GetComponent<RectTransform>().anchoredPosition = greetingPosition + Vector2.down * (16f * t);
        if (t >= 1f) StopTutorial();
    }

    private void OnApplicationFocus(bool focused) { if (!focused) EndPetting(); }
    private void OnDisable() { EndPetting(); }

    private void ResetLobbyReveal()
    {
        EndPetting();
        recentPetStrokes.Clear(); nextYawnAt = petAllowedAt = scratchEndsAt = 0f;
        revealStarted = -1; sampledSurface = false; surfaceStableAt = Time.unscaledTime;
    }

    private bool LobbySurfaceSettled()
    {
        if (lobbyCamera.targetTexture == null) return true;
        if (lobbySurface == null || !lobbySurface.isActiveAndEnabled || lobbySurface.texture != lobbyCamera.targetTexture)
        {
            lobbySurface = null;
            foreach (var image in lobbyUI.GetComponentsInChildren<RawImage>())
                if (image.texture == lobbyCamera.targetTexture && image.isActiveAndEnabled) { lobbySurface = image; break; }
            ResetLobbyReveal();
        }
        if (lobbySurface == null) return false;
        lobbySurface.rectTransform.GetWorldCorners(surfaceCorners);
        bool moving = !sampledSurface;
        for (int i = 0; i < 4; i++)
        {
            moving |= (surfaceCorners[i] - previousCorners[i]).sqrMagnitude > .0001f;
            previousCorners[i] = surfaceCorners[i];
        }
        // Includes tween delays. Corner sampling also catches legacy Animation / parent movement.
        for (Transform tr = lobbySurface.transform; tr != null; tr = tr.parent)
        {
            moving |= DOTween.IsTweening(tr, true);
            if (tr.GetComponent<Canvas>() != null) break;
        }
        sampledSurface = true;
        if (moving) surfaceStableAt = Time.unscaledTime;
        return !moving && Time.unscaledTime - surfaceStableAt >= table.lobbyReturnStableSeconds;
    }

    private void UpdateHighlight()
    {
        if (trainingScene && practiceStarted)
        {
            highlight.gameObject.SetActive(false);
            foreach (var blocker in blockers) blocker.gameObject.SetActive(false);
            return;
        }
        var data = Steps[step];
        bool found = targets.TryGetValue(data.highlight, out var target) && target != null && target.gameObject.activeInHierarchy;
        highlight.gameObject.SetActive(found);
        if (!found) { SetRect(blockers[0].rectTransform, Vector2.zero, Vector2.one); for (int i = 1; i < 4; i++) blockers[i].gameObject.SetActive(false); return; }
        Vector3[] corners = new Vector3[4]; ((RectTransform)target.transform).GetWorldCorners(corners);
        Canvas targetCanvas = target.GetComponentInParent<Canvas>(); Camera camera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in corners) { Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corner); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        min -= table.highlightPadding * canvas.scaleFactor; max += table.highlightPadding * canvas.scaleFactor;
        min = new Vector2(Mathf.Clamp01(min.x / Screen.width), Mathf.Clamp01(min.y / Screen.height));
        max = new Vector2(Mathf.Clamp01(max.x / Screen.width), Mathf.Clamp01(max.y / Screen.height));
        SetRect(highlight, min, max);
        if (data.waitForTargetClick && Time.unscaledTime >= clickAfter)
        {
            SetRect(blockers[0].rectTransform, Vector2.zero, new Vector2(min.x, 1));
            SetRect(blockers[1].rectTransform, new Vector2(max.x, 0), Vector2.one);
            SetRect(blockers[2].rectTransform, new Vector2(min.x, 0), new Vector2(max.x, min.y));
            SetRect(blockers[3].rectTransform, new Vector2(min.x, max.y), new Vector2(max.x, 1));
        }
        else { SetRect(blockers[0].rectTransform, Vector2.zero, Vector2.one); for (int i = 1; i < 4; i++) blockers[i].gameObject.SetActive(false); }
        float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / table.highlightPulseSeconds);
        foreach (var edge in border) edge.color = new Color(table.highlightColor.r, table.highlightColor.g, table.highlightColor.b, .45f + pulse * .55f);
        arrow.rectTransform.anchoredPosition = new Vector2(0, 30 + pulse * 12);
        if (table.highlightSound != null && Time.unscaledTime >= nextHighlightSound)
        { audioSource.PlayOneShot(table.highlightSound, .35f); nextHighlightSound = Time.unscaledTime + table.highlightPulseSeconds * 2; }
    }

    private string Dialogue(CatTutorialStep data)
    {
        return data.Line(body)
            .Replace("{accelerate}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.Accelerate)))
            .Replace("{aim}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.LockOn)))
            .Replace("{parry}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.Parry)))
            .Replace("{slot1}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.MagicSlot1)))
            .Replace("{slot2}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.MagicSlot2)))
            .Replace("{boost}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.Boost)))
            .Replace("{brake}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.QuickBrake)))
            .Replace("{emote}", ControlSettings.KeyLabel(ControlSettings.GetKey(ControlAction.Emote)));
    }

    private static Image Image(string name, Transform parent, Color color, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
    }
    private static TMP_Text Label(string name, Transform parent, string text, int size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>(); label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        SetRect(label.rectTransform, Vector2.zero, Vector2.one); return label;
    }
    private static void Edge(RectTransform rt, Vector2 min, Vector2 max, Vector2 size)
    { rt.anchorMin = min; rt.anchorMax = max; rt.sizeDelta = size; rt.anchoredPosition = Vector2.zero; }
    private static void SetRect(RectTransform rt, Vector2 min, Vector2 max)
    { rt.gameObject.SetActive(true); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; }
    private void OnDestroy()
    {
        RestoreTrainingPractice();
        StopPetPurr();
        Unsubscribe();
        if (next != null) next.onClick.RemoveListener(Next);
        if (replayButton != null) replayButton.onClick.RemoveListener(ReplayTutorial);
        if (avatar != null) Destroy(avatar.gameObject);
        if (portraitCamera != null) portraitCamera.targetTexture = null;
        if (catImageRaw != null) catImageRaw.texture = null;
        if (portrait != null) { portrait.Release(); Destroy(portrait); }
    }
}
