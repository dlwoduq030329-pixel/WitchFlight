using UnityEngine;
using UnityEngine.UI;

// Two nested triangles keep the outline crisp without a texture or an Outline mesh effect.
public sealed class DesignatedTargetTriangle : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        AddTriangle(mesh, rect.width, rect.height, Color.black);
        AddTriangle(mesh, rect.width * .68f, rect.height * .68f, color);
    }

    private static void AddTriangle(VertexHelper mesh, float width, float height, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(0f, height * .5f), tint, Vector2.zero);
        mesh.AddVert(new Vector3(width * .5f, -height * .5f), tint, Vector2.zero);
        mesh.AddVert(new Vector3(-width * .5f, -height * .5f), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }
}
