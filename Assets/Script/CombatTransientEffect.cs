using System.Collections.Generic;
using UnityEngine;

// Owns temporary visual resources even if the caster dies or despawns.
public sealed class CombatTransientEffect : MonoBehaviour
{
    private enum PoolKind { None, Pulse, Beam }
    private const int MaxRetainedPerKind = 32;
    private static readonly Stack<CombatTransientEffect> pulses = new();
    private static readonly Stack<CombatTransientEffect> beams = new();
    private PoolKind poolKind;
    private LineRenderer beamLine;
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
        if (colors == null || colors.Length != materials.Length)
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
            ReturnOrDestroy();
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

    // Only short-lived LOCAL visuals are pooled. Decoy meshes remain separately
    // owned; NetworkObject spawn/despawn and authoritative hits are unchanged.
    public static void PlayPulse(Vector3 position, Color color, float diameter, float lifetime)
    {
        CombatTransientEffect effect = Acquire(PoolKind.Pulse);
        effect.transform.SetPositionAndRotation(position, Quaternion.identity);
        effect.materials[0].color = color; // Undo the previous use's faded alpha.
        effect.Initialize(lifetime, Vector3.one * 0.08f, Vector3.one * diameter, effect.materials);
        effect.gameObject.SetActive(true);
    }

    public static void PlayBeam(Vector3 origin, Vector3 end, Color color)
    {
        CombatTransientEffect effect = Acquire(PoolKind.Beam);
        effect.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        effect.beamLine.SetPosition(0, origin);
        effect.beamLine.SetPosition(1, end);
        effect.materials[0].color = color;
        effect.Initialize(0.16f, Vector3.one, Vector3.one, effect.materials);
        effect.gameObject.SetActive(true);
    }

    private static CombatTransientEffect Acquire(PoolKind kind)
    {
        Stack<CombatTransientEffect> pool = kind == PoolKind.Pulse ? pulses : beams;
        while (pool.Count > 0)
        {
            CombatTransientEffect cached = pool.Pop();
            // Scene unloading destroys both active and idle visuals; discard stale refs.
            if (cached != null) return cached;
        }
        GameObject obj;
        Renderer renderer;
        if (kind == PoolKind.Pulse)
        {
            obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = "Combat pulse (pooled)";
            obj.layer = 2;
            Collider collider = obj.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            renderer = obj.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            obj = new GameObject("Vision beam (pooled)");
            var line = obj.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.12f;
            line.endWidth = 0.04f;
            renderer = line;
        }
        var effect = obj.AddComponent<CombatTransientEffect>();
        effect.poolKind = kind;
        effect.beamLine = renderer as LineRenderer;
        effect.materials = new[] { CombatPresentation.CreateEffectMaterial(Color.white) };
        renderer.sharedMaterial = effect.materials[0];
        return effect;
    }

    private void ReturnOrDestroy()
    {
        if (poolKind == PoolKind.None)
        {
            Destroy(gameObject);
            return;
        }
        Stack<CombatTransientEffect> pool = poolKind == PoolKind.Pulse ? pulses : beams;
        if (pool.Count >= MaxRetainedPerKind)
        {
            Destroy(gameObject); // Bounded retention after unusually large bursts.
            return;
        }
        gameObject.SetActive(false);
        pool.Push(this);
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
