using UnityEngine;
using UnityEngine.UI;

// An empty-center notification: the HP cells beneath it must remain readable.
// BattlePerkUI still owns when this object is shown and dismissed.
[AddComponentMenu("UI/WitchFlight Perk Available Border")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PerkAvailableBorder : MaskableGraphic
{
    [Header("Border pulse")]
    [Tooltip("테두리는 RectTransform 바깥쪽에만 그립니다. 체력 칸 내부는 덮지 않습니다.")]
    [SerializeField, Min(0.1f)] private float borderThickness = 2f;
    [Tooltip("밝아졌다 어두워지는 한 주기의 시간(초).")]
    [SerializeField, Min(0.1f)] private float pulseSeconds = 1.2f;
    [SerializeField, Range(0f, 1f)] private float minimumAlpha = 0.25f;
    private float enabledAt;

    protected override void OnEnable()
    {
        raycastTarget = false;
        enabledAt = Time.unscaledTime;
        base.OnEnable();
    }

    private void Update()
    {
        if (Application.isPlaying) SetVerticesDirty();
    }

    internal static float PulseAlpha(float elapsed, float seconds, float minimum)
    {
        float phase = Mathf.Repeat(Mathf.Max(0f, elapsed), Mathf.Max(0.1f, seconds)) /
            Mathf.Max(0.1f, seconds);
        float wave = 0.5f + 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);
        return Mathf.Lerp(Mathf.Clamp01(minimum), 1f, wave);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f) return;

        float thickness = Mathf.Max(0.1f, borderThickness);
        Color tint = color;
        tint.a *= Application.isPlaying ? PulseAlpha(Time.unscaledTime - enabledAt, pulseSeconds, minimumAlpha) : 1f;

        // Four disjoint strips OUTSIDE the rectangle, never a full-screen/filled quad.
        // UVs use Graphic's default white texture; no sprite/Animator is required.
        AddStrip(vh, rect.xMin - thickness, rect.yMax, rect.xMax + thickness, rect.yMax + thickness, tint);
        AddStrip(vh, rect.xMin - thickness, rect.yMin - thickness, rect.xMax + thickness, rect.yMin, tint);
        AddStrip(vh, rect.xMin - thickness, rect.yMin, rect.xMin, rect.yMax, tint);
        AddStrip(vh, rect.xMax, rect.yMin, rect.xMax + thickness, rect.yMax, tint);
    }

    private static void AddStrip(VertexHelper vh, float left, float bottom, float right, float top, Color tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
        vh.AddVert(new Vector3(left, top), tint, Vector2.zero);
        vh.AddVert(new Vector3(right, top), tint, Vector2.zero);
        vh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
