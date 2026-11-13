using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Scene-authored, empty-centre overlay. Rebuilds only on UI/Inspector changes, not every frame.
[AddComponentMenu("UI/WitchFlight Mana Booster Screen Border")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ManaBoosterScreenBorder : MaskableGraphic
{
    [Tooltip("화면의 짧은 변에 대한 파란 테두리 폭 비율입니다. 중앙은 투명합니다.")]
    [SerializeField, Range(.01f, .3f)] private float edgeWidth = .1f;
    [Header("Zone transition")]
    [SerializeField, Min(.01f)] private float fadeInSeconds = .35f;
    [SerializeField, Min(.01f)] private float fadeOutSeconds = .5f;
    private Coroutine fadeRoutine;
    private bool requestedVisible;

    protected override void OnEnable()
    {
        raycastTarget = false;
        base.OnEnable();
        if (Application.isPlaying) canvasRenderer.SetAlpha(0f);
    }

    public void SetVisible(bool visible)
    {
        if (requestedVisible == visible) return;
        if (visible && !gameObject.activeSelf) gameObject.SetActive(true);
        requestedVisible = visible;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        if (!isActiveAndEnabled) return;
        fadeRoutine = StartCoroutine(FadeTo(visible ? 1f : 0f,
            visible ? fadeInSeconds : fadeOutSeconds));
    }

    private IEnumerator FadeTo(float target, float seconds)
    {
        // Reverse from the current opacity if the player crosses the boundary again.
        float start = canvasRenderer.GetAlpha();
        float elapsed = 0f;
        float duration = Mathf.Max(.01f, seconds);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasRenderer.SetAlpha(Mathf.Lerp(start, target,
                Mathf.SmoothStep(0f, 1f, elapsed / duration)));
            yield return null;
        }
        canvasRenderer.SetAlpha(target);
        fadeRoutine = null;
        if (target == 0f) gameObject.SetActive(false);
    }

    public void HideImmediately()
    {
        requestedVisible = false;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        canvasRenderer.SetAlpha(0f);
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    protected override void OnDisable()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        requestedVisible = false;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect outer = rectTransform.rect;
        if (outer.width <= 0f || outer.height <= 0f) return;
        float inset = Mathf.Min(outer.width, outer.height) * Mathf.Clamp(edgeWidth, .01f, .3f);
        Rect inner = Rect.MinMaxRect(outer.xMin + inset, outer.yMin + inset,
            outer.xMax - inset, outer.yMax - inset);
        Color transparent = color;
        transparent.a = 0f;
        AddCorners(mesh, outer, color);
        AddCorners(mesh, inner, transparent);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            mesh.AddTriangle(i, next, next + 4);
            mesh.AddTriangle(i, next + 4, i + 4);
        }
    }

    private static void AddCorners(VertexHelper mesh, Rect rect, Color tint)
    {
        mesh.AddVert(new Vector3(rect.xMin, rect.yMin), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax, rect.yMin), tint, Vector2.zero);
    }
}
