using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleResultsPresentation : MonoBehaviour
{
    [SerializeField] private GameObject gameOverUI;
    [SerializeField] private GameObject mvpUI;
    [SerializeField] private TMP_Text content, damage, takenDamage, kill, death;
    [SerializeField] private Button nextButton, mainButton;
    [SerializeField] private BattleResultsTable settings;
    [Serializable] public sealed class AwardPanel
    {
        public GameObject root;
        public TMP_Text nickname, count, what, chingho;
        public RawImage character;
        public Button praiseButton;
        public GameObject thumbsupAnimation;
        public Image votePanel;
        public TMP_Text voteText;
        [NonSerialized] public BattleMvpPortrait portrait;
        [NonSerialized] public int lastPraise;
        [NonSerialized] public float hideThumbAt;
    }
    [SerializeField] private AwardPanel[] panels = new AwardPanel[4];
    public bool OwnsFinalUI { get; private set; }
    private bool showingMvp, bound, awardsBound;
    private bool praiseSubmitted;
    private float roundStarted;
    private int shownRound = -1;
    private TMP_FontAsset font;
    private Canvas overlay;
    private CanvasGroup roundGroup;
    private TMP_Text roundLabel;
    private Image roundShade;
    private readonly Dictionary<int, GameObject> characterCache = new();
    private readonly Dictionary<int, (int body, int eyes)> catCache = new();
    private readonly Dictionary<GameObject, bool> hiddenResultChildren = new();
    private readonly List<(Button button, UnityEngine.Events.UnityAction listener)> listeners = new();
    private Coroutine reveal;
    private float nextCacheCheck;
    private BattleAwardResult[] awardSnapshot;
    private readonly Dictionary<Animator,float> roundAnimationSpeeds = new();

    private void Awake()
    {
        settings = settings != null ? settings : BattleResultsTable.Default;
        font = GetComponent<BattleHud>()?.FeedbackFont;
        BindUI();
    }

    private void Update()
    {
        BattleManager battle = BattleManager.Instance;
        if (battle == null) return;
        if (!OwnsFinalUI && Time.unscaledTime >= nextCacheCheck)
        { nextCacheCheck = Time.unscaledTime + .5f; CacheCharacters(); }
        bool round = battle.Phase == BattleStartPhase.RoundResult;
        if (round)
        {
            EnsureRoundOverlay();
            if (shownRound != battle.RoundNumber)
            {
                shownRound = battle.RoundNumber; roundStarted = Time.unscaledTime;
                RestoreRoundAnimationSpeeds();
                foreach (var player in Player.ActiveCombatants)
                    if (player != null)
                        foreach (var animator in player.GetComponentsInChildren<Animator>())
                            if (animator != null) roundAnimationSpeeds[animator] = animator.speed;
                bool won = PlayerData.Local != null && PlayerData.Local.teamIndex == battle.LastRoundWinner;
                roundLabel.text = won ? "승리!" : "패배!";
                roundLabel.color = won ? new Color(1f,.72f,.12f) : new Color(1f,.25f,.2f);
            }
            roundGroup.gameObject.SetActive(true);
            float elapsed = Time.unscaledTime - roundStarted;
            float animationRate = Mathf.Max(.08f,Mathf.Exp(-settings.roundSlowdownResponse*elapsed));
            foreach (var entry in roundAnimationSpeeds) if(entry.Key!=null) entry.Key.speed = entry.Value*animationRate;
            float t = Mathf.SmoothStep(0f,1f,Mathf.Clamp01(elapsed / Mathf.Max(.05f,settings.roundFadeSeconds)));
            roundGroup.alpha = 1f;
            float ending = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((elapsed-(settings.roundResultSeconds-.7f))/.7f));
            roundShade.color = new Color(.008f,.012f,.025f,Mathf.Lerp(.58f*t,1f,ending));
            float textFade = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((elapsed-.25f)/.55f));
            roundLabel.alpha = textFade;
            roundLabel.transform.localScale = Vector3.one * Mathf.Lerp(1.18f,1f,textFade);
        }
        else
        {
            if (roundGroup != null) roundGroup.gameObject.SetActive(false);
            RestoreRoundAnimationSpeeds();
        }
        if (!OwnsFinalUI) return;
        CaptureAwards();
        Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        if (!showingMvp) return;
        BindAwards();
        var flag = BattleFlag.Instance;
        bool synced = flag != null && flag.Object != null && flag.Object.IsValid && flag.AwardsReady;
        bool canPraise = synced && !praiseSubmitted && !flag.HasPraised(flag.Runner.LocalPlayer);
        for (int i = 0; i < panels.Length; i++)
        {
            var panel = panels[i]; if (panel == null) continue;
            if (panel.praiseButton != null) panel.praiseButton.interactable = canPraise;
            int sequence = synced ? flag.PraiseSequences[i] : panel.lastPraise;
            Set(panel.voteText, sequence.ToString());
            if (sequence != panel.lastPraise)
            {
                panel.lastPraise = sequence;
                panel.portrait?.Praise();
                Replay(panel.thumbsupAnimation);
                panel.hideThumbAt = Time.unscaledTime + settings.praiseSeconds;
            }
            if (panel.thumbsupAnimation != null && Time.unscaledTime >= panel.hideThumbAt)
                panel.thumbsupAnimation.SetActive(false);
        }
    }

    private void CacheCharacters()
    {
        var network = NetworkGameManager.Instance;
        if (network == null || network.Runner == null) return;
        foreach (var data in PlayerData.PerkPlayers)
        {
            if (data == null || data.Object == null || !data.Object.IsValid || data.Runner != network.Runner) continue;
            int key = data.ParticipantRef.RawEncoded;
            catCache[key] = (data.CatBodyIndex, data.CatEyeIndex);
            if (characterCache.ContainsKey(key)) continue;
            var player = Player.FindInRunner(data.Runner, data.ParticipantRef);
            if (player == null || !player.IsAlive || !player.IsPresentationReady) continue;
            GameObject cache = BattleMvpPortrait.CacheCharacter(player);
            if (cache != null) { cache.transform.SetParent(transform, false); characterCache.Add(key, cache); }
        }
    }

    public void ShowResults(BattleManager battle)
    {
        if (OwnsFinalUI) return;
        BindUI(); CacheCharacters(); EnsureFinalFallback();
        OwnsFinalUI = true;
        gameOverUI.SetActive(true); if (mvpUI != null) mvpUI.SetActive(false);
        BringToFront(gameOverUI);
        int team = PlayerData.Local != null ? PlayerData.Local.teamIndex : 0;
        bool won = team > 0 && team == battle.WinningTeam;
        if (team > 0 && battle.WinningTeam > 0) CatTutorialDirector.QueueMatchGreeting(won);
        int own = team == 2 ? battle.Team2Wins : battle.Team1Wins;
        int enemy = team == 2 ? battle.Team1Wins : battle.Team2Wins;
        Set(content, $"{own} 대 {enemy} {(won ? "승리" : "패배")}");
        var victory = Find(gameOverUI.transform, "Victory"); if (victory != null) victory.gameObject.SetActive(won);
        var defeat = Find(gameOverUI.transform, "Defeat"); if (defeat != null) defeat.gameObject.SetActive(!won);
        var data = PlayerData.Local;
        CaptureAwards();
        float[] values = { data != null ? data.BattleDamageDealt : 0, data != null ? data.BattleDamageTaken : 0,
            data != null ? data.BattleKills : 0, data != null ? data.BattleDeaths : 0 };
        reveal = StartCoroutine(RevealStats(values));
    }

    private IEnumerator RevealStats(float[] values)
    {
        TMP_Text[] texts = { damage, takenDamage, kill, death };
        if (nextButton != null) nextButton.interactable = false;
        foreach (var text in texts) if (text != null) text.gameObject.SetActive(false);
        for (int i = 0; i < texts.Length; i++)
        {
            var text = texts[i]; if (text == null) continue;
            Vector3 originalScale = text.transform.localScale;
            Color originalColor = text.color;
            text.gameObject.SetActive(true);
            float started = Time.unscaledTime;
            while (Time.unscaledTime - started < settings.statRevealSeconds)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - started) / Mathf.Max(.1f, settings.statRevealSeconds));
                float impact = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .5f));
                float settle = t > .5f ? Mathf.Sin((t - .5f) * Mathf.PI * 4) * .06f * (1 - t) : 0;
                text.transform.localScale = originalScale * (Mathf.Lerp(settings.statStartScale, 1, impact) + settle);
                var color = originalColor; color.a *= Mathf.Lerp(settings.statStartAlpha, 1, impact); text.color = color;
                Set(text, Mathf.RoundToInt(Mathf.Lerp(0, values[i], 1 - Mathf.Pow(1 - t, 2))).ToString("N0"));
                yield return null;
            }
            text.transform.localScale = originalScale; text.color = originalColor;
            Set(text, Mathf.RoundToInt(values[i]).ToString("N0"));
            yield return new WaitForSecondsRealtime(settings.statPauseSeconds);
        }
        if (nextButton != null) nextButton.interactable = true;
        reveal = null;
    }

    public void ShowMvp()
    {
        if (!OwnsFinalUI || reveal != null || showingMvp) return;
        EnsureMvpFallback(); showingMvp = true;
        // MVPUI may be authored below GameOverUI. Keep its ancestors active.
        foreach (Transform child in gameOverUI.transform)
        {
            if (mvpUI.transform == child || mvpUI.transform.IsChildOf(child)) continue;
            hiddenResultChildren[child.gameObject] = child.gameObject.activeSelf;
            child.gameObject.SetActive(false);
        }
        if (!mvpUI.transform.IsChildOf(gameOverUI.transform)) gameOverUI.SetActive(false);
        for (Transform parent = mvpUI.transform; parent != null; parent = parent.parent) parent.gameObject.SetActive(true);
        BringToFront(mvpUI);
        BindAwards();
    }

    public void ReturnToMain() => GetComponent<BattleRoundUI>()?.ReturnToMain();

    public void PraisePanel(int panelIndex)
    {
        if (!showingMvp || praiseSubmitted) return;
        var flag = BattleFlag.Instance;
        if (flag == null || !flag.RequestPraise(panelIndex)) return;
        praiseSubmitted = true; // Block rapid local clicks before the networked vote arrives.
        foreach (var panel in panels)
            if (panel?.praiseButton != null) panel.praiseButton.interactable = false;
    }

    private void BindAwards()
    {
        CaptureAwards();
        var flag = BattleFlag.Instance;
        if (awardsBound || awardSnapshot == null) return;
        for (int i = 0; i < 4; i++)
        {
            var result = awardSnapshot[i]; var panel = panels[i];
            if (panel == null) continue;
            Set(panel.nickname, result.Name.ToString()); Set(panel.chingho, BattleAwardRules.Title(result.Category));
            if (panel.nickname != null)
            {
                int ownTeam = PlayerData.Local != null ? PlayerData.Local.teamIndex : 0;
                panel.nickname.color = ownTeam > 0 && result.Team > 0 && result.Team != ownTeam
                    ? new Color(1f,.2f,.2f) : Color.white;
            }
            Set(panel.count, BattleAwardRules.Count(result.Category, result.Value)); Set(panel.what, BattleAwardRules.Unit(result.Category));
            if (panel.votePanel != null)
            {
                int team = PlayerData.Local != null ? PlayerData.Local.teamIndex : 0;
                panel.votePanel.color = team > 0 && result.Team != team
                    ? new Color32(0xE7, 0x43, 0x57, 255) : new Color32(0x44, 0xDC, 0xCC, 255);
            }
            panel.lastPraise = flag != null && flag.Object != null && flag.Object.IsValid ? flag.PraiseSequences[i] : 0;
            if (characterCache.TryGetValue(result.Winner.RawEncoded, out GameObject template))
            {
                var cat = catCache.TryGetValue(result.Winner.RawEncoded, out var indices) ? indices : (1, 1);
                panel.portrait = BattleMvpPortrait.Create(template, panel.character, i, cat.Item1, cat.Item2);
                if (panel.portrait != null) panel.portrait.transform.SetParent(transform, true);
            }
        }
        awardsBound = true;
    }

    private void CaptureAwards()
    {
        var flag = BattleFlag.Instance;
        if (awardSnapshot != null || flag == null || flag.Object == null || !flag.Object.IsValid || !flag.AwardsReady) return;
        awardSnapshot = new BattleAwardResult[4];
        for (int i = 0; i < 4; i++) awardSnapshot[i] = flag.Awards[i];
    }

    private void BindUI()
    {
        if (bound) return;
        if (gameOverUI == null) gameOverUI = FindInScene("GameOverUI");
        if (mvpUI == null) mvpUI = FindInScene("MVPUI");
        if (gameOverUI != null)
        {
            if (content == null) content = FindText(gameOverUI, "Content");
            if (damage == null) damage = FindText(gameOverUI, "Damage");
            if (takenDamage == null) takenDamage = FindText(gameOverUI, "TakenDamage");
            if (kill == null) kill = FindText(gameOverUI, "Kill");
            if (death == null) death = FindText(gameOverUI, "Death");
            if (nextButton == null) nextButton = Find(gameOverUI.transform, "NextButton", "ConfirmButton")?.GetComponent<Button>();
            Bind(nextButton, ShowMvp);
            if (nextButton != null) Set(nextButton.GetComponentInChildren<TMP_Text>(true), "다음");
            var cancel = Find(gameOverUI.transform, "CancleButton", "CancelButton")?.GetComponent<Button>();
            Bind(cancel, ReturnToMain);
            if (cancel != null) Set(cancel.GetComponentInChildren<TMP_Text>(true), "메인으로");
            BringToFront(gameOverUI); gameOverUI.SetActive(false);
        }
        if (mvpUI != null) { BindPanels(); BringToFront(mvpUI); mvpUI.SetActive(false); }
        bound = gameOverUI != null;
    }

    private void BindPanels()
    {
        if (panels == null || panels.Length != 4) Array.Resize(ref panels, 4);
        for (int i = 0; i < 4; i++)
        {
            panels[i] ??= new AwardPanel(); var panel = panels[i];
            if (panel.root == null) panel.root = Find(mvpUI.transform, "Panel0" + (i + 1))?.gameObject;
            if (panel.root == null) continue;
            if (panel.nickname == null) panel.nickname = FindText(panel.root, "Nickname");
            if (panel.count == null) panel.count = FindText(panel.root, "Count");
            if (panel.what == null) panel.what = FindText(panel.root, "What");
            if (panel.chingho == null) panel.chingho = FindText(panel.root, "Chingho");
            if (panel.character == null) panel.character = Find(panel.root.transform, "RawImage")?.GetComponent<RawImage>();
            if (panel.praiseButton == null) panel.praiseButton = Find(panel.root.transform, "PraiseButton")?.GetComponent<Button>();
            if (panel.thumbsupAnimation == null) panel.thumbsupAnimation = Find(panel.root.transform, "ThumbsupAnimation")?.gameObject;
            var voteRoot = Find(panel.root.transform, "VotePanel", "VotePenel");
            if (panel.votePanel == null && voteRoot != null) panel.votePanel = voteRoot.GetComponent<Image>();
            if (panel.voteText == null && voteRoot != null) panel.voteText = Find(voteRoot, "VoteText")?.GetComponent<TMP_Text>();
            if (panel.thumbsupAnimation != null) panel.thumbsupAnimation.SetActive(false);
            int slot = i; Bind(panel.praiseButton, () => PraisePanel(slot));
        }
        if (mainButton == null) mainButton = Find(mvpUI.transform, "ToMainButton")?.GetComponent<Button>();
        Bind(mainButton, ReturnToMain);
    }

    private void Bind(Button button, UnityEngine.Events.UnityAction action)
    { if (button == null) return; button.onClick.AddListener(action); listeners.Add((button, action)); }
    private static void Set(TMP_Text text, string value) { if (text != null) { text.richText = false; text.text = value; } }
    private static TMP_Text FindText(GameObject root, string name) => Find(root.transform, name)?.GetComponent<TMP_Text>();
    private GameObject FindInScene(string name)
    {
        foreach (Transform child in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (child.gameObject.scene == gameObject.scene && string.Equals(child.name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return child.gameObject;
        return null;
    }
    private static Transform Find(Transform root, params string[] names)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            foreach (string name in names) if (string.Equals(child.name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }
    private static void BringToFront(GameObject root)
    {
        if (root == null) return;
        // Unity's missing-component wrapper can be CLR-non-null in the Editor.
        // Use Unity's null check, not ??, before accessing native Canvas properties.
        var canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = root.AddComponent<Canvas>();
        var parentCanvas = root.transform.parent != null ? root.transform.parent.GetComponentInParent<Canvas>(true) : null;
        if (parentCanvas == null)
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // MVPUI is nested inside GameOverUI. Equal override orders allow the parent's
        // full-screen background to draw over every MVP card even though they are active.
        canvas.enabled = true;
        canvas.overrideSorting = true;
        canvas.sortingLayerID = parentCanvas != null ? parentCanvas.sortingLayerID : 0;
        canvas.sortingOrder = parentCanvas != null ? Mathf.Max(6000, parentCanvas.sortingOrder + 1) : 6000;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
    }
    private static void Replay(GameObject root)
    {
        if (root == null) return;
        root.SetActive(false); root.SetActive(true);
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        { animator.updateMode = AnimatorUpdateMode.UnscaledTime; animator.Rebind(); animator.Update(0); animator.Play(0, 0, 0); }
        foreach (Animation animation in root.GetComponentsInChildren<Animation>(true)) { animation.Stop(); animation.Play(); }
        foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true)) { particle.Clear(); particle.Play(); }
    }

    private GameObject NewPanel(string name)
    {
        if (overlay == null)
        {
            overlay = new GameObject("Battle results overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            overlay.transform.SetParent(transform, false); overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.sortingOrder = 7000;
            var scaler = overlay.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        }
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image)); panel.transform.SetParent(overlay.transform, false);
        var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(.01f, .015f, .025f, .97f); return panel;
    }
    private TMP_Text Label(Transform parent, string name, Vector2 pos, Vector2 size, float fontSize, string value = "")
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false); text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = pos;
        if (font != null) text.font = font; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        Set(text, value); return text;
    }
    private Button MakeButton(Transform parent, string caption, Vector2 pos, UnityEngine.Events.UnityAction action)
    {
        var root = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button)); root.transform.SetParent(parent, false);
        var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(250, 60); rect.anchoredPosition = pos;
        root.GetComponent<Image>().color = new Color(.08f, .55f, .55f); var button = root.GetComponent<Button>(); button.targetGraphic = root.GetComponent<Image>();
        Label(rect, "Label", Vector2.zero, rect.sizeDelta, 25, caption); Bind(button, action); return button;
    }
    private void EnsureRoundOverlay()
    {
        if (roundGroup != null) return;
        var root = NewPanel("RoundResult"); roundGroup = root.AddComponent<CanvasGroup>();
        roundShade = root.GetComponent<Image>();
        roundLabel = Label(root.transform, "Round result", Vector2.zero, new Vector2(1500, 220), 120);
        roundLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
        roundLabel.outlineColor = new Color32(70,38,10,255); roundLabel.outlineWidth = .12f;
    }
    private void EnsureFinalFallback()
    {
        if (gameOverUI != null) return;
        gameOverUI = NewPanel("GameOverUI"); var parent = gameOverUI.transform;
        content = Label(parent, "Content", new Vector2(0, 300), new Vector2(1500, 120), 70);
        string[] captions = { "가한 피해량", "받은 피해량", "처치 횟수", "사망 횟수" };
        var texts = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            float x = -570 + i * 380;
            Label(parent, "Caption", new Vector2(x, 100), new Vector2(340, 60), 30, captions[i]);
            texts[i] = Label(parent, "Stat", new Vector2(x, 0), new Vector2(340, 120), 65);
        }
        damage = texts[0]; takenDamage = texts[1]; kill = texts[2]; death = texts[3];
        nextButton = MakeButton(parent, "다음", new Vector2(0, -280), ShowMvp); bound = true;
    }
    private void EnsureMvpFallback()
    {
        if (mvpUI != null) return;
        mvpUI = NewPanel("MVPUI");
        for (int i = 0; i < 4; i++)
        {
            var root = new GameObject("Panel0" + (i + 1), typeof(RectTransform)); root.transform.SetParent(mvpUI.transform, false);
            root.GetComponent<RectTransform>().anchoredPosition = new Vector2(-630 + i * 420, 0);
            Label(root.transform, "Nickname", new Vector2(0, 340), new Vector2(390, 60), 30);
            Label(root.transform, "Chingho", new Vector2(0, -170), new Vector2(390, 60), 28);
            Label(root.transform, "Count", new Vector2(-30, -230), new Vector2(260, 55), 32);
            Label(root.transform, "What", new Vector2(125, -230), new Vector2(100, 55), 22);
            var image = new GameObject("RawImage", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>(); image.transform.SetParent(root.transform, false);
            image.rectTransform.sizeDelta = new Vector2(380, 430); image.rectTransform.anchoredPosition = new Vector2(0, 80);
            var button = MakeButton(root.transform, "칭찬", new Vector2(0, -300), () => { }); button.name = "PraiseButton";
            var votes = new GameObject("VotePanel", typeof(RectTransform), typeof(Image));
            votes.transform.SetParent(root.transform, false);
            var voteRect = votes.GetComponent<RectTransform>();
            voteRect.anchoredPosition = new Vector2(0, -360); voteRect.sizeDelta = new Vector2(80, 40);
            votes.GetComponent<Image>().raycastTarget = false;
            Label(votes.transform, "VoteText", Vector2.zero, new Vector2(80, 40), 24, "0");
            var thumbs = Label(root.transform, "ThumbsupAnimation", new Vector2(0, 220), new Vector2(340, 70), 42, "좋아요!"); thumbs.gameObject.SetActive(false);
        }
        mainButton = MakeButton(mvpUI.transform, "메인으로", new Vector2(0, -450), ReturnToMain);
        BindPanels();
    }
    private void OnDestroy()
    {
        RestoreRoundAnimationSpeeds();
        foreach (var listener in listeners) if (listener.button != null) listener.button.onClick.RemoveListener(listener.listener);
        foreach (var item in hiddenResultChildren) if (item.Key != null) item.Key.SetActive(item.Value);
        foreach (var cache in characterCache.Values) if (cache != null) Destroy(cache);
        foreach (var panel in panels) if (panel?.portrait != null) Destroy(panel.portrait.gameObject);
    }
    private void OnDisable() => RestoreRoundAnimationSpeeds();
    private void RestoreRoundAnimationSpeeds()
    {
        foreach(var entry in roundAnimationSpeeds) if(entry.Key!=null) entry.Key.speed = entry.Value;
        roundAnimationSpeeds.Clear();
    }
}
