using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public sealed class PlayerDirectionalBlurSettings
{
    public bool enabled = true;
    [Tooltip("전진 3단계 + 실제 Shift 부스트 중에만 적용. 끄면 3단계부터 적용합니다.")]
    public bool requireBoost = true;
    [Tooltip("이동 반대 방향으로 늘어지는 거리(미터). 캐릭터 메시/포즈는 복제하지 않습니다.")]
    [Range(0f, 0.8f)] public float trailDistance = 0.18f;
    [Tooltip("1080p 기준 화면에서 늘어지는 길이의 상한(px).")]
    [Range(0f, 80f)] public float maximumPixels = 24f;
    [Range(0f, 1f)] public float strength = 0.45f;
    [Tooltip("본체 선명함 유지 정도. 1이면 본체는 유지하고 뒤쪽만 번집니다.")]
    [Range(0f, 1f)] public float preserveBody = 0.7f;
    [Min(0.01f)] public float responseSpeed = 10f;
}

// Current-frame silhouette + motion projection only. No cloned meshes or pose history.
internal sealed class PlayerDirectionalBlur : IDisposable
{
    private sealed class Source
    {
        public Renderer renderer;
        public MeshFilter filter;
        public SkinnedMeshRenderer skin;
        public Material[] originals, masks;
        public Mesh Mesh => skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
    }
    private readonly List<Renderer> found = new();
    private readonly List<Material> materials = new();
    private readonly List<Source> sources = new();
    private readonly MaterialPropertyBlock properties = new();
    private Player owner;
    private Shader shader;
    private float nextRefresh, weight;
    private bool hasPosition;
    private Vector3 previousPosition, velocity;
    private PlayerDirectionalBlurSettings settings;
    public bool ProtectPlayer { get; private set; }
    public bool HasBlur => weight > 0.001f && settings != null && settings.enabled &&
        settings.trailDistance > 0f && settings.maximumPixels > 0f && settings.strength > 0f;
    public bool NeedsMask => ProtectPlayer || HasBlur;
    public Vector4 Trail { get; private set; }
    public Vector4 Anchor { get; private set; }
    public Vector4 ScreenRect { get; private set; }

