using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleHud
{
    [Header("Aim-centered incoming warning")]
    [Tooltip("움직이는 기수 에임을 중심으로 표시합니다. 끄면 조준점 중심을 사용합니다.")]
    [SerializeField] private bool warningFollowForwardAim = true;
    [SerializeField, Min(32f)] private float warningOrbitRadius = 110f;
    [Tooltip("양쪽 거리 바의 합산 길이. 중앙 느낌표 간격은 제외합니다.")]
    [SerializeField, Min(8f)] private float warningDistanceBarWidth = 48f;
    [SerializeField, Min(1f)] private float warningDistanceBarHeight = 4f;
    [SerializeField, Min(0f)] private float warningDistanceBarGap = 28f;
    private RectTransform warningAimAnchor;
    private Canvas warningAimCanvas;
    private ThreatDistanceBar primaryDistanceBar;

    private sealed class ThreatDistanceBar
    {
        public RectTransform root;
        public readonly Slider[] halves = new Slider[2];
        public readonly Image[] fills = new Image[2];
    }

    private Rect GetThreatOrbitBounds()
    {
        Vector2 center = worldCamera.pixelRect.center;
        var anchor = warningFollowForwardAim && forwardAimMarker != null && forwardAimMarker.gameObject.activeInHierarchy
            ? forwardAimMarker : desiredAimMarker;
        if (anchor != warningAimAnchor)
        {
            warningAimAnchor = anchor;
            warningAimCanvas = anchor != null ? anchor.GetComponentInParent<Canvas>() : null;
        }
        if (anchor != null && anchor.gameObject.activeInHierarchy)
        {
            Canvas root = warningAimCanvas != null ? warningAimCanvas.rootCanvas : null;
            Camera camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            center = RectTransformUtility.WorldToScreenPoint(camera, anchor.TransformPoint(anchor.rect.center));
        }
        Rect viewport = worldCamera.pixelRect;
        float margin = (warningDistanceBarWidth + warningDistanceBarGap) * .5f + warningIconSize;
        float radius = Mathf.Min(warningOrbitRadius, Mathf.Max(16f, Mathf.Min(viewport.width, viewport.height) * .5f - margin));
        center.x = Mathf.Clamp(center.x, viewport.xMin + radius + margin, viewport.xMax - radius - margin);
        center.y = Mathf.Clamp(center.y, viewport.yMin + radius + margin, viewport.yMax - radius - margin);
        return new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
    }

    private static float ThreatDistanceFraction(float distance, float farDistance)
        => Mathf.Clamp01(distance / Mathf.Max(1f, farDistance));

    private ThreatDistanceBar CreateThreatDistanceBar(Transform parent)
    {
        var view = new ThreatDistanceBar();
        view.root = new GameObject("Threat distance (both ends)", typeof(RectTransform)).GetComponent<RectTransform>();
        view.root.SetParent(parent, false);
        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject(i == 0 ? "Left distance" : "Right distance", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(view.root, false);
            var slider = go.GetComponent<Slider>();
            var background = CreateThreatGraphic<Image>("Track", go.transform);
            background.color = new Color(.05f, .02f, .03f, .65f);
            StretchThreatBar(background.rectTransform);
            var fillArea = new GameObject("Fill area", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(go.transform, false); StretchThreatBar(fillArea);
            var fill = CreateThreatGraphic<Image>("Fill", fillArea);
            StretchThreatBar(fill.rectTransform);
            slider.fillRect = fill.rectTransform;
            slider.direction = i == 0 ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = 1; slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            view.halves[i] = slider; view.fills[i] = fill;
        }
        return view;
    }

    private static void StretchThreatBar(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void UpdateThreatDistanceBar(ThreatDistanceBar view, float distance, bool cue)
    {
        float fraction = ThreatDistanceFraction(distance, warningDistance);
        Color color = cue ? parryCueColor : warningGlowColor; color.a = 1f;
        float half = warningDistanceBarWidth * .5f;
        for (int i = 0; i < 2; i++)
        {
            var slider = view.halves[i];
            var rect = (RectTransform)slider.transform;
            rect.sizeDelta = new Vector2(half, warningDistanceBarHeight);
            rect.anchoredPosition = new Vector2((i == 0 ? -1 : 1) * (warningDistanceBarGap + half) * .5f, 0);
            slider.SetValueWithoutNotify(fraction);
            view.fills[i].color = color;
        }
    }

    private void PlaceThreatDistanceBar(ThreatDistanceBar view, Vector2 point, Vector2 center, bool visible, bool local)
    {
        if (view == null) return;
        if (local) view.root.anchoredPosition = Vector2.zero;
        else visible &= PlaceScreen(view.root, point);
        Vector2 bearing = point - center;
        view.root.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(bearing.y, bearing.x) * Mathf.Rad2Deg - 90f);
        Visible(view.root.gameObject, visible);
    }
}
