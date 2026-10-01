using UnityEngine;

// Owns temporary visual resources even if the caster dies or despawns.
public sealed class CombatTransientEffect : MonoBehaviour
{
    private float started;
    private float duration;
    private float fadeDuration;
    private Vector3 fromScale;
    private Vector3 toScale;
    private Material[] materials;
    private Color[] colors;
    private Mesh[] meshes;

    public void Initialize(float lifetime, Vector3 from, Vector3 to, Material[] ownedMaterials,
        Mesh[] ownedMeshes = null, float fadeSeconds = -1f)
    {
        started = Time.unscaledTime;
        duration = Mathf.Max(0.01f, lifetime);
        fadeDuration = fadeSeconds < 0f ? duration : Mathf.Min(duration, fadeSeconds);
        fromScale = from;
        toScale = to;
        transform.localScale = from;
        materials = ownedMaterials;
        meshes = ownedMeshes;
        colors = new Color[materials.Length];
        for (int i = 0; i < materials.Length; i++)
            colors[i] = materials[i].color;
    }

    private void Update()
    {
        if (materials == null)
            return;
        float elapsed = Time.unscaledTime - started;
        if (elapsed >= duration)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = Vector3.Lerp(fromScale, toScale, elapsed / duration);
        float opacity = Mathf.Clamp01((duration - elapsed) / Mathf.Max(0.01f, fadeDuration));
        for (int i = 0; i < materials.Length; i++)
        {
            Color color = colors[i];
            color.a *= opacity;
            materials[i].color = color;
        }
    }

    private void OnDestroy()
    {
        if (materials != null)
            foreach (Material material in materials)
                if (material != null)
                    Destroy(material);
        if (meshes != null)
            foreach (Mesh mesh in meshes)
                if (mesh != null)
                    Destroy(mesh);
    }
}
