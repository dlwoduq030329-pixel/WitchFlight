using UnityEngine;
using UnityEngine.UI;

// Code-native fallback ring, exclamation mark and legacy edge gradient.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CombatFeedbackGraphic : MaskableGraphic
{
    public bool Edges;
    public bool Exclamation;
    public bool Glow;
    public bool CornerFrame;
    public bool HitMarker;
    public bool Flag;
    public Vector4 HitMarkerAngles = new Vector4(-30f, -120f, 30f, 120f);
    public float HitMarkerRadius = 28f;
    public float HitMarkerLength = 18f;
    public float HitMarkerWidth = 3f;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        if (Flag)
        {
            AddBar(vh, rect, -0.28f, -0.45f, -0.2f, 0.45f);
            int start = vh.currentVertCount;
            vh.AddVert(rect.center + Vector2.Scale(new Vector2(-0.2f, 0.45f), rect.size), color, Vector2.zero);
            vh.AddVert(rect.center + Vector2.Scale(new Vector2(0.4f, 0.24f), rect.size), color, Vector2.zero);
            vh.AddVert(rect.center + Vector2.Scale(new Vector2(-0.2f, 0.03f), rect.size), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            return;
        }
        if (HitMarker)
        {
            for (int i = 0; i < 4; i++)
            {
                float radians = HitMarkerAngles[i] * Mathf.Deg2Rad;
                Vector2 radial = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                Vector2 side = new Vector2(-radial.y, radial.x);
                Vector2 center = rect.center + radial * Mathf.Max(0f, HitMarkerRadius);
                Vector2 longAxis = radial * (Mathf.Max(1f, HitMarkerLength) * 0.5f);
                Vector2 shortAxis = side * (Mathf.Max(1f, HitMarkerWidth) * 0.5f);
                int start = vh.currentVertCount;
                vh.AddVert(center + longAxis, color, Vector2.zero);
                vh.AddVert(center + shortAxis, color, Vector2.zero);
                vh.AddVert(center - longAxis, color, Vector2.zero);
                vh.AddVert(center - shortAxis, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
            return;
        }
        if (CornerFrame)
        {
            // Clipped-corner, charcoal badge with a thin colored outline: matches the slot UI.
            Vector2[] corners = { new(-0.38f, -0.5f), new(-0.5f, -0.38f), new(-0.5f, 0.38f), new(-0.38f, 0.5f),
                new(0.38f, 0.5f), new(0.5f, 0.38f), new(0.5f, -0.38f), new(0.38f, -0.5f) };
            Color background = new Color(0.055f, 0.065f, 0.075f, color.a * 0.85f);
            vh.AddVert(rect.center, background, Vector2.zero);
            foreach (Vector2 corner in corners)
                vh.AddVert(rect.center + Vector2.Scale(corner, rect.size) * 0.95f, background, Vector2.zero);
            for (int i = 0; i < 8; i++) vh.AddTriangle(0, i + 1, (i + 1) % 8 + 1);
            for (int i = 0; i < 8; i++)
            {
                Vector2 point = Vector2.Scale(corners[i], rect.size);
                vh.AddVert(rect.center + point, color, Vector2.zero);
                vh.AddVert(rect.center + point * 0.95f, color, Vector2.zero);
            }
            for (int i = 0; i < 8; i++) Quad(vh, 9 + i * 2, 9 + (i + 1) % 8 * 2);
            return;
        }
        if (Glow)
        {
            const int glowSegments = 48;
            float glowRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            vh.AddVert(rect.center, color, Vector2.zero);
            Color clear = color; clear.a = 0f;
            for (int i = 0; i < glowSegments; i++)
            {
                float a = i * Mathf.PI * 2f / glowSegments;
                vh.AddVert(rect.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * glowRadius, clear, Vector2.zero);
            }
            for (int i = 0; i < glowSegments; i++) vh.AddTriangle(0, i + 1, (i + 1) % glowSegments + 1);
            return;
        }
        if (Exclamation)
        {
            // Two opaque quads: the stem and dot. No font/texture dependency.
            AddBar(vh, rect, -0.09f, -0.1f, 0.09f, 0.43f);
            AddBar(vh, rect, -0.09f, -0.4f, 0.09f, -0.22f);
            return;
        }
        if (Edges)
        {
            Vector2 c = rect.center;
            Vector2 half = rect.size * 0.5f;
            Vector2[] corners = { new(-1f, -1f), new(-1f, 1f), new(1f, 1f), new(1f, -1f) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = Vector2.Scale(corners[i], half);
                vh.AddVert(c + p, color, Vector2.zero);
                Color clear = color; clear.a = 0f;
                vh.AddVert(c + p * 0.76f, clear, Vector2.zero);
            }
            for (int i = 0; i < 4; i++) Quad(vh, i * 2, ((i + 1) % 4) * 2);
            return;
        }
        const int segments = 64;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float inner = Mathf.Max(0f, radius - 2.5f);
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(rect.center + direction * radius, color, Vector2.zero);
            vh.AddVert(rect.center + direction * inner, color, Vector2.zero);
        }
        for (int i = 0; i < segments; i++) Quad(vh, i * 2, ((i + 1) % segments) * 2);
    }
    private void AddBar(VertexHelper vh, Rect rect, float x0, float y0, float x1, float y1)
    {
        int start = vh.currentVertCount;
        vh.AddVert(rect.center + new Vector2(x0 * rect.width, y0 * rect.height), color, Vector2.zero);
        vh.AddVert(rect.center + new Vector2(x0 * rect.width, y1 * rect.height), color, Vector2.zero);
        vh.AddVert(rect.center + new Vector2(x1 * rect.width, y1 * rect.height), color, Vector2.zero);
        vh.AddVert(rect.center + new Vector2(x1 * rect.width, y0 * rect.height), color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
    private static void Quad(VertexHelper vh, int a, int b)
    {
        vh.AddTriangle(a, b, b + 1);
        vh.AddTriangle(a, b + 1, a + 1);
    }
}
