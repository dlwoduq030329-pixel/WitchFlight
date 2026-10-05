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
    [Tooltip("화면 중앙에 고정할 크로스헤어입니다. 움직이는 원과 별개로 연결합니다.")]
    [SerializeField] private RectTransform desiredAimMarker;
    [Tooltip("움직이는 원형 UI: 캐릭터의 실제 정면 발사 방향입니다.")]
    [SerializeField] private RectTransform forwardAimMarker;
    [Tooltip("큰 원의 Image. 비워두면 Desired Aim Marker 자체의 Image를 사용합니다.")]
    [SerializeField] private Image desiredAimImage;
    [Tooltip("작은 원의 Image. 비워두면 Forward Aim Marker 자체의 Image를 사용합니다.")]
    [SerializeField] private Image forwardAimImage;
    [SerializeField] private TMP_Text aimAlignmentText;
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
    private int nextDamageSlot;
    private Image tintedDesiredImage, tintedForwardImage;
    private Color originalDesiredColor, originalForwardColor;
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
        Canvas.willRenderCanvases += BindFlightAim;
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
            BindOwner(null);
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
                BindOwner(local);
                wasAlive = owner.IsAlive;
            }
            if (wasAlive && !owner.IsAlive)
                respawnAt = Time.unscaledTime + battle.RespawnDelaySeconds;
            if (owner.IsAlive) respawnAt = 0f;
            wasAlive = owner.IsAlive;
        }
        else BindOwner(null);
        if (worldCamera == null) worldCamera = Camera.main;

        Visible(hudRoot, true);
        Visible(playerPanel, valid);
        if (matchTimeText != null)
            SetText(matchTimeText, TimeSpan.FromSeconds(Mathf.Max(0f, battle.RemainingSeconds)).ToString(@"mm\:ss"));
        BattleFlag flag = BattleFlag.Instance;
        bool hasFlag = flag != null && flag.Object != null && flag.Object.IsValid && flag.Carrier != PlayerRef.None;
        if (flagText != null) SetText(flagText, !hasFlag ? "FLAG: collect it at the marker" :
            valid && flag.Carrier == owner.Object.InputAuthority ? "YOU HAVE THE FLAG" : $"FLAG: {flag.Carrier}");
        Visible(resultPanel, battle.IsBattleEnded);
        Visible(resultText != null ? resultText.gameObject : null, battle.IsBattleEnded);
        if (resultText != null) SetText(resultText, battle.WinningTeam > 0 ? $"TEAM {battle.WinningTeam} WINS" : "DRAW - NO FLAG HOLDER");
        bool respawning = respawnAt > 0f && !battle.IsBattleEnded;
        Visible(respawnPanel, respawning);
        Visible(respawnText != null ? respawnText.gameObject : null, respawning);
        if (respawnText != null) SetText(respawnText, Time.unscaledTime < respawnAt
            ? $"DOWNED - respawn in {Mathf.Max(0f, respawnAt - Time.unscaledTime):0.0}s" : "Waiting for respawn...");
        BindPlayer(valid);
        BindAim(valid && owner.IsAlive && battle.IsGameplayActive && !MenuOpen);
        UpdateDamage();
    }

    private void BindPlayer(bool valid)
    {
        SetBar(apSlider, apFill, valid ? owner.NowAp / Mathf.Max(1f, owner.MaxAp) : 0f);
        if (apText != null) SetText(apText, valid ? $"{owner.NowAp:0} / {owner.MaxAp:0}" : "");
        if (teamText != null) SetText(teamText, valid ? owner.TeamIndex.ToString() : "");
        if (speedStageText != null) SetText(speedStageText, valid ? owner.CurrentSpeedStage.ToString("+0;-0;0") : "");
        if (speedText != null) SetText(speedText, valid ? $"{owner.CurrentSpeed:0.0} m/s" : "");
        if (selectedMagicText != null) SetText(selectedMagicText, valid ? owner.CurrentMagicSlot == 3 ? "Parry" : MagicName(owner.GetSelectedMagic()) : "");
        if (magicCostText != null) SetText(magicCostText, valid ? owner.SelectedMagicApCost.ToString("0") : "");
    }

    private void BindOwner(Player next)
    {
        if (owner == next) return;
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
        if (hpGridBar != null) hpGridBar.UpdateHP(fraction);
        SetBar(hpSlider, hpFill, fraction);
        if (hpText != null) SetText(hpText, owner != null ? $"{hp:0} / {maxHp:0}" : "");
    }

    private void BindAim(bool show)
    {
        Visible(reticle, show && desiredAimMarker == null);
        if (!show) lockTarget = null;
        else lockTarget = owner.GetDisplayedLockTarget();
        bool locked = show && owner.SelectedMagicStats.requiresTarget && lockTarget != null &&
            lockTarget.Object != null && lockTarget.Object.IsValid;
        if (locked && lockMarker != null) locked = Place(lockMarker, lockTarget.LockAimPoint);
        Visible(lockMarker != null ? lockMarker.gameObject : null, locked);
        Visible(lockProgressFill != null ? lockProgressFill.gameObject : null, locked);
        Visible(lockText != null ? lockText.gameObject : null, locked);
        if (lockProgressFill != null) lockProgressFill.fillAmount = locked ? Mathf.Clamp01(owner.LockProgress) : 0f;
        if (lockText != null) SetText(lockText, locked ? owner.IsFullyLocked ? "RELEASE TO FIRE" : $"LOCK {owner.LockProgress:P0}" : "");
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
