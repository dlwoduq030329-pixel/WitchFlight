using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-active controller. The optional panel contains only the animated visuals.
[DisallowMultipleComponent]
public sealed class BattleKillFeed : MonoBehaviour
{
    public static BattleKillFeed Instance { get; private set; }
    [Header("Kill card (empty = generated UI)")]
    [SerializeField] private GameObject killPanel;
    [SerializeField] private TMP_Text killerNameText;
    [SerializeField] private TMP_Text victimNameText;
    [SerializeField] private Image killerProfileImage;
    [SerializeField] private Image victimProfileImage;
    [Header("Profile lookup")]
    [Tooltip("공용 프로필 테이블. 비우면 Resources/ProfileImageTable을 자동 사용합니다.")]
    [SerializeField] private ProfileImageTable profileTable;
    [SerializeField] private TMP_FontAsset uiFont;
    [Header("Stacked notices")]
    [Tooltip("등장과 퇴장 연출을 포함한 각 처치 UI의 총 표시 시간입니다.")]
    [SerializeField, Min(0.1f)] private float displaySeconds = 2f;
    [SerializeField, Min(0f)] private float rowSpacing = 12f;
    [SerializeField, Range(1, 16)] private int maxVisibleNotices = 8;
    [Header("Pop -> shrink + fade (unscaled seconds)")]
    [SerializeField, Min(0.01f)] private float popSeconds = 0.12f;
    [SerializeField, Min(0.01f)] private float shrinkFadeSeconds = 0.75f;
    [SerializeField, Min(0.01f)] private float startScale = 0.75f;
    [SerializeField, Min(0.01f)] private float peakScale = 1.2f;
    [SerializeField, Min(0.01f)] private float endScale = 0.6f;

    private sealed class NoticeView
    {
        public GameObject Root;
        public CanvasGroup Group;
        public float Started;
    }
    private readonly List<NoticeView> notices = new List<NoticeView>();
    private CanvasGroup group;
    private Vector3 authoredScale;
    private float authoredAlpha = 1f;
    private GameObject generatedRoot;

    private void Awake()
    {
        Instance = this;
        if (uiFont == null) uiFont = GetComponent<BattleHud>()?.FeedbackFont;
        if (killPanel != null && transform.IsChildOf(killPanel.transform))
        {
            Debug.LogWarning("BattleKillFeed must be outside Kill Panel. Using a generated card.", this);
            killPanel = null;
        }
        if (killPanel != null) PreparePanel();
    }

