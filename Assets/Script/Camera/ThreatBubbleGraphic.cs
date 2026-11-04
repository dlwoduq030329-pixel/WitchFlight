using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Concave comic-bubble stencil geometry. No texture asset or custom shader required.
public sealed class ThreatBubbleGraphic : MaskableGraphic
{
    private static readonly Vector2[] outline = BuildOutline();
    private static readonly int[] triangles = Triangulate(outline);
    private bool mirrored;
    public bool Mirrored { get => mirrored; set { mirrored = value; SetVerticesDirty(); } }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        foreach (Vector2 p in outline)
        {
            float x = mirrored ? 1f - p.x : p.x;
            vh.AddVert(new Vector3(rect.xMin + x * rect.width, rect.yMin + p.y * rect.height), color, new Vector2(x, p.y));
        }
        for (int i = 0; i < triangles.Length; i += 3)
            if (mirrored) vh.AddTriangle(triangles[i + 2], triangles[i + 1], triangles[i]);
            else vh.AddTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
    }

    internal static Vector2[] BuildOutline()
    {
        var points = new List<Vector2> { new(.13f, .95f) };
        Curve(points, new(.5f, .79f), new(.91f, .92f));
        Curve(points, new(.9f, .73f), new(.99f, .55f));
        Curve(points, new(.86f, .38f), new(.85f, .2f));
        points.Add(new(.7f, .21f));
        points.Add(new(.6f, .015f));
        points.Add(new(.57f, .23f));
        Curve(points, new(.3f, .32f), new(.09f, .18f));
        Curve(points, new(.12f, .36f), new(.015f, .53f));
        Curve(points, new(.1f, .7f), points[0]);
        points.RemoveAt(points.Count - 1); // closing point is implicit
        return points.ToArray();
    }

    private static void Curve(List<Vector2> points, Vector2 control, Vector2 end)
    {
        Vector2 start = points[points.Count - 1];
        for (int i = 1; i <= 6; i++)
        {
            float t = i / 6f;
            points.Add((1f - t) * (1f - t) * start + 2f * (1f - t) * t * control + t * t * end);
        }
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    // Ear clipping preserves the inward-curved edges and narrow tail (a fan would fill the notches).
    internal static int[] Triangulate(Vector2[] points)
    {
        float area = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length];
            area += a.x * b.y - a.y * b.x;
        }
        float sign = area >= 0f ? 1f : -1f;
        var remaining = new List<int>();
        var output = new List<int>();
        for (int i = 0; i < points.Length; i++) remaining.Add(i);
        int guard = points.Length * points.Length;
        while (remaining.Count > 2 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                int b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                if (Cross(points[a], points[b], points[c]) * sign <= .000001f) continue;
                bool contains = false;
                foreach (int p in remaining)
                    if (p != a && p != b && p != c &&
                        Cross(points[a], points[b], points[p]) * sign >= -.000001f &&
                        Cross(points[b], points[c], points[p]) * sign >= -.000001f &&
                        Cross(points[c], points[a], points[p]) * sign >= -.000001f)
                    { contains = true; break; }
                if (contains) continue;
                output.Add(a); output.Add(b); output.Add(c);
                remaining.RemoveAt(i); clipped = true; break;
            }
            if (!clipped) break;
        }
        return output.ToArray();
    }
}
