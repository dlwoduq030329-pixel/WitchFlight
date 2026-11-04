using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattlePerkUI
{
    [Header("Level up: MP wings and prompt (empty = auto bind/create)")]
    [SerializeField] private RectTransform levelUpManaAnchor;
    [Tooltip("MP바의 Wing 오브젝트. MP바 자체가 아니라 날개만 연결하세요.")]
    [SerializeField] private RectTransform levelUpWings;
    [SerializeField] private TMP_Text perkPromptText;
    [SerializeField] private Vector2 perkPromptOffset = new Vector2(0f, -30f);
    [SerializeField, Min(1f)] private float levelUpWingScale = 1.3f;
    [SerializeField, Min(.05f)] private float levelUpWingSeconds = .55f;
    [SerializeField, Min(.1f)] private float perkUnavailableSeconds = 1.2f;
    private PlayerData levelUpObservedPlayer;
    private int levelUpObservedEarned = -1;
    private float levelUpWingStarted = -100f, perkUnavailableUntil;
    private float noEnemyInRangeUntil;
    private float manaFeedbackPriorityUntil;
    public static void PrioritizeManaFeedback()
    {
        if (active == null) return;
        active.noEnemyInRangeUntil = 0f;
        active.manaFeedbackPriorityUntil = Time.unscaledTime + 1f;
    }
    public static void ShowNoEnemyInRange()
    {
        if (active != null && active.isActiveAndEnabled && Time.unscaledTime >= active.manaFeedbackPriorityUntil)
            active.noEnemyInRangeUntil = Time.unscaledTime + 1.5f;
    }
    private bool levelUpBindingsReady, wingScaleSaved, generatedPerkPrompt;
    private Vector3 levelUpOriginalWingScale;
    private Image levelUpWingFlash;
    private CombatFeedbackGraphic[] levelUpSparkles;
    private readonly Vector3[] perkPromptAnchorCorners = new Vector3[4];

    private bool CanOfferPerk(PlayerData data)
    {
        return Ready(data) && data.CanSelectPerks && !requestPending &&
            (data.GetPerkCandidate(false) != PerkId.None || data.GetPerkCandidate(true) != PerkId.None);
    }

    private void EnsureLevelUpBindings()
    {
        if (levelUpBindingsReady) return;
        if (levelUpManaAnchor == null) levelUpManaAnchor = GetComponent<BattleHud>()?.ManaBarRect;
        if (levelUpManaAnchor == null)
            foreach (Slider slider in FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (slider.gameObject.scene == gameObject.scene &&
                    (slider.name == "ManaSlider" || slider.name == "MPSlider" || slider.name == "MpSlider"))
                { levelUpManaAnchor = slider.transform as RectTransform; break; }
        if (levelUpWings == null && levelUpManaAnchor != null)
            foreach (RectTransform child in levelUpManaAnchor.GetComponentsInChildren<RectTransform>(true))
                if (child.name == "Wing") { levelUpWings = child; break; }
        if (perkPromptText == null)
        {
            // A separate overlay avoids inheriting MP masks/CanvasGroup fades or its oversized layout box.
            perkPromptText = Label("Perk selection prompt", CanvasRoot(), Vector2.zero, new Vector2(510f, 40f), 20f, TextAlignmentOptions.Center);
            generatedPerkPrompt = true;
            RectTransform rect = perkPromptText.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        }
        perkPromptText.richText = false;
        perkPromptText.raycastTarget = false;
        perkPromptText.outlineColor = Color.black;
        perkPromptText.outlineWidth = .25f;
        if (levelUpWings != null)
        {
            levelUpOriginalWingScale = levelUpWings.localScale;
            wingScaleSaved = true;
            Image wing = levelUpWings.GetComponent<Image>();
            if (wing != null)
            {
                levelUpWingFlash = Image("Level up wing flash", levelUpWings, Vector2.zero, levelUpWings.rect.size, Color.white);
                levelUpWingFlash.sprite = wing.sprite;
                levelUpWingFlash.rectTransform.anchorMin = Vector2.zero;
                levelUpWingFlash.rectTransform.anchorMax = Vector2.one;
                levelUpWingFlash.rectTransform.sizeDelta = Vector2.zero;
                levelUpWingFlash.enabled = false;
            }
            levelUpSparkles = new CombatFeedbackGraphic[2];
            for (int i = 0; i < 2; i++)
            {
                RectTransform rect = Rect("Level up sparkle", levelUpWings, new Vector2(i, .7f), Vector2.zero, new Vector2(32f, 32f));
                var sparkle = rect.gameObject.AddComponent<CombatFeedbackGraphic>();
                sparkle.HitMarker = true;
                sparkle.HitMarkerAngles = new Vector4(0, 90, 180, 270);
                sparkle.HitMarkerRadius = 0f;
                sparkle.HitMarkerLength = 28f;
                sparkle.HitMarkerWidth = 5f;
                sparkle.raycastTarget = false;
                sparkle.enabled = false;
                levelUpSparkles[i] = sparkle;
            }
        }
        levelUpBindingsReady = true;
    }

    private void UpdateLevelUpFeedback()
    {
        EnsureLevelUpBindings();
        bool ready = Ready(local);
        if (levelUpObservedPlayer != local)
        {
            levelUpObservedPlayer = local;
            levelUpObservedEarned = -1;
            ResetLevelUpFeedback();
        }
        if (ready)
        {
            if (local.PerkChoicesEarned > 0 && local.PerkChoicesEarned > levelUpObservedEarned)
                levelUpWingStarted = Time.unscaledTime;
            levelUpObservedEarned = local.PerkChoicesEarned;
        }
        if (Allowed && !BattleEmoteUI.CapturesMouse && ready && !local.CanSelectPerks && Input.GetKeyDown(KeyCode.Space))
            perkUnavailableUntil = Time.unscaledTime + perkUnavailableSeconds;
        bool available = Allowed && !BattleEmoteUI.CapturesMouse && CanOfferPerk(local);
        if (available) perkUnavailableUntil = 0f;
        bool denied = Allowed && ready && Time.unscaledTime < perkUnavailableUntil;
        bool show = denied || (available && !gate.Showing);
        perkPromptText.text = denied ? "레벨이 부족합니다" : "space바를 눌러서 특전을 선택하세요";
        perkPromptText.color = denied ? new Color(1f, .75f, .65f, 1f) : Color.white;
        perkPromptText.enabled = show;
        if (Allowed && ready && Time.unscaledTime < noEnemyInRangeUntil)
        {
            perkPromptText.text = "탐지 거리 내에 적이 없습니다!";
            perkPromptText.color = new Color(1f, .15f, .15f, Mathf.Clamp01((noEnemyInRangeUntil - Time.unscaledTime) / .35f));
            perkPromptText.enabled = true;
        }
    }

    internal static float WingPulse(float elapsed, float duration)
    {
        float t = Mathf.Clamp01(elapsed / Mathf.Max(.05f, duration));
        return elapsed < 0f || t >= 1f ? 0f : Mathf.Sin(t * Mathf.PI);
    }

    private void AnimateLevelUpWings()
    {
        PositionGeneratedPerkPrompt();
        if (!Allowed || !Ready(local)) { ResetLevelUpFeedback(); return; }
        float pulse = WingPulse(Time.unscaledTime - levelUpWingStarted, levelUpWingSeconds);
        if (wingScaleSaved && levelUpWings != null)
            levelUpWings.localScale = levelUpOriginalWingScale * Mathf.Lerp(1f, levelUpWingScale, pulse);
        if (levelUpWingFlash != null)
        {
            levelUpWingFlash.enabled = pulse > 0f;
            levelUpWingFlash.color = new Color(1f, 1f, 1f, pulse * .9f);
        }
        if (levelUpSparkles != null)
            foreach (CombatFeedbackGraphic sparkle in levelUpSparkles)
            {
                sparkle.enabled = pulse > 0f;
                sparkle.color = new Color(1f, .95f, .65f, pulse);
                sparkle.rectTransform.localScale = Vector3.one * Mathf.Lerp(.5f, 1.4f, pulse);
            }
    }

    private void PositionGeneratedPerkPrompt()
    {
        if (!generatedPerkPrompt || perkPromptText == null || canvas == null) return;
        RectTransform parent = canvas.transform as RectTransform;
        RectTransform anchor = levelUpWings != null ? levelUpWings : levelUpManaAnchor;
        Vector2 point = new Vector2(0f, parent.rect.yMin + 100f);
        if (anchor != null)
        {
            anchor.GetWorldCorners(perkPromptAnchorCorners);
            Canvas source = anchor.GetComponentInParent<Canvas>();
            Camera sourceCamera = source != null && source.renderMode != RenderMode.ScreenSpaceOverlay ? source.worldCamera : null;
            Vector3 bottom = (perkPromptAnchorCorners[0] + perkPromptAnchorCorners[3]) * .5f;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(sourceCamera, bottom);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out point);
        }
        RectTransform label = perkPromptText.rectTransform;
        float width = Mathf.Min(510f, Mathf.Max(1f, parent.rect.width - 16f));
        label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        Vector2 half = label.rect.size * .5f;
        point += perkPromptOffset;
        label.anchoredPosition = ClampPerkPrompt(point, parent.rect, half);
    }

    internal static Vector2 ClampPerkPrompt(Vector2 point, Rect bounds, Vector2 half)
    {
        point.x = Mathf.Clamp(point.x, bounds.xMin + half.x + 8f, bounds.xMax - half.x - 8f);
        point.y = Mathf.Clamp(point.y, bounds.yMin + half.y + 8f, bounds.yMax - half.y - 8f);
        return point;
    }

    private void ResetLevelUpFeedback()
    {
        if (wingScaleSaved && levelUpWings != null) levelUpWings.localScale = levelUpOriginalWingScale;
        if (levelUpWingFlash != null) levelUpWingFlash.enabled = false;
        if (levelUpSparkles != null) foreach (CombatFeedbackGraphic sparkle in levelUpSparkles) if (sparkle != null) sparkle.enabled = false;
        if (perkPromptText != null) perkPromptText.enabled = false;
        levelUpWingStarted = -100f;
        perkUnavailableUntil = 0f;
        noEnemyInRangeUntil = 0f;
    }

    private void DestroyLevelUpFeedback()
    {
        ResetLevelUpFeedback();
        if (generatedPerkPrompt && perkPromptText != null) Destroy(perkPromptText.gameObject);
        if (levelUpWingFlash != null) Destroy(levelUpWingFlash.gameObject);
        if (levelUpSparkles != null)
            foreach (CombatFeedbackGraphic sparkle in levelUpSparkles) if (sparkle != null) Destroy(sparkle.gameObject);
    }
}
