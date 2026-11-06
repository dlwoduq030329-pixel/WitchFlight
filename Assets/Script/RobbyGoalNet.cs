using UnityEngine;

// Cosmetic only: shared mesh and collision geometry remain unchanged.
public sealed class RobbyGoalNet : MonoBehaviour
{
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField, Min(0.1f)] private float impactRadius = 2f;
    [SerializeField, Min(0f)] private float strength = 0.05f;
    [SerializeField, Min(0.1f)] private float settleSeconds = 2f;
    private Mesh mesh, originalMesh;
    private Vector3[] original, deformed;
    private float[] weights;
    private Vector3 impulse;
    private float elapsed;
    private void Awake()
    {
        if (meshFilter == null || meshFilter.sharedMesh == null || !meshFilter.sharedMesh.isReadable) { enabled = false; return; }
        originalMesh = meshFilter.sharedMesh;
        mesh = Instantiate(originalMesh); mesh.name = "Robby Net instance"; mesh.MarkDynamic(); meshFilter.sharedMesh = mesh;
        original = mesh.vertices; deformed = new Vector3[original.Length]; weights = new float[original.Length];
        enabled = false;
    }
    public void Impact(Vector3 point, Vector3 velocity)
    {
        if (mesh == null) return;
        elapsed = 0f;
        impulse = transform.InverseTransformVector(Vector3.ClampMagnitude(velocity * strength, 0.6f));
        float top = originalMesh.bounds.max.y;
        for (int i = 0; i < original.Length; i++)
        {
            float distance = Vector3.Distance(transform.TransformPoint(original[i]), point);
            float anchor = Mathf.Clamp01((top - original[i].y) / Mathf.Max(0.01f, originalMesh.bounds.size.y * 0.12f));
            weights[i] = Mathf.Pow(Mathf.Clamp01(1f - distance / impactRadius), 2f) * anchor;
        }
        enabled = true;
    }
    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / settleSeconds);
        float wave = Mathf.Sin(t * Mathf.PI * 5f) * (1f - t) * (1f - t);
        for (int i = 0; i < original.Length; i++) deformed[i] = original[i] + impulse * (weights[i] * wave);
        mesh.vertices = deformed; mesh.RecalculateBounds();
        if (t >= 1f) enabled = false;
    }
    private void OnDestroy()
    {
        if (meshFilter != null && meshFilter.sharedMesh == mesh) meshFilter.sharedMesh = originalMesh;
        if (mesh != null) Destroy(mesh);
    }
}
