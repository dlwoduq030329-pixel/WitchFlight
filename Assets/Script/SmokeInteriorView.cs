using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Depth fog is drawn after smoke particles but before QuickOutline's fill (queue 3110).
// No global fog, material, targeting or network visibility state is changed.
public sealed class SmokeInteriorView : MonoBehaviour
{
    private static SmokeInteriorView instance;
    private Camera view;
    private Material material;
    private GameObject screen;
    private readonly List<(Renderer renderer, bool forcedOff)> hidden = new();
    private bool inside;
    public static void Ensure()
    {
        if (instance == null) instance = new GameObject("Smoke interior view").AddComponent<SmokeInteriorView>();
    }
    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }
    private void LateUpdate()
    {
        Player player = Player.LocalPlayer;
        BattleFlag flag = BattleFlag.Instance;
        view = Camera.main;
        inside = view != null && player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            flag != null && flag.Runner == player.Runner && flag.ContainsSmoke(player.LockAimPoint);
        if (!inside) { if (screen != null) screen.SetActive(false); return; }
        if (screen == null)
        {
            Shader shader = Resources.Load<Shader>("SmokeInterior");
            if (shader == null) return;
            if (material != null) Destroy(material);
            material = new Material(shader);
            screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Smoke depth veil (local camera only)";
            var collider = screen.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        screen.transform.SetParent(view.transform, false);
        screen.transform.localPosition = Vector3.forward * (view.nearClipPlane + .05f);
        screen.transform.localRotation = Quaternion.identity;
        screen.transform.localScale = Vector3.one;
        view.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
        var settings = FlightCombatTable.Default;
        material.SetVector("_SmokeOrigin", player.LockAimPoint);
        material.SetVector("_SmokeCamera", view.transform.position);
        material.SetFloat("_ClearDistance", settings != null ? settings.smokeClearDistance : 5f);
        material.SetFloat("_FadeDistance", settings != null ? settings.smokeFadeDistance : 2f);
        material.SetColor("_SmokeColor", settings != null ? settings.smokeInteriorColor : new Color(.22f,.24f,.26f,.995f));
        screen.SetActive(true);
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        Restore();
        if (screen != null) screen.SetActive(inside && camera == view);
        if (!inside || camera != view) return;
        // Interior particles can otherwise cover even objects within the clear 5 m.
        foreach (var cloud in UtilityMagicVisual.SmokeViews)
            if (cloud != null)
                foreach (var renderer in cloud.GetComponentsInChildren<Renderer>(true))
                {
                    hidden.Add((renderer, renderer.forceRenderingOff));
                    renderer.forceRenderingOff = true;
                }
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera) => Restore();
    private void Restore()
    {
        foreach (var entry in hidden)
            if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
        hidden.Clear();
    }
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        Restore();
        if (screen != null) screen.SetActive(false);
    }
    private void OnDestroy()
    {
        if (screen != null) Destroy(screen);
        if (material != null) Destroy(material);
        if (instance == this) instance = null;
    }
}
