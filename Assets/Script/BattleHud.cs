using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Designer-authored UI with generated lock-on / incoming-magic fallbacks.
[DisallowMultipleComponent]
public sealed class BattleHud : MonoBehaviour
{
    [Header("UI style")]
    [Tooltip("DungGeunMo SDF: 자동 생성되는 경고/록온/부활 텍스트에 사용합니다.")]
    [SerializeField] private TMP_FontAsset feedbackFont;
    public TMP_FontAsset FeedbackFont => feedbackFont;
    [Header("Panels (keep this component outside these panels)")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private GameObject playerPanel;
    [SerializeField] private GameObject respawnPanel;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private Button resumeButton;
    [Header("Player")]
    [Tooltip("내 닉네임을 표시할 TextMeshPro UI. 현재 접속자의 PlayerData.playerName을 사용합니다.")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private GridHPBar hpGridBar;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;
    [Tooltip("지정하면 Hp Text는 현재 값만, 이 항목은 /최대 값을 표시합니다.")]
    [SerializeField] private TMP_Text maxHpText;
    [SerializeField] private Slider apSlider;
    [SerializeField] private Image apFill;
    [SerializeField] private TMP_Text apText;
    [SerializeField] private TextMeshProUGUI manaPercentText;
    [Tooltip("UI에 % 기호가 따로 있다면 끄세요.")]
    [SerializeField] private bool manaPercentIncludeSymbol = true;
    [SerializeField] private TMP_Text teamText;
    [SerializeField] private TMP_Text speedStageText;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text selectedMagicText;
    [SerializeField] private TMP_Text magicCostText;
    [Header("Match")]
    [Tooltip("내 진영의 라운드 승리 횟수만 표시합니다. 팀 번호와 무관하게 로컬 플레이어 기준입니다.")]
    [SerializeField] private TextMeshProUGUI myRoundWinsText;
    [Tooltip("상대 진영의 라운드 승리 횟수만 표시합니다. VS 문구는 별도 UI로 두세요.")]
    [SerializeField] private TextMeshProUGUI enemyRoundWinsText;
    [SerializeField] private TMP_Text matchTimeText;
    [SerializeField] private TMP_Text flagText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text respawnText;
    [SerializeField] private string respawnPrefix = "부활까지";
    [Header("Aim")]
    [SerializeField] private GameObject reticle;
    [Tooltip("화면 중앙에 고정할 Dot입니다. 암흑/비전 즉발 마법도 이 Dot을 기준으로 조준합니다. 움직이는 원과 별개로 연결합니다.")]
    [SerializeField] private RectTransform desiredAimMarker;
    [Tooltip("움직이는 원형 UI: 캐릭터의 실제 정면 발사 방향입니다.")]
    [SerializeField] private RectTransform forwardAimMarker;
    [Tooltip("큰 원의 Image. 비워두면 Desired Aim Marker 자체의 Image를 사용합니다.")]
    [SerializeField] private Image desiredAimImage;
    [Tooltip("작은 원의 Image. 비워두면 Forward Aim Marker 자체의 Image를 사용합니다.")]
    [SerializeField] private Image forwardAimImage;
    [SerializeField] private TMP_Text aimAlignmentText;
    [Tooltip("완료 표식. 비워 두면 초록색 원을 생성합니다.")]
    [SerializeField] private RectTransform lockMarker;
    [Tooltip("충전 중 줄어드는 원. 비워 두면 기본 원을 생성합니다.")]
    [SerializeField] private RectTransform lockChargeMarker;
    [Tooltip("자동 생성할 충전 원 Sprite. 비우면 기본 흰 원을 사용합니다. Lock Charge Marker를 직접 연결했다면 해당 UI가 우선합니다.")]
    [SerializeField] private Sprite lockChargeSprite;
    [Tooltip("자동 생성할 록온 완료 표식 Sprite. 비우면 기본 초록 원을 사용합니다. Lock Marker를 직접 연결했다면 해당 UI가 우선합니다.")]
    [SerializeField] private Sprite lockCompleteSprite;
    [SerializeField, Min(1f)] private float lockChargeStartSize = 100f;
    [SerializeField, Min(1f)] private float lockChargeEndSize = 28f;
    [Header("Incoming magic / parry warning")]
    // Keep the old reference only to hide a previously authored full-screen red overlay.
    [SerializeField, HideInInspector] private Image incomingMagicWarning;
    [Tooltip("선택 사항: 방향 경고용 UI Image. 비워 두면 자동 생성합니다.")]
    [SerializeField] private Image incomingMagicIndicator;
    [Tooltip("경고 이미지 A. A/B를 모두 넣으면 번갈아 표시합니다. 하나만 있으면 해당 이미지가 깜빡입니다.")]
    [SerializeField] private Sprite warningSpriteA;
    [Tooltip("경고 이미지 B. 두 Sprite 모두 비우면 기본 느낌표가 깜빡입니다.")]
    [SerializeField] private Sprite warningSpriteB;
    [SerializeField, Min(16f)] private float warningIconSize = 96f;
    [Tooltip("화면 끝에서 경고 이미지 중심까지의 여백(픽셀).")]
    [SerializeField, Min(0f)] private float warningEdgePadding = 64f;
    [SerializeField] private Color warningIconColor = Color.white;
    [Tooltip("느낌표 주변의 붉은 후광 크기 배율입니다. 화면 전체에는 적용하지 않습니다.")]
    [SerializeField, Range(1f, 3f)] private float warningGlowScale = 1.8f;
    [SerializeField] private Color warningGlowColor = new Color(1f, 0.15f, 0.2f, 0.65f);
    [SerializeField] private bool showWarningFrame = true;
    [SerializeField, Min(1f)] private float warningDistance = 90f;
    [Tooltip("거리 밖이어도 이 시간 안에 충돌할 것으로 예상되는 투사체는 미리 알립니다.")]
    [SerializeField, Min(0.1f)] private float warningLeadSeconds = 1.5f;
    [Tooltip("초당 점멸 주기. A/B 이미지일 때는 한 주기마다 A→B→A로 전환됩니다.")]
    [SerializeField, Range(0.2f, 12f)] private float warningFarFrequency = 1.5f;
    [SerializeField, Range(0.2f, 12f)] private float warningNearFrequency = 8f;
    [Header("Parry timing cue (projectiles only)")]
    [SerializeField] private bool enableParryTimingCue = true;
    [Tooltip("충돌 예상 시간이 패링 시간에서 이 여유(초)를 뺀 값 안으로 들어오면 표시합니다. 보조 신호이며 성공을 보장하지 않습니다.")]
    [SerializeField, Min(0f)] private float parryCueSafetyMargin = 0.08f;
    [SerializeField] private Color parryCueColor = new Color(0.24f, 0.84f, 0.8f, 1f);
    [SerializeField, Range(1f, 2f)] private float parryCueRingScale = 1.35f;
    [SerializeField, Range(1f, 1.6f)] private float parryCuePunchScale = 1.2f;
    [SerializeField, Min(0.05f)] private float parryCuePunchSeconds = 0.18f;
    [Tooltip("앞을 보면서 방어할 수 있도록 중앙 아래에 PARRY 신호를 표시합니다. 비우면 자동 생성합니다.")]
    [SerializeField] private TMP_Text parryTimingText;
    [SerializeField] private bool showCentralParryCue = true;
    private RectTransform parryCueRing;
    private Graphic parryCueRingGraphic;
    private bool parryCueActive;
    private MagicProjectile parryCueThreat;
    private float parryCueStarted;
    private Canvas generatedFeedbackCanvas;
    private TMP_Text generatedWarning;
    private CombatFeedbackGraphic warningGlow;
    private CombatFeedbackGraphic warningFrame;
    private MagicProjectile incomingThreat;
    private Predicate<MagicProjectile> offscreenThreatFilter;
    private float warningPhase, threatStrength, nextThreatCheck;
    [SerializeField] private Image lockProgressFill;
    [SerializeField] private TMP_Text lockText;
    [SerializeField] private Camera worldCamera;
    [Header("Received damage direction")]
    [Tooltip("비우면 BattleUI의 DamageIndicator를 자동 연결합니다. 공격자의 피격 시점 위치를 표시합니다.")]
    [SerializeField] private DamageIndicator damageIndicator;
    [SerializeField] private Color receivedDamageColor = Color.red;
    [Header("Damage numbers (preplaced TMP labels, not prefabs)")]
    [SerializeField] private TMP_Text[] damageLabels = Array.Empty<TMP_Text>();
    [SerializeField, Min(0.1f)] private float damageDuration = 0.9f;
    [SerializeField] private float damageRise = 1.5f;
    [Header("Confirmed hit marker (attacker only)")]
    [Tooltip("선택 사항: 네 방향 전체 모양을 가진 독립 UI Image. 지정하면 이 Image를 표시/페이드합니다. Dot 자체를 넣지 마세요. 비우면 얇은 마름모 4개를 생성합니다.")]
    [SerializeField] private Image hitMarkerImage;
    [Tooltip("중심이 될 Dot의 RectTransform. 비우면 Desired Aim Marker, 그것도 없으면 카메라 화면 중앙입니다.")]
    [SerializeField] private RectTransform hitMarkerCenter;
    [Tooltip("즉시 나타난 뒤 완전한 밝기로 유지하는 시간입니다.")]
    [SerializeField, Min(0f)] private float hitMarkerHoldSeconds = 0.04f;
    [SerializeField, Min(0.01f)] private float hitMarkerFadeSeconds = 0.3f;
    [Tooltip("기본 도형만의 색상. 직접 지정한 Image는 원래 색상을 유지합니다.")]
    [SerializeField] private Color hitMarkerColor = Color.white;
    [Tooltip("Dot 기준 네 방향 각도. 0도=오른쪽, 양수=반시계 방향입니다. 기본 -30, -120, 30, 120도.")]
    [SerializeField] private Vector4 hitMarkerAngles = new Vector4(-30f, -120f, 30f, 120f);
    [SerializeField, Min(0f)] private float hitMarkerRadius = 28f;
    [SerializeField, Min(1f)] private float hitMarkerLength = 18f;
    [SerializeField, Min(1f)] private float hitMarkerWidth = 3f;
    private Graphic activeHitMarker;
    private CombatFeedbackGraphic generatedHitMarker;
    private Color hitMarkerBaseColor;
    private float hitMarkerStarted = float.NegativeInfinity;

    private sealed class DamageSlot
    {
        public TMP_Text Label;
        public Vector3 Position;
        public Vector3 Scale;
        public Color Color;
        public float Started;
        public bool Active;
    }

    private static BattleHud instance;
    private DamageSlot[] damageSlots;
    private Player owner;
    private Player lockTarget;
    private float respawnAt;
    private int nextDamageSlot;
    private Image tintedDesiredImage, tintedForwardImage;
    private Color originalDesiredColor, originalForwardColor;
    public static bool MenuOpen { get; private set; }

    private void Awake()
    {
        offscreenThreatFilter = IsOffscreenThreat;
        damageSlots = new DamageSlot[damageLabels.Length];
        for (int i = 0; i < damageLabels.Length; i++)
        {
            TMP_Text label = damageLabels[i];
            damageSlots[i] = new DamageSlot
            {
                Label = label,
                Scale = label != null ? label.transform.localScale : Vector3.one,
                Color = label != null ? label.color : Color.white
            };
        }
        HideViews();
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += BindFlightAim;
        instance = this;
        MenuOpen = false;
        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
    }

    private void Update()
    {
        BattleManager battle = BattleManager.Instance;
        // Update even during intermission/VS and while the local character is despawned.
        BindRoundScores();
        bool show = battle != null && (battle.IsGameplayActive || battle.IsBattleEnded);
        if (!show)
        {
            if (MenuOpen) SetMenuOpen(false);
            respawnAt = 0f;
            BindOwner(null);
            HideViews();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) ToggleMenu();
        if (MenuOpen && (menuPanel == null || !menuPanel.activeInHierarchy))
            SetMenuOpen(false);

        Player local = Player.LocalPlayer;
        bool valid = local != null && local.Object != null && local.Object.IsValid;
        if (valid)
        {
            if (owner != local)
            {
                BindOwner(local);
            }
            if (owner.IsAlive) respawnAt = 0f;
            else
            {
                // Read the replicated server deadline, then keep a local clock through despawn.
                float? remaining = owner.RespawnTimer.RemainingTime(owner.Runner);
                if (remaining.HasValue) respawnAt = Time.unscaledTime + Mathf.Max(0f, remaining.Value);
                else if (respawnAt <= 0f) respawnAt = Time.unscaledTime + battle.RespawnDelaySeconds;
            }
        }
        else BindOwner(null);
        if (worldCamera == null) worldCamera = Camera.main;

        Visible(hudRoot, true);
        Visible(playerPanel, valid);
        if (matchTimeText != null)
            SetText(matchTimeText, battle.IsOvertime ? "연장전" : TimeSpan.FromSeconds(Mathf.Max(0f, battle.RemainingSeconds)).ToString(@"mm\:ss"));
        BattleFlag flag = BattleFlag.Instance;
        bool hasFlag = flag != null && flag.Object != null && flag.Object.IsValid && flag.Carrier != PlayerRef.None;
        if (flagText != null) SetText(flagText, !hasFlag ? "FLAG: collect it at the marker" :
            valid && flag.Carrier == owner.Object.InputAuthority ? "YOU HAVE THE FLAG" : $"FLAG: {flag.Carrier}");
        Visible(resultPanel, battle.IsBattleEnded);
        Visible(resultText != null ? resultText.gameObject : null, battle.IsBattleEnded);
        if (resultText != null) SetText(resultText, battle.WinningTeam > 0 ? $"TEAM {battle.WinningTeam} WINS" : "DRAW - NO FLAG HOLDER");
        bool respawning = respawnAt > 0f && !battle.IsBattleEnded;
        if (respawning && respawnText == null)
        {
            respawnText = CreateCentralPrompt("Respawn countdown", 55f, 36f);
            respawnText.rectTransform.sizeDelta = new Vector2(520f, 64f);
        }
        Visible(respawnPanel, respawning);
        Visible(respawnText != null ? respawnText.gameObject : null, respawning);
        if (respawnText != null) SetText(respawnText, Time.unscaledTime < respawnAt
            ? $"{respawnPrefix}  {Mathf.CeilToInt(respawnAt - Time.unscaledTime)}" : "부활 중...");
        BindPlayer(valid);
        BindAim(valid && owner.IsAlive && battle.IsGameplayActive && !MenuOpen);
        UpdateDamage();
        UpdateHitMarker(valid && owner.IsAlive && battle.IsGameplayActive && !MenuOpen);
    }

    private void BindRoundScores()
    {
        PlayerData data = PlayerData.Local;
        BattleFlag flag = BattleFlag.Instance;
        int myWins = 0, enemyWins = 0;
        if (data != null && data.Object != null && data.Object.IsValid && data.Object.HasInputAuthority &&
            data.IsLoadoutInitialized && (data.teamIndex == 1 || data.teamIndex == 2) &&
            flag != null && flag.Object != null && flag.Object.IsValid)
        {
            myWins = data.teamIndex == 1 ? flag.Team1Wins : flag.Team2Wins;
            enemyWins = data.teamIndex == 1 ? flag.Team2Wins : flag.Team1Wins;
        }
        SetRoundWinsText(myRoundWinsText, myWins);
        SetRoundWinsText(enemyRoundWinsText, enemyWins);
    }

    private static void SetRoundWinsText(TextMeshProUGUI label, int wins)
    {
        if (label == null) return;
        // Best-of-three digits are constant strings: no per-frame number allocations.
        string value = wins <= 0 ? "0" : wins == 1 ? "1" : "2";
        if (label.text != value) label.text = value;
    }

    private void BindPlayer(bool valid)
    {
        if (playerNameText != null)
        {
            PlayerData data = PlayerData.Local;
            playerNameText.richText = false;
            SetText(playerNameText, data != null && data.Object != null && data.Object.IsValid &&
                data.IsLoadoutInitialized ? data.playerName.ToString() : string.Empty);
        }
        SetBar(apSlider, apFill, valid ? owner.NowAp / Mathf.Max(1f, owner.MaxAp) : 0f);
        if (apText != null) SetText(apText, valid ? $"{owner.NowAp:0} / {owner.MaxAp:0}" : "");
        if (manaPercentText != null)
        {
            int percent = ManaPercent(valid ? owner.NowAp : 0f, valid ? owner.MaxAp : 0f);
            SetText(manaPercentText, manaPercentIncludeSymbol ? $"{percent}%" : percent.ToString());
        }
        if (teamText != null) SetText(teamText, valid ? owner.TeamIndex.ToString() : "");
        if (speedStageText != null) SetText(speedStageText, valid ? owner.CurrentSpeedStage.ToString("+0;-0;0") : "");
        if (speedText != null) SetText(speedText, valid ? $"{owner.CurrentSpeed:0.0} m/s" : "");
        if (selectedMagicText != null) SetText(selectedMagicText, valid ? owner.CurrentMagicSlot == 3 ? "Parry" : MagicName(owner.GetSelectedMagic()) : "");
        if (magicCostText != null) SetText(magicCostText, valid ? owner.SelectedMagicApCost.ToString("0") : "");
    }

    private void BindOwner(Player next)
    {
        if (owner == next) return;
        ClearHitMarker();
        if (hpGridBar != null) hpGridBar.ResetBinding();
        if (damageIndicator != null) damageIndicator.ClearAllIndicators();
        if (owner != null) owner.HealthChanged -= BindHealth;
        owner = next;
        if (owner != null)
        {
            owner.HealthChanged += BindHealth;
            BindHealth(owner.NowHp, owner.MaxHp);
        }
        else BindHealth(0f, 0f);
    }

    private void BindHealth(float hp, float maxHp)
    {
        float fraction = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
        if (hpGridBar != null) hpGridBar.UpdateHealth(hp, maxHp);
        SetBar(hpSlider, hpFill, fraction);
        if (hpText != null) SetText(hpText, owner != null ? maxHpText != null ? $"{hp:0}" : $"{hp:0} / {maxHp:0}" : "");
        if (maxHpText != null) SetText(maxHpText, owner != null ? $"/{maxHp:0}" : "");
    }

    public static int ManaPercent(float current, float maximum)
    {
        if (maximum <= 0f || float.IsNaN(maximum) || float.IsInfinity(maximum) ||
            float.IsNaN(current) || float.IsInfinity(current)) return 0;
        return Mathf.RoundToInt(Mathf.Clamp01(current / maximum) * 100f);
    }

    private void BindAim(bool show)
    {
        Visible(reticle, show && desiredAimMarker == null);
        if (!show) lockTarget = null;
        else lockTarget = owner.GetDisplayedLockTarget();
        bool locked = show && owner.SelectedMagicStats.requiresTarget && lockTarget != null &&
            lockTarget.Object != null && lockTarget.Object.IsValid;
        if (show && (lockMarker == null || lockChargeMarker == null)) EnsureLockFeedback();
        bool completed = locked && owner.IsFullyLocked;
        bool charging = locked && !completed;
        if (completed && lockMarker != null) completed = Place(lockMarker, lockTarget.LockAimPoint);
        if (charging && lockChargeMarker != null)
        {
            charging = Place(lockChargeMarker, lockTarget.LockAimPoint);
            lockChargeMarker.sizeDelta = Vector2.one * Mathf.Lerp(lockChargeStartSize, lockChargeEndSize, owner.LockProgress);
        }
        Visible(lockMarker != null ? lockMarker.gameObject : null, completed);
        Visible(lockChargeMarker != null ? lockChargeMarker.gameObject : null, charging);
        Visible(lockProgressFill != null ? lockProgressFill.gameObject : null, locked);
        Visible(lockText != null ? lockText.gameObject : null, locked);
        if (lockProgressFill != null) lockProgressFill.fillAmount = locked ? Mathf.Clamp01(owner.LockProgress) : 0f;
        if (lockText != null) SetText(lockText, locked ? owner.IsFullyLocked ? "RELEASE TO FIRE" : $"LOCK {owner.LockProgress:P0}" : "");
        UpdateThreatWarning(show);
    }


    private void EnsureFeedbackCanvas()
    {
        if (generatedFeedbackCanvas != null) return;
        var root = new GameObject("Generated combat feedback", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        generatedFeedbackCanvas = root.GetComponent<Canvas>();
        generatedFeedbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        generatedFeedbackCanvas.overrideSorting = true;
        generatedFeedbackCanvas.sortingOrder = 80;
    }

    private RectTransform CreateFeedbackRing(string title, Color color, float size, Sprite sprite)
    {
        EnsureFeedbackCanvas();
        var view = new GameObject(title, typeof(RectTransform), typeof(CanvasRenderer),
            sprite != null ? typeof(Image) : typeof(CombatFeedbackGraphic));
        view.transform.SetParent(generatedFeedbackCanvas.transform, false);
        var graphic = view.GetComponent<Graphic>();
        if (graphic is Image icon)
        {
            icon.sprite = sprite;
            icon.preserveAspect = true;
        }
        graphic.raycastTarget = false;
        graphic.color = sprite != null ? Color.white : color;
        var rect = (RectTransform)view.transform;
        rect.sizeDelta = Vector2.one * size;
        return rect;
    }

    private void EnsureLockFeedback()
    {
        if (lockChargeMarker == null) lockChargeMarker = CreateFeedbackRing("Lock charge", Color.white, lockChargeStartSize, lockChargeSprite);
        if (lockMarker == null) lockMarker = CreateFeedbackRing("Locked", Color.green, lockChargeEndSize, lockCompleteSprite);
        if (lockText == null) lockText = CreateCentralPrompt("Lock status", -105f, 20f);
    }

    private TMP_Text CreateCentralPrompt(string title, float y, float fontSize)
    {
        EnsureFeedbackCanvas();
        var go = new GameObject(title, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(generatedFeedbackCanvas.transform, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (feedbackFont != null) text.font = feedbackFont;
        text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.sizeDelta = new Vector2(260f, 40f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        return text;
    }

    private void UpdateThreatWarning(bool show)
    {
        Visible(incomingMagicWarning != null ? incomingMagicWarning.gameObject : null, false);
        if (!show)
        {
            incomingThreat = null;
            threatStrength = warningPhase = nextThreatCheck = 0f;
            HideThreatIndicator();
            return;
        }
        if (show && Time.unscaledTime >= nextThreatCheck)
        {
            nextThreatCheck = Time.unscaledTime + 0.05f;
            MagicProjectile.TryGetIncomingThreat(owner, warningDistance, out _, out incomingThreat,
                warningLeadSeconds, offscreenThreatFilter);
        }
        if (!HasIncomingThreat() || !IsOffscreenThreat(incomingThreat))
        {
            // The camera may have turned since the last 20 Hz candidate search.
            if (HasIncomingThreat()) nextThreatCheck = 0f;
            threatStrength = warningPhase = 0f;
            HideThreatIndicator();
            return;
        }
        float distance = Vector3.Distance(owner.LockAimPoint, incomingThreat.transform.position);
        bool timed = incomingThreat.TryGetImpactTime(owner, out float impactSeconds);
        threatStrength = timed ? 1f - Mathf.Clamp01(impactSeconds / Mathf.Max(0.1f, warningLeadSeconds))
            : 1f - Mathf.Clamp01(distance / Mathf.Max(1f, warningDistance));
        warningPhase = Mathf.Repeat(warningPhase + Time.unscaledDeltaTime *
            Mathf.Lerp(warningFarFrequency, warningNearFrequency, threatStrength), 1f);
        bool firstFrame = warningPhase < 0.5f;
        bool custom = warningSpriteA != null || warningSpriteB != null;
        Graphic warning = EnsureThreatIndicator(custom);
        bool cue = enableParryTimingCue && owner.CanStartParryNow && !owner.IsParrying &&
            !Input.GetMouseButton(1) && timed &&
            IsParryCueTiming(impactSeconds, owner.ParryWindowSeconds, parryCueSafetyMargin);
        UpdateParryCue(warning, cue);
        // Hold a readable symbol in the timing window rather than hiding it mid-blink.
        if (cue) firstFrame = true;
        Visible(incomingMagicIndicator != null ? incomingMagicIndicator.gameObject : null, custom);
        Visible(generatedWarning != null ? generatedWarning.gameObject : null, !custom);
        if (custom)
            incomingMagicIndicator.sprite = SelectWarningSprite(warningSpriteA, warningSpriteB, firstFrame);
        Color tint = warningIconColor;
        // A/B remain readable while alternating. A single sprite / fallback pulses.
        tint.a *= cue || (custom && warningSpriteA != null && warningSpriteB != null) ? 1f : firstFrame ? 1f : 0.15f;
        warning.color = tint;
        warning.raycastTarget = false;
        float punch = cue ? Mathf.Lerp(parryCuePunchScale, 1f,
            Mathf.Clamp01((Time.unscaledTime - parryCueStarted) / Mathf.Max(0.05f, parryCuePunchSeconds))) : 1f;
        warning.rectTransform.sizeDelta = Vector2.one * warningIconSize * punch;
        EnsureThreatGlow(warning);
        Color glow = cue ? new Color(parryCueColor.r, parryCueColor.g, parryCueColor.b, warningGlowColor.a) : warningGlowColor;
        // The halo stays visible between flashes, so the direction is never lost.
        glow.a *= Mathf.Lerp(0.6f, 1f, threatStrength);
        warningGlow.color = glow;
        warningGlow.rectTransform.sizeDelta = Vector2.one * warningIconSize * warningGlowScale;
        if (warningFrame != null)
        {
            warningFrame.color = cue ? parryCueColor : new Color(1f, 0.35f, 0.4f, 0.9f);
            warningFrame.rectTransform.sizeDelta = Vector2.one * warningIconSize * 1.05f * punch;
        }
        if (cue) parryCueRing.sizeDelta = Vector2.one * warningIconSize * parryCueRingScale * punch;
        PlaceThreatIndicator();
    }

    private static Sprite SelectWarningSprite(Sprite a, Sprite b, bool firstFrame)
        => a != null && b != null ? (firstFrame ? a : b) : a != null ? a : b;

    private static bool IsParryCueTiming(float seconds, float window, float margin)
    {
        if (window <= 0f || float.IsNaN(window) || float.IsInfinity(window)) return false;
        // Never extend the actual window. Very short configured windows still get a cue.
        float usableWindow = window - Mathf.Clamp(margin, 0f, window * 0.5f);
        return seconds >= 0f && seconds <= usableWindow;
    }

    private void UpdateParryCue(Graphic warning, bool show)
    {
        if (show)
        {
            if (!parryCueActive || parryCueThreat != incomingThreat) parryCueStarted = Time.unscaledTime;
            if (parryCueRing == null)
            {
                parryCueRing = CreateFeedbackRing("Parry timing ring", parryCueColor, warningIconSize * parryCueRingScale, null);
                parryCueRingGraphic = parryCueRing.GetComponent<Graphic>();
            }
            if (parryCueRing.parent != warning.transform.parent)
                parryCueRing.SetParent(warning.transform.parent, false);
            parryCueRingGraphic.color = parryCueColor;
        }
        parryCueActive = show;
        parryCueThreat = show ? incomingThreat : null;
        Visible(parryCueRing != null ? parryCueRing.gameObject : null, show);
        if (show && showCentralParryCue && parryTimingText == null)
            parryTimingText = CreateCentralPrompt("Parry now", -65f, 26f);
        if (parryTimingText != null)
        {
            parryTimingText.color = parryCueColor;
            SetText(parryTimingText, "PARRY  [RMB]");
            Visible(parryTimingText.gameObject, show && showCentralParryCue);
        }
    }

    private bool HasIncomingThreat() => owner != null && owner.Object != null && owner.Object.IsValid &&
        incomingThreat != null && incomingThreat.Object != null && incomingThreat.Object.IsValid && !incomingThreat.Finished;

    private Graphic EnsureThreatIndicator(bool custom)
    {
        EnsureFeedbackCanvas();
        if (custom)
        {
            if (incomingMagicIndicator == null)
            {
                var icon = new GameObject("Incoming magic direction", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                icon.transform.SetParent(generatedFeedbackCanvas.transform, false);
                incomingMagicIndicator = icon.GetComponent<Image>();
            }
            incomingMagicIndicator.preserveAspect = true;
            return incomingMagicIndicator;
        }
        if (generatedWarning == null)
        {
            generatedWarning = CreateCentralPrompt("Incoming magic exclamation", 0f, warningIconSize * 0.8f);
            generatedWarning.text = "!";
            generatedWarning.margin = Vector4.zero;
        }
        return generatedWarning;
    }

    private void HideThreatIndicator()
    {
        Visible(incomingMagicIndicator != null ? incomingMagicIndicator.gameObject : null, false);
        Visible(generatedWarning != null ? generatedWarning.gameObject : null, false);
        Visible(warningGlow != null ? warningGlow.gameObject : null, false);
        Visible(warningFrame != null ? warningFrame.gameObject : null, false);
        Visible(parryCueRing != null ? parryCueRing.gameObject : null, false);
        Visible(parryTimingText != null ? parryTimingText.gameObject : null, false);
        parryCueActive = false;
        parryCueThreat = null;
    }

    private void EnsureThreatGlow(Graphic warning)
    {
        if (warningGlow == null)
        {
            var view = new GameObject("Incoming magic red halo", typeof(RectTransform), typeof(CanvasRenderer), typeof(CombatFeedbackGraphic));
            warningGlow = view.GetComponent<CombatFeedbackGraphic>();
            warningGlow.Glow = true;
            warningGlow.raycastTarget = false;
        }
        // Put the halo behind the icon even when the designer supplies a different canvas.
        if (warningGlow.transform.parent != warning.transform.parent)
            warningGlow.transform.SetParent(warning.transform.parent, false);
        if (warningGlow.transform.GetSiblingIndex() >= warning.transform.GetSiblingIndex())
            warningGlow.transform.SetSiblingIndex(warning.transform.GetSiblingIndex());
        if (showWarningFrame && warningFrame == null)
        {
            var view = new GameObject("Incoming magic HUD frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(CombatFeedbackGraphic));
            warningFrame = view.GetComponent<CombatFeedbackGraphic>();
            warningFrame.CornerFrame = true;
            warningFrame.raycastTarget = false;
        }
        if (warningFrame != null)
        {
            if (warningFrame.transform.parent != warning.transform.parent)
                warningFrame.transform.SetParent(warning.transform.parent, false);
            if (warningFrame.transform.GetSiblingIndex() >= warning.transform.GetSiblingIndex())
                warningFrame.transform.SetSiblingIndex(warning.transform.GetSiblingIndex());
        }
    }

    private void PlaceThreatIndicator()
    {
        if (!HasIncomingThreat() || worldCamera == null) { HideThreatIndicator(); return; }
        // Recheck after camera rendering too: no one-frame on-screen warning after a turn.
        if (!IsOffscreenThreat(incomingThreat))
        {
            nextThreatCheck = 0f;
            HideThreatIndicator();
            return;
        }
        Graphic warning = warningSpriteA != null || warningSpriteB != null ? incomingMagicIndicator : generatedWarning;
        if (warning == null) return;
        // Use the viewing camera's origin AND rotation, independently of the pilot's facing.
        // Its unshaken Transform keeps the warning stable during render-only speed shake.
        Vector3 relative = GetThreatViewDirection(worldCamera.transform.position, worldCamera.transform.rotation,
            incomingThreat.transform.position);
        float padding = Mathf.Max(warningEdgePadding, warningIconSize * warningGlowScale * 0.5f + 8f);
        Vector2 point = GetThreatScreenPoint(relative, worldCamera.pixelRect, padding);
        bool placed = PlaceScreen(warning.rectTransform, point);
        Visible(warning.gameObject, placed);
        if (warningGlow != null)
            Visible(warningGlow.gameObject, placed && PlaceScreen(warningGlow.rectTransform, point));
        if (warningFrame != null)
            Visible(warningFrame.gameObject, placed && showWarningFrame && PlaceScreen(warningFrame.rectTransform, point));
        if (parryCueRing != null)
            Visible(parryCueRing.gameObject, placed && parryCueActive && PlaceScreen(parryCueRing, point));
    }

    private static Vector3 GetThreatViewDirection(Vector3 cameraPosition, Quaternion cameraRotation, Vector3 threatPosition)
    {
        return Quaternion.Inverse(cameraRotation) * (threatPosition - cameraPosition);
    }

    private bool IsOffscreenThreat(MagicProjectile shot)
    {
        if (shot == null || worldCamera == null) return false;
        Vector3 local = GetThreatViewDirection(worldCamera.transform.position, worldCamera.transform.rotation,
            shot.transform.position);
        return !IsInsideThreatViewport(local, worldCamera.projectionMatrix, worldCamera.nearClipPlane, worldCamera.farClipPlane);
    }

    private static bool IsInsideThreatViewport(Vector3 cameraLocal, Matrix4x4 projection, float nearClip, float farClip)
    {
        // Use the unshaken camera pose but the actual projection, including FOV/aspect changes.
        if (cameraLocal.z < nearClip || cameraLocal.z > farClip || cameraLocal.z <= 0f) return false;
        Vector4 clip = projection * new Vector4(cameraLocal.x, cameraLocal.y, -cameraLocal.z, 1f);
        return clip.w > 0f && clip.x >= -clip.w && clip.x <= clip.w && clip.y >= -clip.w && clip.y <= clip.w;
    }

    private static Vector2 GetThreatScreenPoint(Vector3 direction, Rect screen, float padding)
    {
        Vector2 bearing = new Vector2(direction.x, direction.y);
        // Rear threats occupy the lower arc; dead-ahead is top, directly behind is bottom.
        if (direction.z < 0f) bearing.y = -Mathf.Abs(direction.y) - Mathf.Abs(direction.z);
        if (bearing.sqrMagnitude < 0.0001f) bearing = Vector2.up;
        bearing.Normalize();
        Vector2 half = new Vector2(Mathf.Max(0f, screen.width * 0.5f - padding),
            Mathf.Max(0f, screen.height * 0.5f - padding));
        return screen.center + Vector2.Scale(bearing, half);
    }

    // Input samples the assigned Dot, never the moving nose-direction circle.
    // Only the owning client reads UI/camera state; the host receives a direction.
    public static Ray GetMagicAimRay(Camera camera)
    {
        if (camera == null) return default;
        Vector2 screenPoint = camera.pixelRect.center;
        RectTransform dot = instance != null && instance.isActiveAndEnabled ? instance.desiredAimMarker : null;
        if (dot != null && dot.gameObject.activeInHierarchy)
        {
            Canvas canvas = dot.GetComponentInParent<Canvas>();
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            Camera uiCamera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, dot.TransformPoint(dot.rect.center));
        }
        return camera.ScreenPointToRay(screenPoint);
    }

    // Run after the network render pose/camera, avoiding a one-frame lag in the small circle.
    private void BindFlightAim()
    {
        Player pilot = Player.LocalPlayer;
        if (worldCamera == null) worldCamera = Camera.main;
        CameraFollow follow = worldCamera != null ? worldCamera.GetComponent<CameraFollow>() : null;
        bool show = isActiveAndEnabled && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            !MenuOpen && pilot != null && pilot.Object != null && pilot.Object.IsValid && pilot.IsAlive &&
            !pilot.IsReturningToMap && follow != null && !follow.IsBoundaryPresentationActive;
        bool desiredVisible = false;
        bool forwardVisible = false;
        bool aligned = false;
        if (show)
        {
            Vector3 desiredPoint = follow.GetDisplayedAimPoint();
            Vector3 origin = pilot.LockAimPoint;
            float distance = Mathf.Max(1f, Vector3.Distance(origin, desiredPoint));
            Vector3 forwardPoint = origin + pilot.transform.forward * distance;
            // Never project the fixed crosshair through a shaken render matrix.
            desiredVisible = desiredAimMarker != null && PlaceScreen(desiredAimMarker, worldCamera.pixelRect.center);
            forwardVisible = forwardAimMarker != null && forwardAimMarker != desiredAimMarker &&
                PlaceStableAim(forwardAimMarker, forwardPoint);
            aligned = pilot.IsAimAligned((desiredPoint - origin).normalized);
            SetText(aimAlignmentText, aligned ? "AIM ALIGNED" : "ALIGNING");
        }
        SetAimColors(show && aligned);
        Visible(desiredAimMarker != null ? desiredAimMarker.gameObject : null, desiredVisible);
        if (forwardAimMarker != desiredAimMarker)
            Visible(forwardAimMarker != null ? forwardAimMarker.gameObject : null, forwardVisible);
        Visible(aimAlignmentText != null ? aimAlignmentText.gameObject : null, show);
        // Reposition after Fusion/camera rendering, without advancing the blink timer twice.
        if (show) PlaceThreatIndicator();
        else HideThreatIndicator();
        if (show) PlaceHitMarker();
        else ClearHitMarker();
    }

    public void ToggleMenu() => SetMenuOpen(!MenuOpen);

    private void SetAimColors(bool aligned)
    {
        Image desired = desiredAimImage != null ? desiredAimImage :
            desiredAimMarker != null ? desiredAimMarker.GetComponent<Image>() : null;
        Image forward = forwardAimImage != null ? forwardAimImage :
            forwardAimMarker != null ? forwardAimMarker.GetComponent<Image>() : null;
        TintAimImage(desired, aligned, ref tintedDesiredImage, ref originalDesiredColor);
        TintAimImage(forward, aligned, ref tintedForwardImage, ref originalForwardColor);
    }

    private static void TintAimImage(Image image, bool aligned, ref Image previousImage, ref Color originalColor)
    {
        if (previousImage != image)
        {
            if (previousImage != null) previousImage.color = originalColor;
            previousImage = image;
            if (image != null) originalColor = image.color;
        }
        if (image != null)
            image.color = aligned ? new Color(0f, 1f, 0f, originalColor.a) : originalColor;
    }
    public void Resume() => SetMenuOpen(false);

    private void SetMenuOpen(bool open)
    {
        BattleManager battle = BattleManager.Instance;
        MenuOpen = open && menuPanel != null && battle != null &&
            (battle.IsGameplayActive || battle.IsBattleEnded);
        Visible(menuPanel, MenuOpen);
        bool flight = battle != null && battle.IsGameplayActive && !MenuOpen;
        Cursor.lockState = flight ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !flight;
    }

    public static void ShowHitMarker(Player attacker)
    {
        if (instance == null || !instance.isActiveAndEnabled || attacker == null ||
            attacker.Object == null || !attacker.Object.IsValid || !attacker.Object.HasInputAuthority ||
            attacker != Player.LocalPlayer || !attacker.IsAlive || MenuOpen ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        instance.EnsureHitMarker();
        instance.hitMarkerStarted = Time.unscaledTime;
        // Every confirmed hit restores full visibility, even during the previous fade.
        instance.UpdateHitMarker(true);
    }

    private void EnsureHitMarker()
    {
        Graphic next = hitMarkerImage;
        if (next == null)
        {
            if (generatedHitMarker == null)
            {
                EnsureFeedbackCanvas();
                var view = new GameObject("Confirmed hit marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(CombatFeedbackGraphic));
                view.transform.SetParent(generatedFeedbackCanvas.transform, false);
                generatedHitMarker = view.GetComponent<CombatFeedbackGraphic>();
                generatedHitMarker.HitMarker = true;
            }
            generatedHitMarker.HitMarkerAngles = hitMarkerAngles;
            generatedHitMarker.HitMarkerRadius = hitMarkerRadius;
            generatedHitMarker.HitMarkerLength = hitMarkerLength;
            generatedHitMarker.HitMarkerWidth = hitMarkerWidth;
            generatedHitMarker.rectTransform.sizeDelta = Vector2.one *
                (2f * Mathf.Max(0f, hitMarkerRadius) + Mathf.Max(1f, hitMarkerLength) + Mathf.Max(1f, hitMarkerWidth));
            generatedHitMarker.SetVerticesDirty();
            next = generatedHitMarker;
        }
        if (activeHitMarker != next)
        {
            if (activeHitMarker != null)
            {
                activeHitMarker.enabled = false;
                activeHitMarker.color = hitMarkerBaseColor;
            }
            activeHitMarker = next;
            hitMarkerBaseColor = next == generatedHitMarker ? hitMarkerColor : next.color;
        }
        if (next == generatedHitMarker) hitMarkerBaseColor = hitMarkerColor;
        next.raycastTarget = false;
        Visible(next.gameObject, true);
    }

    public static float HitMarkerAlpha(float age, float holdSeconds, float fadeSeconds)
    {
        if (age < 0f || float.IsNaN(age) || float.IsInfinity(age)) return 0f;
        return 1f - Mathf.Clamp01((age - Mathf.Max(0f, holdSeconds)) / Mathf.Max(0.01f, fadeSeconds));
    }

    private void UpdateHitMarker(bool show)
    {
        if (!show) { ClearHitMarker(); return; }
        if (activeHitMarker == null) return;
        float alpha = HitMarkerAlpha(Time.unscaledTime - hitMarkerStarted, hitMarkerHoldSeconds, hitMarkerFadeSeconds);
        if (alpha <= 0f) { ClearHitMarker(); return; }
        Color tint = hitMarkerBaseColor;
        tint.a *= alpha;
        activeHitMarker.color = tint;
        activeHitMarker.enabled = true;
        PlaceHitMarker();
    }

    private void PlaceHitMarker()
    {
        if (activeHitMarker == null || !activeHitMarker.enabled) return;
        RectTransform center = hitMarkerCenter != null ? hitMarkerCenter : desiredAimMarker;
        Vector2 point;
        if (center != null)
        {
            Canvas canvas = center.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            point = RectTransformUtility.WorldToScreenPoint(uiCamera, center.TransformPoint(center.rect.center));
        }
        else point = worldCamera != null ? worldCamera.pixelRect.center : new Vector2(Screen.width, Screen.height) * 0.5f;
        // Do not toggle layout / colliders / the reticle itself; this is a separate overlay.
        activeHitMarker.enabled = PlaceScreen(activeHitMarker.rectTransform, point);
    }

    private void ClearHitMarker()
    {
        hitMarkerStarted = float.NegativeInfinity;
        if (activeHitMarker != null)
        {
            activeHitMarker.enabled = false;
            activeHitMarker.color = hitMarkerBaseColor;
        }
        if (hitMarkerImage != null) hitMarkerImage.enabled = false;
    }

    public static void ShowDamage(Vector3 position, float amount)
    {
        if (instance == null || !instance.isActiveAndEnabled || amount <= 0f ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        instance.ShowDamageLabel(position, amount);
    }

    // Called once by the victim-only RPC, never by a per-frame attacker Transform lookup.
    public static void ShowReceivedDamage(Player victim, Vector3 attackerPositionAtHit)
    {
        if (instance == null || !instance.isActiveAndEnabled || victim == null ||
            victim.Object == null || !victim.Object.IsValid || !victim.Object.HasInputAuthority ||
            victim != Player.LocalPlayer || MenuOpen) return;
        if (instance.damageIndicator == null) instance.damageIndicator = DamageIndicator.Instance;
        if (instance.worldCamera == null) instance.worldCamera = Camera.main;
        if (instance.damageIndicator == null || !instance.damageIndicator.isActiveAndEnabled || instance.worldCamera == null)
            return;
        instance.damageIndicator.SetReceiver(instance.worldCamera.transform, victim.transform);
        // No reuse key: simultaneous hits retain their own frozen source positions.
        instance.damageIndicator.TriggerDamageSense(attackerPositionAtHit, instance.receivedDamageColor);
    }

    private void ShowDamageLabel(Vector3 position, float amount)
    {
        // Reuse designer-supplied slots; if none are supplied, draw nothing.
        for (int n = 0; n < damageSlots.Length; n++)
        {
            int index = (nextDamageSlot + n) % damageSlots.Length;
            DamageSlot slot = damageSlots[index];
            if (slot.Label == null) continue;
            nextDamageSlot = (index + 1) % damageSlots.Length;
            slot.Position = position;
            slot.Started = Time.unscaledTime;
            slot.Active = true;
            slot.Label.text = amount.ToString("0");
            return;
        }
    }

    private void UpdateDamage()
    {
        foreach (DamageSlot slot in damageSlots)
        {
            if (slot.Label == null || !slot.Active) continue;
            float age = Mathf.Clamp01((Time.unscaledTime - slot.Started) / Mathf.Max(0.1f, damageDuration));
            if (age >= 1f) { ResetDamage(slot); continue; }
            Visible(slot.Label.gameObject, !MenuOpen && Place(slot.Label.rectTransform, slot.Position + Vector3.up * age * damageRise));
            slot.Label.transform.localScale = slot.Scale * Mathf.Lerp(1f, 0.5f, age);
            Color tint = slot.Color;
            tint.a *= 1f - age;
            slot.Label.color = tint;
        }
    }

    private bool Place(RectTransform rect, Vector3 position)
    {
        if (worldCamera == null || rect.parent is not RectTransform parent) return false;
        Vector3 viewport = worldCamera.WorldToViewportPoint(position);
        if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return false;
        return PlaceScreen(rect, worldCamera.WorldToScreenPoint(position));
    }

    private bool PlaceStableAim(RectTransform rect, Vector3 position)
    {
        // Render-only camera shake must not make the aiming circle jitter.
        Vector3 local = worldCamera.transform.InverseTransformPoint(position);
        if (local.z <= 0f) return false;
        Vector4 clip = worldCamera.projectionMatrix * new Vector4(local.x, local.y, -local.z, 1f);
        if (Mathf.Abs(clip.w) < 0.0001f) return false;
        Vector2 viewport = new Vector2(clip.x / clip.w, clip.y / clip.w) * 0.5f + Vector2.one * 0.5f;
        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;
        Rect pixels = worldCamera.pixelRect;
        return PlaceScreen(rect, new Vector2(pixels.x + viewport.x * pixels.width, pixels.y + viewport.y * pixels.height));
    }

    private bool PlaceScreen(RectTransform rect, Vector2 screenPoint)
    {
        if (rect.parent is not RectTransform parent) return false;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, uiCamera, out Vector2 point))
            return false;
        rect.localPosition = new Vector3(point.x, point.y, rect.localPosition.z);
        return true;
    }

    private static void SetText(TMP_Text target, string value) { if (target != null) target.text = value; }
    private static void Visible(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    private static string MagicName(MagicType magic) => magic.ToString();
    private static void SetBar(Slider slider, Image fill, float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (slider != null) slider.SetValueWithoutNotify(Mathf.Lerp(slider.minValue, slider.maxValue, fraction));
        if (fill != null) fill.fillAmount = fraction;
    }

    private static void ResetDamage(DamageSlot slot)
    {
        slot.Active = false;
        if (slot.Label == null) return;
        slot.Label.transform.localScale = slot.Scale;
        slot.Label.color = slot.Color;
        Visible(slot.Label.gameObject, false);
    }

    private void HideViews()
    {
        ClearHitMarker();
        Visible(lockChargeMarker != null ? lockChargeMarker.gameObject : null, false);
        UpdateThreatWarning(false);
        SetAimColors(false);
        Visible(desiredAimMarker != null ? desiredAimMarker.gameObject : null, false);
        Visible(forwardAimMarker != null ? forwardAimMarker.gameObject : null, false);
        Visible(aimAlignmentText != null ? aimAlignmentText.gameObject : null, false);
        Visible(hudRoot, false); Visible(playerPanel, false);
        Visible(respawnPanel, false); Visible(resultPanel, false); Visible(menuPanel, false);
        Visible(resultText != null ? resultText.gameObject : null, false);
        Visible(respawnText != null ? respawnText.gameObject : null, false);
        BindPlayer(false);
        BindAim(false);
        if (damageSlots != null) foreach (DamageSlot slot in damageSlots) ResetDamage(slot);
    }

    private void OnDisable()
    {
        BindOwner(null);
        Canvas.willRenderCanvases -= BindFlightAim;
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
        HideViews();
        if (instance != this) return;
        instance = null;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
