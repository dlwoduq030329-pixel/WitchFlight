using System.Collections.Generic;
using UnityEngine;

// Sample once when a visual is created, never bake skinned meshes every frame.
public static class CatSeatGeometry
{
    public static bool TryBounds(Transform root, out Bounds bounds)
        => TryBounds(root, root.worldToLocalMatrix, out bounds);

    public static bool TryBounds(Transform root, Vector3 origin, Quaternion rotation, out Bounds bounds)
        => TryBounds(root, Matrix4x4.TRS(origin, rotation, Vector3.one).inverse, out bounds);

    private static bool TryBounds(Transform root, Matrix4x4 worldToSeat, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        var vertices = new List<Vector3>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            Mesh baked = null;
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                // Compensate the FBX renderer scale before transforming vertices to world space.
                baked = new Mesh(); skin.BakeMesh(baked, true); mesh = baked;
            }
            else if (renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            if (mesh == null) continue;
            if (mesh.isReadable)
            {
                vertices.Clear(); mesh.GetVertices(vertices);
                foreach (var vertex in vertices)
                {
                    Vector3 p = worldToSeat.MultiplyPoint3x4(renderer.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                    else bounds.Encapsulate(p);
                }
            }
            else
            {
                Bounds b = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    p = worldToSeat.MultiplyPoint3x4(renderer.transform.TransformPoint(p));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            if (baked != null) Object.Destroy(baked);
        }
        return found;
    }

    // Intersect a vertical line with triangles in the supplied seating frame.
    public static float SurfaceHeight(Transform model, Vector3 origin, Quaternion rotation, float x, float z, float fallback)
    {
        float height = float.NegativeInfinity;
        Quaternion inverse = Quaternion.Inverse(rotation);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            Mesh baked = null, mesh = null;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            { baked = new Mesh(); skin.BakeMesh(baked, true); mesh = baked; }
            else if (renderer.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = inverse * (renderer.transform.TransformPoint(vertices[i]) - origin);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < .0000001f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                if (u < 0 || v < 0 || u + v > 1) continue;
                height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
            }
            if (baked != null) Object.Destroy(baked);
        }
        return float.IsNegativeInfinity(height) ? fallback : height;
    }
}
