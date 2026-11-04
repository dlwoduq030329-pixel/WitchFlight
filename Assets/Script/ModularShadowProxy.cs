using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ModularShadowProxy : MonoBehaviour
{
    [Tooltip("Bake this map using Tools/WitchFlight/Shadows/Bake selected map. Disable this component to restore original shadows.")]
    [SerializeField] private ModularShadowBake bake;
    [Tooltip("Static-map mesh/material/transform safety checks are spread over frames to avoid CPU spikes. Visibility and camera fading are checked immediately.")]
    [SerializeField, Range(1, 16)] private int geometryChecksPerFrame = 4;
    public ModularShadowBake Bake { get => bake; set => bake = value; }

    private sealed class Source
    {
        public MeshRenderer renderer;
        public MeshFilter filter;
        public ModularShadowBake.Source baked;
        public ShadowCastingMode original;
        public int fading;
    }
    private sealed class Batch
    {
        public ModularShadowProxy owner;
        public ModularShadowBake.Batch baked;
        public MeshRenderer proxy;
        public Source[] sources;
        public bool combined;
        public bool geometryValid;
    }
    private readonly List<Batch> batches = new();
    private readonly List<Material> materialScratch = new();
    private static readonly Dictionary<Renderer, Batch> owners = new();
    private GameObject generated;
    private int validationCursor;
    private static readonly Unity.Profiling.ProfilerMarker ValidationMarker = new("WitchFlight.ShadowProxyValidation");
    public int ActiveBatchCount { get; private set; }
    public int CombinedSourceCount { get; private set; }

    private void OnEnable()
    {
        if (!Application.isPlaying || bake == null || bake.batches == null) return;
        Initialize();
    }

    private void Initialize()
    {
        generated = new GameObject("Map shadow proxies (generated)");
        generated.transform.SetParent(transform, false);
        foreach (var data in bake.batches)
        {
            if (data == null || data.mesh == null || data.sources == null || data.sources.Length < 2) continue;
            var batch = new Batch { owner = this, baked = data, sources = new Source[data.sources.Length] };
            bool valid = true;
            for (int i = 0; i < data.sources.Length; i++)
            {
                var entry = data.sources[i];
                if (entry == null) { valid = false; break; }
                Transform t = Resolve(transform, entry.path);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                var f = t != null ? t.GetComponent<MeshFilter>() : null;
                if (r == null || f == null || t.name != entry.objectName || owners.ContainsKey(r) ||
                    f.sharedMesh != entry.mesh || r.shadowCastingMode != ShadowCastingMode.On)
                { valid = false; break; }
                batch.sources[i] = new Source { renderer = r, filter = f, baked = entry, original = r.shadowCastingMode };
            }
            if (!valid) continue; // Stale/missing bake never disables the original shadows.
            var go = new GameObject("Shadow chunk " + batches.Count, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = data.layer;
            go.transform.SetParent(generated.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = data.mesh;
            batch.proxy = go.GetComponent<MeshRenderer>();
            batch.proxy.enabled = false;
            batch.proxy.sharedMaterials = data.materials;
            batch.proxy.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            batch.proxy.receiveShadows = false;
            batch.proxy.renderingLayerMask = data.renderingLayerMask;
            batch.proxy.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            batch.proxy.lightProbeUsage = LightProbeUsage.Off;
            batch.proxy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            batches.Add(batch);
            foreach (var source in batch.sources) owners.Add(source.renderer, batch);
            batch.geometryValid = CanCombine(batch, transform.worldToLocalMatrix);
        }
        LateUpdate();
    }

    private void LateUpdate()
    {
        using var marker = ValidationMarker.Auto();
        ActiveBatchCount = 0;
        CombinedSourceCount = 0;
        Matrix4x4 inverse = transform.worldToLocalMatrix;
        for (int i = 0; i < Mathf.Min(geometryChecksPerFrame, batches.Count); i++)
        {
            if (validationCursor >= batches.Count) validationCursor = 0;
            Batch checkedBatch = batches[validationCursor++];
            checkedBatch.geometryValid = CanCombine(checkedBatch, inverse);
        }
        foreach (var batch in batches)
        {
            bool valid = batch.geometryValid && CanShowBatch(batch);
            SetCombined(batch, valid);
            if (valid) { ActiveBatchCount++; CombinedSourceCount += batch.sources.Length; }
        }
    }

    private static bool CanShowBatch(Batch batch)
    {
        foreach (var source in batch.sources)
        {
            var r = source.renderer;
            if (r == null || source.fading > 0 || !r.enabled || !r.gameObject.activeInHierarchy ||
                r.forceRenderingOff) return false;
        }
        return true;
    }

    private bool CanCombine(Batch batch, Matrix4x4 inverse)
    {
        foreach (var source in batch.sources)
        {
            var r = source.renderer;
            if (r == null || source.filter == null || source.fading > 0 || !r.enabled ||
                !r.gameObject.activeInHierarchy || r.forceRenderingOff || r.HasPropertyBlock() ||
                r.shadowCastingMode != (batch.combined ? ShadowCastingMode.Off : source.original) ||
                source.filter.sharedMesh != source.baked.mesh || r.gameObject.layer != batch.baked.layer ||
                r.renderingLayerMask != batch.baked.renderingLayerMask ||
                !ModularShadowBake.SameMatrix(inverse * r.localToWorldMatrix, source.baked.matrix)) return false;
            r.GetSharedMaterials(materialScratch);
            if (materialScratch.Count != batch.baked.materials.Length) return false;
            for (int i = 0; i < materialScratch.Count; i++)
                if (materialScratch[i] != batch.baked.materials[i]) return false;
        }
        return true;
    }

    private static void SetCombined(Batch batch, bool combined)
    {
        if (batch.combined == combined) return;
        if (batch.proxy != null) batch.proxy.enabled = combined;
        foreach (var source in batch.sources)
            if (source.renderer != null)
            {
                // If another feature changed the flag, do not overwrite that change on fallback.
                if (combined) source.renderer.shadowCastingMode = ShadowCastingMode.Off;
                else if (source.renderer.shadowCastingMode == ShadowCastingMode.Off)
                    source.renderer.shadowCastingMode = source.original;
            }
        batch.combined = combined;
    }

    // The obstruction fade temporarily changes materials for one camera. Fall back to original
    // casters for that chunk so transparent walls do not keep casting a solid proxy shadow.
    public static void BeginSourceFade(Renderer renderer)
    {
        if (renderer == null || !owners.TryGetValue(renderer, out var batch)) return;
        foreach (var source in batch.sources) if (source.renderer == renderer) { source.fading++; break; }
        SetCombined(batch, false);
    }
    public static void EndSourceFade(Renderer renderer)
    {
        if (ReferenceEquals(renderer, null) || !owners.TryGetValue(renderer, out var batch)) return;
        foreach (var source in batch.sources) if (source.renderer == renderer)
        { source.fading = Mathf.Max(0, source.fading - 1); break; }
        if (batch.owner != null && batch.owner.isActiveAndEnabled)
        {
            batch.geometryValid = batch.owner.CanCombine(batch, batch.owner.transform.worldToLocalMatrix);
            SetCombined(batch, batch.geometryValid);
        }
    }

    private void OnDisable()
    {
        foreach (var batch in batches)
        {
            SetCombined(batch, false);
            foreach (var source in batch.sources)
                if (!ReferenceEquals(source.renderer, null)) owners.Remove(source.renderer);
        }
        batches.Clear();
        validationCursor = 0;
        if (generated != null)
        {
            generated.SetActive(false);
            if (Application.isPlaying) Destroy(generated); else DestroyImmediate(generated);
        }
        generated = null;
        ActiveBatchCount = CombinedSourceCount = 0;
    }

    public static Transform Resolve(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        foreach (string part in path.Split('/'))
        {
            if (!int.TryParse(part, out int index) || index < 0 || index >= root.childCount) return null;
            root = root.GetChild(index);
        }
        return root;
    }
}
