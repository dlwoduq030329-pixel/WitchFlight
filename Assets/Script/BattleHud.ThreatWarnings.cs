using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleHud
{
    [Header("Multiple incoming projectile warnings")]
    [Tooltip("같은 방향으로 접근하는 투사체들의 느낌표 사이 간격입니다. 아이콘 크기에 대한 배율입니다.")]
    [SerializeField, Min(1f)] private float warningSpacingScale = 1.25f;

    private sealed class ThreatWarningView
    {
        public RectTransform root;
        public Image icon;
        public TMP_Text text;
        public CombatFeedbackGraphic glow, frame, cueRing;
        public MagicProjectile target;
        public float phase, cueStarted;
        public bool cueActive;
    }
    private readonly List<MagicProjectile> incomingThreats = new();
    private readonly List<ThreatWarningView> additionalThreatViews = new();
    private readonly List<Vector2> occupiedThreatPoints = new();
    private readonly HashSet<(MagicProjectile shot, Fusion.NetworkId id)> soundedParryThreats = new();

    private void PlayParryReadySound(MagicProjectile shot)
    {
        soundedParryThreats.RemoveWhere(entry => entry.shot == null || entry.shot.Object == null ||
            !entry.shot.Object.IsValid || entry.shot.Finished || entry.shot.Object.Id != entry.id);
        if (shot == null || shot.Object == null || !shot.Object.IsValid || shot.Finished) return;
        // The nearest warning and pooled extra warnings can swap roles. One projectile still beeps once.
        if (soundedParryThreats.Add((shot, shot.Object.Id))) BattleAudio.Play(BattleSound.ParryReady, local: true);
    }

    private void UpdateAdditionalThreatWarnings(bool show)
    {
        if (!show || owner == null || owner.Object == null || !owner.Object.IsValid)
        {
            HideAdditionalThreatWarnings();
            return;
        }
        int index = 0;
        foreach (MagicProjectile shot in incomingThreats)
        {
            if (shot == incomingThreat || shot == null || shot.Object == null || !shot.Object.IsValid ||
                shot.Finished || !IsOffscreenThreat(shot)) continue;
            if (index == additionalThreatViews.Count) additionalThreatViews.Add(CreateAdditionalThreatWarning());
            ThreatWarningView view = additionalThreatViews[index++];
            if (view.target != shot) { view.target = shot; view.phase = 0f; view.cueActive = false; }
            bool timed = shot.TryGetImpactTime(owner, out float seconds);
            float strength = timed ? 1f - Mathf.Clamp01(seconds / Mathf.Max(.1f, warningLeadSeconds)) :
                1f - Mathf.Clamp01(Vector3.Distance(owner.LockAimPoint, shot.transform.position) / Mathf.Max(1f, warningDistance));
            bool cue = enableParryTimingCue && owner.CanStartParryNow && !owner.IsParrying &&
                !ControlSettings.Held(ControlAction.Parry) && timed && IsParryCueTiming(seconds, owner.ParryWindowSeconds, parryCueSafetyMargin);
            if (cue && !view.cueActive) view.cueStarted = Time.unscaledTime;
            if (cue) PlayParryReadySound(shot);
            view.cueActive = cue;
            view.phase = Mathf.Repeat(view.phase + Time.unscaledDeltaTime * Mathf.Lerp(warningFarFrequency, warningNearFrequency, strength), 1f);
            bool first = cue || view.phase < .5f;
            bool custom = warningSpriteA != null || warningSpriteB != null;
            float punch = cue ? Mathf.Lerp(parryCuePunchScale, 1f,
                Mathf.Clamp01((Time.unscaledTime - view.cueStarted) / Mathf.Max(.05f, parryCuePunchSeconds))) : 1f;
            Color tint = cue ? ParryWarningFlash(parryCueColor, Time.unscaledTime - view.cueStarted) : warningIconColor;
            tint.a *= cue || (custom && warningSpriteA != null && warningSpriteB != null) ? 1f : first ? 1f : .15f;
            view.icon.sprite = SelectWarningSprite(warningSpriteA, warningSpriteB, first);
            view.icon.color = view.text.color = tint;
            view.icon.rectTransform.sizeDelta = view.text.rectTransform.sizeDelta = Vector2.one * warningIconSize * punch;
            view.text.fontSize = warningIconSize * .8f;
            Visible(view.icon.gameObject, custom);
            Visible(view.text.gameObject, !custom);
            Color glow = cue ? new Color(parryCueColor.r, parryCueColor.g, parryCueColor.b, warningGlowColor.a) : warningGlowColor;
            glow.a *= Mathf.Lerp(.6f, 1f, strength);
            view.glow.color = glow;
            view.glow.rectTransform.sizeDelta = Vector2.one * warningIconSize * warningGlowScale;
            view.frame.color = cue ? parryCueColor : new Color(1f, .35f, .4f, .9f);
            view.frame.rectTransform.sizeDelta = Vector2.one * warningIconSize * 1.05f * punch;
            Visible(view.frame.gameObject, showWarningFrame);
            view.cueRing.color = parryCueColor;
            view.cueRing.rectTransform.sizeDelta = Vector2.one * warningIconSize * parryCueRingScale * punch;
            Visible(view.cueRing.gameObject, cue);
            Visible(view.root.gameObject, true);
        }
        for (; index < additionalThreatViews.Count; index++)
        {
            additionalThreatViews[index].target = null;
            Visible(additionalThreatViews[index].root.gameObject, false);
        }
    }

    private ThreatWarningView CreateAdditionalThreatWarning()
    {
        EnsureFeedbackCanvas();
        var root = new GameObject("Incoming projectile warning (pooled)", typeof(RectTransform));
        root.transform.SetParent(generatedFeedbackCanvas.transform, false);
        var view = new ThreatWarningView { root = (RectTransform)root.transform };
        view.glow = CreateThreatGraphic<CombatFeedbackGraphic>("Halo", view.root);
        view.glow.Glow = true;
        view.frame = CreateThreatGraphic<CombatFeedbackGraphic>("Frame", view.root);
        view.frame.CornerFrame = true;
        view.cueRing = CreateThreatGraphic<CombatFeedbackGraphic>("Parry timing", view.root);
        view.icon = CreateThreatGraphic<Image>("Icon", view.root);
        view.icon.preserveAspect = true;
        if (incomingMagicIndicator != null) view.icon.material = incomingMagicIndicator.material;
        view.text = CreateThreatGraphic<TextMeshProUGUI>("Exclamation", view.root);
        if (feedbackFont != null) view.text.font = feedbackFont;
        view.text.text = "!";
        view.text.alignment = TextAlignmentOptions.Center;
        view.text.margin = Vector4.zero;
        return view;
    }

    private static T CreateThreatGraphic<T>(string name, Transform parent) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.transform.SetParent(parent, false);
        T graphic = go.GetComponent<T>();
        graphic.raycastTarget = false;
        graphic.rectTransform.anchoredPosition = Vector2.zero;
        return graphic;
    }

    private void HideAdditionalThreatWarnings()
    {
        incomingThreats.Clear();
        nextThreatCheck = 0f;
        foreach (ThreatWarningView view in additionalThreatViews)
        {
            view.target = null;
            Visible(view.root.gameObject, false);
        }
    }

    private void PlaceAdditionalThreatIndicators()
    {
        foreach (ThreatWarningView view in additionalThreatViews)
        {
            MagicProjectile shot = view.target;
            if (shot == null || shot == incomingThreat || worldCamera == null || shot.Object == null ||
                !shot.Object.IsValid || shot.Finished || !IsOffscreenThreat(shot))
            { Visible(view.root.gameObject, false); continue; }
            Vector3 direction = GetThreatViewDirection(worldCamera.transform.position, worldCamera.transform.rotation, shot.transform.position);
            float padding = Mathf.Max(warningEdgePadding, warningIconSize * warningGlowScale * .5f + 8f);
            Vector2 point = GetThreatScreenPoint(direction, worldCamera.pixelRect, padding);
            point = SeparateThreatPoint(point, worldCamera.pixelRect, padding,
                warningIconSize * Mathf.Max(1f, warningSpacingScale), occupiedThreatPoints);
            bool placed = PlaceScreen(view.root, point);
            Visible(view.root.gameObject, placed);
            if (placed) occupiedThreatPoints.Add(point);
        }
    }

    private static Vector2 SeparateThreatPoint(Vector2 desired, Rect screen, float padding, float spacing, List<Vector2> occupied)
    {
        Vector2 half = new Vector2(Mathf.Max(1f, screen.width * .5f - padding), Mathf.Max(1f, screen.height * .5f - padding));
        Vector2 delta = desired - screen.center;
        float angle = Mathf.Atan2(delta.y / half.y, delta.x / half.x);
        float step = Mathf.Min(.5f, Mathf.Max(1f, spacing) / Mathf.Max(1f, Mathf.Min(half.x, half.y)));
        for (int i = 0; i < 128; i++)
        {
            int offset = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            float candidateAngle = angle + offset * step;
            Vector2 candidate = screen.center + new Vector2(Mathf.Cos(candidateAngle) * half.x, Mathf.Sin(candidateAngle) * half.y);
            bool overlap = false;
            foreach (Vector2 used in occupied)
                if ((candidate - used).sqrMagnitude < spacing * spacing) { overlap = true; break; }
            if (!overlap) return candidate;
        }
        return desired; // Extremely crowded/small viewports retain every warning rather than dropping threats.
    }
}