    public void Update(Player player, bool canShow, bool protect, PlayerDirectionalBlurSettings options, bool allowDirectional = true)
    {
        settings = options;
        if (!canShow || player == null) { ProtectPlayer = false; ResetMotion(); return; }
        if (owner != player)
        {
            ReleaseSources(); owner = player; nextRefresh = 0f; ResetMotion();
        }
        ProtectPlayer = protect;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshSources();
        }
        Vector3 position = player.transform.position;
        Vector3 travelled = hasPosition ? position - previousPosition : Vector3.zero;
        previousPosition = position;
        bool continuous = hasPosition && travelled.sqrMagnitude < 144f;
        hasPosition = true;
        float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        // Actual displacement rather than requested speed: a blocked player cannot smear.
        Vector3 targetVelocity = continuous ? travelled / dt : Vector3.zero;
        velocity = continuous ? Vector3.Lerp(velocity, targetVelocity, 1f - Mathf.Exp(-20f * dt)) : Vector3.zero;
        bool show = allowDirectional && continuous && ShouldBlur(options.enabled, player.CurrentSpeedStage, player.IsBoosting,
            options.requireBoost) && targetVelocity.sqrMagnitude > 0.04f;
        weight = show ? Mathf.Lerp(weight, 1f, 1f - Mathf.Exp(-Mathf.Max(0.01f, options.responseSpeed) * dt)) : 0f;
    }

    internal static bool ShouldBlur(bool enabled, int stage, bool boosting, bool requireBoost) =>
        enabled && stage == 3 && (!requireBoost || boosting);

    private void ResetMotion()
    {
        hasPosition = false; velocity = Vector3.zero; weight = 0f; Trail = Vector4.zero;
    }

    public void Prepare(Camera camera)
    {
        Trail = Vector4.zero;
        ScreenRect = new Vector4(1f, 1f, 0f, 0f);
        if (!HasBlur || owner == null) return;
        Vector3 center = camera.WorldToViewportPoint(owner.LockAimPoint);
        if (center.z <= camera.nearClipPlane) return;
        Vector3 back = camera.WorldToViewportPoint(owner.LockAimPoint - velocity.normalized * settings.trailDistance);
        if (back.z <= camera.nearClipPlane) return;
        // SAME camera for both projections: follow-camera movement cannot cancel the trail.
        // Perspective scaling handles straight-ahead flight as well as sideways movement.
        float scale = camera.orthographic ? 0f : center.z / back.z - 1f;
        Vector2 shift = new Vector2(back.x - center.x, back.y - center.y);
        Vector2 min = new Vector2(1f, 1f), max = Vector2.zero;
        foreach (Source source in sources)
        {
            if (!IsVisible(source, camera)) continue;
            Bounds bounds = source.renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 uv = camera.WorldToViewportPoint(p);
                if (uv.z <= camera.nearClipPlane) continue;
                min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
            }
        }
        if (min.x > max.x || min.y > max.y) return;
        Vector2 size = new Vector2(Mathf.Max(1, camera.scaledPixelWidth), Mathf.Max(1, camera.scaledPixelHeight));
        Vector2 radius = Vector2.Max(new Vector2(Mathf.Abs(min.x - center.x), Mathf.Abs(min.y - center.y)),
            new Vector2(Mathf.Abs(max.x - center.x), Mathf.Abs(max.y - center.y)));
        float pixels = Mathf.Clamp(settings.maximumPixels, 0f, 80f) * size.y / 1080f;
        float limit = ProjectionLimit(shift, scale, radius, size, pixels);
        Trail = new Vector4(shift.x * limit, shift.y * limit, scale * limit, Mathf.Clamp01(settings.strength) * weight);
        Anchor = new Vector4(center.x, center.y, (back.z - center.z) * limit, Mathf.Clamp01(settings.preserveBody));
        ScreenRect = new Vector4(min.x - pixels / size.x, min.y - pixels / size.y, max.x + pixels / size.x, max.y + pixels / size.y);
    }

    internal static float ProjectionLimit(Vector2 shift, float scale, Vector2 radius, Vector2 size, float maximumPixels)
    {
        float distance = Vector2.Scale(shift, size).magnitude + Mathf.Abs(scale) * Vector2.Scale(radius, size).magnitude;
        return distance > 0.0001f ? Mathf.Clamp01(maximumPixels / distance) : 0f;
    }

    private void RefreshSources()
    {
        owner.GetComponentsInChildren(true, found);
        int index = 0; bool changed = false;
        foreach (Renderer renderer in found)
        {
            if (!IsModelRenderer(renderer)) continue;
            if (index >= sources.Count || sources[index].renderer != renderer) { changed = true; break; }
            renderer.GetSharedMaterials(materials);
            Material[] old = sources[index].originals;
            if (old.Length != materials.Count) { changed = true; break; }
            for (int i = 0; i < old.Length; i++) if (old[i] != materials[i]) { changed = true; break; }
            if (changed) break;
            index++;
        }
        if (!changed && index == sources.Count) return;
        ReleaseSources();
        if (shader == null) shader = Resources.Load<Shader>("PlayerMotionMask");
        foreach (Renderer renderer in found)
        {
            if (!IsModelRenderer(renderer)) continue;
            var source = new Source { renderer = renderer, skin = renderer as SkinnedMeshRenderer,
                filter = renderer.GetComponent<MeshFilter>(), originals = renderer.sharedMaterials };
            source.masks = new Material[source.originals.Length];
            for (int i = 0; i < source.originals.Length; i++)
                if (shader != null && source.originals[i] != null)
                    source.masks[i] = new Material(shader) { name = "Player motion mask (runtime)", hideFlags = HideFlags.HideAndDontSave };
            sources.Add(source);
        }
    }

    private static bool IsModelRenderer(Renderer renderer) => renderer is SkinnedMeshRenderer ||
        (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() != null);
    private static bool IsVisible(Source source, Camera camera) => source.renderer != null && source.renderer.enabled &&
        !source.renderer.forceRenderingOff && source.renderer.gameObject.activeInHierarchy && source.Mesh != null &&
        source.renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && (camera.cullingMask & (1 << source.renderer.gameObject.layer)) != 0;

    internal void DrawMask(RasterCommandBuffer cmd, Camera camera, Texture depth)
    {
        foreach (Source source in sources)
        {
            if (!IsVisible(source, camera)) continue;
            int count = Mathf.Min(source.Mesh.subMeshCount, source.masks.Length);
            for (int i = 0; i < count; i++)
            {
                Material mask = source.masks[i], original = source.originals[i];
                if (mask == null || original == null) continue;
                source.renderer.GetPropertyBlock(properties, i);
                string texture = original.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap";
                if (original.HasProperty(texture))
                {
                    mask.SetTexture("_MainTex", properties.HasTexture(texture) ? properties.GetTexture(texture) : original.GetTexture(texture));
                    mask.SetTextureScale("_MainTex", original.GetTextureScale(texture));
                    mask.SetTextureOffset("_MainTex", original.GetTextureOffset(texture));
                }
                mask.SetTexture("_SpeedSceneDepth", depth);
                if (original.HasProperty("_Color")) mask.SetColor("_Color", original.GetColor("_Color"));
                else if (original.HasProperty("_BaseColor")) mask.SetColor("_Color", original.GetColor("_BaseColor"));
                if (original.HasProperty("_AlphaMask")) mask.SetTexture("_AlphaMask", original.GetTexture("_AlphaMask"));
                CopyFloat(original, mask, "_AlphaMaskMode"); CopyFloat(original, mask, "_AlphaMaskScale");
                CopyFloat(original, mask, "_AlphaMaskValue"); CopyFloat(original, mask, "_Cutoff");
                CopyFloat(original, mask, "_Cull"); CopyFloat(original, mask, "_Invisible");
                cmd.DrawRenderer(source.renderer, mask, i, 0);
            }
        }
    }
    private static void CopyFloat(Material from, Material to, string property)
    {
        if (from.HasProperty(property)) to.SetFloat(property, from.GetFloat(property));
    }
    private void ReleaseSources()
    {
        foreach (Source source in sources) foreach (Material mask in source.masks)
            if (mask != null) UnityEngine.Object.Destroy(mask);
        sources.Clear();
    }
    public void Dispose()
    {
        ReleaseSources(); found.Clear(); materials.Clear(); owner = null; ProtectPlayer = false; ResetMotion();
    }
}
