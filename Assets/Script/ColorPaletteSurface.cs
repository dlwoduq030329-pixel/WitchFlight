using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A tiny UI mesh replaces generated textures. Rebuilt only on hue/layout changes.
public sealed class ColorPaletteSurface : MaskableGraphic, IPointerDownHandler, IDragHandler
{
    [SerializeField] private ClothColorPaletteUI owner;
    [SerializeField] private bool hueStrip;
    private float hue;

    public void SetHue(float next)
    {
        if (Mathf.Approximately(hue, next)) return;
        hue = next;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (hueStrip)
        {
            for (int i = 0; i < 6; i++)
            {
                float from = i / 6f, to = (i + 1) / 6f;
                Color bottom = Color.HSVToRGB(from, 1, 1), top = Color.HSVToRGB(to, 1, 1);
                Quad(mesh, rect.xMin, rect.xMax, Mathf.Lerp(rect.yMin, rect.yMax, from),
                    Mathf.Lerp(rect.yMin, rect.yMax, to), bottom, bottom, top, top);
            }
        }
        else
        {
            // Subdivision matches HSV's saturation/value product, unlike a two-triangle gradient.
            const int steps = 16;
            for (int y = 0; y < steps; y++)
                for (int x = 0; x < steps; x++)
                {
                    float s0 = x / (float)steps, s1 = (x + 1) / (float)steps;
                    float v0 = y / (float)steps, v1 = (y + 1) / (float)steps;
                    Quad(mesh, Mathf.Lerp(rect.xMin, rect.xMax, s0), Mathf.Lerp(rect.xMin, rect.xMax, s1),
                        Mathf.Lerp(rect.yMin, rect.yMax, v0), Mathf.Lerp(rect.yMin, rect.yMax, v1),
                        Color.HSVToRGB(hue, s0, v0), Color.HSVToRGB(hue, s1, v0),
                        Color.HSVToRGB(hue, s1, v1), Color.HSVToRGB(hue, s0, v1));
                }
        }
    }

    private static void Quad(VertexHelper mesh, float left, float right, float bottom, float top,
        Color a, Color b, Color c, Color d)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(left, bottom), a, Vector2.zero);
        mesh.AddVert(new Vector3(right, bottom), b, Vector2.right);
        mesh.AddVert(new Vector3(right, top), c, Vector2.one);
        mesh.AddVert(new Vector3(left, top), d, Vector2.up);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    public void OnPointerDown(PointerEventData e) => Pick(e);
    public void OnDrag(PointerEventData e) => Pick(e);
    private void Pick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || owner == null || !raycastTarget) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out var local)) return;
        Rect rect = rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        owner.PickColor(hueStrip, new Vector2(Mathf.Clamp01((local.x - rect.xMin) / rect.width),
            Mathf.Clamp01((local.y - rect.yMin) / rect.height)));
    }
}
