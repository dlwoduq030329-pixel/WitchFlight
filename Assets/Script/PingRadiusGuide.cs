using UnityEngine;
using UnityEngine.UI;

// Editor preview ring; its RectTransform diameter is controlled by Ping Radius.
public sealed class PingRadiusGuide : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        const int segments = 96;
        float outer = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
        float inner = Mathf.Max(0, outer - 1.5f);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            Vector2 unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(unit * outer, color, Vector2.zero);
            mesh.AddVert(unit * inner, color, Vector2.zero);
            if (i == 0) continue;
            int index = i * 2;
            mesh.AddTriangle(index - 2, index, index - 1);
            mesh.AddTriangle(index, index + 1, index - 1);
        }
    }
}
