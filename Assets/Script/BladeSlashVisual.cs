using UnityEngine;

// A stationary crescent, not a travelling projectile. Its outer edge is the hit radius.
public sealed class BladeSlashVisual : MonoBehaviour
{
    private Mesh mesh;
    private Material material;
    private float age;
    public static void Play(Vector3 origin, Vector3 forward, float radius, float angle, float height)
    {
        var root = new GameObject("Charged blade slash");
        root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
        var effect = root.AddComponent<BladeSlashVisual>();
        const int segments = 48;
        var vertices = new Vector3[(segments + 1) * 2];
        var indices = new int[segments * 12];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float a = Mathf.Lerp(-angle * .5f, angle * .5f, t) * Mathf.Deg2Rad;
            var edge = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            vertices[i * 2] = edge * radius;
            vertices[i * 2 + 1] = edge * radius * (1 - .3f * Mathf.Sin(t * Mathf.PI));
            if (i == segments) continue;
            int v = i * 2, k = i * 12;
            indices[k] = v; indices[k+1] = v+2; indices[k+2] = v+1;
            indices[k+3] = v+1; indices[k+4] = v+2; indices[k+5] = v+3;
            for (int j=0;j<6;j+=3) { indices[k+6+j]=indices[k+j]; indices[k+7+j]=indices[k+2+j]; indices[k+8+j]=indices[k+1+j]; }
        }
        effect.mesh = new Mesh { name = "Charged blade arc" };
        effect.mesh.vertices = vertices; effect.mesh.triangles = indices; effect.mesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh = effect.mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        effect.material = CombatPresentation.CreateEffectMaterial(new Color(.75f,.35f,1f,.85f));
        renderer.sharedMaterial = effect.material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
    private void Update()
    {
        age += Time.deltaTime;
        if (age >= .3f) { Destroy(gameObject); return; }
        if (material != null) material.color = new Color(.75f,.35f,1f,.85f * (1-age/.3f));
    }
    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