    // Called by the host-authenticated BattleFlag RPC. Never derives a kill from local damage.
    public void ShowKill(string killer, int killerProfile, string victim, int victimProfile)
    {
        if (!isActiveAndEnabled) return;
        EnsurePanel();
        while (notices.Count >= Mathf.Max(1, maxVisibleNotices)) RemoveNotice(0);
        // The assigned panel stays inactive as the template; each kill owns independent text and timing.
        GameObject copy = Instantiate(killPanel, killPanel.transform.parent, false);
        copy.name = "Kill notice";
        var notice = new NoticeView { Root = copy, Group = copy.GetComponent<CanvasGroup>(), Started = Time.unscaledTime };
        SetName(CloneBinding(killerNameText, copy.transform), killer);
        SetName(CloneBinding(victimNameText, copy.transform), victim);
        SetProfile(CloneBinding(killerProfileImage, copy.transform), killerProfile);
        SetProfile(CloneBinding(victimProfileImage, copy.transform), victimProfile);
        foreach (Graphic graphic in copy.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        LayoutElement layout = copy.GetComponent<LayoutElement>();
        if (layout == null) layout = copy.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        notices.Add(notice);
        PositionNotices();
        ApplyAnimation(notice, 0f);
        copy.SetActive(true);
    }

    private void Update()
    {
        for (int i = notices.Count - 1; i >= 0; i--)
        {
            NoticeView notice = notices[i];
            float elapsed = Time.unscaledTime - notice.Started;
            if (notice.Root == null || elapsed >= Mathf.Max(.1f, displaySeconds)) RemoveNotice(i);
            else ApplyAnimation(notice, elapsed);
        }
        PositionNotices();
    }

    private T CloneBinding<T>(T source, Transform copy) where T : Component
    {
        if (source == null || !source.transform.IsChildOf(killPanel.transform)) return null;
        // Sibling indices handle duplicate child names such as the two "Profile" images.
        var path = new Stack<int>();
        for (Transform node = source.transform; node != killPanel.transform; node = node.parent)
            path.Push(node.GetSiblingIndex());
        while (path.Count > 0) copy = copy.GetChild(path.Pop());
        return copy.GetComponent<T>();
    }

    private void PositionNotices()
    {
        if (killPanel == null) return;
        RectTransform template = killPanel.transform as RectTransform;
        float height = template != null ? template.rect.height : 100f;
        float step = height * Mathf.Abs(authoredScale.y) * Mathf.Max(startScale, Mathf.Max(peakScale, endScale)) + rowSpacing;
        for (int i = 0; i < notices.Count; i++)
        {
            if (notices[i].Root == null) continue;
            if (template != null && notices[i].Root.transform is RectTransform rect)
                rect.anchoredPosition3D = template.anchoredPosition3D + Vector3.down * (step * i);
            else notices[i].Root.transform.localPosition = killPanel.transform.localPosition + Vector3.down * (step * i);
        }
    }

    private void RemoveNotice(int index)
    {
        GameObject root = notices[index].Root;
        if (root != null) { root.SetActive(false); Destroy(root); }
        notices.RemoveAt(index);
    }

    public static Vector2 EvaluateAnimation(float elapsed, float pop, float fade, float start, float peak, float end)
    {
        pop = Mathf.Max(0.01f, pop); fade = Mathf.Max(0.01f, fade);
        if (elapsed < pop)
        {
            float t = Mathf.Clamp01(elapsed / pop);
            return new Vector2(Mathf.Lerp(start, peak, 1f - (1f - t) * (1f - t)), 1f);
        }
        float shrink = Mathf.Clamp01((elapsed - pop) / fade);
        return new Vector2(Mathf.Lerp(peak, end, 1f - Mathf.Pow(1f - shrink, 3f)), 1f - shrink);
    }

    public static Vector2 EvaluateNoticeAnimation(float elapsed, float total, float pop, float fade, float start, float peak, float end)
    {
        total = Mathf.Max(.1f, total);
        if (elapsed >= total) return new Vector2(end, 0f);
        pop = Mathf.Clamp(pop, .01f, total - .01f);
        fade = Mathf.Clamp(fade, .01f, total - pop);
        if (elapsed < pop) return EvaluateAnimation(elapsed, pop, fade, start, peak, end);
        float fadeStart = total - fade;
        if (elapsed < fadeStart) return new Vector2(peak, 1f);
        return EvaluateAnimation(pop + elapsed - fadeStart, pop, fade, start, peak, end);
    }

    private void ApplyAnimation(NoticeView notice, float elapsed)
    {
        Vector2 sample = EvaluateNoticeAnimation(elapsed, displaySeconds, popSeconds, shrinkFadeSeconds, startScale, peakScale, endScale);
        notice.Root.transform.localScale = authoredScale * sample.x;
        notice.Group.alpha = authoredAlpha * sample.y;
    }

    private static void SetName(TMP_Text label, string name)
    {
        if (label == null) return;
        label.richText = false;
        label.text = name ?? string.Empty;
    }

    private void SetProfile(Image image, int id)
    {
        if (image == null) return;
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        Sprite sprite = profileTable != null ? profileTable.GetSprite(id) : null;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private void PreparePanel()
    {
        group = killPanel.GetComponent<CanvasGroup>();
        if (group == null) group = killPanel.AddComponent<CanvasGroup>();
        authoredScale = killPanel.transform.localScale;
        authoredAlpha = group.alpha > 0f ? group.alpha : 1f;
        group.interactable = group.blocksRaycasts = false;
        killPanel.SetActive(false);
    }

    private void EnsurePanel()
    {
        if (killPanel != null) return;
        var root = new GameObject("Generated kill feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        generatedRoot = root;
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 250;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        RectTransform panel = Rect("Kill card", root.transform, new Vector2(0, 280), new Vector2(900, 100));
        killPanel = panel.gameObject;
        var background = killPanel.AddComponent<Image>();
        background.color = new Color(0.025f, 0.07f, 0.1f, 0.9f); background.raycastTarget = false;
        killerProfileImage = Profile(panel, -393);
        victimProfileImage = Profile(panel, 393);
        killerNameText = Label(panel, "Killer name", -205, 290);
        victimNameText = Label(panel, "Victim name", 205, 290);
        killerNameText.color = new Color(0.25f, 0.95f, 0.85f);
        victimNameText.color = new Color(1f, 0.45f, 0.5f);
        Label(panel, "Action", 0, 120).text = "처치 ▶";
        PreparePanel();
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
    private Image Profile(Transform parent, float x)
    {
        var image = Rect("Profile", parent, new Vector2(x, 0), new Vector2(72, 72)).gameObject.AddComponent<Image>();
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }
    private TMP_Text Label(Transform parent, string name, float x, float width)
    {
        var label = Rect(name, parent, new Vector2(x, 0), new Vector2(width, 80)).gameObject.AddComponent<TextMeshProUGUI>();
        if (uiFont != null) label.font = uiFont;
        label.fontSize = 28; label.enableAutoSizing = true; label.fontSizeMin = 16; label.fontSizeMax = 28;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        label.richText = false;
        return label;
    }
    private void RestorePanel()
    {
        if (killPanel == null) return;
        killPanel.SetActive(false); killPanel.transform.localScale = authoredScale;
        if (group != null) group.alpha = authoredAlpha;
    }
    private void OnDisable() { while (notices.Count > 0) RemoveNotice(notices.Count - 1); RestorePanel(); }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (generatedRoot != null) Destroy(generatedRoot);
    }
}
