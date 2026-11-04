using UnityEngine;

// One shared mesh per client. Pooled instances keep their renderer and material.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BladeWaveVisual : MonoBehaviour
{
    private static Mesh sharedMesh;
    private void Awake()
    {
        if (sharedMesh == null)
        {
            const int segments = 32;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(-1.35f, 1.35f, t);
                float width = .32f * Mathf.Sin(t * Mathf.PI);
                Vector3 edge = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle) - .55f, 0f) * 1.2f;
                vertices[i * 2] = edge;
                vertices[i * 2 + 1] = edge - new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * width;
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
                if (i == segments) continue;
                int a = i * 2, k = i * 6;
                triangles[k] = a; triangles[k + 1] = a + 2; triangles[k + 2] = a + 1;
                triangles[k + 3] = a + 1; triangles[k + 4] = a + 2; triangles[k + 5] = a + 3;
            }
            sharedMesh = new Mesh { name = "Blade crescent" };
            sharedMesh.vertices = vertices; sharedMesh.uv = uv; sharedMesh.triangles = triangles;
            sharedMesh.RecalculateBounds(); sharedMesh.RecalculateNormals();
            sharedMesh.UploadMeshData(true);
        }
        GetComponent<MeshFilter>().sharedMesh = sharedMesh;
    }
}
