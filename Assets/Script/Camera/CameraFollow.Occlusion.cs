using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CameraFollow
{
    [Header("Map obstruction transparency (main camera only)")]
    [SerializeField] private bool fadeMapObstructions = true;
    [SerializeField] private LayerMask obstructionFadeLayers = ~0;
    [SerializeField, Range(0f, 1f)] private float obstructedMapOpacity = .18f;
    [SerializeField, Min(.01f)] private float obstructionProbeRadius = .25f;
    [SerializeField, Min(.01f)] private float obstructionFadeInSeconds = .08f;
    [SerializeField, Min(.01f)] private float obstructionFadeOutSeconds = .2f;
    [SerializeField, Min(0f)] private float obstructionReleaseDelay = .08f;
    private readonly Dictionary<Renderer, ObstructionFade> obstructionFades = new();
    private readonly List<Renderer> clearedObstructions = new();
    private readonly List<ObstructionFade> appliedObstructions = new();
    private readonly RaycastHit[] obstructionHits = new RaycastHit[48];
    private readonly Collider[] obstructionOverlaps = new Collider[32];
    private readonly Dictionary<Collider, Renderer[]> obstructionRenderers = new();
    private readonly List<Material> obstructionMaterialScratch = new();
    private Collider[] currentObstructionOverlaps;
    private int currentObstructionOverlapCount;
    private int obstructionFrame = -1;
    private static readonly Unity.Profiling.ProfilerMarker ObstructionMarker = new("WitchFlight.CameraObstructions");
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    private sealed class ObstructionFade
    {
        public Renderer renderer;
        public Material[] originals, transparent;
        public MaterialPropertyBlock[] saved, applied;
        public float alpha = 1f, seen;
    }

    private void EnableOcclusionFade()
    {
        RenderPipelineManager.beginCameraRendering += BeginOcclusionFade;
        RenderPipelineManager.endCameraRendering += EndOcclusionFade;
        Camera.onPreCull += BeginBuiltinOcclusionFade;
        Camera.onPostRender += EndBuiltinOcclusionFade;
    }

    private void DisableOcclusionFade()
    {
        RenderPipelineManager.beginCameraRendering -= BeginOcclusionFade;
        RenderPipelineManager.endCameraRendering -= EndOcclusionFade;
        Camera.onPreCull -= BeginBuiltinOcclusionFade;
        Camera.onPostRender -= EndBuiltinOcclusionFade;
        ClearOcclusionFade();
    }

    private void BeginBuiltinOcclusionFade(Camera camera)
    { if (GraphicsSettings.currentRenderPipeline == null) BeginOcclusionFade(default, camera); }
    private void EndBuiltinOcclusionFade(Camera camera)
    { if (GraphicsSettings.currentRenderPipeline == null) EndOcclusionFade(default, camera); }

    private bool TryOcclusionFocus(out Vector3 focus)
    {
        focus = default;
        if (!fadeMapObstructions || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return false;
        if (deathObserving && deathBody != null) { focus = deathBody.CameraBounds.center; return true; }
        if (targetPlayer == null || targetPlayer.Object == null || !targetPlayer.Object.IsValid ||
            !targetPlayer.Object.HasInputAuthority || !targetPlayer.IsAlive) return false;
        focus = targetPlayer.LockAimPoint;
        return true;
    }

    private void BeginOcclusionFade(ScriptableRenderContext context, Camera camera)
    {
        // Restore even if another camera starts before an expected end callback.
        RestoreOcclusionMaterials();
        if (camera != viewCamera || !isActiveAndEnabled) return;
        using var profile = ObstructionMarker.Auto();
        if (!TryOcclusionFocus(out Vector3 focus)) { ClearOcclusionFade(); return; }
        if (obstructionFrame != Time.frameCount)
        {
            obstructionFrame = Time.frameCount;
            // All three focus probes start at the same camera position.
            currentObstructionOverlapCount = Physics.OverlapSphereNonAlloc(transform.position,
                Mathf.Max(.01f, obstructionProbeRadius), obstructionOverlaps, obstructionFadeLayers, QueryTriggerInteraction.Ignore);
            currentObstructionOverlaps = currentObstructionOverlapCount == obstructionOverlaps.Length
                ? Physics.OverlapSphere(transform.position, Mathf.Max(.01f, obstructionProbeRadius),
                    obstructionFadeLayers, QueryTriggerInteraction.Ignore) : obstructionOverlaps;
            if (currentObstructionOverlaps != obstructionOverlaps) currentObstructionOverlapCount = currentObstructionOverlaps.Length;
            ProbeObstructions(focus);
            ProbeObstructions(focus + Vector3.up * .45f);
            ProbeObstructions(focus - Vector3.up * .45f);
            clearedObstructions.Clear();
            foreach (var pair in obstructionFades)
            {
                ObstructionFade state = pair.Value;
                float goal = Time.unscaledTime - state.seen <= obstructionReleaseDelay ? obstructedMapOpacity : 1f;
                state.alpha = Mathf.MoveTowards(state.alpha, goal, Time.unscaledDeltaTime /
                    Mathf.Max(.01f, goal < state.alpha ? obstructionFadeInSeconds : obstructionFadeOutSeconds));
                // Keep recently used materials warm when circling the same pillars.
                // Bounded retention still releases distant/removed map objects.
                if (state.renderer == null || (goal >= 1f && state.alpha >= .999f &&
                    (Time.unscaledTime - state.seen > 8f || obstructionFades.Count > 256)))
                    clearedObstructions.Add(pair.Key);
            }
            foreach (Renderer renderer in clearedObstructions)
            {
                DisposeOcclusionMaterials(obstructionFades[renderer]);
                obstructionFades.Remove(renderer);
            }
        }
        try
        {
            foreach (ObstructionFade state in obstructionFades.Values)
            {
                if (state.renderer == null || !state.renderer.enabled || state.alpha >= .999f) continue;
                PrepareOcclusionMaterials(state);
                for (int i = 0; i < state.originals.Length; i++)
                    state.renderer.GetPropertyBlock(state.saved[i], i);
                // Snapshot every slot before mutation, including exception/disable cleanup.
                appliedObstructions.Add(state);
                ModularShadowProxy.BeginSourceFade(state.renderer);
                for (int i = 0; i < state.originals.Length; i++)
                {
                    if (state.transparent[i] == state.originals[i]) continue;
                    state.renderer.GetPropertyBlock(state.applied[i], i);
                    if (state.applied[i].isEmpty) state.renderer.GetPropertyBlock(state.applied[i]);
                    Color tint = ReadObstructionColor(state.originals[i], state.applied[i]);
                    tint.a *= state.alpha;
                    state.applied[i].SetColor(BaseColorProperty, tint);
                    state.applied[i].SetColor(ColorProperty, tint);
                    state.renderer.SetPropertyBlock(state.applied[i], i);
                }
                state.renderer.sharedMaterials = state.transparent;
            }
        }
        catch
        {
            RestoreOcclusionMaterials();
            throw;
        }
    }

    private void EndOcclusionFade(ScriptableRenderContext context, Camera camera)
    { if (camera == viewCamera) RestoreOcclusionMaterials(); }

    private void ProbeObstructions(Vector3 focus)
    {
        Vector3 from = transform.position, delta = focus - from;
        float distance = delta.magnitude;
        if (distance < .05f) return;
        float radius = Mathf.Max(.01f, obstructionProbeRadius);
        int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, obstructionHits,
            Mathf.Max(0f, distance - .1f), obstructionFadeLayers, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == obstructionHits.Length ? Physics.SphereCastAll(from, radius, delta / distance,
            Mathf.Max(0f, distance - .1f), obstructionFadeLayers, QueryTriggerInteraction.Ignore) : obstructionHits;
        if (hits != obstructionHits) count = hits.Length;
        for (int i = 0; i < count; i++) AddObstruction(hits[i].collider, from, focus);
        // Reverse sweep also catches the exit face when the camera starts inside a concave map shell.
        count = Physics.SphereCastNonAlloc(focus, radius, -delta / distance, obstructionHits,
            distance, obstructionFadeLayers, QueryTriggerInteraction.Ignore);
        hits = count == obstructionHits.Length ? Physics.SphereCastAll(focus, radius, -delta / distance,
            distance, obstructionFadeLayers, QueryTriggerInteraction.Ignore) : obstructionHits;
        if (hits != obstructionHits) count = hits.Length;
        for (int i = 0; i < count; i++) AddObstruction(hits[i].collider, from, focus);
        // Sphere casts alone miss a camera already inside a solid wall.
        for (int i = 0; i < currentObstructionOverlapCount; i++) AddObstruction(currentObstructionOverlaps[i], from, focus);
    }

    private void AddObstruction(Collider collider, Vector3 from, Vector3 focus)
    {
        if (collider == null || collider.isTrigger || collider.GetComponentInParent<Player>() != null ||
            collider.GetComponentInParent<MagicProjectile>() != null ||
            collider.GetComponentInParent<Suimono.Core.SuimonoObject>() != null) return;
        if (!obstructionRenderers.TryGetValue(collider, out Renderer[] renderers))
        {
            Renderer own = collider.GetComponent<Renderer>();
            if (own == null) own = collider.GetComponentInParent<Renderer>();
            renderers = own != null ? new[] { own } : collider.GetComponentsInChildren<Renderer>(false);
            Transform parent = collider.transform.parent;
            for (int depth = 0; renderers.Length == 0 && parent != null && depth < 2; depth++, parent = parent.parent)
                renderers = parent.GetComponentsInChildren<Renderer>(false);
            if (obstructionRenderers.Count > 256) obstructionRenderers.Clear();
            obstructionRenderers[collider] = renderers;
        }
        Vector3 direction = focus - from;
        float distance = direction.magnitude;
        var ray = new Ray(from, direction.normalized);
        foreach (Renderer renderer in renderers)
        {
            if (renderer is not MeshRenderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.GetComponentInParent<Player>() != null || renderer.GetComponentInParent<MagicProjectile>() != null) continue;
            Bounds bounds = renderer.bounds;
            bounds.Expand(obstructionProbeRadius * 2f);
            if (!bounds.Contains(from) && (!bounds.IntersectRay(ray, out float hitDistance) || hitDistance >= distance)) continue;
            if (!obstructionFades.TryGetValue(renderer, out ObstructionFade state))
            {
                state = new ObstructionFade { renderer = renderer };
                obstructionFades.Add(renderer, state);
            }
            state.seen = Time.unscaledTime;
        }
    }

    private static Color ReadObstructionColor(Material material, MaterialPropertyBlock block)
    {
        if (material == null) return Color.white;
        int property = material.HasProperty(BaseColorProperty) ? BaseColorProperty : ColorProperty;
        if (block.HasColor(property)) return block.GetColor(property);
        return material.HasProperty(property) ? material.GetColor(property) : Color.white;
    }

    private void PrepareOcclusionMaterials(ObstructionFade state)
    {
        state.renderer.GetSharedMaterials(obstructionMaterialScratch);
        bool same = state.originals != null && state.originals.Length == obstructionMaterialScratch.Count;
        if (same) for (int i = 0; i < obstructionMaterialScratch.Count; i++)
            if (obstructionMaterialScratch[i] != state.originals[i]) { same = false; break; }
        if (same) return;
        Material[] current = obstructionMaterialScratch.ToArray();
        DisposeOcclusionMaterials(state);
        state.originals = current;
        state.transparent = new Material[current.Length];
        state.saved = new MaterialPropertyBlock[current.Length];
        state.applied = new MaterialPropertyBlock[current.Length];
        for (int i = 0; i < current.Length; i++)
        {
            state.saved[i] = new MaterialPropertyBlock(); state.applied[i] = new MaterialPropertyBlock();
            Material original = current[i];
            if (original == null || original.renderQueue >= 3000) { state.transparent[i] = original; continue; }
            Material copy;
            if (original.HasProperty("_Surface") || original.shader.name == "Standard" || original.shader.name == "Standard (Specular setup)")
            {
                copy = new Material(original);
                if (copy.HasProperty("_Surface")) copy.SetFloat("_Surface", 1f);
                if (copy.HasProperty("_Mode")) copy.SetFloat("_Mode", 2f);
                if (copy.HasProperty("_Blend")) copy.SetFloat("_Blend", 0f);
                if (copy.HasProperty("_AlphaClip")) copy.SetFloat("_AlphaClip", 0f);
                copy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                copy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (copy.HasProperty("_SrcBlendAlpha")) copy.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                if (copy.HasProperty("_DstBlendAlpha")) copy.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                copy.SetInt("_ZWrite", 0);
                copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (!copy.HasProperty("_Surface")) copy.EnableKeyword("_ALPHABLEND_ON");
                copy.DisableKeyword("_ALPHATEST_ON"); copy.DisableKeyword("_ALPHAPREMULTIPLY_ON"); copy.DisableKeyword("_ALPHAMODULATE_ON");
                copy.SetOverrideTag("RenderType", "Transparent");
                copy.SetShaderPassEnabled("ShadowCaster", false);
                copy.SetShaderPassEnabled("DepthOnly", false);
                copy.SetShaderPassEnabled("DepthNormals", false);
            }
            else
            {
                // Unsupported opaque shaders still become translucent using their base texture/color.
                copy = CombatPresentation.CreateEffectMaterial(Color.white);
                string texture = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                if (original.HasProperty(texture))
                {
                    copy.mainTexture = original.GetTexture(texture);
                    copy.mainTextureScale = original.GetTextureScale(texture);
                    copy.mainTextureOffset = original.GetTextureOffset(texture);
                }
            }
            copy.name = original.name + " (camera obstruction)";
            copy.hideFlags = HideFlags.DontSave;
            copy.renderQueue = 3000;
            state.transparent[i] = copy;
        }
    }

    private void RestoreOcclusionMaterials()
    {
        foreach (ObstructionFade state in appliedObstructions)
        {
            if (state.renderer == null) continue;
            state.renderer.sharedMaterials = state.originals;
            for (int i = 0; i < state.saved.Length; i++)
                state.renderer.SetPropertyBlock(state.saved[i].isEmpty ? null : state.saved[i], i);
            ModularShadowProxy.EndSourceFade(state.renderer);
        }
        appliedObstructions.Clear();
    }

    private static void DisposeOcclusionMaterials(ObstructionFade state)
    {
        if (state.transparent == null) return;
        for (int i = 0; i < state.transparent.Length; i++)
            if (state.transparent[i] != null && state.transparent[i] != state.originals[i]) Destroy(state.transparent[i]);
        state.transparent = null;
    }

    private void ClearOcclusionFade()
    {
        RestoreOcclusionMaterials();
        foreach (ObstructionFade state in obstructionFades.Values) DisposeOcclusionMaterials(state);
        obstructionFades.Clear(); obstructionRenderers.Clear(); obstructionFrame = -1;
    }
}
