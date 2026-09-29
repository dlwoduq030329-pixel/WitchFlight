using System;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Data binding only. All visible UI is supplied in the scene by the designer.
[DisallowMultipleComponent]
public sealed class BattleHud : MonoBehaviour
{
    [Header("Panels (keep this component outside these panels)")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private GameObject playerPanel;
    [SerializeField] private GameObject respawnPanel;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private Button resumeButton;
    [Header("Player")]
    [SerializeField] private GridHPBar hpGridBar;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image hpFill;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private Slider apSlider;
    [SerializeField] private Image apFill;
    [SerializeField] private TMP_Text apText;
    [SerializeField] private TMP_Text teamText;
    [SerializeField] private TMP_Text speedStageText;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text selectedMagicText;
    [SerializeField] private TMP_Text magicCostText;
    [Header("Match")]
    [SerializeField] private TMP_Text matchTimeText;
    [SerializeField] private TMP_Text flagText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text respawnText;
    [Header("Aim")]
    [SerializeField] private GameObject reticle;
    [SerializeField] private RectTransform lockMarker;
    [SerializeField] private Image lockProgressFill;
    [SerializeField] private TMP_Text lockText;
    [SerializeField] private Camera worldCamera;
    [Header("Damage numbers (preplaced TMP labels, not prefabs)")]
    [SerializeField] private TMP_Text[] damageLabels = Array.Empty<TMP_Text>();
    [SerializeField, Min(0.1f)] private float damageDuration = 0.9f;
    [SerializeField] private float damageRise = 1.5f;

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
    private bool wasAlive;
    private float respawnAt;
    private float nextRefresh;
    private int nextDamageSlot;
    public static bool MenuOpen { get; private set; }

    private void Awake()
    {
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
        instance = this;
        MenuOpen = false;
        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
    }

    private void Update()
    {
        BattleManager battle = BattleManager.Instance;
        bool show = battle != null && (battle.IsGameplayActive || battle.IsBattleEnded);
        if (!show)
        {
            if (MenuOpen) SetMenuOpen(false);
            respawnAt = 0f;
            owner = null;
            wasAlive = false;
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
                owner = local;
                wasAlive = owner.IsAlive;
            }
            if (wasAlive && !owner.IsAlive)
                respawnAt = Time.unscaledTime + battle.RespawnDelaySeconds;
            if (owner.IsAlive) respawnAt = 0f;
            wasAlive = owner.IsAlive;
        }
        else owner = null;
        if (worldCamera == null) worldCamera = Camera.main;

        Visible(hudRoot, true);
        Visible(playerPanel, valid);
        SetText(matchTimeText, TimeSpan.FromSeconds(Mathf.Max(0f, battle.RemainingSeconds)).ToString(@"mm\:ss"));
        BattleFlag flag = BattleFlag.Instance;
        bool hasFlag = flag != null && flag.Object != null && flag.Object.IsValid && flag.Carrier != PlayerRef.None;
        SetText(flagText, !hasFlag ? "FLAG: collect it at the marker" :
            valid && flag.Carrier == owner.Object.InputAuthority ? "YOU HAVE THE FLAG" : $"FLAG: {flag.Carrier}");
        Visible(resultPanel, battle.IsBattleEnded);
        Visible(resultText != null ? resultText.gameObject : null, battle.IsBattleEnded);
        SetText(resultText, battle.WinningTeam > 0 ? $"TEAM {battle.WinningTeam} WINS" : "DRAW - NO FLAG HOLDER");
        bool respawning = respawnAt > 0f && !battle.IsBattleEnded;
        Visible(respawnPanel, respawning);
        Visible(respawnText != null ? respawnText.gameObject : null, respawning);
        SetText(respawnText, Time.unscaledTime < respawnAt
            ? $"DOWNED - respawn in {Mathf.Max(0f, respawnAt - Time.unscaledTime):0.0}s" : "Waiting for respawn...");
        BindPlayer(valid);
        BindAim(valid && owner.IsAlive && battle.IsGameplayActive && !MenuOpen);
        UpdateDamage();
    }

    private void BindPlayer(bool valid)
    {
        float hpPercent = valid && owner.MaxHp > 0f ? Mathf.Clamp01(owner.NowHp / owner.MaxHp) : 0f;
        if (hpGridBar != null) hpGridBar.UpdateHP(hpPercent);
        SetBar(hpSlider, hpFill, hpPercent);
        SetBar(apSlider, apFill, valid ? owner.NowAp / Mathf.Max(1f, owner.MaxAp) : 0f);
        SetText(hpText, valid ? $"{owner.NowHp:0} / {owner.MaxHp:0}" : "");
        SetText(apText, valid ? $"{owner.NowAp:0} / {owner.MaxAp:0}" : "");
        SetText(teamText, valid ? owner.TeamIndex.ToString() : "");
        SetText(speedStageText, valid ? owner.CurrentSpeedStage.ToString("+0;-0;0") : "");
        SetText(speedText, valid ? $"{owner.CurrentSpeed:0.0} m/s" : "");
        SetText(selectedMagicText, valid ? owner.CurrentMagicSlot == 3 ? "Parry" : MagicName(owner.GetSelectedMagic()) : "");
        SetText(magicCostText, valid ? owner.SelectedMagicApCost.ToString("0") : "");
    }

    private void BindAim(bool show)
    {
        Visible(reticle, show);
        if (!show) lockTarget = null;
        else if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.1f;
            lockTarget = null;
            foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (candidate.Object != null && candidate.Object.IsValid && owner.HasLockTarget(candidate.Object))
                { lockTarget = candidate; break; }
        }
        bool locked = show && owner.SelectedMagicStats.requiresTarget && lockTarget != null &&
            lockTarget.Object != null && lockTarget.Object.IsValid;
        if (locked && lockMarker != null) locked = Place(lockMarker, lockTarget.LockAimPoint);
        Visible(lockMarker != null ? lockMarker.gameObject : null, locked);
        Visible(lockProgressFill != null ? lockProgressFill.gameObject : null, locked);
        Visible(lockText != null ? lockText.gameObject : null, locked);
        if (lockProgressFill != null) lockProgressFill.fillAmount = locked ? Mathf.Clamp01(owner.LockProgress) : 0f;
        SetText(lockText, locked ? owner.IsFullyLocked ? "RELEASE TO FIRE" : $"LOCK {owner.LockProgress:P0}" : "");
    }

    public void ToggleMenu() => SetMenuOpen(!MenuOpen);
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

    public static void ShowDamage(Vector3 position, float amount)
    {
        if (instance == null || !instance.isActiveAndEnabled || amount <= 0f ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        instance.ShowDamageLabel(position, amount);
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
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, worldCamera.WorldToScreenPoint(position), uiCamera, out Vector2 point))
            return false;
        rect.localPosition = new Vector3(point.x, point.y, rect.localPosition.z);
        return true;
    }

    private static void SetText(TMP_Text target, string value) { if (target != null) target.text = value; }
    private static void Visible(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    private static string MagicName(MagicType magic) => magic == MagicType.Dark ? "Wind" : magic == MagicType.Scane ? "Scan" : magic.ToString();
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
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
        HideViews();
        if (instance != this) return;
        instance = null;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
